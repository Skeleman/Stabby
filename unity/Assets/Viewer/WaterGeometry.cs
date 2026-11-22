using System.Collections.Generic;
using Stabby.World;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Viewer
{
    public sealed class MeshParts
    {
        public string Name;
        public Vector3[] Verts, Norms;
        public Color[] Cols;
        public Vector2[] Uvs;
        public Vector4[] Uv1, Uv2;
        public int[] Tris;
    }

    public struct FallCurtain
    {
        public int K;
        public float LipX, LipZ, LipY;
        public float BaseX, BaseZ, BaseY;
        public float Width;
        public float DirX, DirZ;
        public float PoolRadius;
        public float Discharge;
        public float Drop => LipY - BaseY;
    }

    public static class WaterGeometry
    {
        const int RiverPieceVertices = 16000;
        public const float RiverLift = 0.1f, LakeLift = 0.5f;
        const float SlopeRange = 0.1f;
        const int SlopeReach = 8, TurnReach = 6, PlungeReach = 16;
        public const float FallMinDrop = 0.3f;
        const float FallMargin = 0.3f, FallThrow = 1.5f, FallLipMin = 0.2f, FallLipMax = 0.75f, FallScour = 1.9f, FallScourDrop = 0.225f, FallScourQ = 0.54f, FallPoolMin = 0.6f, FallPoolMax = 2f, FallDischargePerArea = 2f, FallSink = 0.5f, FootGap = 0.5f, FootMax = 0.95f, PoolSpread = 2f, MistPerWidth = 7f, MistPerHeight = 10f, MistPerFlow = 0.5f, MistPerPool = 0.5f, MistPerDrop = 0.25f, MistPhase = 0.618034f, SprayWidth = 0.8f, SprayLength = 1.1f, SprayLift = 0.4f, SprayAhead = 0.6f, VeilWidth = 0.575f, VeilHeight = 0.34f, VeilAhead = 1f;
        const int CurtainRowsMin = 7, CurtainRowsMax = 24, MistMax = 6;
        const int CurtainColsMin = 2, CurtainColsMax = 9;
        const float CurtainRowStep = 2f, CurtainColStep = 4f, HugClear = 0.4f, HugRamp = 2f, HugClearPerM = 0.04f, HugUnder = 0.3f, HugReach = 2f, HugReachPerM = 0.6f, HugStep = 0.5f, HugSteps = 12f, HugGap = 0.3f, HugBank = 0.3f;
        static readonly Vector4 NoPool = new Vector4(-1e5f, 0, 0, 0);
        static readonly Color SeaKind = new Color(0.5f, 0.5f, 0, 1), LakeKind = new Color(0.5f, 0.5f, 0, 0.5f);

        public static Mesh ToMesh(MeshParts d)
        {
            var mesh = new Mesh { name = d.Name, indexFormat = d.Verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(d.Verts);
            mesh.SetNormals(d.Norms);
            mesh.SetColors(d.Cols);
            if (d.Uvs != null) mesh.SetUVs(0, d.Uvs);
            if (d.Uv1 != null) mesh.SetUVs(1, d.Uv1);
            if (d.Uv2 != null) mesh.SetUVs(2, d.Uv2);
            mesh.SetTriangles(d.Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static bool FallAt(RiverLine r, int k, out FallCurtain f, out float lipT)
        {
            f = default;
            lipT = 0;
            if (k < 1 || k >= r.X.Length) return false;
            int at = r.Lip == null ? -1 : System.Array.IndexOf(r.Lip, k - 1);
            if (at >= 0)
            {
                var rf = r.Falls[at];
                f = new FallCurtain
                {
                    K = k - 1, LipX = rf.LipX, LipZ = rf.LipZ, LipY = rf.LipY + RiverLift,
                    BaseX = rf.LipX + rf.DirX * FallThrowAt(rf.Drop), BaseZ = rf.LipZ + rf.DirZ * FallThrowAt(rf.Drop), BaseY = rf.BaseY + RiverLift,
                    Width = 2 * Mathf.Max(0.5f * rf.Width, 6f), DirX = rf.DirX, DirZ = rf.DirZ,
                    PoolRadius = rf.PoolRadius, Discharge = rf.Discharge,
                };
                return true;
            }
            if (r.P[k] <= 0) return false;
            float drop = r.S[k - 1] - r.S[k];
            var d = new Vector2(r.X[k] - r.X[k - 1], r.Z[k] - r.Z[k - 1]);
            if (drop < FallMinDrop || d.sqrMagnitude < 1e-6f) return false;
            lipT = Mathf.Clamp((drop + FallMargin - r.D[k - 1] - r.P[k - 1]) / Mathf.Max(drop + r.P[k] - r.P[k - 1], 1e-3f), FallLipMin, FallLipMax);
            d.Normalize();
            float lx = Mathf.Lerp(r.X[k - 1], r.X[k], lipT), lz = Mathf.Lerp(r.Z[k - 1], r.Z[k], lipT), th = FallThrowAt(drop), w = Mathf.Lerp(r.W[k - 1], r.W[k], lipT);
            float width = 2 * Mathf.Max(w, 6f), discharge = FallDischargePerArea * w * Mathf.Lerp(r.D[k - 1], r.D[k], lipT);
            f = new FallCurtain
            {
                K = k - 1, LipX = lx, LipZ = lz, LipY = r.S[k - 1] + RiverLift,
                BaseX = lx + d.x * th, BaseZ = lz + d.y * th, BaseY = r.S[k] + RiverLift,
                Width = width, DirX = d.x, DirZ = d.y,
                PoolRadius = PoolRadiusOf(drop, width, discharge),
                Discharge = discharge,
            };
            return true;
        }

        public static float PoolRadiusOf(float drop, float width, float discharge)
        {
            float scour = FallScour * Mathf.Pow(Mathf.Max(drop, 0), FallScourDrop) * Mathf.Pow(Mathf.Max(discharge, 0) / width, FallScourQ);
            return Mathf.Clamp(0.5f * width + scour, FallPoolMin * width, FallPoolMax * width);
        }

        public static int CurtainRowsOf(float height) => Mathf.Clamp(Mathf.RoundToInt(height / CurtainRowStep), CurtainRowsMin, CurtainRowsMax);

        public static float FallThrowAt(float height) => FallThrow * Mathf.Sqrt(2 * Mathf.Max(height, 0) / 9.81f);

        public static int CurtainColsOf(float width) => Mathf.Clamp(Mathf.RoundToInt(width / CurtainColStep) + 1, CurtainColsMin, CurtainColsMax);

        public static float[] CurtainOffsets(FallCurtain f, TerrainDetail detail)
        {
            float h = f.Drop + FallSink, half = 0.5f * f.Width;
            int rows = CurtainRowsOf(h), cols = CurtainColsOf(f.Width);
            var off = new float[rows * cols];
            for (int j = 0; j < rows; j++)
            {
                float u = j / (rows - 1f), y = u * u * h;
                for (int c = 0; c < cols; c++)
                {
                    float sc = (2f * c / (cols - 1) - 1) * half, from = j > 0 ? off[(j - 1) * cols + c] : 0;
                    off[j * cols + c] = HugAt(detail, f, f.LipX + f.DirZ * sc, f.LipZ - f.DirX * sc, y, from);
                }
            }
            var tmp = new float[rows * cols];
            Swell(off, tmp, rows, cols, cols, 1);
            Swell(off, tmp, cols, rows, 1, cols);
            for (int c = 0; c < cols; c++) off[c] = 0;
            return off;
        }

        static float HugAt(TerrainDetail detail, FallCurtain f, float x, float z, float y, float from)
        {
            float free = FallThrowAt(y), start = Mathf.Max(free, from);
            if (detail == null || y <= 0) return start;
            float floor = Mathf.Max(f.LipY - y - HugClear * Mathf.Clamp01(y / HugRamp) - HugClearPerM * y, f.BaseY - HugUnder);
            float ground = detail.Sample(x + f.DirX * start, z + f.DirZ * start);
            if (ground <= floor || ground > f.LipY + HugBank) return start;
            float far = free + HugReachPerM * y + HugReach, step = Mathf.Max(HugStep, (far - start) / HugSteps), prev = start;
            for (float t = start + step; t <= far + 1e-3f; t += step)
            {
                if (detail.Sample(x + f.DirX * t, z + f.DirZ * t) > floor) { prev = t; continue; }
                float lo = prev, hi = t;
                for (int k = 0; k < 4; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (detail.Sample(x + f.DirX * mid, z + f.DirZ * mid) > floor) lo = mid;
                    else hi = mid;
                }
                return hi + HugGap;
            }
            return start;
        }

        static void Swell(float[] a, float[] tmp, int lines, int len, int lineStride, int step)
        {
            for (int l = 0; l < lines; l++)
            {
                int o = l * lineStride;
                for (int i = 0; i < len; i++)
                {
                    float m = a[o + i * step];
                    if (i > 0) m = Mathf.Max(m, a[o + (i - 1) * step]);
                    if (i < len - 1) m = Mathf.Max(m, a[o + (i + 1) * step]);
                    tmp[o + i * step] = m;
                }
                for (int i = 0; i < len; i++)
                {
                    float p = tmp[o + Mathf.Max(i - 1, 0) * step], q = tmp[o + Mathf.Min(i + 1, len - 1) * step];
                    a[o + i * step] = 0.25f * (p + 2 * tmp[o + i * step] + q);
                }
            }
        }

        public static MeshParts Lakes(GeneratedWorld world, int cx, int cy, int cells)
        {
            int n = world.Config.Size;
            float cm = world.Config.CellMeters, half = cm * 0.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = cy; y < Mathf.Min(cy + cells, n); y++)
                for (int x = cx; x < Mathf.Min(cx + cells, n); x++)
                {
                    float h = LakeHeight(world, x, y);
                    if (h == float.MinValue) continue;
                    int b = verts.Count;
                    verts.Add(new Vector3(x * cm - half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm - half, h, y * cm + half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm - half));
                    verts.Add(new Vector3(x * cm + half, h, y * cm + half));
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 3);
                }
            return verts.Count == 0 ? null : Flat($"Water {cx},{cy}", verts, null, tris, LakeKind);
        }

        public static float LakeHeight(GeneratedWorld world, int x, int y)
        {
            int n = world.Config.Size, c = y * n + x;
            var hy = world.Water;
            if (world.Sea[c]) return float.MinValue;
            if (hy.LakeId[c] >= 0) return Mathf.Max(hy.WaterSurface[c], world.Height[c]) + LakeLift;
            float h = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Stabby.World.Grid.DX[k], ny = y + Stabby.World.Grid.DY[k];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                int o = ny * n + nx;
                if (!world.Sea[o] && hy.LakeId[o] >= 0 && world.Height[c] >= hy.WaterSurface[o]) h = Mathf.Max(h, hy.WaterSurface[o] + LakeLift);
            }
            return h;
        }

        sealed class RiverBuffers
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Color> Cols = new List<Color>();
            public readonly List<Vector2> Chan = new List<Vector2>();
            public readonly List<Vector4> Reach = new List<Vector4>(), Fall = new List<Vector4>();
            public readonly List<int> Tris = new List<int>();

            public void Clear() { Verts.Clear(); Cols.Clear(); Chan.Clear(); Reach.Clear(); Fall.Clear(); Tris.Clear(); }

            public void Row(Vector3 p, Vector3 dir, float half, float slope, Vector4 reach, Vector4 fall, bool join)
            {
                var side = new Vector3(dir.z, 0, -dir.x) * half;
                Verts.Add(p - side);
                Verts.Add(p + side);
                var flow = new Color(dir.x * 0.5f + 0.5f, dir.z * 0.5f + 0.5f, slope, 0);
                Cols.Add(flow);
                Cols.Add(flow);
                Chan.Add(new Vector2(-1, half));
                Chan.Add(new Vector2(1, half));
                Reach.Add(reach);
                Reach.Add(reach);
                Fall.Add(fall);
                Fall.Add(fall);
                if (!join) return;
                int v = Verts.Count - 2;
                Tris.Add(v - 2); Tris.Add(v); Tris.Add(v - 1);
                Tris.Add(v - 1); Tris.Add(v); Tris.Add(v + 1);
            }

            public float Curtain(FallCurtain f, float along, TerrainDetail detail)
            {
                float h = f.Drop + FallSink, half = 0.5f * f.Width;
                var fwd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(f.DirZ, 0, -f.DirX) * half;
                var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
                var flow = new Color(f.DirX * 0.5f + 0.5f, f.DirZ * 0.5f + 0.5f, 1, 0);
                var reach = new Vector4(f.Discharge, f.PoolRadius, along, 0);
                int b = Verts.Count, rows = CurtainRowsOf(h), cols = CurtainColsOf(f.Width);
                var off = CurtainOffsets(f, detail);
                for (int j = 0; j < rows; j++)
                {
                    float u = j / (rows - 1f), y = u * u * h;
                    var fv = new Vector4(y / h, h, along, 1);
                    for (int c = 0; c < cols; c++)
                    {
                        float sc = 2f * c / (cols - 1) - 1;
                        Verts.Add(lip + side * sc + fwd * off[j * cols + c] - Vector3.up * y);
                        Cols.Add(flow);
                        Chan.Add(new Vector2(sc, half));
                        Reach.Add(reach);
                        Fall.Add(fv);
                        if (j == 0 || c == 0) continue;
                        int v = b + j * cols + c, a = v - cols;
                        Tris.Add(a - 1); Tris.Add(v - 1); Tris.Add(a);
                        Tris.Add(a); Tris.Add(v - 1); Tris.Add(v);
                        Tris.Add(a - 1); Tris.Add(a); Tris.Add(v - 1);
                        Tris.Add(a); Tris.Add(v); Tris.Add(v - 1);
                    }
                }
                float shift = 0;
                for (int c = 0; c < cols; c++) shift += off[(rows - 1) * cols + c];
                shift = Mathf.Max(0, shift / cols - FallThrowAt(h));
                var push = fwd * shift;
                int mists = Mathf.Clamp(Mathf.RoundToInt(f.Width / MistPerWidth + f.Drop / MistPerHeight + MistPerFlow * Mathf.Sqrt(f.Discharge)), 1, MistMax);
                float radius = Mathf.Clamp(MistPerPool * f.PoolRadius + MistPerDrop * f.Drop, 2.5f, 14f);
                var foot = new Vector3(f.BaseX, f.BaseY, f.BaseZ) + push + fwd * (0.3f * radius);
                for (int m = 0; m < mists; m++)
                {
                    var c = foot + side * ((m + 0.5f) / mists * 2 - 1) * 0.7f;
                    float phase = Mathf.Repeat(m * MistPhase + along * 0.0137f, 1f);
                    Sheet(c, Vector3.zero, Vector3.zero, flow, new Vector2(phase, f.Drop), reach, radius, 2);
                }
                float seed = Mathf.Repeat(along * 0.0137f, 1f), spread = SprayWidth * radius, length = SprayLength * radius;
                var spray = new Vector3(f.BaseX, f.BaseY + SprayLift, f.BaseZ) + push + fwd * (SprayAhead * radius);
                Sheet(spray, side.normalized * spread, fwd * length, flow, new Vector2(seed, length / spread), reach, radius, 3);
                float veilHalf = VeilWidth * half, veilTall = h * VeilHeight * 0.5f;
                var veil = new Vector3(f.BaseX, f.BaseY + veilTall, f.BaseZ) + push + fwd * VeilAhead;
                Sheet(veil, side.normalized * veilHalf, Vector3.up * veilTall, flow, new Vector2(seed + 0.5f, veilTall / veilHalf), reach, Mathf.Max(veilHalf, veilTall), 4);
                return shift;
            }

            void Sheet(Vector3 c, Vector3 ax, Vector3 ay, Color flow, Vector2 chan, Vector4 reach, float radius, int kind)
            {
                int q = Verts.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float x = (corner & 1) * 2 - 1, y = (corner >> 1) * 2 - 1;
                    Verts.Add(c + ax * x + ay * y);
                    Cols.Add(flow);
                    Chan.Add(chan);
                    Reach.Add(reach);
                    Fall.Add(new Vector4(x, y, radius, kind));
                }
                Tris.Add(q); Tris.Add(q + 2); Tris.Add(q + 1);
                Tris.Add(q + 1); Tris.Add(q + 2); Tris.Add(q + 3);
                Tris.Add(q); Tris.Add(q + 1); Tris.Add(q + 2);
                Tris.Add(q + 1); Tris.Add(q + 3); Tris.Add(q + 2);
            }

            public MeshParts Piece(string name)
            {
                var m = Flat(name, Verts, Cols, Tris, default);
                m.Uvs = Chan.ToArray();
                m.Uv1 = Reach.ToArray();
                m.Uv2 = Fall.ToArray();
                return m;
            }
        }

        public static List<MeshParts> Rivers(GeneratedWorld world, TerrainDetail detail)
        {
            var pieces = new List<MeshParts>();
            var buf = new RiverBuffers();
            foreach (var r in NearField.Lines(world))
            {
                if (buf.Verts.Count >= RiverPieceVertices)
                {
                    pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
                    buf.Clear();
                }
                var pts = new List<Vector4>();
                for (int k = 0; k < r.X.Length; k++)
                    pts.Add(new Vector4(r.X[k], r.S[k], r.Z[k], 2 * r.W[k]));
                int last = pts.Count - 1;
                var slopes = new float[pts.Count];
                for (int k = 0; k < pts.Count; k++)
                {
                    var u = pts[Mathf.Max(k - SlopeReach, 0)];
                    var d = pts[Mathf.Min(k + SlopeReach, last)];
                    float run = new Vector2(d.x - u.x, d.z - u.z).magnitude;
                    slopes[k] = Mathf.Clamp01((run > 0 ? Mathf.Max(0, u.y - d.y) / run : 0) / SlopeRange);
                }
                float s = 0;
                var pool = NoPool;
                for (int k = 0; k < pts.Count; k++)
                {
                    var a = pts[Mathf.Max(k - 1, 0)];
                    var z = pts[Mathf.Min(k + 1, last)];
                    var dir = new Vector3(z.x - a.x, 0, z.z - a.z).normalized;
                    float half = Mathf.Max(pts[k].w * 0.5f, 6f), seg = 0;
                    var p = new Vector3(pts[k].x, 0, pts[k].z);
                    if (k > 0) s += seg = new Vector2(p.x - pts[k - 1].x, p.z - pts[k - 1].z).magnitude;
                    p.y = pts[k].y + RiverLift;
                    var i0 = pts[Mathf.Max(k - TurnReach, 0)];
                    var i1 = pts[Mathf.Min(k + TurnReach, last)];
                    float ix = p.x - i0.x, iz = p.z - i0.z, ox = i1.x - p.x, oz = i1.z - p.z;
                    float li = Mathf.Sqrt(ix * ix + iz * iz), lo = Mathf.Sqrt(ox * ox + oz * oz);
                    float bend = li * lo > 0 ? (ix * oz - iz * ox) / (li * lo) / (0.5f * (li + lo)) : 0;
                    float plunge = 0;
                    for (int j = Mathf.Max(k - PlungeReach, 0); j <= k; j++)
                        plunge = Mathf.Max(plunge, slopes[j] * (1 - (k - j) / (float)(PlungeReach + 1)));
                    if (pool.x > NoPool.x) half = Mathf.Max(half, pool.z * (1 - Mathf.Clamp01((s - pool.x) / (PoolSpread * pool.z))));
                    var rv = new Vector4(Mathf.Clamp(bend * half * 3, -1, 1), plunge, s, 0);
                    bool join = k > 0;
                    if (k > 0 && FallAt(r, k, out var f, out float lt))
                    {
                        var fd = new Vector3(f.DirX, 0, f.DirZ);
                        var prev = new Vector3(pts[k - 1].x, 0, pts[k - 1].z);
                        float sl = s - seg * (1 - lt), th = FallThrowAt(f.Drop), ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                        var lipRow = new Vector3(f.LipX, f.LipY, f.LipZ);
                        buf.Row(lipRow, fd, 0.5f * f.Width, slopes[k - 1], new Vector4(rv.x, rv.y, sl, 0), pool, true);
                        th += buf.Curtain(f, sl, detail);
                        ft = Mathf.Min(lt + (th + FootGap) / Mathf.Max(seg, 1e-3f), FootMax);
                        pool = new Vector4(sl + th, f.Drop, f.PoolRadius, 0);
                        var footRow = Vector3.Lerp(prev, new Vector3(p.x, 0, p.z), ft);
                        footRow.y = f.BaseY;
                        buf.Row(footRow, fd, Mathf.Lerp(0.5f * f.Width, half, (ft - lt) / (1 - lt)), slopes[k], new Vector4(rv.x, rv.y, s - seg * (1 - ft), 0), pool, false);
                    }
                    buf.Row(p, dir, half, slopes[k], rv, pool, join);
                }
            }
            if (buf.Verts.Count > 0) pieces.Add(buf.Piece($"Rivers {pieces.Count}"));
            return pieces;
        }

        public static MeshParts Sea(int size, float cm)
        {
            float span = size * cm, pad = span * 1.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-pad, 0, -pad), new Vector3(-pad, 0, span + pad),
                new Vector3(span + pad, 0, -pad), new Vector3(span + pad, 0, span + pad),
            };
            return Flat("Sea", verts, null, new List<int> { 0, 1, 2, 2, 1, 3 }, SeaKind);
        }

        static MeshParts Flat(string name, List<Vector3> verts, List<Color> perVertex, List<int> tris, Color kind)
        {
            var cols = perVertex != null ? perVertex.ToArray() : new Color[verts.Count];
            var up = new Vector3[verts.Count];
            for (int k = 0; k < up.Length; k++) { up[k] = Vector3.up; if (perVertex == null) cols[k] = kind; }
            return new MeshParts { Name = name, Verts = verts.ToArray(), Norms = up, Cols = cols, Tris = tris.ToArray() };
        }
    }
}
