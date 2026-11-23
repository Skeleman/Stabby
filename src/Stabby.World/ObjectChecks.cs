using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const int ObjectCheckChunks = 4;

        static void CheckObjects(GeneratedWorld w, TerrainDetail detail, EditGrid grid, List<string> e)
        {
            var edits = grid.Edits;
            var ids = new List<int>();
            var field = new ObjectField(w, detail, edits, w.Objects);
            int n = w.Config.Size, across = field.ChunksAcross;
            float cm = w.Config.CellMeters, span = WorldChunks.Size * cm;
            var bed = RiverBedCells(w, out var segs);
            bool first = true;
            var seen = new HashSet<int>();
            void Fail(int kind, string message) { if (seen.Add(kind)) e.Add(message); }
            var contacts = new List<ObjectColumn>();
            var bogs = field.BogPools();
            foreach (int c in ObjectChunks(w, edits, bogs, across, span))
            {
                int cx = c % across, cz = c / across;
                var a = field.Chunk(cx, cz);
                if (first && !SameObjects(a, new ObjectField(w, detail, edits, w.Objects).Place(cx, cz))) { e.Add($"objects in chunk {cx},{cz} differ between runs"); return; }
                if (first && !SolidIdsMatch(a, new ObjectField(w, detail, edits, w.Objects).Place(cx, cz, true))) { e.Add($"object ids in chunk {cx},{cz} change when only solid objects are placed"); return; }
                if (first) { string fault = RegionFault(field, new ObjectField(w, detail, edits, w.Objects), a, c); if (fault != null) { e.Add($"collision region {cx},{cz} {fault}"); return; } }
                first = false;
                CheckObjectIds(a, c, e);
                foreach (var o in a)
                {
                    var def = w.Objects.Objects[o.Def];
                    float r = field.Footprint(o.Def) * o.Size;
                    string at = $"{def.Id} at {o.X:F0},{o.Z:F0}";
                    if (o.X < cx * span || o.X >= (cx + 1) * span || o.Z < cz * span || o.Z >= (cz + 1) * span) Fail(0, at + " lies outside its chunk");
                    int gx = Math.Min(n - 1, (int)(o.X / cm + 0.5f)), gz = Math.Min(n - 1, (int)(o.Z / cm + 0.5f)), i = gz * n + gx;
                    float ground = ObjectField.GroundAt(detail, cm, o.X, o.Z);
                    if (w.Sea[i]) Fail(1, at + " stands in the sea");
                    int lake = w.Water.LakeId[i], site = def.Placement.Bank == null ? -1 : BankSites.Of(def.Placement.Bank);
                    if (site < 0 && (lake >= 0 || (lake = NearLake(w, gx, gz, ground)) >= 0)) Fail(2, at + $" stands in lake {lake}");
                    if (site < 0 && OnBed(bed, segs, i, o.X, o.Z, 0.5f * r)) Fail(3, at + " lies on a river bed");
                    if (site >= 0 && !OnBank(w, detail, site, gx, gz, o.X, o.Z, ground)) Fail(9, at + " is off its river or lake bank");
                    grid.Near(o.X - 0.5f * r, o.Z - 0.5f * r, o.X + 0.5f * r, o.Z + 0.5f * r, ids);
                    if (def.Placement.Discovery > 0) ids.RemoveAll(f => edits[f].Shape == FeatureEdit.Spring || (def.Placement.Discovery == ObjectField.WolfDen && edits[f].Shape == FeatureEdit.Boulder));
                    if (ids.Count > 0) Fail(4, at + " sits inside a feature edit");
                    for (int k = 0; k < bogs.Length; k += ObjectField.BogStride)
                    {
                        float bx = o.X - bogs[k], bz = o.Z - bogs[k + 1], reach = ObjectField.BogMargin(bogs[k + 2]) + 0.5f * r;
                        if (bx * bx + bz * bz < reach * reach) Fail(11, at + $" stands in the bog margin at {bogs[k]:F0},{bogs[k + 1]:F0}");
                    }
                    if (o.Y > ground + 0.05f || o.Y < ground - o.Size) Fail(5, at + $" is not grounded ({o.Y:F2} vs ground {ground:F2})");
                    float sg = ObjectField.SlopeStepM, sx = (ObjectField.GroundAt(detail, cm, o.X + sg, o.Z) - ObjectField.GroundAt(detail, cm, o.X - sg, o.Z)) / (2 * sg);
                    float sz = (ObjectField.GroundAt(detail, cm, o.X, o.Z + sg) - ObjectField.GroundAt(detail, cm, o.X, o.Z - sg)) / (2 * sg), slope = (float)Math.Sqrt(sx * sx + sz * sz);
                    if (slope < def.Placement.SlopeMin - 1e-3f || slope > def.Placement.SlopeMax + 1e-3f) Fail(8, at + $" stands on slope {slope:F2} outside {def.Placement.SlopeMin:F2}-{def.Placement.SlopeMax:F2}");
                    string seat = SeatFault(w, detail, o);
                    if (seat != null) Fail(7, at + seat);
                    var biomes = def.Placement.Biomes;
                    if (biomes.Count > 0 && (w.Biome[i] < 0 || !biomes.Contains(w.BiomeIds[w.Biome[i]]))) Fail(6, at + " is outside its biomes");
                    for (int pi = 0; pi < def.Parts.Count; pi++)
                    {
                        contacts.Clear();
                        field.PartContactsOf(o, pi, contacts);
                        if (!contacts.Exists(c => ObjectField.Stamps(w.Objects, o, c))) Fail(10, at + $" part {def.Parts[pi].Id} leaves no contact stamp");
                    }
                }
            }
        }

        public const int SiteCheckLimit = 6;

        static void CheckSites(GeneratedWorld w, TerrainDetail detail, EditGrid grid, List<string> e)
        {
            var field = new ObjectField(w, detail, grid.Edits, w.Objects);
            int across = field.ChunksAcross, checkedSites = 0;
            float span = field.ChunkMeters;
            var columns = new List<ObjectColumn>();
            var dens = new List<FeatureEdit>();
            for (int c = 0; c < across * across && checkedSites < SiteCheckLimit; c++)
                foreach (var s in field.Sites(c % across, c / across))
                {
                    if (checkedSites++ >= SiteCheckLimit) break;
                    string at = $"{w.Objects.Objects[s.Def].Id} at {s.X:F0},{s.Z:F0}";
                    float reach = 2 * s.Size;
                    dens.Clear();
                    if (w.Objects.Objects[s.Def].Placement.Discovery == ObjectField.WolfDen) field.DenEditsOf(s, dens);
                    foreach (var f in grid.Edits)
                        if (f.Shape == FeatureEdit.Boulder && !dens.Exists(d => d.Ax == f.Ax && d.Az == f.Az && d.R == f.R) && field.SiteGap(s, 0.5f * (f.MinX + f.MaxX), 0.5f * (f.MinZ + f.MaxZ)) < f.R) { e.Add($"boulder edit sits inside {at}"); return; }
                    int x0 = Math.Max(0, (int)((s.X - reach) / span)), x1 = Math.Min(across - 1, (int)((s.X + reach) / span));
                    int z0 = Math.Max(0, (int)((s.Z - reach) / span)), z1 = Math.Min(across - 1, (int)((s.Z + reach) / span));
                    for (int cz = z0; cz <= z1; cz++)
                        for (int cx = x0; cx <= x1; cx++)
                            foreach (var o in field.Chunk(cx, cz))
                            {
                                if (field.IsSite(o.Def) || w.Objects.Objects[o.Def].Kind == ObjectLibrary.Structure) continue;
                                columns.Clear();
                                field.ColumnsOf(o, columns);
                                foreach (var col in columns)
                                    if (field.SiteGap(s, col.X, col.Z) < col.R) { e.Add($"{w.Objects.Objects[o.Def].Id} at {o.X:F0},{o.Z:F0} has a column inside {at}"); return; }
                            }
                }
        }

        static string RegionFault(ObjectField full, ObjectField fresh, ObjectInstance[] placed, int c)
        {
            var columns = new List<ObjectColumn>();
            var owners = new List<ulong>();
            ColumnRegion.Collect(full, placed, columns, owners);
            var want = ColumnRegion.Of(c, columns, owners);
            var got = ColumnRegion.Build(fresh, c);
            if (!want.Grid.Equals(got.Grid)) return "has a different grid when built alone";
            if (want.Count != got.Count) return $"holds {got.Count} columns, its placements give {want.Count}";
            for (int k = 0; k < want.Count; k++)
            {
                var x = want.Items[k]; var y = got.Items[k];
                if (want.Owners[k] != got.Owners[k] || x.X != y.X || x.Z != y.Z || x.R != y.R || x.Bottom != y.Bottom || x.Top != y.Top) return $"column {k} differs from its chunk placements";
                if (ObjectIds.Chunk(got.Owners[k]) != c) return $"column {k} belongs to chunk {ObjectIds.Chunk(got.Owners[k])}";
            }
            for (int b = 0; b <= got.Grid.Cells; b++)
                if (want.Starts[b] != got.Starts[b]) return "cell starts differ from its chunk placements";
            for (int b = 0; b < got.Grid.Cells; b++)
                for (int k = got.Starts[b]; k < got.Starts[b + 1]; k++)
                    if ((ColumnRegion.CellOf(got.Items[k].Z) - got.Grid.Z0) * got.Grid.W + ColumnRegion.CellOf(got.Items[k].X) - got.Grid.X0 != b) return $"column {k} sits in the wrong cell";
            return null;
        }

        public const int StructureSweep = 16;

        static string SeatFault(GeneratedWorld w, TerrainDetail detail, ObjectInstance o)
        {
            var def = w.Objects.Objects[o.Def];
            var seat = ObjectField.Seats(def);
            if (seat == null) return null;
            ObjectField.SeatRange(detail, w.Config.CellMeters, seat, o, out float lo, out float hi);
            float sink = def.Placement.Sink * o.Size;
            if (o.Y > lo + 0.05f) return $" floats above its lowest seat ({o.Y:F2} vs {lo:F2})";
            if (hi - o.Y > Structures.SeatM + sink + 0.05f) return $" is buried {hi - o.Y:F2} m at its highest seat";
            return null;
        }

        static void CheckStructures(GeneratedWorld w, TerrainDetail detail, EditGrid grid, List<string> e)
        {
            var field = new ObjectField(w, detail, grid.Edits, w.Objects);
            int across = field.ChunksAcross, found = 0;
            for (int c = 0; c < across * across && found < StructureSweep; c++)
                foreach (var o in field.Place(c % across, c / across, true))
                {
                    if (ObjectField.Seats(w.Objects.Objects[o.Def]) == null) continue;
                    found++;
                    int own = ObjectIds.Chunk(o.Id), ox = (int)Math.Floor(o.X / field.ChunkMeters), oz = (int)Math.Floor(o.Z / field.ChunkMeters);
                    if (own != c || ox != c % across || oz != c / across) { e.Add($"{w.Objects.Objects[o.Def].Id} at {o.X:F0},{o.Z:F0} leaves its object chunk"); return; }
                    string seat = SeatFault(w, detail, o);
                    if (seat != null) { e.Add($"{w.Objects.Objects[o.Def].Id} at {o.X:F0},{o.Z:F0}" + seat); return; }
                }
        }

        public const float BoulderStepM = 1f, BoulderTolerance = 1f;
        public const int BoulderMeshSamples = 12;

        static void CheckBoulders(GeneratedWorld w, TerrainDetail detail, EditGrid grid, List<string> e)
        {
            var edits = grid.Edits;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, span = WorldChunks.Size * cm, worldM = n * cm;
            var bed = RiverBedCells(w, out var segs);
            var ids = new List<int>();
            var boulders = new List<int>();
            for (int q = 0; q < edits.Length; q++)
            {
                var b = edits[q];
                if (b.Shape != FeatureEdit.Boulder) continue;
                boulders.Add(q);
                float x = 0.5f * (b.MinX + b.MaxX), z = 0.5f * (b.MinZ + b.MaxZ), r = 0.5f * Math.Max(b.MaxX - b.MinX, b.MaxZ - b.MinZ);
                string at = $"boulder at {x:F0},{z:F0}";
                if (b.Kind != FeatureEdit.Add) { e.Add(at + " carves solid"); return; }
                float cx0 = (float)Math.Floor(x / span) * span, cz0 = (float)Math.Floor(z / span) * span;
                if (b.MinX < cx0 || b.MaxX >= Math.Min(cx0 + span, worldM) || b.MinZ < cz0 || b.MaxZ >= Math.Min(cz0 + span, worldM)) { e.Add(at + " leaves its object chunk"); return; }
                int gx = Math.Min(n - 1, (int)(x / cm + 0.5f)), gz = Math.Min(n - 1, (int)(z / cm + 0.5f)), i = gz * n + gx;
                float ground = ObjectField.GroundAt(detail, cm, x, z);
                if (w.Sea[i]) { e.Add(at + " stands in the sea"); return; }
                int lake = w.Water.LakeId[i];
                if (lake >= 0 || (lake = NearLake(w, gx, gz, ground)) >= 0) { e.Add(at + $" stands in lake {lake}"); return; }
                if (OnBed(bed, segs, i, x, z, r)) { e.Add(at + " lies on a river bed"); return; }
                grid.Near(b.MinX, b.MinZ, b.MaxX, b.MaxZ, ids);
                foreach (int f in ids)
                    if (edits[f].Shape != FeatureEdit.Boulder) { e.Add(at + " overlaps a terrain feature"); return; }
            }
            int samples = Math.Min(BoulderMeshSamples, boulders.Count);
            for (int k = 0; k < samples; k++)
            {
                var b = edits[boulders[(int)((long)k * boulders.Count / samples)]];
                int before = e.Count;
                CheckBoulderMesh(detail, grid, b, e);
                if (e.Count == before) CheckBoulderFade(detail, grid, b, e);
                if (e.Count > before) return;
            }
        }

        static FeatureEdit[] NearAt(EditGrid grid, float s, int x0, int z0, int cells, List<int> ids)
        {
            float bx0 = x0 * s, bz0 = z0 * s, bx1 = (x0 + cells) * s, bz1 = (z0 + cells) * s;
            grid.Near(bx0 - 0.5f * s, bz0 - 0.5f * s, bx1 + 0.5f * s, bz1 + 0.5f * s, ids);
            var near = new List<FeatureEdit>();
            foreach (int q in ids) if (grid.Edits[q].MeshedIn(s, bx0, bz0, bx1, bz1)) near.Add(grid.Edits[q].AtStep(s));
            return near.ToArray();
        }

        static NetsChunk BoulderChunk(FeatureEdit b, float s)
        {
            int x0 = (int)Math.Floor(b.MinX / s) - 3, z0 = (int)Math.Floor(b.MinZ / s) - 3;
            int cells = Math.Max((int)Math.Ceiling(b.MaxX / s) + 3 - x0, (int)Math.Ceiling(b.MaxZ / s) + 3 - z0);
            return new NetsChunk { X0 = x0, Z0 = z0, Cells = cells, StepM = s };
        }

        static void MeshBoulder(TerrainDetail detail, EditArray list, NetsChunk c, out NetsBuffers m, out NetsBuffers g, out NetsBuffers h)
        {
            float s = c.StepM;
            var cols = new DetailColumns { Detail = detail, StepM = s };
            SurfaceNets.HeightRange(cols, c, out int jMin, out int jMax);
            foreach (var f in list.Items)
            {
                jMin = Math.Min(jMin, (int)Math.Floor(f.MinY / s) - 2);
                jMax = Math.Max(jMax, (int)Math.Floor(f.MaxY / s) + 3);
            }
            m = new NetsBuffers();
            SurfaceNets.Edited(cols, list, c, ref m);
            g = new NetsBuffers();
            SurfaceNets.General(new EditedSolid<DetailColumns, EditArray> { Columns = cols, Edits = list, StepM = s }, c, jMin, jMax, ref g);
            h = new NetsBuffers();
            SurfaceNets.Heightfield(cols, c, ref h);
        }

        static void CheckBoulderFade(TerrainDetail detail, EditGrid grid, FeatureEdit b, List<string> e)
        {
            string at = $"boulder at {b.Ax:F0},{b.Az:F0}";
            float s = BoulderStepM;
            while (b.R >= FeatureEdit.MinMeshSteps * s) s *= 2;
            if (s > NearField.NodeStep(NearField.NodeLevels - 1)) return;
            var a = b.AtStep(s);
            if (a.R < FeatureEdit.MinMeshSteps * s || Math.Abs(a.MaxY - b.MaxY) > 1e-3f) { e.Add(at + $" does not keep its top when it fades at step {s:F0}"); return; }
            var c = BoulderChunk(a, s);
            var list = new EditArray { Items = NearAt(grid, s, c.X0, c.Z0, c.Cells, new List<int>()) };
            MeshBoulder(detail, list, c, out var m, out var g, out var h);
            if (!m.SameSurfaceAs(g)) e.Add(at + $" faded mesh at step {s:F0} differs from the general mesher");
            if (m.SameSurfaceAs(h)) e.Add(at + $" vanishes at step {s:F0} instead of fading");
        }

        static void CheckBoulderMesh(TerrainDetail detail, EditGrid grid, FeatureEdit b, List<string> e)
        {
            float s = BoulderStepM;
            string at = $"boulder at {b.Ax:F0},{b.Az:F0}";
            if (!b.MeshedIn(s, b.MinX, b.MinZ, b.MaxX, b.MaxZ)) { e.Add(at + " is too small to mesh at the finest step"); return; }
            var c = BoulderChunk(b, s);
            var list = new EditArray { Items = NearAt(grid, s, c.X0, c.Z0, c.Cells, new List<int>()) };
            MeshBoulder(detail, list, c, out var m, out var g, out var h);
            if (!m.SameSurfaceAs(g)) e.Add(at + " mesh differs from the general mesher");
            if (m.SameSurfaceAs(h)) e.Add(at + " leaves the ground unchanged");
            var ground = new DetailGround { Detail = detail };
            float mx = 0.5f * (b.Ax + b.Bx), mz = 0.5f * (b.Az + b.Bz), top = 0.5f * (b.Ay + b.By) + b.R;
            foreach (var f in list.Items) top = Math.Max(top, TopAt(f, mx, mz));
            float wx = mx, wy = top + Walk.StepUp, wz = mz;
            Walk.Step(ground, list, Features.LatticeStepM, ref wx, ref wy, ref wz, 0, 0, Walk.Reach);
            float meshTop = float.NegativeInfinity;
            foreach (int t in m.Tris)
            {
                int v = t * 6;
                float vx = m.Verts[v], vy = m.Verts[v + 1], vz = m.Verts[v + 2];
                if (Math.Abs(vx - mx) <= s && Math.Abs(vz - mz) <= s) meshTop = Math.Max(meshTop, vy);
                if (vx < b.MinX || vx > b.MaxX || vz < b.MinZ || vz > b.MaxZ || vy < b.MinY) continue;
                bool solid = false, open = false;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float d = Walk.Solid(ground, list, Features.LatticeStepM, vx + dx * s, vy + dy * s, vz + dz * s);
                            solid |= d >= 0; open |= d < 0;
                        }
                if (!(solid && open)) { e.Add(at + $" collision misses mesh vertex {vx:F1},{vy:F1},{vz:F1}"); return; }
            }
            if (Math.Abs(wy - top) > Walk.Probe) e.Add(at + $" walker stands at {wy:F2}, boulder top is {top:F2}");
            if (Math.Abs(meshTop - top) > BoulderTolerance) e.Add(at + $" mesh top {meshTop:F2} misses boulder top {top:F2}");
        }

        static float TopAt(FeatureEdit f, float x, float z)
        {
            if (f.Kind != FeatureEdit.Add || f.Shape != FeatureEdit.Boulder) return float.NegativeInfinity;
            float ex = f.Bx - f.Ax, ez = f.Bz - f.Az, ll = ex * ex + ez * ez;
            float best = TopAlong(f, x, z, ll > 0 ? Math.Max(0f, Math.Min(1f, ((x - f.Ax) * ex + (z - f.Az) * ez) / ll)) : 0f);
            if (f.Ay != f.By)
                for (int i = 0; i <= TopSamples; i++) best = Math.Max(best, TopAlong(f, x, z, i / (float)TopSamples));
            return best;
        }

        const int TopSamples = 64;

        static float TopAlong(FeatureEdit f, float x, float z, float t)
        {
            float qx = x - f.Ax - t * (f.Bx - f.Ax), qz = z - f.Az - t * (f.Bz - f.Az), dd = f.R * f.R - qx * qx - qz * qz;
            return dd > 0 ? f.Ay + t * (f.By - f.Ay) + (float)Math.Sqrt(dd) : float.NegativeInfinity;
        }

        static List<int> ObjectChunks(GeneratedWorld w, FeatureEdit[] edits, float[] bogs, int across, float span)
        {
            var picked = new List<int>();
            foreach (var f in edits)
            {
                int c = Math.Min(across - 1, (int)(f.Az / span)) * across + Math.Min(across - 1, (int)(f.Ax / span));
                if (!picked.Contains(c)) picked.Add(c);
                if (picked.Count >= ObjectCheckChunks / 2) break;
            }
            for (int k = 0; k < bogs.Length && k < 2 * ObjectField.BogStride; k += ObjectField.BogStride)
            {
                int c = Math.Min(across - 1, (int)(bogs[k + 1] / span)) * across + Math.Min(across - 1, (int)(bogs[k] / span));
                if (!picked.Contains(c)) picked.Add(c);
            }
            int quota = picked.Count + ObjectCheckChunks / 2;
            var rivers = new List<int>();
            foreach (var r in w.Water.Rivers)
            {
                if (r.X.Count == 0) continue;
                int k = r.X.Count / 2;
                int c = Math.Min(across - 1, (int)(r.Y[k] * w.Config.CellMeters / span)) * across + Math.Min(across - 1, (int)(r.X[k] * w.Config.CellMeters / span));
                if (!picked.Contains(c)) picked.Add(c);
                if (picked.Count >= quota) break;
            }
            return picked;
        }

        static bool OnBank(GeneratedWorld w, TerrainDetail detail, int site, int gx, int gz, float x, float z, float ground)
        {
            if (BankSites.Nearest(detail.Cells, x, z, out var q, out float d, out float t))
            {
                float hw = q.Wa + (q.Wb - q.Wa) * t;
                if (d <= hw + NearField.Bank(hw) + BankSites.ReachPadM && BankSites.InBand(site, ground - (q.Sa + (q.Sb - q.Sa) * t))) return true;
            }
            if (site != BankSites.Reed) return false;
            int n = w.Config.Size;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int l = w.Water.LakeId[az * n + ax];
                    if (l >= 0 && BankSites.InBand(site, ground - w.Water.Lakes[l].Level)) return true;
                }
            return false;
        }

        static int NearLake(GeneratedWorld w, int gx, int gz, float y)
        {
            int n = w.Config.Size;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int l = w.Water.LakeId[az * n + ax];
                    if (l >= 0 && y < w.Water.Lakes[l].Level) return l;
                }
            return -1;
        }

        static List<int>[] RiverBedCells(GeneratedWorld w, out List<float[]> segs)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var cells = new List<int>[n * n];
            segs = new List<float[]>();
            foreach (var r in w.Water.Rivers)
                for (int k = 0; k + 1 < r.X.Count; k++)
                {
                    float ax = r.X[k] * cm, az = r.Y[k] * cm, bx = r.X[k + 1] * cm, bz = r.Y[k + 1] * cm;
                    float hw = 0.5f * Math.Max(r.Width[k], r.Width[k + 1]), pad = hw + 30f;
                    int id = segs.Count;
                    segs.Add(new[] { ax, az, bx, bz, hw });
                    int x0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, bx) - pad) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(ax, bx) + pad) / cm));
                    int z0 = Math.Max(0, (int)Math.Floor((Math.Min(az, bz) - pad) / cm)), z1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(az, bz) + pad) / cm));
                    for (int gz = z0; gz <= z1; gz++)
                        for (int gx = x0; gx <= x1; gx++)
                            (cells[gz * n + gx] ??= new List<int>()).Add(id);
                }
            return cells;
        }

        static bool OnBed(List<int>[] cells, List<float[]> segs, int i, float x, float z, float r)
        {
            if (cells[i] == null) return false;
            foreach (int id in cells[i])
            {
                var s = segs[id];
                float bx = s[2] - s[0], bz = s[3] - s[1], l2 = bx * bx + bz * bz;
                float t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - s[0]) * bx + (z - s[1]) * bz) / l2)) : 0;
                float ex = x - s[0] - bx * t, ez = z - s[1] - bz * t;
                if (ex * ex + ez * ez < (s[4] + r) * (s[4] + r)) return true;
            }
            return false;
        }

        static void CheckObjectIds(ObjectInstance[] a, int chunk, List<string> e)
        {
            var ids = new HashSet<ulong>();
            foreach (var o in a)
            {
                if (!ids.Add(o.Id)) { e.Add($"object id {o.Id:x} repeats in chunk {chunk}"); return; }
                if (ObjectIds.Chunk(o.Id) != chunk || ObjectIds.Def(o.Id) != o.Def) { e.Add($"object id {o.Id:x} does not name chunk {chunk} def {o.Def}"); return; }
            }
        }

        static bool SolidIdsMatch(ObjectInstance[] all, ObjectInstance[] solid)
        {
            var byId = new Dictionary<ulong, ObjectInstance>();
            foreach (var o in all) byId[o.Id] = o;
            foreach (var o in solid)
                if (!byId.TryGetValue(o.Id, out var p) || p.X != o.X || p.Z != o.Z || p.Def != o.Def) return false;
            return true;
        }

        static bool SameObjects(ObjectInstance[] a, ObjectInstance[] b)
        {
            if (a.Length != b.Length) return false;
            for (int k = 0; k < a.Length; k++)
            {
                var p = a[k]; var q = b[k];
                if (p.Id != q.Id || p.Def != q.Def || p.Variant != q.Variant || p.X != q.X || p.Y != q.Y || p.Z != q.Z || p.Size != q.Size || p.Qx != q.Qx || p.Qy != q.Qy || p.Qz != q.Qz || p.Qw != q.Qw) return false;
            }
            return true;
        }
    }
}
