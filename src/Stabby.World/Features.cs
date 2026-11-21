using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct FeatureEdit
    {
        public const int Add = 0, Carve = 1;
        public const int Overhang = 0, Arch = 1, Cave = 2, CaveRoom = 3, Shapes = 4;

        public const float Blend = 0.5f;

        public bool MeshedIn(float stepM, float x0, float z0, float x1, float z1) =>
            R >= 0.75f * stepM && MaxX > x0 && MinX < x1 && MaxZ > z0 && MinZ < z1;

        public int Kind, Shape;
        public float Ax, Ay, Az, Bx, By, Bz, R;

        public float MinX => Math.Min(Ax, Bx) - R;
        public float MaxX => Math.Max(Ax, Bx) + R;
        public float MinY => Math.Min(Ay, By) - R;
        public float MaxY => Math.Max(Ay, By) + R;
        public float MinZ => Math.Min(Az, Bz) - R;
        public float MaxZ => Math.Max(Az, Bz) + R;

        public bool Contains(float x, float y, float z) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;

        public float Apply(float d, float x, float y, float z)
        {
            if (!Reach(x, y, z, out float dist)) return d;
            return Kind == Add ? Math.Max(d, R - dist) : Cut(d, Hole(dist), R * Blend);
        }

        public bool Reach(float x, float y, float z, out float dist)
        {
            dist = R;
            if (!Contains(x, y, z)) return false;
            float ex = Bx - Ax, ey = By - Ay, ez = Bz - Az, px = x - Ax, py = y - Ay, pz = z - Az;
            float ll = ex * ex + ey * ey + ez * ez;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
            float qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
            dist = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
            return dist < R;
        }

        public float Hole(float dist) => dist - (R - 1.25f * R * Blend);

        public static float Cut(float d, float c, float k)
        {
            float h = Math.Max(0f, Math.Min(1f, 0.5f + 0.5f * (c - d) / k));
            return c + (d - c) * h - k * h * (1 - h);
        }
    }

    public interface IEditList
    {
        int Count { get; }
        FeatureEdit At(int k);
    }

    public struct NoEdits : IEditList
    {
        public int Count => 0;
        public FeatureEdit At(int k) => default;
    }

    public struct EditArray : IEditList
    {
        public FeatureEdit[] Items;
        public int Count => Items == null ? 0 : Items.Length;
        public FeatureEdit At(int k) => Items[k];
    }

    public struct EditedSolid<TCols, TEdits> : ISolidField where TCols : struct, IColumnField where TEdits : struct, IEditList
    {
        public TCols Columns;
        public TEdits Edits;
        public float StepM;

        public float Density(int i, int j, int k) => Features.Apply(Columns, Edits, Columns.Height(i, k) - j * StepM, i, j, k, StepM);
        public bool Sharp(int i, int j, int k) => Features.Covers(Edits, i * StepM, j * StepM, k * StepM, StepM);
        public float Base(int i, int j, int k) => Features.Level(Columns, Columns.Height(i, k) - j * StepM, i, k, StepM);

        public float Edit(float d, float x, float y, float z) => Features.Edit(Edits, d, x, y, z);
    }

    public static class Features
    {
        public const int TileCells = 32;
        public const int MarginCells = 3;
        public const float OverhangChance = 0.6f, ArchChance = 0.3f;
        public const float OverhangMinDrop = 50f;
        public const float CaveChance = 0.35f, CaveMinDrop = 30f, CaveRoof = 12f, CaveGrade = 0.18f, CaveSideChance = 0.4f, RoofSampleM = 4f;
        public const float CaveHollow = 1f - 1.25f * FeatureEdit.Blend, JoinBlend = 0.25f;
        public const int CavePieces = 4, CavePaths = 3;
        public const float LatticeStepM = 11.25f, MinRadiusSteps = 1.25f;
        public const float UndercutChance = 0.5f, UndercutMinDrop = 30f, UndercutContrast = 0.12f, UndercutStep = 15f;
        public const int UndercutReach = 3;

        public static float Apply<TCols, TEdits>(TCols columns, TEdits edits, float d, int i, int j, int k, float s) where TCols : struct, IColumnField where TEdits : struct, IEditList
            => Edit(edits, Level(columns, d, i, k, s), i * s, j * s, k * s);

        public static float Edit<TEdits>(TEdits edits, float d, float x, float y, float z) where TEdits : struct, IEditList
        {
            float hole = float.MaxValue, k = 0f;
            for (int q = 0, n = edits.Count; q < n; q++)
            {
                var e = edits.At(q);
                if (!e.Reach(x, y, z, out float dist)) continue;
                if (e.Kind == FeatureEdit.Add)
                {
                    if (hole < float.MaxValue) { d = FeatureEdit.Cut(d, hole, k); hole = float.MaxValue; k = 0f; }
                    d = Math.Max(d, e.R - dist);
                }
                else
                {
                    float c = e.Hole(dist), kc = e.R * FeatureEdit.Blend;
                    hole = hole < float.MaxValue ? FeatureEdit.Cut(hole, c, JoinBlend * Math.Min(k, kc)) : c;
                    k = Math.Max(k, kc);
                }
            }
            return hole < float.MaxValue ? FeatureEdit.Cut(d, hole, k) : d;
        }

        public static float Level<TCols>(TCols columns, float d, int i, int k, float s) where TCols : struct, IColumnField
            => d / Slope(columns.Height(i + 1, k), columns.Height(i - 1, k), columns.Height(i, k + 1), columns.Height(i, k - 1), s);

        public static float Slope(float xp, float xm, float zp, float zm, float s)
        {
            float gx = (xp - xm) / (2 * s), gz = (zp - zm) / (2 * s);
            return (float)Math.Sqrt(1 + gx * gx + gz * gz);
        }

        public static bool Covers<TEdits>(TEdits edits, float x, float y, float z, float s) where TEdits : struct, IEditList
        {
            for (int q = 0, n = edits.Count; q < n; q++)
            {
                var e = edits.At(q);
                if (e.MinX <= x + s && e.MaxX >= x && e.MinY <= y + s && e.MaxY >= y && e.MinZ <= z + s && e.MaxZ >= z) return true;
            }
            return false;
        }

        public static int TileOf(FeatureEdit e, float cellMeters)
        {
            float span = TileCells * cellMeters;
            int tx = (int)Math.Floor(e.MinX / span), tz = (int)Math.Floor(e.MinZ / span);
            return (int)Math.Floor(e.MaxX / span) == tx && (int)Math.Floor(e.MaxZ / span) == tz ? tz * 65536 + tx : -1;
        }

        public static bool Inside(FeatureEdit e, float cellMeters)
        {
            float span = TileCells * cellMeters, m = MarginCells * cellMeters;
            float x0 = (float)Math.Floor(e.MinX / span) * span, z0 = (float)Math.Floor(e.MinZ / span) * span;
            return e.MinX >= x0 + m && e.MaxX <= x0 + span - m && e.MinZ >= z0 + m && e.MaxZ <= z0 + span - m;
        }

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail)
        {
            var list = new List<FeatureEdit>();
            if (w.Biome == null || 16f < MinRadiusSteps * LatticeStepM) return list.ToArray();
            if (w.Materials == null) throw new InvalidOperationException("feature placement needs the material library");
            var strata = new StrataField(w.Config, w.Materials);
            int tiles = w.Config.Size / TileCells;
            ulong ro = Rng.Stream(w.Config.Seed, "overhangs").NextU64(), ra = Rng.Stream(w.Config.Seed, "arches").NextU64(), rc = Rng.Stream(w.Config.Seed, "caves").NextU64();
            ulong ru = Rng.Stream(w.Config.Seed, "undercuts").NextU64();
            int from = 0;
            var tile = new List<FeatureEdit>();
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    ulong t = (ulong)(ty * tiles + tx);
                    var r = new Rng(Rng.Mix(ro + t));
                    tile.Clear();
                    Overhang(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                    r = new Rng(Rng.Mix(ra + t));
                    tile.Clear();
                    Arch(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                    r = new Rng(Rng.Mix(rc + t));
                    tile.Clear();
                    Cave(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                    r = new Rng(Rng.Mix(ru + t));
                    tile.Clear();
                    Undercut(w, detail, strata, tx, ty, ref r, tile);
                    if (tile.Count == 0 || !Overlaps(tile[0], list, from)) Keep(w, tile, list);
                    from = list.Count;
                }
            return list.ToArray();
        }

        static void Keep(GeneratedWorld w, List<FeatureEdit> tile, List<FeatureEdit> list)
        {
            foreach (var e in tile) if (!Inside(e, w.Config.CellMeters)) return;
            list.AddRange(tile);
        }

        static bool Overlaps(FeatureEdit e, List<FeatureEdit> list, int from)
        {
            for (int q = from; q < list.Count; q++)
            {
                var o = list[q];
                if (e.MinX < o.MaxX && e.MaxX > o.MinX && e.MinY < o.MaxY && e.MaxY > o.MinY && e.MinZ < o.MaxZ && e.MaxZ > o.MinZ) return true;
            }
            return false;
        }

        static bool Dry(GeneratedWorld w, int i) => !w.Sea[i] && w.Water.LakeId[i] < 0 && !w.Water.IsRiver[i] && w.Height[i] > 0;

        static void Overhang(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), radius = r.Range(28f, 40f), length = r.Range(60f, 120f), lift = r.Range(0.2f, 0.5f);
            if (roll >= OverhangChance) return;
            int n = w.Config.Size, best = -1, bestDir = 0;
            float bestDrop = OverhangMinDrop;
            for (int y = ty * TileCells + MarginCells; y < (ty + 1) * TileCells - MarginCells; y++)
                for (int x = tx * TileCells + MarginCells; x < (tx + 1) * TileCells - MarginCells; x++)
                {
                    int i = y * n + x;
                    if (!Dry(w, i)) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = i + Grid.DY[k] * n + Grid.DX[k];
                        float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                        if (drop > bestDrop && Dry(w, j)) { bestDrop = drop; best = i; bestDir = k; }
                    }
                }
            if (best < 0) return;
            float cm = w.Config.CellMeters, inv = 1f / Grid.Dist[bestDir];
            float gx = Grid.DX[bestDir] * inv, gz = Grid.DY[bestDir] * inv;
            float fx = best % n + 0.7f * gx, fz = best / n + 0.7f * gz;
            float foot = detail.HeightAt(fx, fz);
            float inset = radius * 0.6f / cm, cx = fx - gx * inset, cz = fz - gz * inset;
            float y0 = foot + radius * lift;
            if (detail.HeightAt(cx, cz) < y0 + radius * 0.3f) return;
            if (detail.HeightAt(cx - gx * radius / cm, cz - gz * radius / cm) < y0 + radius + 8f) return;
            float half = length * 0.5f;
            output.Add(new FeatureEdit
            {
                Kind = FeatureEdit.Carve, Shape = FeatureEdit.Overhang, R = radius,
                Ax = cx * cm - gz * half, Az = cz * cm + gx * half, Ay = y0,
                Bx = cx * cm + gz * half, Bz = cz * cm - gx * half, By = y0,
            });
        }

        static void Undercut(GeneratedWorld w, TerrainDetail detail, StrataField strata, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), radius = r.Range(16f, 22f), length = r.Range(80f, 140f);
            if (roll >= UndercutChance) return;
            int n = w.Config.Size, best = -1, bestDir = 0;
            float bestDrop = UndercutMinDrop;
            for (int y = ty * TileCells + MarginCells; y < (ty + 1) * TileCells - MarginCells; y++)
                for (int x = tx * TileCells + MarginCells; x < (tx + 1) * TileCells - MarginCells; x++)
                {
                    int i = y * n + x;
                    if (!Dry(w, i)) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = i + Grid.DY[k] * n + Grid.DX[k];
                        float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                        if (drop > bestDrop && Dry(w, j)) { bestDrop = drop; best = i; bestDir = k; }
                    }
                }
            if (best < 0) return;
            float cm = w.Config.CellMeters, inv = 1f / Grid.Dist[bestDir];
            float gx = Grid.DX[bestDir] * inv, gz = Grid.DY[bestDir] * inv;
            float bx = best % n, bz = best / n, foot = w.Height[best], top = foot;
            for (int s = 1; s <= UndercutReach; s++)
            {
                float h = detail.HeightAt(bx + gx * s, bz + gz * s);
                if (foot - h < UndercutStep) break;
                foot = h;
            }
            for (int s = 1; s <= UndercutReach; s++)
            {
                float h = detail.HeightAt(bx - gx * s, bz - gz * s);
                if (h - top < UndercutStep) break;
                top = h;
            }
            float y0 = 0, score = UndercutContrast;
            for (float y = foot + 0.5f * radius; y <= top - 1.2f * radius; y += 1f)
            {
                float cap = 0;
                for (int s = 0; s <= 4; s++) cap += strata.Hardness(best, y + radius * (0.6f + 0.25f * s));
                float v = cap / 5f - strata.Hardness(best, y);
                if (v > score) { score = v; y0 = y; }
            }
            if (score <= UndercutContrast) return;
            float fx = bx + gx * UndercutReach, fz = bz + gz * UndercutReach;
            for (int s = 0; s < 20 * UndercutReach && detail.HeightAt(fx, fz) < y0; s++) { fx -= gx * 0.1f; fz -= gz * 0.1f; }
            float inset = radius * 0.6f / cm, cx = fx - gx * inset, cz = fz - gz * inset;
            if (detail.HeightAt(cx, cz) < y0 + radius * 0.3f) return;
            if (detail.HeightAt(cx - gx * radius / cm, cz - gz * radius / cm) < y0 + radius + 8f) return;
            float half = length * 0.5f;
            output.Add(new FeatureEdit
            {
                Kind = FeatureEdit.Carve, Shape = FeatureEdit.Overhang, R = radius,
                Ax = cx * cm - gz * half, Az = cz * cm + gx * half, Ay = y0,
                Bx = cx * cm + gz * half, Bz = cz * cm - gx * half, By = y0,
            });
        }

        static void Arch(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), px = r.Range(0.3f, 0.7f), pz = r.Range(0.3f, 0.7f);
            float dx = r.Range(-1f, 1f), dz = r.Range(-1f, 1f), span = r.Range(110f, 200f), radius = r.Range(16f, 22f), rise = r.Range(0.45f, 0.7f);
            if (roll >= ArchChance) return;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.2f) { dx = 1f; dz = 0f; len = 1f; }
            dx /= len; dz /= len;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            float mx = (tx + px) * TileCells, mz = (ty + pz) * TileCells, half = span * 0.5f / cm;
            float ax = mx - dx * half, az = mz - dz * half, bx = mx + dx * half, bz = mz + dz * half;
            if (Math.Min(Math.Min(ax, az), Math.Min(bx, bz)) < 0 || Math.Max(Math.Max(ax, az), Math.Max(bx, bz)) > n - 1) return;
            if (!Dry(w, (int)az * n + (int)ax) || !Dry(w, (int)bz * n + (int)bx) || !Dry(w, (int)mz * n + (int)mx)) return;
            float ha = detail.HeightAt(ax, az), hb = detail.HeightAt(bx, bz);
            float ground = Math.Max(ha, hb);
            for (int s = 1; s < 8; s++) ground = Math.Max(ground, detail.HeightAt(ax + (bx - ax) * s / 8f, az + (bz - az) * s / 8f));
            float apex = Math.Max(ha, hb) + span * rise;
            if (apex - radius < ground + 25f) return;
            float y0 = ha - radius * 0.5f, y1 = hb - radius * 0.5f, ctrl = 2f * apex - 0.5f * (y0 + y1);
            const int Pieces = 5;
            float lx = ax, lz = az, ly = y0;
            for (int s = 1; s <= Pieces; s++)
            {
                float t = s / (float)Pieces, u = 1 - t;
                float nx = ax + (bx - ax) * t, nz = az + (bz - az) * t, ny = u * u * y0 + 2 * u * t * ctrl + t * t * y1;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Arch, R = radius, Ax = lx * cm, Ay = ly, Az = lz * cm, Bx = nx * cm, By = ny, Bz = nz * cm });
                lx = nx; ly = ny; lz = nz;
            }
        }

        struct CavePiece
        {
            public float Length, Turn, Grade, Side, SideTurn, SideGrade, SideLength;
        }

        struct CaveBuild
        {
            public TerrainDetail Detail;
            public float Cm, R, Chamber;
            public List<FeatureEdit> Output;
            public int Start;

            public bool Covered(float x, float y, float z, float r) => Detail.HeightAt(x, z) >= y + r + CaveRoof;

            public bool Add(int shape, float r, float ax, float ay, float az, float bx, float by, float bz, float open = 0, float ground = 0)
            {
                float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) * Cm;
                int steps = Math.Max(2, (int)Math.Ceiling(len / RoofSampleM));
                for (int t = 1; t <= steps; t++)
                {
                    float f = t / (float)steps;
                    if (f * len < open && Detail.HeightAt(ax + (bx - ax) * f, az + (bz - az) * f) >= ground) continue;
                    if (!Covered(ax + (bx - ax) * f, ay + (by - ay) * f, az + (bz - az) * f, r)) return false;
                }
                var e = new FeatureEdit { Kind = FeatureEdit.Carve, Shape = shape, R = r, Ax = ax * Cm, Ay = ay, Az = az * Cm, Bx = bx * Cm, By = by, Bz = bz * Cm };
                if (!Inside(e, Cm)) return false;
                for (int q = Start; q < Output.Count; q++)
                {
                    var o = Output[q];
                    if (Joined(o, e)) continue;
                    if (Over(o, e) || Over(e, o)) return false;
                }
                Output.Add(e);
                return true;
            }

            static bool Joined(FeatureEdit o, FeatureEdit e)
                => (Math.Abs(o.Bx - e.Ax) < 1e-2f && Math.Abs(o.Bz - e.Az) < 1e-2f) || (Math.Abs(o.Ax - e.Ax) < 1e-2f && Math.Abs(o.Az - e.Az) < 1e-2f);

            static bool Over(FeatureEdit o, FeatureEdit e)
            {
                float ex = e.Bx - e.Ax, ez = e.Bz - e.Az, ox = o.Bx - o.Ax, oz = o.Bz - o.Az, ll = ox * ox + oz * oz;
                float reach = (1f - FeatureEdit.Blend * 0.25f) * o.R, tol = 0.25f * Math.Min(o.R, e.R);
                int steps = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(ex * ex + ez * ez) / RoofSampleM));
                for (int t = 0; t <= steps; t++)
                {
                    float f = t / (float)steps, px = e.Ax + ex * f, pz = e.Az + ez * f;
                    float u = ll > 0 ? Math.Max(0f, Math.Min(1f, ((px - o.Ax) * ox + (pz - o.Az) * oz) / ll)) : 0f;
                    float dx = px - o.Ax - ox * u, dz = pz - o.Az - oz * u;
                    if (dx * dx + dz * dz >= reach * reach) continue;
                    float fe = e.Ay + (e.By - e.Ay) * f - e.R * CaveHollow, fo = o.Ay + (o.By - o.Ay) * u - o.R * CaveHollow;
                    if (Math.Abs(fe - fo) > tol) return true;
                }
                return false;
            }

            public void Room(float x, float y, float z, float hx, float hz, float r)
            {
                float big = r * Chamber, reach = big * 0.6f / Cm, lift = (big - r) * CaveHollow;
                if (Covered(x, y + lift, z, big)) Add(FeatureEdit.CaveRoom, big, x, y + lift, z, x + hx * reach, y + lift, z + hz * reach);
            }
        }

        static void Turn(ref float hx, ref float hz, float t)
        {
            float ox = -hz, oz = hx;
            hx += ox * t; hz += oz * t;
            float l = (float)Math.Sqrt(hx * hx + hz * hz);
            hx /= l; hz /= l;
        }

        static int Path(ref CaveBuild c, CavePiece[] plan, int p, int first, ref float x, ref float y, ref float z, ref float hx, ref float hz)
        {
            int made = 0;
            for (int s = first; s < CavePieces; s++)
            {
                var q = plan[p * CavePieces + s];
                if (s > 0) Turn(ref hx, ref hz, q.Turn);
                float step = q.Length / c.Cm, bx = x + hx * step, bz = z + hz * step, by = y - q.Length * q.Grade;
                if (!c.Add(FeatureEdit.Cave, c.R, x, y, z, bx, by, bz)) break;
                x = bx; y = by; z = bz;
                made++;
                if (q.Side >= CaveSideChance) continue;
                float sx = hx, sz = hz;
                Turn(ref sx, ref sz, 1.5f * q.SideTurn);
                float r = c.R * 0.85f, stub = c.R / c.Cm, ex = x + sx * stub, ez = z + sz * stub;
                float fx = ex + sx * q.SideLength / c.Cm, fz = ez + sz * q.SideLength / c.Cm, fy = y + q.SideGrade * q.SideLength;
                int mark = c.Output.Count;
                if (c.Add(FeatureEdit.CaveRoom, r, x, y, z, ex, y, ez) && c.Add(FeatureEdit.CaveRoom, r, ex, y, ez, fx, fy, fz)) c.Room(fx, fy, fz, sx, sz, r);
                else c.Output.RemoveRange(mark, c.Output.Count - mark);
            }
            return made;
        }

        static void Cave(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), pick = r.NextFloat(), radius = r.Range(30f, 38f), chamber = r.Range(1.3f, 1.6f), spread = r.Range(0.7f, 1.1f);
            int paths = Math.Min(CavePaths, 1 + (int)(r.NextFloat() * CavePaths));
            var plan = new CavePiece[CavePaths * CavePieces];
            for (int q = 0; q < plan.Length; q++)
                plan[q] = new CavePiece
                {
                    Length = r.Range(70f, 120f), Turn = r.Range(-0.5f, 0.5f), Grade = r.Range(-0.4f, 1f) * CaveGrade,
                    Side = r.NextFloat(), SideTurn = r.NextFloat() < 0.5f ? -1f : 1f, SideGrade = r.Range(-1f, 1f), SideLength = r.Range(40f, 70f),
                };
            if (roll >= CaveChance) return;
            int n = w.Config.Size, count = 0, best = -1, bestDir = 0;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                int want = (int)(pick * count);
                for (int y = ty * TileCells + MarginCells; y < (ty + 1) * TileCells - MarginCells && best < 0; y++)
                    for (int x = tx * TileCells + MarginCells; x < (tx + 1) * TileCells - MarginCells && best < 0; x++)
                    {
                        int i = y * n + x, dir = Steepest(w, i);
                        if (dir < 0) continue;
                        if (pass == 0) count++;
                        else if (want-- == 0) { best = i; bestDir = dir; }
                    }
                if (count == 0) return;
            }
            float cm = w.Config.CellMeters, inv = 1f / Grid.Dist[bestDir];
            float hx = -Grid.DX[bestDir] * inv, hz = -Grid.DY[bestDir] * inv;
            float ax = best % n - 0.5f * hx, az = best / n - 0.5f * hz, ay = detail.HeightAt(ax, az) + 0.9f * radius * CaveHollow;
            int start = output.Count;
            var c = new CaveBuild { Detail = detail, Cm = cm, R = radius, Chamber = chamber, Output = output, Start = start };
            var mouth = plan[0];
            float bx = ax + hx * mouth.Length / cm, bz = az + hz * mouth.Length / cm, by = ay - mouth.Length * mouth.Grade;
            if (!c.Add(FeatureEdit.Cave, radius, ax, ay, az, bx, by, bz, radius + CaveRoof, detail.HeightAt(ax, az))) return;
            for (int p = 0; p < paths; p++)
            {
                float x = bx, y = by, z = bz, dx = hx, dz = hz;
                if (p > 0) Turn(ref dx, ref dz, p == 1 ? spread : -spread);
                if (Path(ref c, plan, p, 1, ref x, ref y, ref z, ref dx, ref dz) > 0) c.Room(x, y, z, dx, dz, radius);
                else if (p == 0) { output.RemoveRange(start, output.Count - start); return; }
            }
        }

        static int Steepest(GeneratedWorld w, int i)
        {
            if (!Dry(w, i)) return -1;
            int n = w.Config.Size, dir = -1;
            float most = CaveMinDrop;
            for (int k = 0; k < 8; k++)
            {
                int j = i + Grid.DY[k] * n + Grid.DX[k];
                float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                if (drop > most && Dry(w, j)) { most = drop; dir = k; }
            }
            return dir;
        }
    }
}
