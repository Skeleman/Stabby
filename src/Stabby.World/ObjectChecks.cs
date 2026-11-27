using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const int ObjectCheckChunks = 4;

        static void CheckObjects(GeneratedWorld w, TerrainDetail detail, EditGrid grid, ObjectField field, List<string> e)
        {
            var edits = grid.Edits;
            int across = field.ChunksAcross;
            float span = WorldChunks.Size * w.Config.CellMeters;
            List<float[]> segs = null;
            List<int>[] bed = null;
            float[] bogs = null, sands = null;
            Parallel.Invoke(() => bed = RiverBedCells(w, out segs), () => bogs = field.BogPools(), () => sands = field.QuicksandPools());
            var picked = ObjectChunks(w, edits, bogs, sands, across, span);
            if (picked.Count == 0) return;
            var placed = new ObjectInstance[picked.Count][];
            Parallel.For(0, picked.Count, k => placed[k] = field.Chunk(picked[k] % across, picked[k] / across));
            int c0 = picked[0], cx0 = c0 % across, cz0 = c0 / across;
            var a0 = placed[0];
            var fresh = new string[3];
            Parallel.Invoke(
                () => { if (!SameObjects(a0, new ObjectField(w, detail, edits, w.Objects).Place(cx0, cz0))) fresh[0] = $"objects in chunk {cx0},{cz0} differ between runs"; },
                () => { if (!SolidIdsMatch(a0, field.Fresh().Place(cx0, cz0, true))) fresh[1] = $"object ids in chunk {cx0},{cz0} change when only solid objects are placed"; },
                () => { string fault = RegionFault(field, field.Fresh(), a0, c0); if (fault != null) fresh[2] = $"collision region {cx0},{cz0} {fault}"; });
            foreach (var f in fresh) if (f != null) { e.Add(f); return; }
            var found = new List<KeyValuePair<int, string>>[picked.Count];
            Parallel.For(0, picked.Count, k => found[k] = ObjectFaults(w, detail, grid, field, bed, segs, bogs, sands, picked[k], placed[k]));
            var seen = new HashSet<int>();
            foreach (var list in found)
                foreach (var f in list)
                    if (f.Key < 0 || seen.Add(f.Key)) e.Add(f.Value);
        }

        static List<KeyValuePair<int, string>> ObjectFaults(GeneratedWorld w, TerrainDetail detail, EditGrid grid, ObjectField field, List<int>[] bed, List<float[]> segs, float[] bogs, float[] sands, int c, ObjectInstance[] a)
        {
            var edits = grid.Edits;
            var ids = new List<int>();
            int n = w.Config.Size, across = field.ChunksAcross, cx = c % across, cz = c / across;
            float cm = w.Config.CellMeters, span = WorldChunks.Size * cm;
            var found = new List<KeyValuePair<int, string>>();
            var seen = new bool[13];
            var idFaults = new List<string>();
            CheckObjectIds(a, c, idFaults);
            foreach (var m in idFaults) found.Add(new KeyValuePair<int, string>(-1, m));
            var contacts = new List<ObjectColumn>();
            foreach (var o in a)
            {
                var def = w.Objects.Objects[o.Def];
                float r = field.Footprint(o.Def) * o.Size;
                string At() => $"{def.Id} at {o.X:F0},{o.Z:F0}";
                void Fail(int kind, string message) { seen[kind] = true; found.Add(new KeyValuePair<int, string>(kind, At() + message)); }
                if (!seen[0] && (o.X < cx * span || o.X >= (cx + 1) * span || o.Z < cz * span || o.Z >= (cz + 1) * span)) Fail(0, " lies outside its chunk");
                int gx = Math.Min(n - 1, (int)(o.X / cm + 0.5f)), gz = Math.Min(n - 1, (int)(o.Z / cm + 0.5f)), i = gz * n + gx;
                float ground = ObjectField.GroundAt(detail, cm, o.X, o.Z), cenoteFoot = 0f;
                if (!ObjectField.IsCave(def) && def.Parts[0].Shape != ObjectPart.Bridge)
                {
                    if (!seen[1] && w.Sea[i]) Fail(1, " stands in the sea");
                    int lake = w.Water.LakeId[i], site = def.Placement.Bank == null ? -1 : BankSites.Of(def.Placement.Bank);
                    if (!seen[2] && site < 0 && (lake >= 0 || (lake = NearLake(w, gx, gz, ground)) >= 0)) Fail(2, $" stands in lake {lake}");
                    if (!seen[3] && site < 0 && OnBed(bed, segs, i, o.X, o.Z, 0.5f * r)) Fail(3, " lies on a river bed");
                    if (!seen[9] && site >= 0 && !OnBank(w, detail, site, gx, gz, o.X, o.Z, ground)) Fail(9, " is off its river or lake bank");
                    bool oasis = def.Placement.Oasis != null, cenote = def.Placement.PerCenote > 0 && field.CenoteFoot(o.X, o.Z, out cenoteFoot);
                    if (!seen[4] && !oasis)
                    {
                        grid.Near(o.X - 0.5f * r, o.Z - 0.5f * r, o.X + 0.5f * r, o.Z + 0.5f * r, ids);
                        if (def.Placement.Discovery > 0) ids.RemoveAll(f => edits[f].Shape == FeatureEdit.Spring || (def.Placement.Discovery == ObjectField.WolfDen && edits[f].Shape == FeatureEdit.Boulder));
                        ids.RemoveAll(f => edits[f].Link || (cenote && edits[f].Shape == FeatureEdit.Cenote) || (edits[f].Shape == FeatureEdit.Vault && (def.Placement.Discovery > 0 || ground > edits[f].MaxY)));
                        if (ids.Count > 0) Fail(4, " sits inside a feature edit");
                    }
                    for (int k = 0; k < bogs.Length && !seen[11]; k += ObjectField.BogStride)
                    {
                        float bx = o.X - bogs[k], bz = o.Z - bogs[k + 1], reach = ObjectField.BogMargin(bogs[k + 2]) + 0.5f * r;
                        if (bx * bx + bz * bz < reach * reach) Fail(11, $" stands in the bog margin at {bogs[k]:F0},{bogs[k + 1]:F0}");
                    }
                    for (int k = 0; k < sands.Length && !seen[12]; k += ObjectField.BogStride)
                    {
                        float bx = o.X - sands[k], bz = o.Z - sands[k + 1], reach = ObjectField.BogMargin(sands[k + 2]) + 0.5f * r;
                        if (bx * bx + bz * bz < reach * reach) Fail(12, $" stands in the quicksand margin at {sands[k]:F0},{sands[k + 1]:F0}");
                    }
                    float foot = ground;
                    if (oasis) foot = field.OasisFoot(o.X, o.Z);
                    if (cenote) foot = cenoteFoot;
                    if (ObjectPart.IsDungeon(def.Parts[0].Shape)) ObjectField.SeatRange(detail, cm, ObjectField.Seats(def), o, out foot, out _);
                    if (!seen[5] && (o.Y > foot + 0.05f || o.Y < foot - o.Size)) Fail(5, $" is not grounded ({o.Y:F2} vs ground {foot:F2})");
                    if (!seen[8] && !oasis && !cenote)
                    {
                        float sg = ObjectField.SlopeStepM, sx = (ObjectField.GroundAt(detail, cm, o.X + sg, o.Z) - ObjectField.GroundAt(detail, cm, o.X - sg, o.Z)) / (2 * sg);
                        float sz = (ObjectField.GroundAt(detail, cm, o.X, o.Z + sg) - ObjectField.GroundAt(detail, cm, o.X, o.Z - sg)) / (2 * sg), slope = (float)Math.Sqrt(sx * sx + sz * sz);
                        if (slope < def.Placement.SlopeMin - 1e-3f || slope > def.Placement.SlopeMax + 1e-3f) Fail(8, $" stands on slope {slope:F2} outside {def.Placement.SlopeMin:F2}-{def.Placement.SlopeMax:F2}");
                    }
                    if (!seen[7])
                    {
                        string seat = SeatFault(w, detail, o);
                        if (seat != null) Fail(7, seat);
                    }
                    if (!seen[12])
                    {
                        float pit = ObjectField.PitFloat(w.Objects, detail, cm, o);
                        if (pit > 0) Fail(12, $" root pit floats {pit:F2} m past its skirt");
                    }
                    var biomes = def.Placement.Biomes;
                    if (!seen[6] && biomes.Count > 0 && (w.Biome[i] < 0 || !biomes.Contains(w.BiomeIds[w.Biome[i]]))) Fail(6, " is outside its biomes");
                }
                if (!seen[10])
                {
                    string loose = field.ContactFault(o, contacts);
                    if (loose != null) Fail(10, loose);
                }
            }
            return found;
        }

        public const int SiteCheckLimit = 6;

        sealed class DecodedChunks
        {
            readonly ObjectField field;
            readonly Dictionary<int, ObjectInstance[]> seen = new Dictionary<int, ObjectInstance[]>();

            public DecodedChunks(ObjectField field) => this.field = field;

            public ObjectInstance[] Get(int cx, int cz)
            {
                int c = cz * field.ChunksAcross + cx;
                if (!seen.TryGetValue(c, out var o)) seen[c] = o = field.Chunk(cx, cz);
                return o;
            }

            public void Warm(List<int> chunks)
            {
                int across = field.ChunksAcross;
                var got = new ObjectInstance[chunks.Count][];
                Parallel.For(0, chunks.Count, k => got[k] = field.Chunk(chunks[k] % across, chunks[k] / across));
                for (int k = 0; k < chunks.Count; k++) seen[chunks[k]] = got[k];
            }
        }

        static void CheckSites(GeneratedWorld w, TerrainDetail detail, EditGrid grid, ObjectField field, List<string> e)
        {
            int across = field.ChunksAcross, checkedSites = 0;
            float span = field.ChunkMeters;
            var columns = new List<ObjectColumn>();
            var dens = new List<FeatureEdit>();
            var decoded = new DecodedChunks(field);
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
                            foreach (var o in decoded.Get(cx, cz))
                            {
                                if (field.IsSite(o.Def) || w.Objects.Objects[o.Def].Kind == ObjectLibrary.Structure) continue;
                                columns.Clear();
                                field.ColumnsOf(o, columns);
                                foreach (var col in columns)
                                    if (field.SiteGap(s, col.X, col.Z) < col.R) { e.Add($"{w.Objects.Objects[o.Def].Id} at {o.X:F0},{o.Z:F0} has a column inside {at}"); return; }
                            }
                }
        }

        public static void CheckRoads(GeneratedWorld w, EditGrid grid, ObjectField field, List<string> e)
        {
            var defs = w.Objects.Objects;
            var segs = field.Roads();
            float span = field.ChunkMeters;
            int across = field.ChunksAcross;
            var chunks = new HashSet<int>();
            foreach (var s in segs)
            {
                string at = $"road {s.X0:F0},{s.Z0:F0} to {s.X1:F0},{s.Z1:F0}";
                if (s.Kind == RoadSegment.Bridge)
                {
                    string f = field.BridgeFault(s.X0, s.Z0, s.X1, s.Z1, out _, out _, out _);
                    if (f != null) { e.Add($"bridge {s.X0:F0},{s.Z0:F0} to {s.X1:F0},{s.Z1:F0} {f}"); return; }
                }
                else if (s.Kind != RoadSegment.Lane && s.Kind != RoadSegment.Tunnel)
                {
                    string f = field.RoadFault(s.X0, s.Z0, s.X1, s.Z1, out bool ford);
                    if (f != null) { e.Add($"{at} {f}"); return; }
                    if (ford != (s.Kind == RoadSegment.Ford)) { e.Add($"{at} fords a river without being marked as a ford"); return; }
                }
                int x0 = Math.Max(0, (int)((Math.Min(s.X0, s.X1) - s.HalfWidth) / span)), x1 = Math.Min(across - 1, (int)((Math.Max(s.X0, s.X1) + s.HalfWidth) / span));
                int z0 = Math.Max(0, (int)((Math.Min(s.Z0, s.Z1) - s.HalfWidth) / span)), z1 = Math.Min(across - 1, (int)((Math.Max(s.Z0, s.Z1) + s.HalfWidth) / span));
                for (int cz = z0; cz <= z1; cz++) for (int cx = x0; cx <= x1; cx++) chunks.Add(cz * across + cx);
            }
            var bored = new HashSet<int>();
            field.TunnelEdits(out var bores);
            foreach (var g in bores) bored.Add(g.Tile);
            foreach (var f in grid.Edits)
                if (f.Link && !bored.Contains(Features.TileOf(f, w.Config.CellMeters)) && field.OnRoad(0.5f * (f.MinX + f.MaxX), 0.5f * (f.MinZ + f.MaxZ), 0.5f * (float)Math.Sqrt((f.MaxX - f.MinX) * (f.MaxX - f.MinX) + (f.MaxZ - f.MinZ) * (f.MaxZ - f.MinZ)))) { e.Add($"dungeon link edit at {f.Ax:F0},{f.Az:F0} meets a road"); return; }
            foreach (int c in chunks)
                foreach (var o in field.Chunk(c % across, c / across))
                {
                    if (field.IsTownMember(o.Def) || defs[o.Def].Placement.Cave > 0 || o.Def == field.BridgeDef()) continue;
                    if (field.OnRoad(o.X, o.Z, defs[o.Def].FootprintRadius() * o.Size)) { e.Add($"{defs[o.Def].Id} at {o.X:F0},{o.Z:F0} sits on a road"); return; }
                }
            foreach (var s in field.Discoveries())
                if (field.OnRoad(s.X, s.Z, field.SiteRadius(s))) { e.Add($"discovery {defs[s.Def].Id} at {s.X:F0},{s.Z:F0} sits on a road"); return; }
        }

        public static void CheckTowns(GeneratedWorld w, EditGrid grid, ObjectField field, List<string> e)
        {
            var defs = w.Objects.Objects;
            float span = field.ChunkMeters;
            int across = field.ChunksAcross;
            var plans = field.TownPlans();
            var under = new SortedSet<int>();
            foreach (var p in plans)
            {
                int x0 = Math.Max(0, (int)((p.X - p.Radius) / span)), x1 = Math.Min(across - 1, (int)((p.X + p.Radius) / span));
                int z0 = Math.Max(0, (int)((p.Z - p.Radius) / span)), z1 = Math.Min(across - 1, (int)((p.Z + p.Radius) / span));
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                        under.Add(cz * across + cx);
                foreach (var m in p.Members)
                    under.Add(Math.Min(across - 1, (int)(m.Z / span)) * across + Math.Min(across - 1, (int)(m.X / span)));
            }
            var chunks = new List<int>(under);
            var decoded = new DecodedChunks(field);
            decoded.Warm(chunks);
            foreach (var p in plans)
            {
                var t = w.Objects.Towns[p.Town];
                string at = $"town {t.Id} at {p.X:F0},{p.Z:F0}";
                if (p.Members.Count == 0) { e.Add(at + " has no sites"); return; }
                for (int a = 0; a < p.Members.Count; a++)
                {
                    var m = p.Members[a];
                    float ra = defs[m.Def].FootprintRadius() * m.Size;
                    for (int b = a + 1; b < p.Members.Count; b++)
                    {
                        var o = p.Members[b];
                        float g = ra + defs[o.Def].FootprintRadius() * o.Size, dx = m.X - o.X, dz = m.Z - o.Z;
                        if (dx * dx + dz * dz < g * g) { e.Add($"{at}: {defs[m.Def].Id} and {defs[o.Def].Id} overlap"); return; }
                    }
                    int cx = Math.Min(across - 1, (int)(m.X / span)), cz = Math.Min(across - 1, (int)(m.Z / span));
                    if (!Array.Exists(decoded.Get(cx, cz), o => o.Def == m.Def && o.X == m.X && o.Z == m.Z)) { e.Add($"{at}: {defs[m.Def].Id} at {m.X:F0},{m.Z:F0} is missing from its chunk"); return; }
                    if (Math.Sqrt((m.X - p.X) * (m.X - p.X) + (m.Z - p.Z) * (m.Z - p.Z)) + ra > p.Radius) { e.Add($"{at}: {defs[m.Def].Id} lies outside the town"); return; }
                }
                float water = field.TownWater(p.X, p.Z, t.WaterM);
                if (water > t.WaterM) { e.Add($"{at} lies {water:F0} m from water, over {t.WaterM:F0}"); return; }
                foreach (var f in grid.Edits)
                {
                    float fx = Math.Max(f.MinX, Math.Min(p.X, f.MaxX)), fz = Math.Max(f.MinZ, Math.Min(p.Z, f.MaxZ));
                    if ((fx - p.X) * (fx - p.X) + (fz - p.Z) * (fz - p.Z) >= p.Radius * p.Radius) continue;
                    if (f.Shape == FeatureEdit.Boulder ? field.InTownGround(0.5f * (f.MinX + f.MaxX), 0.5f * (f.MinZ + f.MaxZ), f.R, true) : true) { e.Add($"{at} holds a feature edit of shape {f.Shape}"); return; }
                }
                foreach (var s in field.Discoveries())
                {
                    float dx = s.X - p.X, dz = s.Z - p.Z, g = p.Radius + field.SiteRadius(s);
                    if (dx * dx + dz * dz < g * g) { e.Add($"{at} overlaps discovery {defs[s.Def].Id}"); return; }
                }
                int x0 = Math.Max(0, (int)((p.X - p.Radius) / span)), x1 = Math.Min(across - 1, (int)((p.X + p.Radius) / span));
                int z0 = Math.Max(0, (int)((p.Z - p.Radius) / span)), z1 = Math.Min(across - 1, (int)((p.Z + p.Radius) / span));
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                        foreach (var o in decoded.Get(cx, cz))
                        {
                            if (field.IsTownMember(o.Def) || field.IsSite(o.Def) || defs[o.Def].Placement.Cave > 0) continue;
                            if (field.InTownGround(o.X, o.Z, defs[o.Def].FootprintRadius() * o.Size, defs[o.Def].Kind == ObjectLibrary.Structure)) { e.Add($"{defs[o.Def].Id} at {o.X:F0},{o.Z:F0} sits on the ground of {at}"); return; }
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

        static void CheckStructures(GeneratedWorld w, TerrainDetail detail, EditGrid grid, ObjectField field, List<string> e)
        {
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
        public const float BoulderSteepNy = 0.5f, BoulderLowNy = -0.3f, BoulderNormalLift = 0.3f, BoulderNormalShare = 0.1f, BoulderNormalFloor = 2f;

        public static string BoulderNormalFault<TCols>(TCols cols, NetsBuffers m, FeatureEdit b, FeatureEdit[] near, float s) where TCols : struct, IColumnField
        {
            int steep = 0, bad = 0;
            float ex = b.Bx - b.Ax, ey = b.By - b.Ay, ez = b.Bz - b.Az, ll = ex * ex + ey * ey + ez * ez;
            foreach (int t in new HashSet<int>(m.Tris))
            {
                int v = t * 6;
                float x = m.Verts[v], y = m.Verts[v + 1], z = m.Verts[v + 2], ny = m.Verts[v + 4];
                float px = x - b.Ax, py = y - b.Ay, pz = z - b.Az;
                float u = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
                float qx = px - u * ex, qy = Math.Max(0f, py - u * ey), qz = pz - u * ez, d = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
                if (d <= 0f || Math.Abs(d - b.R) > 0.5f * s + b.N || y - cols.Height((int)Math.Round(x / s), (int)Math.Round(z / s)) < 0.5f * s) continue;
                float ty = qy / d;
                if (ty > BoulderSteepNy || ty < BoulderLowNy) continue;
                bool shared = false;
                foreach (var f in near)
                    shared |= (f.Ax != b.Ax || f.Ay != b.Ay || f.Az != b.Az) && f.Kind == FeatureEdit.Add && f.Shape == FeatureEdit.Boulder && f.BoulderDistance(x, y, z) < f.R + s;
                if (shared) continue;
                steep++;
                if (ny - ty > BoulderNormalLift) bad++;
            }
            return bad > Math.Max(BoulderNormalFloor, BoulderNormalShare * steep) ? $" tilts {bad} of {steep} steep-face normals upward" : null;
        }
        public const int BoulderMeshSamples = 12, BoulderFootColumns = 12;
        public const float BoulderFootStep = 0.08f, BoulderFootSkin = 0.05f;

        static void CheckBoulderFoot(TerrainDetail detail, float cm, FeatureEdit b, List<string> e)
        {
            for (int j = 0; j <= BoulderFootColumns; j++)
                for (int i = 0; i <= BoulderFootColumns; i++)
                {
                    float x = b.MinX + (b.MaxX - b.MinX) * i / BoulderFootColumns, z = b.MinZ + (b.MaxZ - b.MinZ) * j / BoulderFootColumns;
                    float ground = ObjectField.GroundAt(detail, cm, x, z);
                    bool air = false;
                    for (float y = ground + BoulderFootSkin; y <= b.MaxY; y += BoulderFootStep * b.R)
                    {
                        if (!b.Reach(x, y, z, out _)) { air = true; continue; }
                        if (air) { e.Add($"boulder at {x:F1},{z:F1} leaves an overhang gap {y - ground:F2} m above its base"); return; }
                        break;
                    }
                }
        }

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
                    if (edits[f].Shape != FeatureEdit.Boulder && edits[f].Shape != FeatureEdit.SlotCanyon && !edits[f].Link) { e.Add(at + " overlaps a terrain feature"); return; }
            }
            int samples = Math.Min(BoulderMeshSamples, boulders.Count);
            for (int k = 0; k < samples; k++)
            {
                var b = edits[boulders[(int)((long)k * boulders.Count / samples)]];
                int before = e.Count;
                CheckBoulderFoot(detail, cm, b, e);
                if (e.Count == before) CheckBoulderMesh(detail, grid, b, e);
                if (e.Count == before) CheckBoulderFade(detail, grid, b, e);
                if (e.Count > before) return;
            }
        }

        static FeatureEdit[] NearAt(EditGrid grid, float s, int x0, int z0, int cells, List<int> ids)
        {
            float bx0 = x0 * s, bz0 = z0 * s, bx1 = (x0 + cells) * s, bz1 = (z0 + cells) * s;
            grid.Near(bx0 - 1.5f * s, bz0 - 1.5f * s, bx1 + 1.5f * s, bz1 + 1.5f * s, ids);
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
            string tilt = BoulderNormalFault(new DetailColumns { Detail = detail, StepM = s }, m, b, list.Items, s);
            if (tilt != null) e.Add(at + tilt);
            var ground = new DetailGround { Detail = detail };
            float mx = 0.5f * (b.Ax + b.Bx), mz = 0.5f * (b.Az + b.Bz), top = TopAt(b, mx, mz);
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
            if (dd <= 0) return float.NegativeInfinity;
            float y = f.Ay + t * (f.By - f.Ay), r = f.R - f.Weathering(x, y + (float)Math.Sqrt(dd), z);
            dd = r * r - qx * qx - qz * qz;
            return dd > 0 ? y + (float)Math.Sqrt(dd) : float.NegativeInfinity;
        }

        static List<int> ObjectChunks(GeneratedWorld w, FeatureEdit[] edits, float[] bogs, float[] sands, int across, float span)
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
            for (int k = 0; k < sands.Length && k < 2 * ObjectField.BogStride; k += ObjectField.BogStride)
            {
                int c = Math.Min(across - 1, (int)(sands[k + 1] / span)) * across + Math.Min(across - 1, (int)(sands[k] / span));
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
