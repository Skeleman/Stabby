using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class WorldChecks
    {
        public const float SurfaceTolerance = 1e-3f;
        public const float CorridorFollowShare = 0.7f;
        public const float CliffMinRise = 30f, BeachMaxDepth = 4f;

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
                }
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

            if (w.Sketch != null) CheckSketch(w, e);
            CheckDetail(w, e);
            CheckGeometry(w, e);
            CheckFeatures(w, e);
            return e;
        }

        static void CheckDetail(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            var hy = w.Water;
            var d = new TerrainDetail(w);
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int i = y * n + x;
                    bool wet = w.Sea[i] || hy.LakeId[i] >= 0 || hy.IsRiver[i];
                    if (wet && d.HeightAt(x, y) != w.Height[i]) { e.Add($"detail moves water cell {i}"); return; }
                    if (w.Height[i] > 0 && w.Height[i + 1] > 0 && w.Height[i + n] > 0 && w.Height[i + n + 1] > 0
                        && d.HeightAt(x + 0.5f, y + 0.5f) <= 0) { e.Add($"detail sinks land below the sea near cell {i}"); return; }
                }
        }

        public const float LatticeStepCells = Features.LatticeStepCells;

        static void CheckGeometry(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size, top = 0;
            for (int i = 1; i < w.Height.Length; i++) if (w.Height[i] > w.Height[top]) top = i;
            var cols = new DetailColumns { Detail = new TerrainDetail(w), StepCells = LatticeStepCells };
            var c = new NetsChunk { X0 = (int)(top % n / LatticeStepCells) - 8, Z0 = (int)(top / n / LatticeStepCells) - 8, Cells = 16, StepM = LatticeStepCells * w.Config.CellMeters };
            SurfaceNets.HeightRange(cols, c, out int jMin, out int jMax);
            var g = new NetsBuffers();
            SurfaceNets.General(new HeightSolid<DetailColumns> { Columns = cols, StepM = c.StepM }, c, jMin, jMax, ref g);
            var f = new NetsBuffers();
            SurfaceNets.Heightfield(cols, c, ref f);
            if (f.Tris.Count == 0) { e.Add($"heightfield mesher made no triangles near cell {top}"); return; }
            if (!f.SameAs(g)) e.Add($"heightfield mesher differs from the general mesher near cell {top}");
        }

        public const int ChunkCells = 32;
        public static readonly float[] ChunkSteps = { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f, 8f };

        static void CheckChunkBounds(GeneratedWorld w, FeatureEdit[] placed, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, coarse = ChunkSteps[ChunkSteps.Length - 1];
            var detail = new TerrainDetail(w);
            var all = new EditArray { Items = placed };
            var origins = new List<(int, int)>();
            int top = 0;
            for (int i = 1; i < w.Height.Length; i++) if (w.Height[i] > w.Height[top]) top = i;
            origins.Add((top % n / ChunkCells * ChunkCells, top / n / ChunkCells * ChunkCells));
            if (placed.Length > 0) origins.Add(((int)(placed[0].Ax / cm) / ChunkCells * ChunkCells, (int)(placed[0].Az / cm) / ChunkCells * ChunkCells));
            for (int o = 0; o < origins.Count; o++)
            {
                var (cx, cz) = origins[o];
                detail.ChunkRange(all, cm, cx, cz, ChunkCells, coarse, out float lo, out float hi);
                foreach (float step in ChunkSteps)
                {
                    if (step < 0.5f && o == 0) continue;
                    int m = (int)(ChunkCells / step);
                    var cols = new DetailColumns { Detail = detail, StepCells = step };
                    var c = new NetsChunk { X0 = (int)(cx / step), Z0 = (int)(cz / step), Cells = m, StepM = step * cm };
                    float sm = step * cm, x0 = cx * cm, z0 = cz * cm, x1 = x0 + ChunkCells * cm, z1 = z0 + ChunkCells * cm;
                    var meshed = new List<FeatureEdit>();
                    foreach (var x in placed) if (x.MeshedIn(sm, x0, z0, x1, z1)) meshed.Add(x);
                    var f = new NetsBuffers();
                    var list = new EditArray { Items = meshed.ToArray() };
                    if (meshed.Count > 0) SurfaceNets.Edited(cols, list, c, ref f);
                    else SurfaceNets.Heightfield(cols, c, ref f);
                    if (meshed.Count > 0 && !StitchTargetsMatch(cols, list, c, f))
                    { e.Add($"chunk {cx},{cz} at step {step} has border vertices a finer neighbour cannot stitch to"); return; }
                    foreach (int t in f.Tris)
                    {
                        int v = t * 6 + 1;
                        if (f.Verts[v] < lo || f.Verts[v] > hi)
                        { e.Add($"bounds {lo:F0}..{hi:F0} miss vertex {f.Verts[v - 1]:F0},{f.Verts[v]:F0},{f.Verts[v + 1]:F0} of chunk {cx},{cz} at step {step}"); return; }
                    }
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
                        int cell = SurfaceNets.CoarseVertex(cols, edits, c.StepM, c.X0 - 1 + i, j, c.Z0 - 1 + k, out float x, out float y, out float z);
                        if (cell < f.ColumnStart(col) || cell >= f.ColumnEnd(col)) return false;
                        int v = 6 * (f.ColumnBase(col) + cell - f.ColumnStart(col));
                        if (x != f.Verts[v] || y != f.Verts[v + 1] || z != f.Verts[v + 2]) return false;
                    }
                }
            return true;
        }

        static void CheckFeatures(GeneratedWorld w, List<string> e)
        {
            var detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail);
            CheckChunkBounds(w, edits, e);
            float cm = w.Config.CellMeters, s = LatticeStepCells * cm;
            foreach (var x in edits)
                if (!Features.Inside(x, cm)) { e.Add($"feature edit at {x.Ax:F0},{x.Az:F0} leaves its tile interior"); return; }
            var cols = new DetailColumns { Detail = detail, StepCells = LatticeStepCells };
            var seen = new bool[3];
            foreach (var x in edits)
            {
                if (seen[x.Shape]) continue;
                seen[x.Shape] = true;
                float minX = x.MinX, maxX = x.MaxX, minZ = x.MinZ, maxZ = x.MaxZ;
                int tile = Features.TileOf(x, cm);
                var group = new List<FeatureEdit>();
                foreach (var y in edits)
                    if (y.Shape == x.Shape && Features.TileOf(y, cm) == tile)
                    {
                        group.Add(y);
                        minX = Math.Min(minX, y.MinX); maxX = Math.Max(maxX, y.MaxX);
                        minZ = Math.Min(minZ, y.MinZ); maxZ = Math.Max(maxZ, y.MaxZ);
                    }
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
                string kind = x.Shape == FeatureEdit.Arch ? "arch" : x.Shape == FeatureEdit.Cave ? "cave" : "overhang";
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
