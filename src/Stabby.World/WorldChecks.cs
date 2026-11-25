using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const float SurfaceTolerance = 1e-3f;
        public const float CorridorFollowShare = 0.7f;
        public const float CliffMinRise = 30f, BeachMaxDepth = 4f, BeachShelfStep = 0.1f, BeachShelfShare = 0.25f, ShoreToeMaxBend = 3f;
        public const int ShoreToeMinPairs = 20;

        static int FirstUndrained(GeneratedWorld w)
        {
            int n = w.Config.Size;
            var recv = w.Water.Receiver;
            var state = new byte[n * n];
            var path = new List<int>();
            for (int i = 0; i < n * n; i++)
            {
                if (w.Sea[i] || state[i] != 0) continue;
                path.Clear();
                int c = i;
                while (!w.Sea[c] && state[c] == 0) { state[c] = 1; path.Add(c); c = recv[c]; }
                byte result = w.Sea[c] || state[c] == 2 ? (byte)2 : (byte)3;
                foreach (int p in path) state[p] = result;
                if (result == 3) return i;
            }
            return -1;
        }

        public static List<string> Run(GeneratedWorld w)
        {
            var e = new List<string>();
            int n = w.Config.Size;
            var hy = w.Water;

            for (int x = 0; x < n; x++)
                if (!w.Sea[x] || !w.Sea[(n - 1) * n + x] || !w.Sea[x * n] || !w.Sea[x * n + n - 1])
                { e.Add("land touches the map border"); break; }

            for (int i = 0; i < n * n; i++)
                if (w.Sea[i] && w.Height[i] >= 0) { e.Add($"sea cell {i} is above sea level"); break; }

            int bad = FirstUndrained(w);
            if (bad >= 0) e.Add($"cell {bad} never drains to the sea");

            for (int i = 0; i < n * n; i++)
            {
                if (w.Sea[i]) continue;
                int r = hy.Receiver[i];
                if (!Grid.Adjacent(n, i, r)) { e.Add($"cell {i} drains to non-neighbour {r}"); break; }
                if (hy.DischargeM3s[r] + 1e-6f < hy.DischargeM3s[i] && !w.Sea[r])
                { e.Add($"discharge drops from {i} to {r}"); break; }
            }

            foreach (var s in hy.Rivers)
            {
                for (int k = 1; k < s.Cells.Count; k++)
                {
                    if (!Grid.Adjacent(n, s.Cells[k - 1], s.Cells[k]))
                    { e.Add($"river {s.Id} has a gap at point {k}"); break; }
                    if (s.Surface[k] > s.Surface[k - 1] + SurfaceTolerance)
                    { e.Add($"river {s.Id} flows uphill at point {k}"); break; }
                    if (s.Depth[k] + 1e-4f < s.Depth[k - 1])
                    { e.Add($"river {s.Id} gets shallower downstream at point {k}"); break; }
                }
                CheckRiverBed(w, s, e);
                int last = s.Cells[s.Cells.Count - 1];
                switch (s.End)
                {
                    case RiverEnd.Sea:
                        if (!w.Sea[last]) e.Add($"river {s.Id} ends at sea but last cell is land");
                        break;
                    case RiverEnd.Lake:
                        if (hy.LakeId[last] != s.LakeId) e.Add($"river {s.Id} misses its lake");
                        else if (s.Surface[s.Surface.Count - 2] + SurfaceTolerance < hy.Lakes[s.LakeId].Level)
                            e.Add($"river {s.Id} enters lake {s.LakeId} below its level");
                        break;
                    case RiverEnd.Junction:
                        var down = hy.Rivers[s.Downstream];
                        if (down.Cells[0] != last) e.Add($"river {s.Id} does not meet river {down.Id}");
                        break;
                }
            }

            foreach (var lake in hy.Lakes)
            {
                if (lake.Outlet < 0) { e.Add($"lake {lake.Id} has no outlet"); continue; }
                int outlets = 0;
                foreach (int i in lake.Cells)
                {
                    if (hy.LakeId[hy.Receiver[i]] != lake.Id) outlets++;
                    if (w.Height[i] > lake.Level + SurfaceTolerance) e.Add($"lake {lake.Id} cell {i} above its surface");
                }
                if (outlets != 1) e.Add($"lake {lake.Id} has {outlets} outlets");
                if (lake.Level < Erosion.MinLandHeight) e.Add($"lake {lake.Id} lies at sea level");
                int below = hy.Receiver[lake.Outlet];
                if (!w.Sea[below] && hy.WaterSurface[below] > lake.Level + SurfaceTolerance)
                    e.Add($"lake {lake.Id} spills uphill");
                if (!w.Sea[below] && hy.DischargeM3s[lake.Outlet] >= w.Config.RiverMinDischarge && !hy.IsRiver[below] && hy.LakeId[below] < 0)
                    e.Add($"lake {lake.Id} outflow has no river");
            }

            if (w.Biome != null)
                for (int i = 0; i < n * n; i++)
                    if (!w.Sea[i] && w.Biome[i] < 0 && hy.LakeId[i] < 0) { e.Add($"land cell {i} has no biome"); break; }

            CheckTint(w, e);
            CheckLakeCover(w, e);
            if (w.Sketch != null) CheckSketch(w, e);
            if (w.Sketch != null) CheckMesas(w, e);
            CheckDetail(w, e);
            CheckNearField(w, e);
            CheckRiverMouths(w, e);
            CheckLakeOutlets(w, e);
            CheckFalls(w, e);
            CheckRiverShape(w, e);
            CheckGeometry(w, e);
            CheckFeatures(w, e);
            return e;
        }

        static void CheckRiverBed(GeneratedWorld w, RiverSegment s, List<string> e)
        {
            if (s.X.Count != s.Cells.Count || s.Y.Count != s.Cells.Count) { e.Add($"river {s.Id} has no centreline"); return; }
            int n = w.Config.Size;
            for (int k = 0; k < s.Cells.Count; k++)
            {
                int i = s.Cells[k];
                float reach = Hydrology.ReachCells(w.Config, s.Width[k]) + SurfaceTolerance;
                float dx = s.X[k] - i % n, dy = s.Y[k] - i / n;
                if (dx * dx + dy * dy > reach * reach) { e.Add($"river {s.Id} meanders off its cell at point {k}"); return; }
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) continue;
                float bed = Math.Max(Erosion.MinLandHeight, s.Surface[k] - 0.5f * s.Depth[k]);
                if (w.Height[i] > bed + SurfaceTolerance) { e.Add($"river {s.Id} has no bed at point {k}"); return; }
            }
        }

        static void CheckDetail(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            var hy = w.Water;
            var d = new TerrainDetail(w);
            var mouth = MouthCells(w);
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int i = y * n + x;
                    bool wet = (w.Sea[i] || hy.LakeId[i] >= 0) && d.Cells.PieceStart(i) == d.Cells.PieceStart(i + 1);
                    if (wet && d.HeightAt(x, y) != w.Height[i]) { e.Add($"detail moves water cell {i}"); return; }
                    if (w.Height[i] > 0 && w.Height[i + 1] > 0 && w.Height[i + n] > 0 && w.Height[i + n + 1] > 0
                        && !mouth.Contains(i) && d.HeightAt(x + 0.5f, y + 0.5f) <= 0) { e.Add($"detail sinks land below the sea near cell {i}"); return; }
                    if (x > 0 && y > 0 && x < n - 2 && y < n - 2 && OpenSea(w, i) && d.HeightAt(x + 0.5f, y + 0.5f) > 0 && !InFall(w, (x + 0.5f) * w.Config.CellMeters, (y + 0.5f) * w.Config.CellMeters, true))
                    { e.Add($"detail raises open sea above the water near cell {i}"); return; }
                }
            float toe = ShoreToeBend(w, d, out int pairs);
            if (pairs >= ShoreToeMinPairs && toe > ShoreToeMaxBend) e.Add($"beach shore toe creases: median bend {toe:F2} over {pairs} coasts");
        }

        public static float ShoreToeBend(GeneratedWorld w, TerrainDetail d, out int pairs)
        {
            int n = w.Config.Size;
            const int m = 16;
            var bends = new List<float>();
            for (int y = 4; y < n - 4; y++)
                for (int x = 4; x < n - 4; x++)
                {
                    int l = y * n + x;
                    if (w.Sea[l] || w.Sketch == null || w.Sketch.Beach[l] < 0.5f) continue;
                    for (int k = 0; k < 8; k += 2)
                    {
                        int s = Grid.DY[k] * n + Grid.DX[k];
                        if (w.Sea[l - s] || !w.Sea[l + s] || !w.Sea[l + 2 * s] || !w.Sea[l + 3 * s]) continue;
                        float step = w.Height[l] - w.Height[l + s];
                        if (step <= 0) continue;
                        float bend = 0, prev = float.NaN, h0 = NearField.Base(d.Cells, x, y);
                        for (int t = 1; t <= 2 * m; t++)
                        {
                            float h1 = NearField.Base(d.Cells, x + Grid.DX[k] * t / (float)m, y + Grid.DY[k] * t / (float)m);
                            float slope = (h1 - h0) * m;
                            if (!float.IsNaN(prev) && h0 < 0) bend = Math.Max(bend, Math.Abs(slope - prev) * m);
                            prev = slope; h0 = h1;
                        }
                        bends.Add(bend / step);
                    }
                }
            pairs = bends.Count;
            if (pairs == 0) return 0;
            bends.Sort();
            return bends[pairs / 2];
        }

        static bool OpenSea(GeneratedWorld w, int i)
        {
            int n = w.Config.Size;
            for (int dy = -1; dy <= 2; dy++)
                for (int dx = -1; dx <= 2; dx++)
                {
                    int j = i + dy * n + dx;
                    if (!w.Sea[j] || w.Height[j] >= 0) return false;
                }
            return true;
        }

        static HashSet<int> MouthCells(GeneratedWorld w)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var cells = new HashSet<int>();
            foreach (var l in NearField.Lines(w))
            {
                int m = l.X.Length;
                if (!w.Sea[NearField.CellOf(l.X[m - 1], l.Z[m - 1], cm, n)]) continue;
                for (int j = m - 1; j >= 0 && l.S[j] <= 0.5f * Erosion.MinLandHeight + 1e-4f; j--)
                {
                    int r = (int)(l.W[j] / cm) + 2, cx = (int)(l.X[j] / cm), cz = (int)(l.Z[j] / cm);
                    for (int y = Math.Max(0, cz - r); y <= Math.Min(n - 1, cz + r); y++)
                        for (int x = Math.Max(0, cx - r); x <= Math.Min(n - 1, cx + r); x++) cells.Add(y * n + x);
                }
            }
            return cells;
        }

        public const float SeamEpsilonM = 0.01f, SeamTolerance = 0.05f, MeshTolerance = 0.25f, BankTolerance = 0.05f, NearStepM = 1f, MouthDepthM = 0.2f;
        public const int SeamStride = 3, NearCells = 24;

        public const float FallPoolDepthM = 0.5f, FallEdgeM = 1f, FallEdgeShare = 0.5f, IsleTolerance = 0.05f, IsleProbe = 0.25f, IsleAboveM = 0.5f;
        public const int IsleRows = 4;

        static void CheckFalls(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var d = new TerrainDetail(w);
            var hy = w.Water;
            bool pool = false, edge = false, lake = false, sheet = false, shelf = false, isle = false, bare = false;
            foreach (var s in hy.Rivers)
                foreach (var f in s.Falls)
                {
                    int lip = Cell(n, cm, f.LipX, f.LipZ), bottom = Cell(n, cm, f.BaseX, f.BaseZ);
                    bool wetLip = w.Sea[lip] || hy.LakeId[lip] >= 0, wetBase = w.Sea[bottom] || hy.LakeId[bottom] >= 0;
                    if (!lake && (wetLip || wetBase && f.BaseY < hy.WaterSurface[bottom] - SurfaceTolerance))
                    {
                        lake = true;
                        e.Add($"river {s.Id} fall at {f.LipX:F0},{f.LipZ:F0} lies inside a {(w.Sea[lip] || w.Sea[bottom] ? "sea" : "lake")}: lip {f.LipY:F1}, base {f.BaseY:F1}, water {hy.WaterSurface[wetLip ? lip : bottom]:F1}");
                    }
                    float up = d.Sample(f.LipX - f.DirX * FallEdgeM, f.LipZ - f.DirZ * FallEdgeM), down = d.Sample(f.BaseX + f.DirX * FallEdgeM, f.BaseZ + f.DirZ * FallEdgeM);
                    if (!edge && (f.Drop < Hydrology.FallMinDrop || up - down < FallEdgeShare * f.Drop))
                    {
                        edge = true;
                        e.Add($"river {s.Id} fall at {f.LipX:F0},{f.LipZ:F0} has no lip above its base: drop {f.Drop:F1}, ground {up:F1} above to {down:F1} below");
                    }
                    if (wetBase || f.BaseY < Erosion.MinLandHeight + FallPoolDepthM) continue;
                    float cx = f.LipX + f.DirX * f.PoolCentre, cz = f.LipZ + f.DirZ * f.PoolCentre, g = d.Sample(cx, cz);
                    if (!pool && g > f.BaseY - FallPoolDepthM)
                    {
                        pool = true;
                        e.Add($"river {s.Id} fall at {f.LipX:F0},{f.LipZ:F0} has no plunge pool: ground {g:F1} at {cx:F0},{cz:F0}, water {f.BaseY:F1}");
                    }
                    float th = Hydrology.FallThrow(f.Drop);
                    var runs = Hydrology.LipRuns(f);
                    for (int r = 0; r < runs.Count; r += 2)
                        for (float a = runs[r]; a <= runs[r + 1]; a += 0.5f * NearStepM)
                        {
                            Hydrology.LipAt(f, a, out float lx, out float lz);
                            float x = lx + f.DirX * th, z = lz + f.DirZ * th, h = d.Sample(x, z);
                            if (!sheet && h > f.BaseY + BankTolerance)
                            {
                                sheet = true;
                                e.Add($"river {s.Id} fall at {f.LipX:F0},{f.LipZ:F0} has rock inside its sheet foot: ground {h:F1} at {x:F0},{z:F0}, {a:F1} m off centre, water {f.BaseY:F1}");
                            }
                            if (isle || runs.Count < 4) continue;
                            for (int k = 1; k <= IsleRows; k++)
                            {
                                float y = f.Drop * k / IsleRows, t = Hydrology.FallThrow(y), ix = lx + f.DirX * t, iz = lz + f.DirZ * t, gi = d.Sample(ix, iz);
                                if (gi <= f.LipY - y + IsleTolerance) continue;
                                isle = true;
                                e.Add($"river {s.Id} fall at {f.LipX:F0},{f.LipZ:F0} has rock inside a curtain beside its islands: ground {gi:F1} at {ix:F0},{iz:F0}, {a:F1} m off centre, sheet {f.LipY - y:F1}");
                                break;
                            }
                        }
                    for (int r = 2; !bare && r < runs.Count; r += 2)
                    {
                        float mid = 0.5f * (runs[r - 1] + runs[r]), back = IsleProbe * (runs[r] - runs[r - 1]);
                        Hydrology.LipAt(f, mid, out float lx, out float lz);
                        float x = lx - f.DirX * back, z = lz - f.DirZ * back, h = d.Sample(x, z);
                        if (h >= f.LipY + IsleAboveM) continue;
                        bare = true;
                        e.Add($"river {s.Id} fall at {f.LipX:F0},{f.LipZ:F0} has a lip gap with no island: ground {h:F1} at {x:F0},{z:F0}, water {f.LipY:F1}");
                    }
                    for (int k = 0; !shelf && f.LipPts != null && k < f.LipPts.Length; k += 2)
                    {
                        float x = f.LipPts[k] - f.DirX * FallEdgeM, z = f.LipPts[k + 1] - f.DirZ * FallEdgeM, h = d.Sample(x, z);
                        if (h <= f.LipY + BankTolerance) continue;
                        shelf = true;
                        e.Add($"river {s.Id} shelf fall at {f.LipX:F0},{f.LipZ:F0} has a dry lip: ground {h:F1} at {x:F0},{z:F0}, water {f.LipY:F1}");
                    }
                }
        }

        static int Cell(int n, float cm, float x, float z)
        {
            int cx = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(x / cm + 0.5f))), cz = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(z / cm + 0.5f)));
            return cz * n + cx;
        }

        static void CheckNearField(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var d = new TerrainDetail(w);
            var pieces = NearField.Pieces(w);
            var pools = pieces.FindAll(q => q.Fr > 0);
            var spots = new List<(float, float)>();
            for (int y = 1; y < n - 1; y += SeamStride)
                for (int x = 1; x < n - 1; x += SeamStride)
                    if (!w.Sea[y * n + x]) spots.Add((x * cm, y * cm));
            foreach (var p in pieces) spots.Add((0.5f * (p.Ax + p.Bx), 0.5f * (p.Az + p.Bz)));
            foreach (var (sx, sz) in spots)
                for (int half = 0; half < 2; half++)
                {
                    float bx = (float)Math.Floor(sx / cm) * cm + half * 0.5f * cm, bz = (float)Math.Floor(sz / cm) * cm + half * 0.5f * cm;
                    float jx = Jump(d, bx, sz, 1, 0), jz = Jump(d, sx, bz, 0, 1);
                    if (jx > SeamTolerance && InFall(w, bx, sz)) jx = 0;
                    if (jz > SeamTolerance && InFall(w, sx, bz)) jz = 0;
                    if (jx > SeamTolerance) { e.Add($"near field jumps {jx:F2} m across x = {bx:F1} at z = {sz:F0}"); return; }
                    if (jz > SeamTolerance) { e.Add($"near field jumps {jz:F2} m across z = {bz:F1} at x = {sx:F0}"); return; }
                }

            int top = 0;
            for (int i = 1; i < w.Height.Length; i++) if (w.Height[i] > w.Height[top]) top = i;
            var areas = new List<(float, float)> { (top % n * cm, top / n * cm) };
            if (pieces.Count > 0) areas.Add((pieces[pieces.Count / 2].Ax, pieces[pieces.Count / 2].Az));
            var ground = new DetailGround { Detail = d };
            var none = new EditArray { Items = Array.Empty<FeatureEdit>() };
            foreach (var (ax, az) in areas)
            {
                var c = new NetsChunk { X0 = (int)Math.Floor(ax / NearStepM) - NearCells / 2, Z0 = (int)Math.Floor(az / NearStepM) - NearCells / 2, Cells = NearCells, StepM = NearStepM };
                var f = new NetsBuffers();
                SurfaceNets.Heightfield(new DetailColumns { Detail = d, StepM = NearStepM }, c, ref f);
                for (int t = 0; t < f.Tris.Count; t += 7)
                {
                    int v = 6 * f.Tris[t];
                    float x = f.Verts[v], y = f.Verts[v + 1], z = f.Verts[v + 2], wy = y + 0.5f;
                    Walk.Step(ground, none, LatticeStepM, ref x, ref wy, ref z, 0, 0, Walk.Reach);
                    float h = d.Sample(x, z), s = NearStepM;
                    float bend = Math.Max(Math.Abs(d.Sample(x + s, z) + d.Sample(x - s, z) - 2 * h), Math.Abs(d.Sample(x, z + s) + d.Sample(x, z - s) - 2 * h));
                    if (Math.Abs(wy - h) > SeamEpsilonM && wy > y + 0.5f - Walk.Reach + SeamEpsilonM || Math.Abs(h - y) > MeshTolerance + 0.25f * bend && !InFall(w, x, z, true)) { e.Add($"collision height {wy:F2} differs from mesh height {y:F2} at {x:F0},{z:F0}"); return; }
                }
            }

            for (int p = 0; p < pieces.Count; p++)
            {
                var q = pieces[p];
                float mx = 0.5f * (q.Ax + q.Bx), mz = 0.5f * (q.Az + q.Bz), ex = q.Bx - q.Ax, ez = q.Bz - q.Az, l = (float)Math.Sqrt(ex * ex + ez * ez);
                if (l < 1e-3f || w.Sea[NearField.CellOf(q.Ax, q.Az, cm, n)] || w.Sea[NearField.CellOf(q.Bx, q.Bz, cm, n)]) continue;
                float s = 0.5f * (q.Sa + q.Sb), hw = 0.5f * (q.Wa + q.Wb), depth = 0.5f * (q.Da + q.Db), reach = hw + NearField.Bank(hw);
                if (!Crowded(pieces, p, mx, mz, -1, float.MinValue) && depth > 0.2f && s > Erosion.MinLandHeight + depth && d.Sample(mx, mz) > s - 0.1f * depth)
                { e.Add($"river bed at {mx:F0},{mz:F0} sits above its water {s:F1}"); return; }
                for (int side = -1; side <= 1; side += 2)
                {
                    float bx = mx + side * ez / l * reach, bz = mz - side * ex / l * reach;
                    int bc = NearField.CellOf(bx, bz, cm, n);
                    if (w.Sea[bc] || w.Water.LakeId[bc] >= 0 || Crowded(pieces, p, bx, bz, reach, s) || InFall(w, bx, bz) || pools.Exists(o => NearField.InPoolWater(o, bx, bz))) continue;
                    float h = d.Sample(bx, bz), held = Math.Min(s, Math.Min(q.Sa, q.Sb) + NearField.Rise(hw));
                    if (h < held - BankTolerance) { e.Add($"river at {mx:F0},{mz:F0} spills over its bank at {bx:F0},{bz:F0}: bank {h:F1}, water {held:F1}"); return; }
                }
            }

            foreach (var l in NearField.Lines(w))
                for (int f = 0; f < l.Lip.Length; f++)
                {
                    var fall = l.Falls[f];
                    float run = 0, reach = NearField.PoolReach(fall.PoolCentre, fall.PoolRadius);
                    for (int k = l.Lip[f] + 1; k + 1 < l.X.Length && run < reach; k++)
                    {
                        float ex = l.X[k + 1] - l.X[k], ez = l.Z[k + 1] - l.Z[k], len = (float)Math.Sqrt(ex * ex + ez * ez);
                        for (float a = 0; a < len && run + a < reach; a += NearStepM)
                        {
                            float x = l.X[k] + ex * a / len, z = l.Z[k] + ez * a / len, g = d.Sample(x, z);
                            if (run + a > NearField.FallJagM + NearStepM && g > fall.BaseY + BankTolerance) { e.Add($"plunge pool below fall at {fall.LipX:F0},{fall.LipZ:F0} has no outlet: ground {g:F1} at {x:F0},{z:F0}, {run + a:F0} m down, above water {fall.BaseY:F1}"); return; }
                        }
                        run += len;
                    }
                }
        }

        static void CheckRiverMouths(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var d = new TerrainDetail(w);
            foreach (var l in NearField.Lines(w))
            {
                int m = l.X.Length;
                if (!w.Sea[NearField.CellOf(l.X[m - 1], l.Z[m - 1], cm, n)]) continue;
                int k = m - 1;
                while (k > 0 && w.Sea[NearField.CellOf(l.X[k - 1], l.Z[k - 1], cm, n)]) k--;
                for (int j = k - 1; j >= 0 && l.S[j] <= 0.5f * Erosion.MinLandHeight + 1e-4f; j--)
                {
                    float g = d.Sample(l.X[j], l.Z[j]);
                    if (g > -MouthDepthM && !InFall(w, l.X[j], l.Z[j], true)) { e.Add($"river mouth at {l.X[j]:F0},{l.Z[j]:F0} is too shallow for the sea to run in: ground {g:F2} above {-MouthDepthM:F2}"); return; }
                }
                for (int j = Math.Max(0, k - 1); j + 1 < m; j++)
                {
                    float ex = l.X[j + 1] - l.X[j], ez = l.Z[j + 1] - l.Z[j], len = (float)Math.Sqrt(ex * ex + ez * ez);
                    bool foot = j > 0 && Math.Abs(l.X[j - 1] - l.X[j]) + Math.Abs(l.Z[j - 1] - l.Z[j]) < NearStepM && l.S[j - 1] > l.S[j];
                    for (float a = foot ? NearStepM : 0; a < len; a += NearStepM)
                    {
                        float t = a / len, x = l.X[j] + ex * t, z = l.Z[j] + ez * t, s = Math.Max(0, l.S[j] + (l.S[j + 1] - l.S[j]) * t), g = d.Sample(x, z);
                        if (g > s + BankTolerance) { e.Add($"river mouth at {x:F0},{z:F0} has a land lip: ground {g:F1} above water {s:F1}"); return; }
                    }
                }
            }
        }

        static void CheckLakeOutlets(GeneratedWorld w, List<string> e)
        {
            var hy = w.Water;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var lines = NearField.Lines(w);
            var lineOf = new RiverLine[hy.Rivers.Count];
            for (int r = 0, l = 0; r < hy.Rivers.Count; r++) if (hy.Rivers[r].Cells.Count >= 2) lineOf[r] = lines[l++];
            var d = new TerrainDetail(w);
            foreach (var lake in hy.Lakes)
            {
                if (lake.Outlet < 0 || hy.DischargeM3s[lake.Outlet] < w.Config.RiverMinDischarge) continue;
                int c = hy.Receiver[lake.Outlet], steps = 0;
                while (!w.Sea[c] && hy.LakeId[c] < 0 && hy.Receiver[c] != c && steps++ < n * n) c = hy.Receiver[c];
                if (!w.Sea[c]) continue;
                int below = hy.Receiver[lake.Outlet];
                int first = hy.Rivers.FindIndex(s => s.Cells.Count >= 2 && s.Cells.Contains(below));
                if (first < 0 || lineOf[first] == null) { e.Add($"lake {lake.Id} drains to the sea with no outlet river"); continue; }
                float px = lake.Outlet % n * cm, pz = lake.Outlet / n * cm, ps = lake.Level;
                int start = 0;
                if (hy.Rivers[first].Cells[0] != below)
                {
                    var l0 = lineOf[first]; float bx = below % n * cm, bz = below / n * cm, near = float.MaxValue;
                    for (int j = 0; j < l0.X.Length; j++)
                    {
                        float dd = (l0.X[j] - bx) * (l0.X[j] - bx) + (l0.Z[j] - bz) * (l0.Z[j] - bz);
                        if (dd < near) { near = dd; start = j; }
                    }
                }
                for (int r = first; r >= 0; r = hy.Rivers[r].End == RiverEnd.Junction ? hy.Rivers[r].Downstream : -1)
                {
                    var l = lineOf[r];
                    if (l == null) break;
                    bool blocked = false;
                    for (int j = start - 1; j + 1 < l.X.Length && !blocked; j++)
                    {
                        float ax = j < start ? px : l.X[j], az = j < start ? pz : l.Z[j], sa = j < start ? ps : l.S[j];
                        float ex = l.X[j + 1] - ax, ez = l.Z[j + 1] - az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                        for (float a = 0; a < len; a += NearStepM)
                        {
                            float t = a / len, x = ax + ex * t, z = az + ez * t, s = Math.Max(0, sa + (l.S[j + 1] - sa) * t), g = d.Sample(x, z);
                            int ci = NearField.CellOf(x, z, cm, n);
                            if (hy.LakeId[ci] == lake.Id || hy.LakeCover[ci] == lake.Id) s = Math.Max(s, lake.Level);
                            if (g > s + BankTolerance && !InFall(w, x, z, true))
                            { e.Add($"lake {lake.Id} outlet to the sea is blocked at {x:F0},{z:F0}: ground {g:F1} above water {s:F1}"); blocked = true; break; }
                        }
                    }
                    if (blocked) break;
                    if (hy.Rivers[r].End != RiverEnd.Junction) break;
                    int m = l.X.Length - 1;
                    px = l.X[m]; pz = l.Z[m]; ps = l.S[m]; start = 0;
                }
            }
        }

        static bool InFall(GeneratedWorld w, float x, float z, bool wall = false)
        {
            foreach (var s in w.Water.Rivers)
                foreach (var f in s.Falls)
                {
                    float dx = x - f.LipX, dz = z - f.LipZ, along = dx * f.DirX + dz * f.DirZ, side = dx * f.DirZ - dz * f.DirX, fs = f.ShelfHalf;
                    if (fs > 0)
                    {
                        float u = Math.Max(-1f, Math.Min(1f, side / fs));
                        along -= f.ShelfBow * u * u;
                    }
                    side = Math.Max(0, Math.Abs(side) - fs);
                    float c = f.PoolCentre, r = NearField.PoolReach(f.PoolCentre, f.PoolRadius) - c + NearStepM;
                    float lip = NearField.FallJagM + NearStepM, span = f.Width + NearField.PoolReach(f.PoolCentre, f.PoolRadius) + f.Drop / NearField.FallOutletSlope;
                    if (along <= lip && along >= -lip - Math.Max(0, side - 0.5f * f.Width) && side <= span) return true;
                    if (wall && along >= -NearField.FallJagM - NearStepM && (along - c) * (along - c) + side * side < r * r) return true;
                }
            return false;
        }

        static float Jump(TerrainDetail d, float x, float z, float ux, float uz)
        {
            float e = SeamEpsilonM;
            float a = d.Sample(x - 3 * e * ux, z - 3 * e * uz), b = d.Sample(x - e * ux, z - e * uz), c = d.Sample(x + e * ux, z + e * uz), f = d.Sample(x + 3 * e * ux, z + 3 * e * uz);
            return Math.Abs(c - b) - 1.5f * Math.Max(Math.Abs(b - a), Math.Abs(f - c));
        }

        static bool Crowded(List<RiverPiece> pieces, int self, float x, float z, float own, float stage)
        {
            for (int r = 0; r < pieces.Count; r++)
            {
                if (r == self) continue;
                var q = pieces[r];
                NearField.Project(q, x, z, out float d, out float t);
                float hw = q.Wa + (q.Wb - q.Wa) * t, s = q.Sa + (q.Sb - q.Sa) * t;
                float wb = hw + NearField.Bank(hw);
                if (d < hw || d < wb - 0.5f && (own < 0 || d < own - 0.5f || s < stage + BankTolerance)) return true;
                if (Math.Abs(r - self) > 2 && s < stage && d < wb + stage - s) return true;
            }
            return false;
        }

        public const float SharpBend = 0.06f, BarLead = 0.15f, BendShare = 0.7f, SteepGradient = 0.04f, StepRatio = 2.5f, StepShare = 0.8f, PlungeFrac = 0.25f, CutBankAt = 0.3f;
        public const int MinBends = 6, MinSteep = 4, StepWindow = 16;

        static void CheckRiverShape(GeneratedWorld w, List<string> e)
        {
            float cm = w.Config.CellMeters;
            var d = new TerrainDetail(w);
            var starts = new List<int>();
            var pieces = NearField.Pieces(w, starts);
            int bends = 0, cut = 0, steep = 0, stepped = 0, falls = 0, plunged = 0;
            for (int p = 2; p + 2 < pieces.Count; p++)
            {
                if (!Joined(pieces, p - 2, p + 2) || Spans(starts, p - 1, p + 2)) continue;
                var q = pieces[p];
                float x0 = pieces[p - 2].Ax, z0 = pieces[p - 2].Az, x2 = pieces[p + 2].Bx, z2 = pieces[p + 2].Bz;
                float ax = q.Ax - x0, az = q.Az - z0, bx = x2 - q.Ax, bz = z2 - q.Az, cx = x2 - x0, cz = z2 - z0;
                float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz), lc = (float)Math.Sqrt(cx * cx + cz * cz);
                if (la < 1e-3f || lb < 1e-3f || lc < 1e-3f) continue;
                float cross = ax * bz - az * bx, hw = q.Wa, depth = q.Da, s = q.Sa;
                if (hw < 3f || depth < 0.4f || s < Erosion.MinLandHeight + 2 * depth) continue;
                if (hw * 2 * Math.Abs(cross) / (la * lb * lc) < SharpBend) continue;
                float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, l = (float)Math.Sqrt(ex * ex + ez * ez);
                if (l < 1e-3f) continue;
                float side = cross > 0 ? 1f : -1f, nx = -ez / l * side, nz = ex / l * side;
                float ix = q.Ax + nx * 0.7f * hw, iz = q.Az + nz * 0.7f * hw, ox = q.Ax - nx * 0.7f * hw, oz = q.Az - nz * 0.7f * hw;
                float bank = hw + CutBankAt * NearField.Bank(hw);
                float jx = q.Ax + nx * bank, jz = q.Az + nz * bank, kx = q.Ax - nx * bank, kz = q.Az - nz * bank;
                if (Foreign(pieces, p, jx, jz) || Foreign(pieces, p, kx, kz)) continue;
                bends++;
                if (d.Sample(ix, iz) - d.Sample(ox, oz) >= BarLead * depth && d.Sample(kx, kz) > d.Sample(jx, jz)) cut++;
            }
            if (bends >= MinBends && cut < BendShare * bends)
                e.Add($"river bends are symmetric: {cut} of {bends} sharp bends have a point bar inside and a cut bank outside");

            for (int p = 0; p + StepWindow <= pieces.Count; p += StepWindow / 2)
            {
                int last = p + StepWindow - 1;
                if (!Joined(pieces, p, last)) continue;
                float run = 0, peak = 0;
                int at = p;
                for (int k = p; k <= last; k++)
                {
                    var q = pieces[k];
                    float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, l = (float)Math.Sqrt(ex * ex + ez * ez);
                    run += l;
                    float g = l > 1e-3f ? (q.Sa - q.Sb) / l : 0;
                    if (g > peak) { peak = g; at = k; }
                }
                float mean = run > 1e-3f ? (pieces[p].Sa - pieces[last].Sb) / run : 0;
                if (mean < SteepGradient || pieces[last].Sb < Erosion.MinLandHeight + 2 * pieces[last].Db) continue;
                steep++;
                if (peak < StepRatio * mean) continue;
                stepped++;
                if (at + 1 > last) continue;
                if (at > 0 && pieces[at - 1].Fr > 0) { falls++; plunged++; continue; }
                var below = pieces[at + 1];
                float drop = NearField.PoolDrop(pieces[at].Sa - pieces[at].Sb, pieces[at].Db), sb = below.Sb;
                if (Foreign(pieces, at + 1, below.Bx, below.Bz)) continue;
                falls++;
                if (d.Sample(below.Bx, below.Bz) <= sb - below.Db - PlungeFrac * drop) plunged++;
            }
            if (steep >= MinSteep && stepped < StepShare * steep)
                e.Add($"steep river reaches never step: {stepped} of {steep} steep windows hold a fall");
            if (falls >= MinSteep && plunged < StepShare * falls)
                e.Add($"river falls lack plunge pools: {plunged} of {falls}");
        }

        static bool Foreign(List<RiverPiece> pieces, int self, float x, float z)
        {
            for (int r = 0; r < pieces.Count; r++)
            {
                if (Math.Abs(r - self) <= 6 && Joined(pieces, Math.Min(r, self), Math.Max(r, self))) continue;
                var q = pieces[r];
                NearField.Project(q, x, z, out float d, out float t);
                float hw = q.Wa + (q.Wb - q.Wa) * t;
                if (d < NearField.Reach(hw, 0)) return true;
            }
            return false;
        }

        static bool Spans(List<int> starts, int a, int b)
        {
            foreach (int s in starts) if (s >= a && s <= b) return true;
            return false;
        }

        static bool Joined(List<RiverPiece> pieces, int a, int b)
        {
            for (int k = a; k < b; k++)
                if (pieces[k].Bx != pieces[k + 1].Ax || pieces[k].Bz != pieces[k + 1].Az) return false;
            return true;
        }

        public const float LatticeStepM = Features.LatticeStepM;

        static void CheckGeometry(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size, top = 0;
            for (int i = 1; i < w.Height.Length; i++) if (w.Height[i] > w.Height[top]) top = i;
            float cm = w.Config.CellMeters;
            var cols = new DetailColumns { Detail = new TerrainDetail(w), StepM = LatticeStepM };
            var c = new NetsChunk { X0 = (int)(top % n * cm / LatticeStepM) - 8, Z0 = (int)(top / n * cm / LatticeStepM) - 8, Cells = 16, StepM = LatticeStepM };
            SurfaceNets.HeightRange(cols, c, out int jMin, out int jMax);
            var g = new NetsBuffers();
            SurfaceNets.General(new HeightSolid<DetailColumns> { Columns = cols, StepM = c.StepM }, c, jMin, jMax, ref g);
            var f = new NetsBuffers();
            SurfaceNets.Heightfield(cols, c, ref f);
            if (f.Tris.Count == 0) { e.Add($"heightfield mesher made no triangles near cell {top}"); return; }
            if (!f.SameAs(g)) e.Add($"heightfield mesher differs from the general mesher near cell {top}");
        }

        static void CheckChunkBounds(GeneratedWorld w, EditGrid grid, List<string> e)
        {
            var placed = grid.Edits;
            var ids = new List<int>();
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var detail = new TerrainDetail(w);
            var all = new EditArray { Items = placed };
            var spots = new List<(float, float)>();
            int top = 0;
            for (int i = 1; i < w.Height.Length; i++) if (w.Height[i] > w.Height[top]) top = i;
            spots.Add((top % n * cm, top / n * cm));
            if (placed.Length > 0) spots.Add((placed[0].Ax, placed[0].Az));
            var pieces = NearField.Pieces(w);
            if (pieces.Count > 0) spots.Add((pieces[pieces.Count / 2].Ax, pieces[pieces.Count / 2].Az));
            foreach (var (sx, sz) in spots)
                for (int level = 0; level < NearField.NodeLevels; level++)
                {
                    float sm = NearField.NodeStep(level), size = NearField.NodeSize(level);
                    float x0 = (float)Math.Floor(sx / size) * size, z0 = (float)Math.Floor(sz / size) * size, x1 = x0 + size, z1 = z0 + size;
                    detail.ChunkRange(all, x0, z0, size, sm, out float lo, out float hi);
                    var cols = new DetailColumns { Detail = detail, StepM = sm };
                    var c = new NetsChunk { X0 = (int)(x0 / sm), Z0 = (int)(z0 / sm), Cells = NearField.NodeQuads, StepM = sm };
                    var meshed = new List<FeatureEdit>();
                    grid.Near(x0 - 0.5f * sm, z0 - 0.5f * sm, x1 + 0.5f * sm, z1 + 0.5f * sm, ids);
                    foreach (int q in ids) if (placed[q].MeshedIn(sm, x0, z0, x1, z1)) meshed.Add(placed[q].AtStep(sm));
                    var f = new NetsBuffers();
                    var list = new EditArray { Items = meshed.ToArray() };
                    if (meshed.Count > 0) SurfaceNets.Edited(cols, list, c, ref f);
                    else SurfaceNets.Heightfield(cols, c, ref f);
                    if (meshed.Count > 0 && !StitchTargetsMatch(cols, list, c, f))
                    { e.Add($"node {x0:F0},{z0:F0} at level {level} has border vertices a finer neighbour cannot stitch to"); return; }
                    foreach (int t in f.Tris)
                    {
                        int v = t * 6 + 1;
                        if (f.Verts[v] < lo || f.Verts[v] > hi)
                        { e.Add($"bounds {lo:F0}..{hi:F0} miss vertex {f.Verts[v - 1]:F0},{f.Verts[v]:F0},{f.Verts[v + 1]:F0} of node {x0:F0},{z0:F0} at level {level}"); return; }
                    }
                }
        }

        static bool StitchTargetsMatch(DetailColumns cols, EditArray edits, NetsChunk c, NetsBuffers f)
        {
            int w = c.Cells + 1;
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    if (i != 0 && k != 0 && i != c.Cells && k != c.Cells && (i + k) % 7 != 0) continue;
                    int col = k * w + i;
                    for (int j = f.ColumnStart(col); j < f.ColumnEnd(col); j++)
                    {
                        int cell = SurfaceNets.CoarseVertex(cols, edits, c.StepM, c.X0 - 1 + i, j, c.Z0 - 1 + k, out float x, out float y, out float z, out float nx, out float ny, out float nz);
                        if (cell < f.ColumnStart(col) || cell >= f.ColumnEnd(col)) return false;
                        int v = 6 * (f.ColumnBase(col) + cell - f.ColumnStart(col));
                        if (x != f.Verts[v] || y != f.Verts[v + 1] || z != f.Verts[v + 2]) return false;
                        if (nx != f.Verts[v + 3] || ny != f.Verts[v + 4] || nz != f.Verts[v + 5]) return false;
                    }
                }
            return true;
        }

        static void CheckArches(GeneratedWorld w, FeatureEdit[] edits, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            for (int a = 0; a < edits.Length; a++)
            {
                if (edits[a].Shape != FeatureEdit.Arch || (a > 0 && edits[a - 1].Shape == FeatureEdit.Arch && edits[a - 1].Bx == edits[a].Ax && edits[a - 1].Bz == edits[a].Az)) continue;
                int b = a;
                while (b + 1 < edits.Length && edits[b + 1].Shape == FeatureEdit.Arch && edits[b + 1].Ax == edits[b].Bx && edits[b + 1].Az == edits[b].Bz) b++;
                float ax = edits[a].Ax / cm, az = edits[a].Az / cm, bx = edits[b].Bx / cm, bz = edits[b].Bz / cm, sx = bx - ax, sz = bz - az, len = (float)Math.Sqrt(sx * sx + sz * sz);
                string at = $"arch at {edits[a].Ax:F0},{edits[a].Az:F0}";
                int fa = Node(n, ax, az), fb = Node(n, bx, bz);
                if (fa < 0 || fb < 0 || !Features.Dry(w, fa) || !Features.Dry(w, fb)) { e.Add(at + " has a foot off dry ground"); return; }
                bool channel = false, across = false;
                int steps = Math.Max(2, (int)Math.Ceiling(2 * len));
                for (int s = 1; s < steps && !across; s++)
                {
                    int i = Node(n, ax + sx * s / steps, az + sz * s / steps);
                    if (i < 0 || !Features.Channel(w, i)) continue;
                    channel = true;
                    if (Features.FlowDir(w, i, out float fx, out float fz) && Math.Abs(fx * sx + fz * sz) <= Features.ArchAcross * len) across = true;
                }
                if (!channel) { e.Add(at + " spans no river or dry bed"); return; }
                if (!across) { e.Add(at + " runs along the flow"); return; }
            }
        }

        static int Node(int n, float x, float z)
        {
            int ix = (int)Math.Round(x), iz = (int)Math.Round(z);
            return ix >= 0 && iz >= 0 && ix < n && iz < n ? iz * n + ix : -1;
        }

        static void CheckFeatures(GeneratedWorld w, List<string> e)
        {
            var detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail, out var caves, out var sites);
            float cm = w.Config.CellMeters;
            var grid = new EditGrid(edits, w.Config.Size * cm);
            CheckChunkBounds(w, grid, e);
            if (sites != null) CheckSiteFootprints(w, grid, sites, e);
            var field = w.Objects != null && w.Biome != null ? new ObjectField(w, detail, edits, w.Objects) : null;
            if (field != null) CheckObjects(w, detail, grid, field, e);
            if (field != null) CheckSites(w, detail, grid, field, e);
            if (field != null) CheckStructures(w, detail, grid, field, e);
            if (field != null) CheckDiscoveries(w, field, e);
            CheckCavePaths(w, detail, edits, e);
            CheckCaves(w, detail, edits, caves, e);
            CheckCaveNormals(w, detail, grid, caves, e);
            CheckCliffFoot(w, detail, edits, e);
            CheckLandforms(w, edits, e);
            CheckArches(w, edits, e);
            if (w.Objects != null && w.Biome != null) CheckBoulders(w, detail, grid, e);
            foreach (var x in edits)
                if (x.Shape != FeatureEdit.Boulder && x.Shape != FeatureEdit.Spring && !Features.Inside(x, cm)) { e.Add($"feature edit at {x.Ax:F0},{x.Az:F0} leaves its tile interior"); return; }
            var seen = new bool[FeatureEdit.Shapes];
            foreach (var x in edits)
            {
                if (x.Shape == FeatureEdit.Boulder || x.Shape == FeatureEdit.Spring || seen[x.Shape]) continue;
                seen[x.Shape] = true;
                float minX = x.MinX, maxX = x.MaxX, minZ = x.MinZ, maxZ = x.MaxZ;
                int tile = Features.TileOf(x, cm);
                bool cave = x.Shape == FeatureEdit.Cave;
                float s = cave ? NearField.NodeStep(0) : LatticeStepM;
                var group = new List<FeatureEdit>();
                foreach (var y in edits)
                    if ((y.Shape == x.Shape || (x.Shape == FeatureEdit.Bridge && (y.Shape == FeatureEdit.Cave || y.Shape == FeatureEdit.CaveRoom))) && Features.TileOf(y, cm) == tile
                        && (!cave || (y.MinX < x.MaxX && y.MaxX > x.MinX && y.MinZ < x.MaxZ && y.MaxZ > x.MinZ)))
                    {
                        group.Add(y);
                        minX = Math.Min(minX, y.MinX); maxX = Math.Max(maxX, y.MaxX);
                        minZ = Math.Min(minZ, y.MinZ); maxZ = Math.Max(maxZ, y.MaxZ);
                    }
                var cols = new DetailColumns { Detail = detail, StepM = s };
                int x0 = (int)Math.Floor(minX / s) - 3, z0 = (int)Math.Floor(minZ / s) - 3;
                int cells = Math.Max((int)Math.Ceiling(maxX / s) + 3 - x0, (int)Math.Ceiling(maxZ / s) + 3 - z0);
                var c = new NetsChunk { X0 = x0, Z0 = z0, Cells = cells, StepM = s };
                SurfaceNets.HeightRange(cols, c, out int jMin, out int jMax);
                var list = new EditArray { Items = group.ToArray() };
                foreach (var y in group)
                {
                    jMin = Math.Min(jMin, (int)Math.Floor(y.MinY / s) - 2);
                    jMax = Math.Max(jMax, (int)Math.Floor(y.MaxY / s) + 3);
                }
                var f = new NetsBuffers();
                SurfaceNets.Edited(cols, list, c, ref f);
                var g = new NetsBuffers();
                SurfaceNets.General(new EditedSolid<DetailColumns, EditArray> { Columns = cols, Edits = list, StepM = s }, c, jMin, jMax, ref g);
                var h = new NetsBuffers();
                SurfaceNets.Heightfield(cols, c, ref h);
                string kind = x.Shape == FeatureEdit.Arch ? "arch" : x.Shape == FeatureEdit.Cave ? "cave" : x.Shape == FeatureEdit.CaveRoom ? "cave room" : x.Shape == FeatureEdit.Lip ? "mesa lip" : x.Shape == FeatureEdit.Bridge ? "rock bridge" : Landforms.Of(x.Shape) ? Landforms.Name(x.Shape) : "overhang";
                if (!f.SameSurfaceAs(g)) e.Add($"{kind} mesh at {x.Ax:F0},{x.Az:F0} differs from the general mesher");
                if (f.SameSurfaceAs(h)) e.Add($"{kind} at {x.Ax:F0},{x.Az:F0} leaves the ground unchanged");
                if (!f.SameRimAs(h, cells)) e.Add($"{kind} at {x.Ax:F0},{x.Az:F0} moves chunk rim vertices");
                if (x.Shape == FeatureEdit.Cave)
                {
                    int bi = (int)Math.Round(x.Bx / s), bj = (int)Math.Round(x.By / s), bk = (int)Math.Round(x.Bz / s);
                    var solid = new EditedSolid<DetailColumns, EditArray> { Columns = cols, Edits = list, StepM = s };
                    if (solid.Density(bi, bj, bk) >= 0) e.Add($"cave at {x.Ax:F0},{x.Az:F0} is not hollow at its first bend");
                    if (cols.Height(bi, bk) < x.By + x.R) e.Add($"cave at {x.Ax:F0},{x.Az:F0} has no roof at its first bend");
                }
            }
        }

        static void CheckLandforms(GeneratedWorld w, FeatureEdit[] edits, List<string> e)
        {
            float cm = w.Config.CellMeters, span = w.Config.Size * cm;
            int n = w.Config.Size;
            var count = new Dictionary<int, int>();
            foreach (var x in edits)
            {
                if (!Landforms.Of(x.Shape)) continue;
                string at = $"{Landforms.Name(x.Shape)} at {x.Ax:F0},{x.Az:F0}";
                if (x.Kind != FeatureEdit.Add) { e.Add(at + " carves solid"); return; }
                if (x.MinX < 0 || x.MinZ < 0 || x.MaxX > span || x.MaxZ > span) { e.Add(at + " leaves the map"); return; }
                int tile = Features.TileOf(x, cm);
                count.TryGetValue(tile, out int k);
                count[tile] = ++k;
                if (k > Landforms.TileBudget) { e.Add($"{at} pushes its tile past {Landforms.TileBudget} landform edits"); return; }
                int i0 = Math.Max(0, (int)Math.Floor(x.MinX / cm)), i1 = Math.Min(n - 1, (int)Math.Ceiling(x.MaxX / cm));
                int j0 = Math.Max(0, (int)Math.Floor(x.MinZ / cm)), j1 = Math.Min(n - 1, (int)Math.Ceiling(x.MaxZ / cm));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        if (x.Under(i * cm, j * cm) && !Features.Dry(w, j * n + i)) { e.Add(at + " stands in water"); return; }
                foreach (var y in edits)
                    if (!Landforms.Of(y.Shape) && y.Shape != FeatureEdit.Boulder && Features.TileOf(y, cm) == tile
                        && x.MinX < y.MaxX && x.MaxX > y.MinX && x.MinY < y.MaxY && x.MaxY > y.MinY && x.MinZ < y.MaxZ && x.MaxZ > y.MinZ) { e.Add(at + " overlaps a terrain feature"); return; }
            }
        }

        static void CheckCavePaths(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, List<string> e)
        {
            float cm = w.Config.CellMeters;
            foreach (var p in edits)
            {
                if (p.Shape != FeatureEdit.Cave) continue;
                float run = (float)Math.Sqrt((p.Bx - p.Ax) * (p.Bx - p.Ax) + (p.Bz - p.Az) * (p.Bz - p.Az));
                if (Math.Abs(p.By - p.Ay) > Features.CaveGrade * run + 1e-3f) { e.Add($"cave path at {p.Ax:F0},{p.Az:F0} is steeper than the cave grade"); return; }
                int tile = Features.TileOf(p, cm);
                var near = new List<FeatureEdit>();
                foreach (var y in edits) if (Features.TileOf(y, cm) == tile) near.Add(y);
                string fault = Underground.PathFault(detail, near.ToArray(), p);
                if (fault != null) { e.Add(fault); return; }
            }
        }

        public const float WalkStride = Underground.WalkStride;

        public static void CheckCaves(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves, List<string> e)
        {
            float cm = w.Config.CellMeters;
            foreach (var x in edits)
                if ((x.Shape == FeatureEdit.Cave || x.Shape == FeatureEdit.CaveRoom) && Underground.Wet(w, x)) { e.Add($"cave at {x.Ax:F0},{x.Az:F0} breaks into the sea or a lake"); return; }
            foreach (var g in caves)
            {
                var near = new List<FeatureEdit>();
                foreach (var q in edits) if (Features.TileOf(q, cm) == g.Tile) near.Add(q);
                var items = near.ToArray();
                foreach (var q in items)
                {
                    if (q.Shape != FeatureEdit.Bridge) continue;
                    string fault = Underground.BridgeFault(detail, items, q);
                    if (fault != null) { e.Add(fault); return; }
                }
                string tight = null;
                foreach (var edge in g.Edges) if ((tight = Underground.Cramped(detail, items, g, edge)) != null) break;
                if (tight != null) e.Add(tight);
                string why = Underground.Unreached(detail, items, g);
                if (why != null) e.Add(why);
                if (tight != null || why != null) return;
            }
        }

        public const float CaveNormalDeg = 1.5f;
        public const int CaveNormalLevels = 3, CaveCoverSteps = 4;

        public static void CheckCaveNormals(GeneratedWorld w, TerrainDetail detail, EditGrid grid, CaveGraph[] caves, List<string> e)
        {
            if (caves == null || caves.Length == 0) return;
            CaveNode spot = null;
            foreach (var g in caves) foreach (var nd in g.Nodes) if (spot == null || nd.Hall && !spot.Hall) spot = nd;
            if (spot == null) return;
            var placed = grid.Edits;
            var ids = new List<int>();
            for (int level = 0; level < CaveNormalLevels; level++)
            {
                float sm = NearField.NodeStep(level), size = NearField.NodeSize(level);
                float x0 = (float)Math.Floor(spot.X / size) * size, z0 = (float)Math.Floor(spot.Z / size) * size, x1 = x0 + size, z1 = z0 + size;
                var meshed = new List<FeatureEdit>();
                grid.Near(x0 - 0.5f * sm, z0 - 0.5f * sm, x1 + 0.5f * sm, z1 + 0.5f * sm, ids);
                foreach (int q in ids) if (placed[q].MeshedIn(sm, x0, z0, x1, z1)) meshed.Add(placed[q].AtStep(sm));
                if (meshed.Count == 0) continue;
                var list = new EditArray { Items = meshed.ToArray() };
                var f = new NetsBuffers();
                SurfaceNets.Edited(new DetailColumns { Detail = detail, StepM = sm }, list, new NetsChunk { X0 = (int)(x0 / sm), Z0 = (int)(z0 / sm), Cells = NearField.NodeQuads, StepM = sm }, ref f);
                var cols = new DetailColumns { Detail = detail, StepM = sm };
                float Base(int i, float y, int k) => Features.Level(cols, cols.Height(i, k) - y, i, k, sm);
                float Field(float x, float y, float z)
                {
                    int i = (int)Math.Floor(x / sm), k = (int)Math.Floor(z / sm);
                    float u = x / sm - i, w = z / sm - k;
                    float a = Base(i, y, k) + (Base(i + 1, y, k) - Base(i, y, k)) * u, b = Base(i, y, k + 1) + (Base(i + 1, y, k + 1) - Base(i, y, k + 1)) * u;
                    return Features.Edit(list, a + (b - a) * w, x, y, z);
                }
                var miss = new List<double>();
                foreach (int t in new HashSet<int>(f.Tris))
                {
                    float x = f.Verts[6 * t], y = f.Verts[6 * t + 1], z = f.Verts[6 * t + 2];
                    bool cave = false;
                    foreach (var m in meshed) if (m.Kind == FeatureEdit.Carve && (m.Shape == FeatureEdit.Cave || m.Shape == FeatureEdit.CaveRoom) && m.Reach(x, y, z, out _)) { cave = true; break; }
                    if (!cave || Math.Abs(Field(x, y, z)) > 0.75f * sm || detail.Sample(x, z) - y < CaveCoverSteps * sm) continue;
                    const float d = 0.05f;
                    float gx = Field(x + d, y, z) - Field(x - d, y, z), gy = Field(x, y + d, z) - Field(x, y - d, z), gz = Field(x, y, z + d) - Field(x, y, z - d);
                    float gl = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    if (!(gl > 0)) continue;
                    float dot = -(gx * f.Verts[6 * t + 3] + gy * f.Verts[6 * t + 4] + gz * f.Verts[6 * t + 5]) / gl;
                    miss.Add(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180 / Math.PI);
                }
                if (miss.Count == 0) continue;
                miss.Sort();
                double mid = miss[miss.Count / 2];
                if (mid > CaveNormalDeg)
                { e.Add($"cave wall normals near {spot.X:F0},{spot.Z:F0} at level {level} miss the field gradient by a median {mid:F1} degrees"); return; }
            }
        }

        public const float CliffSlope = 1.1f, FootSlope = 0.35f, FootWalkM = 24f;
        public const int CliffFeet = 160;

        static void CheckCliffFoot(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, s = LatticeStepM;
            var h = w.Height;
            var ground = new DetailGround { Detail = detail };
            var none = new EditArray { Items = Array.Empty<FeatureEdit>() };
            int cliffs = 0;
            for (int i = n + 1; i < n * n - n - 1; i++)
            {
                int x = i % n;
                if (x == 0 || x == n - 1 || h[i] <= 0) continue;
                float gx = (h[i + 1] - h[i - 1]) / (2 * cm), gz = (h[i + n] - h[i - n]) / (2 * cm);
                if (gx * gx + gz * gz > CliffSlope * CliffSlope) cliffs++;
            }
            if (cliffs == 0) return;
            int stride = Math.Max(1, cliffs / CliffFeet), seen = 0;
            for (int i = n + 1; i < n * n - n - 1; i++)
            {
                int ix = i % n;
                if (ix == 0 || ix == n - 1 || h[i] <= 0) continue;
                float gx = (h[i + 1] - h[i - 1]) / (2 * cm), gz = (h[i + n] - h[i - n]) / (2 * cm), g2 = gx * gx + gz * gz;
                if (g2 <= CliffSlope * CliffSlope || seen++ % stride != 0) continue;
                float inv = 1f / (float)Math.Sqrt(g2), dx = -gx * inv, dz = -gz * inv;
                float fx = ix * cm, fz = i / n * cm;
                bool found = false;
                for (int t = 0; t < 200 && !found; t++, fx += 2f * dx, fz += 2f * dz)
                    found = BaseSlope(detail, fx, fz) < FootSlope;
                if (!found || !FootClear(w, detail, edits, fx, fz, -dz, dx)) continue;
                float px = fx + 0.5f * FootWalkM * dz, pz = fz - 0.5f * FootWalkM * dx, py = detail.Sample(px, pz);
                int steps = (int)(FootWalkM / WalkStride);
                float sx = -dz * WalkStride, sz = dx * WalkStride, fall = Walk.FallSpeed * WalkStride / 6f;
                for (int t = 0; t < steps; t++)
                {
                    float ox = px, oz = pz;
                    Walk.Step(ground, none, s, ref px, ref py, ref pz, sx, sz, fall);
                    if (Math.Abs(px - ox - sx) > 1e-3f || Math.Abs(pz - oz - sz) > 1e-3f) { e.Add($"walker at the cliff foot near {fx:F0},{fz:F0} is stopped at {px:F0},{py:F0},{pz:F0}"); return; }
                }
            }
        }

        static float BaseSlope(TerrainDetail d, float x, float z)
        {
            const float r = 2f;
            float gx = (d.Smooth(x + r, z) - d.Smooth(x - r, z)) / (2 * r), gz = (d.Smooth(x, z + r) - d.Smooth(x, z - r)) / (2 * r);
            return (float)Math.Sqrt(gx * gx + gz * gz);
        }

        static bool FootClear(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, float fx, float fz, float ux, float uz)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, half = 0.5f * FootWalkM;
            for (float t = -half - 2f; t <= half + 2f; t += 1f)
            {
                float x = fx + ux * t, z = fz + uz * t;
                if (x < cm || z < cm || x > (n - 2) * cm || z > (n - 2) * cm) return false;
                int cell = NearField.CellOf(x, z, cm, n);
                if (w.Sea[cell] || (w.Water != null && w.Water.LakeId[cell] >= 0)) return false;
                if (detail.Cells.PieceStart(cell) != detail.Cells.PieceStart(cell + 1)) return false;
                if (BaseSlope(detail, x, z) > FootSlope + 0.1f) return false;
                foreach (var f in edits)
                    if (x >= f.MinX - 2f && x <= f.MaxX + 2f && z >= f.MinZ - 2f && z <= f.MaxZ + 2f) return false;
            }
            return true;
        }

        static void CheckSketch(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            var s = w.Sketch;
            var hy = w.Water;

            var island = Components(n, w.Sea);
            for (int k = 0; k < s.Landmasses.Count; k++)
            {
                var m = s.Landmasses[k];
                int a = island[(int)m.Y * n + (int)m.X];
                if (a < 0) { e.Add($"landmass {k} centre is sea"); continue; }
                for (int j = 0; j < k; j++)
                {
                    var o = s.Landmasses[j];
                    if (island[(int)o.Y * n + (int)o.X] == a) e.Add($"landmasses {j} and {k} are joined");
                }
            }
            CheckShores(w, island, e);
            CheckBeachShelf(w, e);
            if (s.Landmasses.Count > 1)
            {
                int a = island[(int)s.Landmasses[0].Y * n + (int)s.Landmasses[0].X];
                var notA = new bool[n * n];
                for (int i = 0; i < n * n; i++) notA[i] = a < 0 || island[i] != a;
                var dist = WorldSketch.CoastDistance(n, notA);
                float min = w.Config.ChannelFraction * n - WorldSketch.LandmarkPadCells - 1;
                for (int k = 1; k < s.Landmasses.Count; k++)
                {
                    var m = s.Landmasses[k];
                    int b = island[(int)m.Y * n + (int)m.X];
                    if (b < 0 || b == a) continue;
                    float gap = float.MaxValue;
                    for (int i = 0; i < n * n; i++) if (island[i] == b) gap = Math.Min(gap, dist[i]);
                    if (gap < min) e.Add($"channel between landmasses 0 and {k} is {gap:F0} cells");
                }
            }

            for (int k = 0; k < s.Corridors.Count; k++)
            {
                var cor = s.Corridors[k];
                float total = 0;
                for (int p = 1; p < cor.X.Length; p++) total += Length(cor, p);
                int samples = 0, near = 0; float along = 0;
                for (int p = 1; p < cor.X.Length; p++)
                {
                    float len = Length(cor, p);
                    for (float t = 0; t < len; t += 1f)
                    {
                        float pos = along + t;
                        if (pos < 0.25f * total) continue;
                        float x = cor.X[p - 1] + (cor.X[p] - cor.X[p - 1]) * t / len;
                        float y = cor.Y[p - 1] + (cor.Y[p] - cor.Y[p - 1]) * t / len;
                        int cx = (int)x, cy = (int)y;
                        if (cx < 0 || cy < 0 || cx >= n || cy >= n || w.Sea[cy * n + cx]) continue;
                        samples++;
                        if (WaterWithin(n, hy, w.Sea, cx, cy, (int)Math.Ceiling(cor.Width))) near++;
                    }
                    along += len;
                }
                if (samples > 0 && near < CorridorFollowShare * samples)
                    e.Add($"corridor {k}: river follows only {near} of {samples} points");
            }

            if (w.Biome != null)
                foreach (var r in s.Regions)
                {
                    if (Array.IndexOf(w.BiomeIds, r.Biome) < 0) { e.Add($"region biome {r.Biome} is not in the content"); continue; }
                    int bad = 0, total = 0;
                    for (int y = Math.Max(0, (int)(r.Y - r.Radius)); y < Math.Min(n, (int)(r.Y + r.Radius) + 1); y++)
                        for (int x = Math.Max(0, (int)(r.X - r.Radius)); x < Math.Min(n, (int)(r.X + r.Radius) + 1); x++)
                        {
                            int i = y * n + x;
                            float dx = x - r.X, dy = y - r.Y;
                            if (w.Sea[i] || hy.LakeId[i] >= 0 || dx * dx + dy * dy > Biomes.RegionCore * Biomes.RegionCore * r.Radius * r.Radius) continue;
                            total++;
                            if (w.Biome[i] < 0 || w.BiomeIds[w.Biome[i]] != r.Biome) bad++;
                        }
                    if (bad > 0) e.Add($"region {r.Biome}: {bad} of {total} core cells have another biome");
                }

            for (int k = 0; k < s.Lakes.Count; k++)
                if (hy.LakeId[(int)s.Lakes[k].Y * n + (int)s.Lakes[k].X] < 0) e.Add($"sketched lake {k} is dry");

            for (int k = 0; k < s.Landmarks.Count; k++)
            {
                var l = s.Landmarks[k];
                int i = (int)l.Y * n + (int)l.X;
                if (w.Sea[i]) e.Add($"landmark {k} ({l.Kind}) is in the sea");
                else if (hy.LakeId[i] >= 0) e.Add($"landmark {k} ({l.Kind}) is under lake {hy.LakeId[i]}");
            }
        }

        static void CheckShores(GeneratedWorld w, int[] island, List<string> e)
        {
            int n = w.Config.Size, need = Math.Max(3, n / 40);
            float beachMax = Erosion.MinLandHeight + 2.5f + w.Config.BeachGrade * w.Config.CellMeters;
            for (int k = 0; k < w.Sketch.Landmasses.Count; k++)
            {
                var m = w.Sketch.Landmasses[k];
                int a = island[(int)m.Y * n + (int)m.X];
                if (a < 0) continue;
                int cliff = 0, beach = 0;
                for (int y = 1; y < n - 1; y++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        int i = y * n + x;
                        if (island[i] != a) continue;
                        bool coast = false, shallow = true;
                        for (int d = 0; d < 8; d++)
                        {
                            int j = (y + Grid.DY[d]) * n + x + Grid.DX[d];
                            if (!w.Sea[j]) continue;
                            coast = true;
                            if (w.Height[j] < -BeachMaxDepth) shallow = false;
                        }
                        if (!coast) continue;
                        if (w.Height[i] >= CliffMinRise) cliff++;
                        if (w.Height[i] <= beachMax && shallow) beach++;
                    }
                if (cliff < need) e.Add($"landmass {k} has {cliff} cliff cells");
                if (beach < need) e.Add($"landmass {k} has {beach} beach cells");
            }
        }

        static void CheckBeachShelf(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size, pairs = 0, flat = 0;
            for (int y = 2; y < n - 2; y++)
                for (int x = 2; x < n - 2; x++)
                {
                    int i = y * n + x;
                    if (!w.Sea[i] || TouchesLand(w, n, i, 0)) continue;
                    for (int d = 0; d < 8; d++)
                    {
                        int j = i + Grid.DY[d] * n + Grid.DX[d];
                        if (!w.Sea[j] || !TouchesLand(w, n, j, 0.5f)) continue;
                        pairs++;
                        if (w.Height[i] > w.Height[j] - BeachShelfStep) flat++;
                        break;
                    }
                }
            if (pairs > 0 && flat > pairs * BeachShelfShare) e.Add($"beach seabed is flat beside the shore at {flat} of {pairs} cells");
        }

        static bool TouchesLand(GeneratedWorld w, int n, int i, float beach)
        {
            for (int d = 0; d < 8; d++)
            {
                int j = i + Grid.DY[d] * n + Grid.DX[d];
                if (!w.Sea[j] && w.Sketch.Beach[j] >= beach) return true;
            }
            return false;
        }

        static float Length(CorridorSketch c, int p)
        {
            float dx = c.X[p] - c.X[p - 1], dy = c.Y[p] - c.Y[p - 1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static bool WaterWithin(int n, Hydrology hy, bool[] sea, int cx, int cy, int r)
        {
            for (int y = Math.Max(0, cy - r); y <= Math.Min(n - 1, cy + r); y++)
                for (int x = Math.Max(0, cx - r); x <= Math.Min(n - 1, cx + r); x++)
                {
                    int i = y * n + x;
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                    if (hy.IsRiver[i] || hy.LakeId[i] >= 0) return true;
                }
            return false;
        }

        static int[] Components(int n, bool[] sea)
        {
            var id = new int[n * n];
            for (int i = 0; i < id.Length; i++) id[i] = -1;
            var queue = new List<int>();
            int next = 0;
            for (int s = 0; s < n * n; s++)
            {
                if (sea[s] || id[s] >= 0) continue;
                queue.Clear(); queue.Add(s); id[s] = next;
                for (int q = 0; q < queue.Count; q++)
                {
                    int i = queue[q], x = i % n, y = i / n;
                    if (x > 0 && !sea[i - 1] && id[i - 1] < 0) { id[i - 1] = next; queue.Add(i - 1); }
                    if (x < n - 1 && !sea[i + 1] && id[i + 1] < 0) { id[i + 1] = next; queue.Add(i + 1); }
                    if (y > 0 && !sea[i - n] && id[i - n] < 0) { id[i - n] = next; queue.Add(i - n); }
                    if (y < n - 1 && !sea[i + n] && id[i + n] < 0) { id[i + n] = next; queue.Add(i + n); }
                }
                next++;
            }
            return id;
        }
    }
}
