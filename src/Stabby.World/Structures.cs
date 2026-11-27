using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct StructurePiece
    {
        public ulong Key;
        public float X, Y, Z, Sx, Sy, Sz, C, S, L, Q, B, T, A, Calm, Base;
        public int Lo, Hi, M, G, R;
        public bool Coursed, Flat, Mossy, Drape;
    }

    public sealed class StructureLayout
    {
        public readonly List<StructurePiece> Pieces = new List<StructurePiece>();
        public readonly List<ObjectColumn> Columns = new List<ObjectColumn>();
        public readonly List<ObjectColumn> Contacts = new List<ObjectColumn>();
        public readonly List<ObjectColumn> Lush = new List<ObjectColumn>();
        public readonly List<StructureLight> Lights = new List<StructureLight>();
        public float DoorX, DoorZ;
    }

    public struct StructureLight
    {
        public float X, Y, Z;
        public int Shape;
    }

    public static partial class Structures
    {
        public const int Tiers = 3;
        public const float SeatM = 0.5f;
        public const float WallHeight = 0.2f, WallBase = 0.12f, WallBatter = 0.3f, WallColumnStep = 0.05f, Squareness = 0.45f;
        public const float WallSpan = 0.96f, WallOverlap = 0.012f, GateSpan = 0.55f, FootReach = 0.12f;
        public const float SkirtShare = 0.3f, SkirtBand = 0.15f, GroundY = 0.01f;
        static readonly int[] TierRock = { 1, 0, 0 };
        static readonly float[] Dir =
        {
            1f, 0.9238795f, 0.70710678f, 0.3826834f, 0f, -0.3826834f, -0.70710678f, -0.9238795f,
            -1f, -0.9238795f, -0.70710678f, -0.3826834f, 0f, 0.3826834f, 0.70710678f, 0.9238795f,
        };
        static readonly float[] WallSeat = { -0.45f, 0, -0.225f, 0, 0, 0, 0.225f, 0, 0.45f, 0 };
        static readonly float[] CairnSeat = { 0, 0, 0.4f, 0, -0.4f, 0, 0, 0.4f, 0, -0.4f };
        static readonly float[] HeadSeat = { 0, 0, 0.065f, 0, -0.065f, 0, 0, 0.085f, 0, -0.085f };

        internal static float Cos16(int k) => Dir[k & 15];
        internal static float Sin16(int k) => Dir[(k + 12) & 15];

        public static float[] Seat(int shape) => shape == ObjectPart.Wall ? WallSeat : shape == ObjectPart.Cairn ? CairnSeat : shape == ObjectPart.WallHead ? HeadSeat : KitSeat(shape);

        public static float Skirt(in StructurePiece p) => p.Y - p.Base <= GroundY ? SkirtShare * Math.Min(p.Sy, 0.5f * (p.Sx + p.Sz)) : 0;

        public static StructureLayout Layout(int shape, ulong key, int trim = 0)
        {
            var l = new StructureLayout();
            if (shape == ObjectPart.Wall) Wall(key, l);
            else if (shape == ObjectPart.Cairn) Cairn(key, l);
            else if (shape == ObjectPart.WallHead) WallHead(key, l);
            else if (ObjectPart.IsDungeon(shape)) Dungeon(shape, key, l, trim > 0 ? trim : Iron);
            else if (ObjectPart.IsMine(shape)) Mine(shape, key, l);
            else Built(shape, key, l);
            return l;
        }

        [ThreadStatic] static List<float> buildP, buildTones, blockPos;
        [ThreadStatic] static List<int> buildI, blockInd;
        [ThreadStatic] static List<float[]> blockPolys;
        [ThreadStatic] static List<StructurePiece> buildUsed;
        [ThreadStatic] static List<int> buildStarts;

        public static ObjectMesh Build(int shape, ulong key, int lod, int trim = 0)
        {
            int tier = Math.Max(0, Math.Min(Tiers - 1, lod));
            var b = new ObjectMeshes.Builder { P = buildP ??= new List<float>(), I = buildI ??= new List<int>() };
            var tones = buildTones ??= new List<float>();
            var used = buildUsed ??= new List<StructurePiece>();
            var starts = buildStarts ??= new List<int>();
            b.P.Clear(); b.I.Clear(); tones.Clear(); used.Clear(); starts.Clear();
            foreach (var p in Layout(shape, key, trim).Pieces)
            {
                if (tier < p.Lo || tier > p.Hi) continue;
                used.Add(p);
                starts.Add(b.P.Count / 3);
                var tr = new Rng(Rng.Mix(p.Key ^ 0x6A09E667BB67AE85UL));
                float tv = tr.Range(-1f, 1f) * (1 - p.Calm), th = tr.Range(-1f, 1f) * (1 - p.Calm), tl = tr.NextFloat();
                int before = b.P.Count / 3;
                bool cut = p.R == 0 && p.Q >= Faceted && p.M == 0 && !p.Coursed;
                float[] u, lift = null;
                int[] idx;
                if (p.R > 0)
                    u = RoundMesh(p.Key, p.R, tier, out idx, out lift);
                else if ((p.M > 0 && p.M != Soil) || p.Coursed)
                {
                    u = UnitBox;
                    idx = UnitBoxIdx;
                }
                else if (cut)
                {
                    u = Block(p.Key, p.Q < StoneSquare, shape == ObjectPart.Wall && tier > 0, out idx, tier > 0 ? 0 : Math.Max(0, Math.Min(1, (Unworn - p.Q) / WornSpan)), 0.5f * p.Sx, 0.5f * p.Sy, 0.5f * p.Sz);
                }
                else
                {
                    var m = ObjectMeshes.Rock(p.Key, TierRock[tier]);
                    idx = m.Indices;
                    u = Bounds(m.Positions);
                    for (int v = 0; v < u.Length; v += 3)
                    {
                        float inf = Math.Max(Math.Abs(u[v]), Math.Max(Math.Abs(u[v + 1]), Math.Abs(u[v + 2])));
                        if (inf < 1e-4f) continue;
                        float k = 1 + p.Q * ((float)Math.Sqrt(u[v] * u[v] + u[v + 1] * u[v + 1] + u[v + 2] * u[v + 2]) / inf - 1);
                        u[v] *= k; u[v + 1] *= k; u[v + 2] *= k;
                    }
                }
                if (p.M == 0) u = Bounds(u);
                var w = new float[u.Length];
                float skirt = Skirt(p), band = SkirtBand * p.Sy, y0 = float.MaxValue;
                for (int v = 1; v < u.Length; v += 3) y0 = Math.Min(y0, 0.5f * (u[v] + 1) * p.Sy);
                for (int v = 0; v < u.Length; v += 3)
                {
                    float y = 0.5f * (u[v + 1] + 1) * p.Sy, dy = y - 0.5f * p.Sy, x = 0.5f * u[v] * p.Sx + p.L * dy, z = 0.5f * u[v + 2] * (p.Sz - 2 * p.T * dy) + p.B * dy;
                    w[v] = p.X + p.C * x - p.S * z; w[v + 1] = p.Y + y - (y - y0 < band ? skirt * (1 - (y - y0) / band) : 0); w[v + 2] = p.Z + p.S * x + p.C * z;
                }
                if (cut)
                    foreach (int i in idx) b.I.Add(b.Add(w[i * 3], w[i * 3 + 1], w[i * 3 + 2]));
                else
                {
                    int first = b.P.Count / 3;
                    for (int v = 0; v < w.Length; v += 3) b.Add(w[v], w[v + 1], w[v + 2]);
                    foreach (int i in idx) b.I.Add(first + i);
                }
                if (p.M > 0) Grain(p, out tv, out th);
                for (int v = before; v < b.P.Count / 3; v++) { tones.Add(1); tones.Add(tv); tones.Add(th); tones.Add((p.Drape ? 64 : 0) + (p.Mossy ? 32 : 0) + (p.Flat ? 16 : 0) + (lift != null ? tl + lift[v - before] : p.M == Thatch || p.M == Turf ? tl + 0.5f * (u[(v - before) * 3 + 1] + 1) : p.Coursed && ((v - before) >> 2 | 1) != 3 ? tl + 2 : p.Calm > 0 ? 1 + 0.5f * tl : tl)); }
            }
            starts.Add(b.P.Count / 3);
            var mesh = b.Finish();
            mesh.Tones = tones.ToArray();
            Occlude(mesh, used, starts);
            return mesh;
        }

        static bool Inside(in StructurePiece p, float px, float py, float pz)
        {
            float y = py - p.Y;
            if (y <= 0 || y >= p.Sy) return false;
            float dx = px - p.X, dz = pz - p.Z, dy = y - 0.5f * p.Sy;
            float x = p.C * dx + p.S * dz - p.L * dy, z = p.C * dz - p.S * dx - p.B * dy;
            return Math.Abs(x) < 0.5f * p.Sx && Math.Abs(z) < 0.5f * (p.Sz - 2 * p.T * dy);
        }

        static void Occlude(ObjectMesh mesh, List<StructurePiece> pieces, List<int> starts)
        {
            int n = pieces.Count;
            if (n == 0) return;
            var lo = new float[n * 3];
            var hi = new float[n * 3];
            float cell = 0;
            for (int k = 0; k < n; k++)
            {
                var p = pieces[k];
                float r = 0.5f * (Math.Abs(p.C) + Math.Abs(p.S)) * Math.Max(p.Sx, p.Sz) + 0.5f * p.Sy * (Math.Abs(p.L) + Math.Abs(p.B));
                lo[3 * k] = p.X - r; hi[3 * k] = p.X + r;
                lo[3 * k + 1] = p.Y; hi[3 * k + 1] = p.Y + p.Sy;
                lo[3 * k + 2] = p.Z - r; hi[3 * k + 2] = p.Z + r;
                cell += Math.Max(p.Sx, Math.Max(p.Sy, p.Sz));
            }
            cell = Math.Max(2 * cell / n, 1e-3f);
            var grid = new Dictionary<long, List<int>>();
            for (int k = 0; k < n; k++)
                for (int gx = (int)Math.Floor(lo[3 * k] / cell); gx <= (int)Math.Floor(hi[3 * k] / cell); gx++)
                    for (int gz = (int)Math.Floor(lo[3 * k + 2] / cell); gz <= (int)Math.Floor(hi[3 * k + 2] / cell); gz++)
                    {
                        long c = ((long)gx << 32) ^ (uint)gz;
                        if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                        list.Add(k);
                    }
            var pos = mesh.Positions;
            var nor = mesh.Normals;
            var near = new List<int>();
            for (int k = 0; k < n; k++)
            {
                float span = pieces[k].A > 0 ? pieces[k].A : Math.Min(pieces[k].Sx, Math.Min(pieces[k].Sy, pieces[k].Sz)), reach = 0.9f * span;
                for (int v = starts[k]; v < starts[k + 1]; v++)
                {
                    float px = pos[3 * v], py = pos[3 * v + 1], pz = pos[3 * v + 2];
                    float nx = nor[3 * v], ny = nor[3 * v + 1], nz = nor[3 * v + 2];
                    float tx, ty, tz;
                    if (Math.Abs(ny) < 0.9f) { tx = nz; ty = 0; tz = -nx; }
                    else { tx = 0; ty = -nz; tz = ny; }
                    float tl = Math.Max((float)Math.Sqrt(tx * tx + ty * ty + tz * tz), 1e-6f);
                    tx /= tl; ty /= tl; tz /= tl;
                    float sx = ny * tz - nz * ty, sy = nz * tx - nx * tz, sz = nx * ty - ny * tx;
                    near.Clear();
                    for (int gx = (int)Math.Floor((px - reach) / cell); gx <= (int)Math.Floor((px + reach) / cell); gx++)
                        for (int gz = (int)Math.Floor((pz - reach) / cell); gz <= (int)Math.Floor((pz + reach) / cell); gz++)
                            if (grid.TryGetValue(((long)gx << 32) ^ (uint)gz, out var list))
                                foreach (int j in list)
                                    if (j != k && !near.Contains(j) && px + reach > lo[3 * j] && px - reach < hi[3 * j] && py + reach > lo[3 * j + 1] && py - reach < hi[3 * j + 1] && pz + reach > lo[3 * j + 2] && pz - reach < hi[3 * j + 2])
                                        near.Add(j);
                    float hit = 0, total = 0;
                    for (int d = 0; d < OccDirs.Length; d += 3)
                    {
                        float a = OccDirs[d], bt = OccDirs[d + 1], bs = OccDirs[d + 2];
                        float dx = a * nx + bt * tx + bs * sx, dy = a * ny + bt * ty + bs * sy, dz = a * nz + bt * tz + bs * sz;
                        for (int ri = 0; ri < OccRadii.Length; ri++)
                        {
                            float r = OccRadii[ri] * span, w = 1 - 0.4f * ri;
                            float qx = px + dx * r, qy = py + dy * r, qz = pz + dz * r;
                            total += w;
                            foreach (int j in near)
                                if (Inside(pieces[j], qx, qy, qz)) { hit += w; break; }
                        }
                    }
                    float open = (1 - hit / total) * (1 - 0.35f * Math.Max(0, -ny));
                    mesh.Tones[4 * v] = 2 * pieces[k].M + 0.5f + 0.5f * Math.Max(0, Math.Min(1, open));
                }
            }
        }

        static readonly float[] OccDirs =
        {
            1, 0, 0, 0.7f, 0.7f, 0, 0.7f, -0.7f, 0, 0.7f, 0, 0.7f, 0.7f, 0, -0.7f,
            0.2f, 1, 0, 0.2f, -1, 0, 0.2f, 0, 1, 0.2f, 0, -1,
        };
        static readonly float[] OccRadii = { 0.3f, 0.75f };

        static float[] Block(ulong key, bool slab, bool plain, out int[] idx, float wear = 0, float hx = 1, float hy = 1, float hz = 1)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x2C1B3C6D4E5F6071UL));
            var polys = blockPolys ??= new List<float[]>();
            polys.Clear();
            for (int f = 0; f < 6; f++)
            {
                int ax = f >> 1, ua = (ax + 1) % 3, va = (ax + 2) % 3;
                float sg = (f & 1) == 0 ? 1 : -1;
                var q = new float[12];
                float[] cu = { -1, 1, 1, -1 }, cv = { -1, -1, 1, 1 };
                for (int k = 0; k < 4; k++)
                {
                    int c = sg > 0 ? k : 3 - k;
                    q[k * 3 + ax] = sg; q[k * 3 + ua] = cu[c]; q[k * 3 + va] = cv[c];
                }
                polys.Add(q);
            }
            for (int f = 0; f < 6; f++)
            {
                if (slab && f == 2) continue;
                var n = new float[3];
                int ax = f >> 1;
                float tilt = slab ? 0.05f : ax == 2 ? FaceTilt : 0.12f;
                for (int k = 0; k < 3; k++) n[k] = k == ax ? ((f & 1) == 0 ? 1 : -1) : k == 2 && !slab ? rng.Range(-FaceTilt, FaceTilt) : rng.Range(-tilt, tilt);
                float len = (float)Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                n[0] /= len; n[1] /= len; n[2] /= len;
                Clip(polys, n[0], n[1], n[2], Math.Abs(n[ax]) - Math.Abs(n[(ax + 1) % 3]) - Math.Abs(n[(ax + 2) % 3]));
            }
            int cuts = plain ? 0 : slab ? rng.Range(0, 2) : rng.Range(1, 4);
            for (int k = 0; k < cuts; k++)
            {
                float nx = rng.Range(-1f, 1f), ny = rng.Range(-0.6f, slab ? 1f : 0.6f), nz = slab ? rng.Range(-1f, 1f) : rng.Range(-0.15f, 0.15f);
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-3f) continue;
                nx /= len; ny /= len; nz /= len;
                Clip(polys, nx, ny, nz, (Math.Abs(nx) + Math.Abs(ny) + Math.Abs(nz)) * rng.Range(slab ? 0.92f : 0.84f, 0.97f));
            }
            if (wear > 0 && !plain) Wear(polys, key, wear, hx, hy, hz);
            var pos = blockPos ??= new List<float>();
            var ind = blockInd ??= new List<int>();
            pos.Clear(); ind.Clear();
            foreach (var poly in polys)
            {
                int first = pos.Count / 3, count = poly.Length / 3;
                pos.AddRange(poly);
                for (int k = 1; k + 1 < count; k++) { ind.Add(first); ind.Add(first + k); ind.Add(first + k + 1); }
            }
            idx = ind.ToArray();
            return pos.ToArray();
        }

        static void Wear(List<float[]> polys, ulong key, float wear, float hx, float hy, float hz)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x5BE0CD19137E2179UL));
            float[] h = { hx, hy, hz };
            float[] n = new float[3];
            float least = Math.Min(hx, Math.Min(hy, hz));
            for (int e = 0; e < 12; e++)
            {
                int c = e >> 2, a = (c + 1) % 3, b = (c + 2) % 3;
                float sa = (e & 1) == 0 ? 1 : -1, sb = (e & 2) == 0 ? 1 : -1;
                float r = Math.Min(MaxRound, wear * Math.Min(h[a], h[b]) * rng.Range(0.14f, 0.42f));
                if (sb > 0 && c != 1) r *= rng.Range(1f, 1.6f);
                for (int t = 0; t < 2; t++)
                {
                    float ca = t == 0 ? RoundCos : RoundSin, cb = t == 0 ? RoundSin : RoundCos;
                    ca *= rng.Range(0.85f, 1.15f); cb *= rng.Range(0.85f, 1.15f);
                    float rr = r * rng.Range(0.7f, 1.25f);
                    n[a] = sa * ca; n[b] = sb * cb; n[c] = rng.Range(-RoundSkew, RoundSkew);
                    Cut(polys, n, h, ca * (h[a] - rr) + cb * (h[b] - rr) + rr * (float)Math.Sqrt(ca * ca + cb * cb));
                }
            }
            int chips = (int)(wear * 4 + rng.NextFloat());
            for (int k = 0; k < chips; k++)
            {
                float lift = 0;
                for (int i = 0; i < 3; i++)
                {
                    float w = rng.Range(0.4f, 1.6f);
                    n[i] = (rng.NextFloat() < 0.5f ? 1 : -1) * w;
                    lift += w * h[i];
                }
                float len = (float)Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                Cut(polys, n, h, lift - len * wear * least * rng.Range(0.35f, 0.8f));
            }
        }

        static void Cut(List<float[]> polys, float[] n, float[] h, float d)
        {
            float ux = n[0] * h[0], uy = n[1] * h[1], uz = n[2] * h[2];
            float len = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (len < 1e-6f) return;
            Clip(polys, ux / len, uy / len, uz / len, d / len);
        }

        [ThreadStatic] static List<float> clipCap, clipOut, clipPts;
        [ThreadStatic] static int[] clipOrder;
        [ThreadStatic] static float[] clipAng;

        static void Clip(List<float[]> polys, float nx, float ny, float nz, float d)
        {
            var cap = clipCap ??= new List<float>();
            var outp = clipOut ??= new List<float>();
            var pts = clipPts ??= new List<float>();
            cap.Clear();
            pts.Clear();
            for (int pi = polys.Count - 1; pi >= 0; pi--)
            {
                var poly = polys[pi];
                int count = poly.Length / 3;
                outp.Clear();
                bool cut = false;
                for (int k = 0; k < count; k++)
                {
                    int a = k * 3, b = (k + 1) % count * 3;
                    float da = poly[a] * nx + poly[a + 1] * ny + poly[a + 2] * nz - d;
                    float db = poly[b] * nx + poly[b + 1] * ny + poly[b + 2] * nz - d;
                    if (da <= 0) { outp.Add(poly[a]); outp.Add(poly[a + 1]); outp.Add(poly[a + 2]); }
                    else cut = true;
                    if ((da <= 0) != (db <= 0))
                    {
                        cut = true;
                        float t = da / (da - db);
                        float x = poly[a] + t * (poly[b] - poly[a]), y = poly[a + 1] + t * (poly[b + 1] - poly[a + 1]), z = poly[a + 2] + t * (poly[b + 2] - poly[a + 2]);
                        outp.Add(x); outp.Add(y); outp.Add(z);
                        cap.Add(x); cap.Add(y); cap.Add(z);
                    }
                }
                if (!cut) continue;
                if (outp.Count >= 9) polys[pi] = outp.ToArray();
                else polys.RemoveAt(pi);
            }
            for (int k = 0; k < cap.Count; k += 3)
            {
                bool dup = false;
                for (int j = 0; j < pts.Count && !dup; j += 3)
                    dup = Math.Abs(pts[j] - cap[k]) + Math.Abs(pts[j + 1] - cap[k + 1]) + Math.Abs(pts[j + 2] - cap[k + 2]) < 1e-5f;
                if (!dup) { pts.Add(cap[k]); pts.Add(cap[k + 1]); pts.Add(cap[k + 2]); }
            }
            int m = pts.Count / 3;
            if (m < 3) return;
            float cx = 0, cy = 0, cz = 0;
            for (int k = 0; k < m; k++) { cx += pts[k * 3]; cy += pts[k * 3 + 1]; cz += pts[k * 3 + 2]; }
            cx /= m; cy /= m; cz /= m;
            float ex, ey, ez;
            if (Math.Abs(nx) < 0.9f) { ex = 0; ey = nz; ez = -ny; } else { ex = -nz; ey = 0; ez = nx; }
            float el = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
            ex /= el; ey /= el; ez /= el;
            float fx = ny * ez - nz * ey, fy = nz * ex - nx * ez, fz = nx * ey - ny * ex;
            if (clipOrder == null || clipOrder.Length < m) { clipOrder = new int[Math.Max(m, 32)]; clipAng = new float[clipOrder.Length]; }
            var order = clipOrder;
            var ang = clipAng;
            for (int k = 0; k < m; k++)
            {
                float px = pts[k * 3] - cx, py = pts[k * 3 + 1] - cy, pz = pts[k * 3 + 2] - cz;
                float u = px * ex + py * ey + pz * ez, v = px * fx + py * fy + pz * fz;
                float r = Math.Abs(u) + Math.Abs(v);
                float g = r < 1e-9f ? 0 : v / r;
                ang[k] = u >= 0 ? (v >= 0 ? g : 4 + g) : 2 - g;
                order[k] = k;
            }
            Array.Sort(ang, order, 0, m);
            var c2 = new float[m * 3];
            for (int k = 0; k < m; k++)
            {
                c2[k * 3] = pts[order[k] * 3]; c2[k * 3 + 1] = pts[order[k] * 3 + 1]; c2[k * 3 + 2] = pts[order[k] * 3 + 2];
            }
            polys.Add(c2);
        }

        static float[] Bounds(float[] pos)
        {
            var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int v = 0; v < pos.Length; v++) { min[v % 3] = Math.Min(min[v % 3], pos[v]); max[v % 3] = Math.Max(max[v % 3], pos[v]); }
            var u = new float[pos.Length];
            for (int v = 0; v < pos.Length; v++) u[v] = (2 * pos[v] - min[v % 3] - max[v % 3]) / Math.Max(1e-4f, max[v % 3] - min[v % 3]);
            return u;
        }

        static void Add(StructureLayout l, ulong key, float x, float y, float z, float sx, float sy, float sz, float c, float s, int lo, int hi, float q = Squareness, float lean = 0, float batter = 0, float taper = 0)
        {
            ulong k = Rng.Mix(key ^ ((ulong)(l.Pieces.Count + 1) * 0x9E3779B97F4A7C15UL));
            l.Pieces.Add(new StructurePiece { Key = k, X = x, Y = y, Z = z, Sx = sx, Sy = sy, Sz = sz, C = c, S = s, L = lean, Q = q, B = batter, T = taper, Lo = lo, Hi = hi });
        }

        static void Yaw(ref Rng rng, float range, out float c, out float s)
        {
            s = rng.Range(-range, range);
            c = (float)Math.Sqrt(1 - s * s);
        }

        static float WallTop(StructureLayout l, float x)
        {
            float top = 0;
            foreach (var p in l.Pieces)
                if (p.Lo == 0 && (p.Hi >= 1 || p.Y >= 0) && Math.Abs(x - p.X) <= 0.5f * p.Sx) top = Math.Max(top, p.Y + p.Sy);
            return top;
        }

        static void Wall(ulong key, StructureLayout l)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x3C6EF372FE94F82BUL));
            float h = WallHeight * rng.Range(0.8f, 1.15f);
            var courses = new List<float>();
            float y = 0, rh = CourseRow;
            while (h - y > 0.012f)
            {
                rh *= CourseDecay;
                float next = (CourseRow - rh) / CourseShrink;
                if (h - next < 0.6f * (next - y)) next = h;
                courses.Add(y);
                y = next;
            }
            courses.Add(h);
            int count = courses.Count - 1;
            float stepL = rng.Range(0f, 0.008f), stepR = rng.Range(0f, 0.008f);
            bool breach = count > 2 && rng.NextFloat() < 0.25f;
            float bx = rng.Range(-0.25f, 0.25f), bw = rng.Range(0.03f, 0.07f);
            int bBase = rng.Range(1, Math.Max(2, count - 1));
            float slope = 0.5f * WallBase * WallBatter / h;
            for (int j = 0; j < count; j++)
            {
                float yb = courses[j], ch = courses[j + 1] - yb;
                float a = -0.48f + stepL * j, e = 0.48f - stepR * j;
                if (e - a < 0.05f) continue;
                float w = WallBase * (1 - WallBatter * (yb + 0.5f * ch) / h);
                float ga = 1, gb = -1;
                if (breach && j >= bBase)
                {
                    float half = bw * (1 + 0.8f * (j - bBase));
                    ga = bx - half;
                    gb = bx + half;
                    Run(l, key, ref rng, j, a, Math.Min(e, ga), yb, ch, w, slope, true, e <= ga);
                    Run(l, key, ref rng, j, Math.Max(a, gb), e, yb, ch, w, slope, a >= gb, true);
                }
                else Run(l, key, ref rng, j, a, e, yb, ch, w, slope, true, true);
                if (j == count - 1) Cope(l, key, ref rng, ga, gb, a, e, h, WallBase * (1 - WallBatter));
            }
            int tumble = rng.Range(2, 7) + (breach ? 4 : 0);
            for (int k = 0; k < tumble; k++)
            {
                float tx = breach && k < 4 ? bx + rng.Range(-0.08f, 0.08f) : rng.Range(-0.45f, 0.45f);
                float side = rng.NextFloat() < 0.5f ? -1f : 1f, s = rng.Range(0.025f, 0.045f);
                float tz = side * (0.5f * WallBase + rng.Range(0.01f, 0.06f));
                float sy = s * rng.Range(0.6f, 0.9f);
                int dir = rng.Range(0, 16);
                Add(l, key, tx, -0.2f * sy, tz, s * rng.Range(1.2f, 1.8f), sy, s, Cos16(dir), Sin16(dir), 0, 1);
            }
            for (float x = -0.5f; x <= 0.5f + 1e-4f; x += WallColumnStep)
            {
                float top = WallTop(l, x);
                if (top > 0.01f) l.Columns.Add(new ObjectColumn { X = x, Z = 0, R = 0.5f * WallBase, Bottom = 0, Top = top });
            }
        }

        public const float HeadLong = 0.13f, HeadWide = 0.17f;

        static void WallHead(ulong key, StructureLayout l)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x5851F42D4C957F2DUL));
            float h = rng.Range(0.29f, 0.32f), cap = rng.Range(0.026f, 0.034f), body = h - cap;
            int count = rng.Range(4, 6);
            float y = 0, step = body / count;
            for (int j = 0; j < count; j++)
            {
                float ch = j == count - 1 ? body - y : step * rng.Range(0.9f, 1.1f);
                float shrink = 1 - 0.06f * (y + 0.5f * ch) / body + (j == 0 ? 0.04f : 0);
                float sx = HeadLong * shrink, sz = HeadWide * shrink, cut = rng.Range(0.42f, 0.58f);
                bool across = ((j + (int)(key & 1)) & 1) == 0;
                for (int b = 0; b < 2; b++)
                {
                    float share = b == 0 ? cut : 1 - cut, off = b == 0 ? -0.5f * (1 - cut) : 0.5f * cut;
                    Yaw(ref rng, FaceYaw, out float c, out float s);
                    float bx = across ? 0 : off * sx, bz = across ? off * sz : 0;
                    float lx = across ? sx : share * sx - 0.002f, lz = across ? share * sz - 0.002f : sz;
                    Add(l, key, bx + rng.Range(-0.002f, 0.002f), y, bz + rng.Range(-0.002f, 0.002f), lx, ch * rng.Range(0.97f, 1f), lz, c, s, 0, 1, StoneSquare);
                }
                y += ch;
            }
            Add(l, key, 0, 0, 0, HeadLong, body, HeadWide, 1, 0, 2, 2, StoneSquare, 0, 0, 0.03f * HeadWide / body);
            Yaw(ref rng, 0.03f, out float cc, out float cs);
            Add(l, key, 0, body - 0.002f, 0, HeadLong + 0.02f, cap, HeadWide + 0.02f, cc, cs, 0, 2, SlabSquare, rng.Range(-0.04f, 0.04f));
            Calm(l);
            l.Columns.Add(new ObjectColumn { X = 0, Z = 0, R = 0.5f * HeadWide, Bottom = 0, Top = h });
        }

        const float CopeCalm = 0.67f;
        const float Unworn = 0.985f, WornSpan = 0.075f, MaxRound = 0.09f, RoundCos = 0.92388f, RoundSin = 0.38268f, RoundSkew = 0.12f;
        const float StoneSquare = 0.98f, SlabSquare = 0.94f, Faceted = 0.9f, FaceTilt = 0.025f, FaceYaw = 0.012f, Proud = 0.003f;
        const float QuoinLong = 0.05f, QuoinShort = 0.62f, QuoinProud = 0.004f, QuoinSquare = 0.99f;
        const float CourseRow = 0.038f, CourseShrink = 0.055f, CourseDecay = 0.9464851f;

        static void Run(StructureLayout l, ulong key, ref Rng rng, int j, float a, float e, float yb, float ch, float w, float slope, bool endA, bool endB)
        {
            float span = e - a;
            if (span < 0.004f) return;
            Add(l, key, 0.5f * (a + e), yb, 0, span, ch, w, 1, 0, 0, Tiers - 1, 1, 0, 0, slope);
            var body = l.Pieces[l.Pieces.Count - 1];
            body.Coursed = true;
            l.Pieces[l.Pieces.Count - 1] = body;
            for (int side = -1; side <= 1; side += 2)
            {
                float len = ch * rng.Range(1.1f, 2.4f) * ((j & 1) == 1 ? 0.7f : 1f);
                if (side < 0 ? endA : endB)
                {
                    float edge = side < 0 ? -0.48f - Proud : 0.48f + Proud;
                    float ql = Math.Min(QuoinLong * (((j + (side + 1) / 2) & 1) == 0 ? 1f : QuoinShort), 0.45f * (e - a)) + (side < 0 ? a + 0.48f : 0.48f - e);
                    Add(l, key, edge - side * 0.5f * ql, yb, 0, ql, ch, w + 2 * QuoinProud, 1, 0, 0, 1, QuoinSquare, 0, 0, slope);
                    continue;
                }
                if (span < 2.4f * ch) { if (side > 0) break; len = span; }
                else len = Math.Min(len, 0.45f * span);
                float xc = side < 0 ? a - Proud + 0.5f * len : e + Proud - 0.5f * len;
                float sy = ch * rng.Range(0.92f, 1.06f), y0 = yb - 0.04f * ch;
                float sz = w - 2 * slope * (y0 + 0.5f * sy - yb - 0.5f * ch) + 2 * Proud;
                Yaw(ref rng, FaceYaw, out float c, out float s);
                Add(l, key, xc, y0, 0, len + (span < 2.4f * ch ? 2 * Proud : 0), sy, sz, c, s, 0, 1, StoneSquare, 0, 0, slope);
            }
        }

        static void Cope(StructureLayout l, ulong key, ref Rng rng, float gapA, float gapB, float a, float e, float h, float w)
        {
            float t = a, lean = rng.Range(-0.12f, 0.12f), tall = rng.Range(0.036f, 0.046f);
            while (e - t > 0.012f)
            {
                lean = Math.Max(-0.15f, Math.Min(0.15f, lean + rng.Range(-0.03f, 0.03f)));
                float th = rng.Range(0.012f, 0.02f), sy = tall * rng.Range(0.94f, 1.04f);
                float sz = w * rng.Range(0.9f, 1.05f), xc = t + 0.5f * th;
                Yaw(ref rng, 0.04f, out float c, out float s);
                if (xc < gapA - 0.5f * th || xc > gapB + 0.5f * th)
                {
                    Add(l, key, xc, h - 0.004f, rng.Range(-0.002f, 0.002f), th, sy, sz, c, s, 0, 1, SlabSquare, lean);
                    Calm(l);
                }
                t += th * rng.Range(0.97f, 1.03f);
            }
            float ea = gapA < gapB ? Math.Min(e, gapA) : e;
            if (ea - a > 0.012f) { Add(l, key, 0.5f * (a + ea), h - 0.004f, 0, ea - a, 0.97f * tall, w, 1, 0, 2, 2, StoneSquare); Calm(l); }
            float eb = Math.Max(a, gapB);
            if (gapA < gapB && e - eb > 0.012f) { Add(l, key, 0.5f * (eb + e), h - 0.004f, 0, e - eb, 0.97f * tall, w, 1, 0, 2, 2, StoneSquare); Calm(l); }
        }

        static void Calm(StructureLayout l)
        {
            var p = l.Pieces[l.Pieces.Count - 1];
            p.Calm = CopeCalm;
            l.Pieces[l.Pieces.Count - 1] = p;
        }

        static void Flat(StructureLayout l, int before)
        {
            for (int k = before; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                p.Flat = true;
                l.Pieces[k] = p;
            }
        }

        static void Mossy(StructureLayout l, int before)
        {
            for (int k = before; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                p.Mossy = true;
                l.Pieces[k] = p;
            }
        }

        static void Drape(StructureLayout l, int before)
        {
            for (int k = before; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                p.Drape = true;
                l.Pieces[k] = p;
            }
        }

        static void Cairn(ulong key, StructureLayout l) => Cairn(l, key, Frame.Root(1));

        static void Cairn(StructureLayout l, ulong key, in Frame f)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x7F4A7C159E3779B9UL));
            int layers = rng.Range(4, 7);
            float r = rng.Range(0.3f, 0.34f), s = rng.Range(0.18f, 0.22f), y = 0;
            for (int j = 0; j < layers; j++)
            {
                float sy = s * rng.Range(0.55f, 0.75f);
                int count = Math.Max(3, Math.Min(16, (int)(6.2831853f * r / (s * 0.95f) + 0.5f)));
                int off = rng.Range(0, 16);
                for (int i = 0; i < count; i++)
                {
                    int k = off + i * 16 / count;
                    float dx = Cos16(k), dz = Sin16(k), rr = r * rng.Range(0.92f, 1.06f);
                    Hewn(l, key, f, dx * rr, y, dz * rr, s * rng.Range(1f, 1.35f), sy * rng.Range(0.9f, 1.1f), s * rng.Range(0.75f, 0.95f), -dz, dx, 0, 1, Squareness);
                }
                Hewn(l, key, f, 0, y, 0, 1.8f * r, 0.9f * sy, 1.8f * r, 1, 0, 0, 1, Squareness);
                int pd = rng.Range(0, 16);
                Hewn(l, key, f, 0, y, 0, 2 * r + 0.9f * s, sy, 2 * r + 0.9f * s, Cos16(pd), Sin16(pd), 2, 2, Squareness);
                Col(l, f, 0, 0, r + 0.4f * s, 0, y + sy);
                y += 0.82f * sy;
                r *= 0.72f;
                s *= 0.86f;
            }
            bool standing = rng.NextFloat() < 0.5f;
            float tx = s * (standing ? 0.7f : 1.5f), tz = s * (standing ? 0.5f : 1.3f), ty = s * (standing ? 1.8f : 0.5f);
            int td = rng.Range(0, 16);
            Hewn(l, key, f, 0, y, 0, tx, ty, tz, Cos16(td), Sin16(td), 0, 2, Squareness);
            Col(l, f, 0, 0, 0.5f * Math.Min(tx, tz), 0, y + ty);
            int loose = rng.Range(3, 7);
            for (int k = 0; k < loose; k++)
            {
                int dir = rng.Range(0, 16), yd = rng.Range(0, 16);
                float rr = rng.Range(0.38f, 0.43f), ss = rng.Range(0.05f, 0.09f), sy = 0.7f * ss;
                Hewn(l, key, f, Cos16(dir) * rr, -0.2f * sy, Sin16(dir) * rr, 1.4f * ss, sy, ss, Cos16(yd), Sin16(yd), 0, 0, Squareness);
            }
        }
    }
}
