using System;
using System.Collections.Generic;
using System.Diagnostics;

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

        public static readonly string[] Groups = { "core", "hydrology", "biomes", "sketch", "surface", "meshing", "caves", "sites", "objects", "creatures" };

        sealed class GroupClock
        {
            readonly ICollection<string> only;
            readonly IDictionary<string, double> ms;
            readonly Stopwatch watch = new Stopwatch();
            string at;

            public GroupClock(ICollection<string> only, IDictionary<string, double> ms) { this.only = only; this.ms = ms; }

            public bool Has(string g) => only == null || only.Contains(g);

            public bool Start(string g)
            {
                Stop();
                if (!Has(g)) return false;
                Time(g);
                return true;
            }

            public void Time(string g)
            {
                Stop();
                at = g;
                watch.Restart();
            }

            public void Stop()
            {
                if (at == null) return;
                if (ms != null) { ms.TryGetValue(at, out double t); ms[at] = t + watch.Elapsed.TotalMilliseconds; }
                at = null;
            }
        }

        public static List<string> Run(GeneratedWorld w) => Run(w, null, null);

        public static List<string> Run(GeneratedWorld w, ICollection<string> groups, IDictionary<string, double> ms)
        {
            var e = new List<string>();
            var clock = new GroupClock(groups, ms);
            if (clock.Start("core")) CheckCore(w, e);
            if (clock.Start("hydrology")) CheckHydrology(w, e);
            if (clock.Start("biomes")) CheckBiomes(w, e);
            if (clock.Start("sketch") && w.Sketch != null) { CheckSketch(w, e); CheckMesas(w, e); }
            if (clock.Start("surface")) { CheckDetail(w, e); CheckNearField(w, e); CheckGeometry(w, e); }
            CheckFeatures(w, e, clock);
            clock.Stop();
            return e;
        }

        static void CheckCore(GeneratedWorld w, List<string> e)
        {
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
        }

        static void CheckHydrology(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            var hy = w.Water;

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

            CheckRiverMouths(w, e);
            CheckLakeOutlets(w, e);
            CheckFalls(w, e);
            CheckRiverShape(w, e);
        }

        static void CheckBiomes(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            if (w.Biome != null)
                for (int i = 0; i < n * n; i++)
                    if (!w.Sea[i] && w.Biome[i] < 0 && w.Water.LakeId[i] < 0) { e.Add($"land cell {i} has no biome"); break; }

            CheckTint(w, e);
            CheckLakeCover(w, e);
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

        struct ColumnCache : IColumnField
        {
            DetailColumns inner;
            int i0, k0, w;
            float[] h;

            public static ColumnCache Over(DetailColumns inner, int i0, int k0, int w)
            {
                var c = new ColumnCache { inner = inner, i0 = i0, k0 = k0, w = w, h = new float[w * w] };
                for (int k = 0; k < w; k++)
                    for (int i = 0; i < w; i++)
                        c.h[k * w + i] = inner.Height(i0 + i, k0 + k);
                return c;
            }

            public float Height(int i, int k)
            {
                int a = i - i0, b = k - k0;
                return a >= 0 && b >= 0 && a < w && b < w ? h[b * w + a] : inner.Height(i, k);
            }
        }

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
            int brook = Array.FindIndex(placed, CaveBrooks.Channel);
            if (brook >= 0)
            {
                spots.Add((placed[brook].Ax, placed[brook].Az));
                if (!FineSeamsMatch(detail, grid, placed[brook], out string at)) { e.Add(at); return; }
            }
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

        static bool FineSeamsMatch(TerrainDetail detail, EditGrid grid, FeatureEdit b, out string at)
        {
            at = null;
            float s = NearField.NodeStep(0), size = NearField.NodeSize(0);
            int n0x = (int)Math.Floor(b.MinX / size), n1x = (int)Math.Floor(b.MaxX / size), n0z = (int)Math.Floor(b.MinZ / size), n1z = (int)Math.Floor(b.MaxZ / size);
            var ids = new List<int>();
            var fine = new HashSet<(int, int)>();
            grid.Near((n0x - 1) * size, (n0z - 1) * size, (n1x + 2) * size, (n1z + 2) * size, ids);
            foreach (int q in ids)
            {
                var c = grid.Edits[q];
                if (!c.Fine) continue;
                for (int iz = (int)Math.Floor(c.MinZ / size); iz <= (int)Math.Floor(c.MaxZ / size); iz++)
                    for (int ix = (int)Math.Floor(c.MinX / size); ix <= (int)Math.Floor(c.MaxX / size); ix++)
                        fine.Add((ix, iz));
            }
            var cols = new DetailColumns { Detail = detail, StepM = s };
            var meshed = new Dictionary<(int, int), (NetsChunk, NetsBuffers)>();
            for (int fz = n0z; fz <= n1z; fz++)
                for (int fx = n0x; fx <= n1x; fx++)
                {
                    var near = MeshedAt(grid, s, (fx - 1) * size, (fz - 1) * size, (fx + 2) * size, (fz + 2) * size, ids);
                    for (int d = 0; d < 9; d++)
                    {
                        int nx = fx + d % 3 - 1, nz = fz + d / 3 - 1;
                        if (fine.Contains((nx, nz))) continue;
                        if (!meshed.TryGetValue((nx, nz), out var m))
                        {
                            var list = MeshedAt(grid, s, nx * size, nz * size, (nx + 1) * size, (nz + 1) * size, ids);
                            var c = new NetsChunk { X0 = nx * NearField.NodeQuads, Z0 = nz * NearField.NodeQuads, Cells = NearField.NodeQuads, StepM = s };
                            var f = new NetsBuffers();
                            if (list.Count > 0) SurfaceNets.Edited(cols, list, c, ref f);
                            else SurfaceNets.Heightfield(cols, c, ref f);
                            m = (c, f);
                            meshed[(nx, nz)] = m;
                        }
                        if (!StitchTargetsMatch(cols, near, m.Item1, m.Item2))
                        {
                            at = $"fine brook node {fx * size:F0},{fz * size:F0} cannot stitch to its level 0 neighbour {nx * size:F0},{nz * size:F0}";
                            return false;
                        }
                    }
                }
            return true;
        }

        static EditArray MeshedAt(EditGrid grid, float s, float x0, float z0, float x1, float z1, List<int> ids)
        {
            var list = new List<FeatureEdit>();
            grid.Near(x0 - 0.5f * s, z0 - 0.5f * s, x1 + 0.5f * s, z1 + 0.5f * s, ids);
            foreach (int q in ids) if (grid.Edits[q].MeshedIn(s, x0, z0, x1, z1)) list.Add(grid.Edits[q].AtStep(s));
            return new EditArray { Items = list.ToArray() };
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

        static void CheckFeatures(GeneratedWorld w, List<string> e, GroupClock clock)
        {
            bool surface = clock.Has("surface") || clock.Has("meshing"), caved = clock.Has("caves"), sited = clock.Has("sites"), objects = clock.Has("objects");
            if (!surface && !caved && !sited && !objects) return;
            clock.Time("features");
            var detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail, out var caves, out var sites);
            float cm = w.Config.CellMeters;
            var grid = new EditGrid(edits, w.Config.Size * cm);
            var field = (sited || objects) && w.Objects != null && w.Biome != null ? new ObjectField(w, detail, edits, w.Objects) : null;
            if (clock.Start("sites"))
            {
                if (sites != null) CheckSiteFootprints(w, grid, sites, e);
                if (field != null) { CheckSites(w, detail, grid, field, e); CheckStructures(w, detail, grid, field, e); CheckDiscoveries(w, field, e); }
            }
            if (clock.Start("objects"))
            {
                if (field != null) CheckObjects(w, detail, grid, field, e);
                CheckLandforms(w, edits, e);
                CheckArches(w, edits, e);
                if (w.Objects != null && w.Biome != null) CheckBoulders(w, detail, grid, e);
            }
            if (clock.Start("caves"))
            {
                CheckCavePaths(w, detail, edits, e);
                CheckCaves(w, detail, edits, caves, e);
                CheckMines(w, detail, edits, caves, e);
                CheckDungeonLinks(w, detail, edits, caves, sites, e);
                CheckCaveIce(w, edits, caves, e);
                CheckCaveNormals(w, detail, grid, caves, e);
            }
            if (clock.Start("surface"))
            {
                CheckChunkBounds(w, grid, e);
                CheckCliffFoot(w, detail, edits, e);
                foreach (var x in edits)
                    if (x.Shape != FeatureEdit.Boulder && x.Shape != FeatureEdit.Spring && !Features.Inside(x, cm)) { e.Add($"feature edit at {x.Ax:F0},{x.Az:F0} leaves its tile interior"); return; }
            }
            if (!clock.Start("meshing")) return;
            var seen = new bool[FeatureEdit.Shapes];
            foreach (var x in edits)
            {
                if (x.Shape == FeatureEdit.Boulder || x.Shape == FeatureEdit.Spring || x.Shape == FeatureEdit.Speleothem || seen[x.Shape]) continue;
                seen[x.Shape] = true;
                float minX = x.MinX, maxX = x.MaxX, minZ = x.MinZ, maxZ = x.MaxZ;
                int tile = Features.TileOf(x, cm);
                bool cave = x.Shape == FeatureEdit.Cave || x.Shape == FeatureEdit.Sink || x.Shape == FeatureEdit.Vault || x.Shape == FeatureEdit.Mine;
                float s = cave || x.Shape == FeatureEdit.Shaft ? NearField.NodeStep(0) : LatticeStepM;
                var group = new List<FeatureEdit>();
                foreach (var y in edits)
                    if ((y.Shape == x.Shape || (x.Shape == FeatureEdit.Bridge && FeatureEdit.IsCaveCarve(y.Shape))) && Features.TileOf(y, cm) == tile
                        && (!cave || (y.MinX < x.MaxX && y.MaxX > x.MinX && y.MinZ < x.MaxZ && y.MaxZ > x.MinZ)))
                    {
                        group.Add(y);
                        minX = Math.Min(minX, y.MinX); maxX = Math.Max(maxX, y.MaxX);
                        minZ = Math.Min(minZ, y.MinZ); maxZ = Math.Max(maxZ, y.MaxZ);
                    }
                int x0 = (int)Math.Floor(minX / s) - 3, z0 = (int)Math.Floor(minZ / s) - 3;
                int cells = Math.Max((int)Math.Ceiling(maxX / s) + 3 - x0, (int)Math.Ceiling(maxZ / s) + 3 - z0);
                var cols = ColumnCache.Over(new DetailColumns { Detail = detail, StepM = s }, x0 - 4, z0 - 4, cells + 9);
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
                SurfaceNets.General(new EditedSolid<ColumnCache, EditArray> { Columns = cols, Edits = list, StepM = s }, c, jMin, jMax, ref g);
                var h = new NetsBuffers();
                SurfaceNets.Heightfield(cols, c, ref h);
                string kind = x.Shape == FeatureEdit.Arch ? "arch" : x.Shape == FeatureEdit.Cave ? "cave" : x.Shape == FeatureEdit.CaveRoom ? "cave room" : x.Shape == FeatureEdit.CrystalCell ? "crystal cell" : x.Shape == FeatureEdit.Lip ? "mesa lip" : x.Shape == FeatureEdit.Bridge ? "rock bridge" : x.Shape == FeatureEdit.Sink ? "brook sink" : x.Shape == FeatureEdit.Shaft ? "skylight shaft" : x.Shape == FeatureEdit.Vault ? "dungeon vault" : x.Shape == FeatureEdit.Mine ? "mine" : Landforms.Of(x.Shape) ? Landforms.Name(x.Shape) : "overhang";
                if (!f.SameSurfaceAs(g)) e.Add($"{kind} mesh at {x.Ax:F0},{x.Az:F0} differs from the general mesher");
                if (f.SameSurfaceAs(h)) e.Add($"{kind} at {x.Ax:F0},{x.Az:F0} leaves the ground unchanged");
                if (!f.SameRimAs(h, cells)) e.Add($"{kind} at {x.Ax:F0},{x.Az:F0} moves chunk rim vertices");
                if (x.Shape == FeatureEdit.Cave)
                {
                    int bi = (int)Math.Round(x.Bx / s), bj = (int)Math.Round(x.By / s), bk = (int)Math.Round(x.Bz / s);
                    var solid = new EditedSolid<ColumnCache, EditArray> { Columns = cols, Edits = list, StepM = s };
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
                foreach (var y in edits) if (Features.TileOf(y, cm) == tile && !CaveBrooks.Channel(y)) near.Add(y);
                string fault = Underground.PathFault(detail, near.ToArray(), p);
                if (fault != null) { e.Add(fault); return; }
            }
        }

        public const float WalkStride = Underground.WalkStride;

        public static void CheckCaveIce(GeneratedWorld w, FeatureEdit[] edits, CaveGraph[] caves, List<string> e)
        {
            if (caves == null) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var iced = new HashSet<int>();
            foreach (var g in caves)
            {
                if (!g.Ice) continue;
                iced.Add(g.Tile);
                float t = Underground.EntranceTempC(w, g);
                if (t <= w.Caves.IceMaxTempC && !g.Crystal) continue;
                var m = g.Nodes.Find(q => q.Entrance);
                int x = m == null ? 0 : Math.Min(n - 1, Math.Max(0, (int)Math.Round(m.X / cm))), z = m == null ? 0 : Math.Min(n - 1, Math.Max(0, (int)Math.Round(m.Z / cm)));
                string biome = m != null && w.Biome != null && w.BiomeIds != null ? w.BiomeIds[w.Biome[z * n + x]] : "unknown";
                e.Add($"caves: ice cave on tile {g.Tile} sits in {biome} at {t:F1} C, warmer than {w.Caves.IceMaxTempC:F1} C or crystal");
                return;
            }
            foreach (var x in edits)
                if (x.Ice && (!x.CaveCarve || !iced.Contains(Features.TileOf(x, cm)))) { e.Add($"caves: ice edit at {x.Ax:F0},{x.Az:F0} outside an ice cave"); return; }
        }

        public static void CheckCaves(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves, List<string> e)
        {
            float cm = w.Config.CellMeters;
            foreach (var x in edits)
                if (FeatureEdit.IsCaveCarve(x.Shape) && Underground.Wet(w, x)) { e.Add($"cave at {x.Ax:F0},{x.Az:F0} breaks into the sea or a lake"); return; }
            var rooms = new List<FeatureEdit>();
            foreach (var x in edits) if (x.Kind == FeatureEdit.Carve && FeatureEdit.IsRoom(x.Shape)) rooms.Add(x);
            foreach (var x in edits)
            {
                if (x.Shape != FeatureEdit.Shaft) continue;
                if (!Underground.SkylightOpen(w, detail, x, w.Caves.SkylightDepthM)) { e.Add($"skylight shaft at {x.Ax:F0},{x.Az:F0} does not open onto dry ground within {w.Caves.SkylightDepthM:F0} m of its chamber"); return; }
                if (!Underground.InChamber(rooms, x.Ax, x.Ay, x.Az)) { e.Add($"skylight shaft at {x.Ax:F0},{x.Az:F0} does not drop into a cave chamber"); return; }
            }
            var ground = new DetailGround { Detail = detail };
            int probes = 0, settled = 0;
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
                var dry = Array.FindAll(items, q => !CaveBrooks.Channel(q));
                foreach (var edge in g.Edges) if ((tight = Underground.Cramped(detail, dry, g, edge)) != null) break;
                if (tight != null) e.Add(tight);
                string why = Underground.Unreached(detail, items, g);
                if (why != null) e.Add(why);
                if (tight != null || why != null) return;
                var rock = new List<FeatureEdit>();
                foreach (var q in items) if (q.Shape != FeatureEdit.Speleothem) rock.Add(q);
                var bare = rock.ToArray();
                var field = new EditArray { Items = bare };
                foreach (var q in items)
                {
                    if (q.Shape != FeatureEdit.Speleothem) continue;
                    string fault = Underground.DripFault(ground, field, LatticeStepM, q, bare, 0, bare.Length, Underground.PassM);
                    if (fault != null) { e.Add(fault); return; }
                }
                Underground.SettledFloor(detail, Array.FindAll(bare, q => !CaveBrooks.Channel(q)), ref probes, ref settled);
            }
            if (probes > 0 && settled < Underground.SettledShare * probes) e.Add($"cave floors are not settled: {settled} of {probes} side probes sit within {Underground.SettledM:F1} m of the passage centre");
            var pools = CheckCaveObjects(w, detail, edits, e);
            var brooks = CaveBrooks.Build(w, detail, edits, caves);
            CheckCaveBrooks(w, caves, edits, brooks, pools, e);
            CheckCaveBrookBanks(w, detail, edits, caves, brooks, e);
        }

        public const float StreamSlackM = 1e-3f, StreamEndM = 1f, StreamPoolM = 0.05f;
        public const float BankPadM = 0.3f, BankRiseM = 0.1f, BankShare = 0.95f, BankProbeM = 1.5f, BedShowM = 0.05f, BedShare = 0.9f, BedCapM = 0.5f;
        public const int BedEvery = 8, BedCells = 8;

        public static void CheckCaveBrookBanks(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves, CaveBrook[] streams, List<string> e)
        {
            float fine = 0.5f * NearField.NodeStep(0);
            foreach (var c in edits)
                if (CaveBrooks.Channel(c) && !c.MeshedIn(fine, c.MinX, c.MinZ, c.MaxX, c.MaxZ))
                {
                    e.Add($"cave stream channel at {c.Ax:F0},{c.Ay:F1},{c.Az:F0} is {c.Thick:F2} m thick, too thin to mesh at the {fine:F1} m feature step");
                    return;
                }
            var ground = new DetailGround { Detail = detail };
            var grid = new EditGrid(edits, w.Config.Size * w.Config.CellMeters);
            var ids = new List<int>();
            var meshed = new List<FeatureEdit>();
            int beds = 0, sunk = 0;
            bool lying = false;
            foreach (var s in streams)
            {
                var q = s.Points;
                int rows = s.Rows, st = CaveBrook.Stride, probes = 0, banked = 0;
                float pool = s.End == CaveBrook.Pool ? q[(rows - 1) * st + 1] + StreamSlackM : float.MinValue;
                var at = new float[rows];
                for (int k = 1; k < rows; k++) at[k] = at[k - 1] + Plan(q, k, st);
                float total = at[rows - 1];
                for (int k = 0; k < rows; k++)
                {
                    float along = at[k], tail = total - along;
                    if (along < CaveBrooks.TaperM || tail < CaveBrooks.TaperM || q[k * st + 1] <= pool) continue;
                    int a = Math.Max(k - 1, 0) * st, b = Math.Min(k + 1, rows - 1) * st, o = k * st;
                    float tx = q[b] - q[a], tz = q[b + 2] - q[a + 2], tl = (float)Math.Sqrt(tx * tx + tz * tz);
                    if (tl <= 0f) continue;
                    float px = -tz / tl, pz = tx / tl, y = q[o + 1], d = q[o + 3] + BankPadM;
                    if (k % BedEvery == 0)
                    {
                        beds++;
                        if (MeshedFloor(detail, edits, grid, ids, meshed, fine, q[o], y, q[o + 2]) <= y - BedShowM) sunk++;
                    }
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = q[o] + side * px * d, z = q[o + 2] + side * pz * d;
                        if (Folded(q, at, k, x, z, d + BankProbeM)) continue;
                        float f = Walk.Surface(ground, grid.Cell(x, z), LatticeStepM, x, z, y + BankProbeM, y - BankProbeM);
                        probes++;
                        if (f >= y + BankRiseM) banked++;
                    }
                }
                if (!lying && probes > 0 && banked < BankShare * probes)
                {
                    e.Add($"cave stream in {caves[s.Graph].Class} graph {s.Graph} lies on the floor: {banked} of {probes} bank probes {BankPadM:F1} m out rise {BankRiseM:F2} m above the water");
                    lying = true;
                }
            }
            if (beds > 0 && sunk < BedShare * beds) e.Add($"cave stream beds hide the water: {sunk} of {beds} meshed beds at the {fine:F1} m step sit {BedShowM:F2} m below the water surface");
        }

        static float MeshedFloor(TerrainDetail detail, FeatureEdit[] edits, EditGrid grid, List<int> ids, List<FeatureEdit> meshed, float sm, float x, float y, float z)
        {
            int i0 = (int)Math.Floor(x / sm) - BedCells / 2, k0 = (int)Math.Floor(z / sm) - BedCells / 2;
            float x0 = i0 * sm, z0 = k0 * sm, x1 = x0 + BedCells * sm, z1 = z0 + BedCells * sm;
            meshed.Clear();
            grid.Near(x0 - 0.5f * sm, z0 - 0.5f * sm, x1 + 0.5f * sm, z1 + 0.5f * sm, ids);
            foreach (int id in ids) if (edits[id].MeshedIn(sm, x0, z0, x1, z1)) meshed.Add(edits[id].AtStep(sm));
            var f = new NetsBuffers();
            var cols = new DetailColumns { Detail = detail, StepM = sm };
            SurfaceNets.Edited(cols, new EditArray { Items = meshed.ToArray() }, new NetsChunk { X0 = i0, Z0 = k0, Cells = BedCells, StepM = sm }, ref f);
            float top = float.MinValue;
            for (int t = 0; t + 2 < f.Tris.Count; t += 3)
            {
                int a = f.Tris[t] * 6, b = f.Tris[t + 1] * 6, c = f.Tris[t + 2] * 6;
                float ax = f.Verts[a] - x, az = f.Verts[a + 2] - z, bx = f.Verts[b] - x, bz = f.Verts[b + 2] - z, cx = f.Verts[c] - x, cz = f.Verts[c + 2] - z;
                float det = (bx - ax) * (cz - az) - (cx - ax) * (bz - az);
                if (Math.Abs(det) < 1e-9f) continue;
                float u = (-ax * (cz - az) + (cx - ax) * az) / det, v = (-(bx - ax) * az + ax * (bz - az)) / det;
                if (u < 0f || v < 0f || u + v > 1f) continue;
                float h = f.Verts[a + 1] + u * (f.Verts[b + 1] - f.Verts[a + 1]) + v * (f.Verts[c + 1] - f.Verts[a + 1]);
                if (h < y + BedCapM && h > top) top = h;
            }
            return top;
        }

        static bool Folded(float[] q, float[] at, int k, float x, float z, float near)
        {
            int st = CaveBrook.Stride;
            for (int j = 0; j < at.Length; j++)
            {
                if (Math.Abs(at[j] - at[k]) < 2f * near) continue;
                float dx = q[j * st] - x, dz = q[j * st + 2] - z, r = q[j * st + 3] + BankProbeM;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        static float Plan(float[] q, int k, int st)
        {
            float dx = q[k * st] - q[(k - 1) * st], dz = q[k * st + 2] - q[(k - 1) * st + 2];
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static void CheckCaveBrooks(GeneratedWorld w, CaveGraph[] caves, FeatureEdit[] edits, CaveBrook[] streams, LiquidPool[] pools, List<string> e)
        {
            foreach (var s in streams)
            {
                var g = caves[s.Graph];
                int rows = s.Rows, last = (rows - 1) * CaveBrook.Stride;
                string at = $"cave stream in {g.Class} graph {s.Graph}";
                if (rows < 2 || s.Nodes.Length < 2) { e.Add(at + " has no run"); return; }
                for (int k = 1; k < rows; k++)
                    if (s.Points[k * CaveBrook.Stride + 1] > s.Points[(k - 1) * CaveBrook.Stride + 1] + StreamSlackM) { e.Add(at + $" flows uphill at {s.Points[k * CaveBrook.Stride]:F0},{s.Points[k * CaveBrook.Stride + 2]:F0}"); return; }
                float ex = s.Points[last], ey = s.Points[last + 1], ez = s.Points[last + 2];
                var end = g.Nodes[s.Nodes[s.Nodes.Length - 1]];
                if (s.End == CaveBrook.Spring)
                {
                    float d = (float)Math.Sqrt((ex - end.X) * (ex - end.X) + (ez - end.Z) * (ez - end.Z));
                    if (!end.Entrance || d > StreamEndM) { e.Add(at + " springs away from a cave mouth"); return; }
                    if (s.Outlet != CaveBrooks.CellOf(w, end.X, end.Z) || w.Sea[s.Outlet]) { e.Add(at + " springs onto no drainage cell"); return; }
                }
                else if (s.End == CaveBrook.Pool)
                {
                    if (pools == null) continue;
                    bool fed = false;
                    foreach (var p in pools)
                        if (p.Kind == ObjectField.CavePool && (ex - p.X) * (ex - p.X) + (ez - p.Z) * (ez - p.Z) <= (p.Reach + StreamEndM) * (p.Reach + StreamEndM) && ey >= p.Level - StreamPoolM) fed = true;
                    if (!fed) { e.Add(at + $" ends at {ex:F0},{ez:F0} with no cave pool to fill"); return; }
                }
                else if (s.End == CaveBrook.Sink)
                {
                    if (end.Entrance || CaveBrooks.Lower(w, g, s.Nodes[s.Nodes.Length - 1], s.Nodes, out _) >= 0) { e.Add(at + " sinks where it could still run downhill"); return; }
                    bool hole = false;
                    foreach (var x in edits)
                        if (x.Kind == FeatureEdit.Carve && x.Shape == FeatureEdit.Sink && x.Reach(ex, end.Y - CaveBrooks.SinkProbeM, ez, out float d) && x.Hole(d) < 0f
                            && (ex - end.X) * (ex - end.X) + (ez - end.Z) * (ez - end.Z) <= (CaveBrooks.SinkOffM + StreamEndM) * (CaveBrooks.SinkOffM + StreamEndM) && ey < end.Y) hole = true;
                    if (!hole) { e.Add(at + $" sinks at {ex:F0},{ez:F0} into no hole"); return; }
                }
                else { e.Add(at + " ends at no spring, pool or sink"); return; }
            }
        }

        public const int CaveObjectChunks = 6;

        public static void CheckMines(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves, List<string> e)
        {
            float cm = w.Config.CellMeters;
            int count = 0;
            foreach (var g in caves)
            {
                if (!Mines.IsMine(g)) continue;
                if (++count > Mines.Most) { e.Add($"mines: more than {Mines.Most} mines"); return; }
                if (g.Nodes.Count == 0 || !g.Nodes[0].Entrance) { e.Add($"mines: mine in tile {g.Tile} has no entrance"); return; }
                var near = new List<FeatureEdit>();
                foreach (var q in edits) if (Features.TileOf(q, cm) == g.Tile) near.Add(q);
                var items = near.ToArray();
                int own = 0;
                foreach (var q in items)
                {
                    if (q.Shape != FeatureEdit.Mine) continue;
                    own++;
                    if (Mines.Vertical(q)) continue;
                    float gx = q.Ax / Structures.KitCellM, gz = q.Az / Structures.KitCellM;
                    if (Math.Abs(q.Ax - q.Bx) > 0.01f && Math.Abs(q.Az - q.Bz) > 0.01f) { e.Add($"mines: passage at {q.Ax:F0},{q.Az:F0} leaves the kit grid"); return; }
                    if (Math.Abs(q.Ax - q.Bx) > 0.01f ? Math.Abs(gz - Math.Round(gz)) > 0.001f : Math.Abs(gx - Math.Round(gx)) > 0.001f) { e.Add($"mines: passage at {q.Ax:F0},{q.Az:F0} is off the kit grid"); return; }
                    string why = Underground.PathFault(detail, items, q);
                    if (why != null) { e.Add($"mines: passage at {q.Ax:F0},{q.Az:F0} {why}"); return; }
                }
                if (own == 0) { e.Add($"mines: mine in tile {g.Tile} carves nothing"); return; }
            }
            foreach (var q in edits)
                if (q.Shape == FeatureEdit.Mine && q.Kind != FeatureEdit.Carve) { e.Add($"mines: edit at {q.Ax:F0},{q.Az:F0} is not a carve"); return; }
        }

        public const float LinkSocketM = 0.5f;

        public static void CheckDungeonLinks(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves, ObjectInstance[] sites, List<string> e)
        {
            if (caves == null) return;
            float cm = w.Config.CellMeters;
            var tiles = new HashSet<int>();
            var sockets = new List<CaveNode>();
            if (sites != null && w.Objects != null)
                foreach (var o in sites)
                    if (ObjectField.IsDungeon(w.Objects, o) && ObjectField.BreachOf(w.Objects, o, out var m)) sockets.Add(m);
            foreach (var q in edits)
                for (int t = 0; q.Link && t <= 2; t++)
                {
                    float f = 0.5f * t, x = q.Ax + (q.Bx - q.Ax) * f, z = q.Az + (q.Bz - q.Az) * f, top = q.Ay + (q.By - q.Ay) * f + q.R * (1f - Features.CaveHollow);
                    if (detail.HeightAt(x / cm, z / cm) < top) { e.Add($"dungeon links: link carve at {x:F0},{z:F0} opens to the surface"); return; }
                }
            foreach (var g in caves)
            {
                if (!DungeonLinks.IsLink(g)) continue;
                string at = $"dungeon links: link in tile {g.Tile}";
                if (!tiles.Add(g.Tile)) { e.Add(at + " shares its tile with another link"); return; }
                var n0 = g.Nodes.Count == 0 ? null : g.Nodes[0];
                if (n0 == null || !n0.Entrance || !sockets.Exists(m => Math.Abs(m.X - n0.X) < LinkSocketM && Math.Abs(m.Z - n0.Z) < LinkSocketM && Math.Abs(m.Y - n0.Y) < LinkSocketM)) { e.Add(at + " does not start at a dungeon breach socket"); return; }
                for (int k = 1; k < g.Nodes.Count; k++) if (g.Nodes[k].Entrance) { e.Add(at + " has a second entrance"); return; }
                var near = new List<FeatureEdit>();
                foreach (var q in edits) if (Features.TileOf(q, cm) == g.Tile) near.Add(q);
                var items = near.ToArray();
                string why = Underground.Unreached(detail, items, g);
                if (why != null) { e.Add($"{at}: {why}"); return; }
                float width = DungeonLinks.Width(detail, items, g);
                if (!DungeonLinks.Fits(width)) { e.Add($"{at} passages average {width:F1} m wide at 1 m, outside {DungeonLinks.WidthMin:F0}-{DungeonLinks.WidthMax:F1} m"); return; }
            }
            foreach (var q in edits)
            {
                if (!q.Link) continue;
                int tile = Features.TileOf(q, cm);
                if ((!q.CaveCarve && !(q.Kind == FeatureEdit.Add && q.Shape == FeatureEdit.Speleothem)) || !tiles.Contains(tile)) { e.Add($"dungeon links: edit at {q.Ax:F0},{q.Az:F0} is not a carve of a link cave"); return; }
            }
        }

        public static LiquidPool[] CheckCaveObjects(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, List<string> e)
        {
            if (w.Objects == null) return null;
            var field = new ObjectField(w, detail, edits, w.Objects);
            var pools = field.CavePools();
            CheckCavePools(w, detail, edits, pools, e);
            float span = field.ChunkMeters;
            int across = field.ChunksAcross;
            var picked = new List<int>();
            foreach (var x in edits)
            {
                if (x.Kind != FeatureEdit.Carve || x.Shape != FeatureEdit.Cave) continue;
                float mx = 0.5f * (x.Ax + x.Bx), mz = 0.5f * (x.Az + x.Bz);
                int c = Math.Min(across - 1, Math.Max(0, (int)(mz / span))) * across + Math.Min(across - 1, Math.Max(0, (int)(mx / span)));
                if (!picked.Contains(c)) picked.Add(c);
                if (picked.Count >= CaveObjectChunks) break;
            }
            int gems = 0, cells = 0, mineEdits = 0, timbers = 0;
            foreach (var x in edits)
            {
                if (x.Shape != FeatureEdit.Mine || Mines.Vertical(x)) continue;
                mineEdits++;
                float mx = 0.5f * (x.Ax + x.Bx), mz = 0.5f * (x.Az + x.Bz);
                int c = Math.Min(across - 1, Math.Max(0, (int)(mz / span))) * across + Math.Min(across - 1, Math.Max(0, (int)(mx / span)));
                if (!picked.Contains(c)) picked.Add(c);
            }
            foreach (var x in edits)
            {
                if (x.Kind != FeatureEdit.Carve || x.Shape != FeatureEdit.CrystalCell) continue;
                int c = Math.Min(across - 1, Math.Max(0, (int)(x.Az / span))) * across + Math.Min(across - 1, Math.Max(0, (int)(x.Ax / span)));
                if (!picked.Contains(c)) picked.Add(c);
                if (++cells >= CaveObjectChunks) break;
            }
            float length = 0f;
            foreach (var x in edits)
            {
                if (x.Kind != FeatureEdit.Carve || x.Shape != FeatureEdit.Cave || x.Link) continue;
                float mx = 0.5f * (x.Ax + x.Bx), mz = 0.5f * (x.Az + x.Bz), ex = x.Bx - x.Ax, ez = x.Bz - x.Az;
                int c = Math.Min(across - 1, Math.Max(0, (int)(mz / span))) * across + Math.Min(across - 1, Math.Max(0, (int)(mx / span)));
                if (picked.Contains(c)) length += (float)Math.Sqrt(ex * ex + ez * ez);
            }
            int drips = 0;
            var contacts = new List<ObjectColumn>();
            foreach (int c in picked)
                foreach (var o in field.Chunk(c % across, c / across))
                {
                    var def = w.Objects.Objects[o.Def];
                    if (!ObjectField.IsCave(def)) continue;
                    if (def.Placement.Cave == ObjectField.CaveStalactite) drips++;
                    if (def.Placement.Cave == ObjectField.CaveCrystal) gems++;
                    if (def.Placement.Cave == ObjectField.CaveMineTimber) timbers++;
                    string loose = field.ContactFault(o, contacts);
                    string fault = field.CaveFault(o) ?? (loose == null ? null : $"{def.Id} at {o.X:F0},{o.Z:F0}{loose}");
                    if (fault != null) { e.Add(fault); return pools; }
                }
            if (cells > 0 && gems == 0) e.Add($"caves: {cells} crystal cells hold no crystals");
            if (mineEdits > 0 && timbers == 0) e.Add($"mines: {mineEdits} passages hold no timber supports");
            if (length >= ObjectField.CaveDripSurveyM && drips * 100f < ObjectField.CaveDripPerHundredM * length)
                e.Add($"caves: {drips} stalactites over {length:F0} m of passage, under {ObjectField.CaveDripPerHundredM} per 100 m");
            CheckCaveLights(w, edits, field, e);
            return pools;
        }

        public static void CheckCaveLights(GeneratedWorld w, FeatureEdit[] edits, ObjectField field, List<string> e)
        {
            var perTile = new Dictionary<int, int>();
            for (int i = 0; i < field.CaveLightCount; i++)
            {
                var p = field.CaveLight(i);
                perTile.TryGetValue(p.Tile, out int had);
                if ((perTile[p.Tile] = had + 1) > w.Caves.LightMost) { e.Add($"caves: tile {p.Tile} holds more than {w.Caves.LightMost} natural lights"); return; }
                var c = edits[p.Edit];
                if ((p.Kind == ObjectField.CaveSkylight) != (c.Shape == FeatureEdit.Shaft)) { e.Add($"caves: light {i} pairs a skylight with a non-shaft edit or a shaft with another light"); return; }
                if (!field.CaveLightAt(i, out var o)) continue;
                string fault = field.CaveFault(o) ?? CaveLightOverlap(w, field, o);
                if (fault != null) { e.Add(fault); return; }
                if (p.Kind != ObjectField.CaveSkylight) continue;
                float dx = o.X - c.Ax, dz = o.Z - c.Az;
                if (dx * dx + dz * dz > c.HoleR * c.HoleR || o.Y > c.Ay) { e.Add($"caves: skylight light at {o.X:F0},{o.Z:F0} does not sit under its shaft"); return; }
            }
        }

        static string CaveLightOverlap(GeneratedWorld w, ObjectField field, ObjectInstance o)
        {
            var defs = w.Objects.Objects;
            float reach = defs[o.Def].FootprintRadius() * o.Size, span = field.ChunkMeters, edge = 2f * reach + ObjectField.CaveColumnGapMaxM;
            int across = field.ChunksAcross, cx = Math.Min(across - 1, (int)(o.X / span)), cz = Math.Min(across - 1, (int)(o.Z / span));
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = cx + dx, az = cz + dz;
                    if (ax < 0 || az < 0 || ax >= across || az >= across) continue;
                    if ((dx < 0 && o.X - cx * span > edge) || (dx > 0 && (cx + 1) * span - o.X > edge)) continue;
                    if ((dz < 0 && o.Z - cz * span > edge) || (dz > 0 && (cz + 1) * span - o.Z > edge)) continue;
                    foreach (var m in field.Chunk(ax, az))
                    {
                        int kind = defs[m.Def].Placement.Cave;
                        if (kind != ObjectField.CaveStalagmite && kind != ObjectField.CaveColumn) continue;
                        float gx = m.X - o.X, gz = m.Z - o.Z, keep = reach + defs[m.Def].FootprintRadius() * m.Size;
                        if (gx * gx + gz * gz < keep * keep) return $"{defs[o.Def].Id} at {o.X:F0},{o.Z:F0} overlaps {defs[m.Def].Id} at {m.X:F0},{m.Z:F0}";
                    }
                }
            return null;
        }

        public const float CavePoolHeadM = 1f, CavePoolSealM = 0.3f;

        public static void CheckCavePools(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, LiquidPool[] pools, List<string> e)
        {
            var ground = new DetailGround { Detail = detail };
            float s = LatticeStepM;
            foreach (var p in pools)
            {
                if (p.Kind != ObjectField.CavePool) continue;
                if (p.Leaks) { e.Add($"cave pool at {p.X:F0},{p.Z:F0} leaks out through its chamber"); return; }
                if (p.WetCount == 0 || !(p.Level - p.Floor >= ObjectField.PoolMinDepthM)) { e.Add($"cave pool at {p.X:F0},{p.Z:F0} holds no water"); return; }
                float x0 = p.NodeX(0), z0 = p.NodeZ(0), x1 = p.NodeX(p.Nx - 1), z1 = p.NodeZ(p.Nz - 1);
                var near = new List<FeatureEdit>();
                foreach (var f in edits)
                    if (f.MaxX >= x0 - s && f.MinX <= x1 + s && f.MaxZ >= z0 - s && f.MinZ <= z1 + s) near.Add(f);
                var list = new EditArray { Items = near.ToArray() };
                for (int k = 0; k < p.Wet.Length; k++)
                {
                    if (!p.Wet[k]) continue;
                    float x = p.NodeX(k % p.Nx), z = p.NodeZ(k / p.Nx), head = p.Level + CavePoolHeadM;
                    if (Walk.Solid(ground, list, s, x, p.Ground[k] - CavePoolSealM, z) < 0) { e.Add($"cave pool at {p.X:F0},{p.Z:F0} drains through its floor at {x:F1},{z:F1}"); return; }
                    if (Walk.Solid(ground, list, s, x, head, z) >= 0 || !Underground.InChamber(near, x, head, z)) { e.Add($"cave pool at {p.X:F0},{p.Z:F0} spills outside its carved chamber at {x:F1},{z:F1}"); return; }
                }
            }
        }

        public const float CaveNormalDeg = 1.5f;
        public const int CaveNormalLevels = 3, CaveCoverSteps = 4, CaveNormalMin = 8;

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
                    foreach (var m in meshed) if (m.CaveCarve && m.Reach(x, y, z, out _)) { cave = true; break; }
                    if (!cave || Math.Abs(Field(x, y, z)) > 0.75f * sm || detail.Sample(x, z) - y < CaveCoverSteps * sm) continue;
                    const float d = 0.05f;
                    float gx = Field(x + d, y, z) - Field(x - d, y, z), gy = Field(x, y + d, z) - Field(x, y - d, z), gz = Field(x, y, z + d) - Field(x, y, z - d);
                    float gl = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    if (!(gl > 0)) continue;
                    float dot = -(gx * f.Verts[6 * t + 3] + gy * f.Verts[6 * t + 4] + gz * f.Verts[6 * t + 5]) / gl;
                    miss.Add(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180 / Math.PI);
                }
                if (miss.Count < CaveNormalMin) continue;
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
