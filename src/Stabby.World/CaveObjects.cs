using System;
using System.Collections.Generic;
using System.Threading;

namespace Stabby.World
{
    public struct EditSlice : IEditList
    {
        public FeatureEdit[] Items;
        public int N;
        public int Count => N;
        public FeatureEdit At(int k) => Items[k];
    }

    public sealed partial class ObjectField
    {
        public static readonly string[] CaveNames = { "", "stalactite", "stalagmite", "rubble", "column", "crystal", "fungus", "crystal-light", "skylight", "mine-timber", "mine-rail", "mine-cart", "lava-glow" };
        public const int CaveStalactite = 1, CaveStalagmite = 2, CaveRubble = 3, CaveColumn = 4, CaveCrystal = 5, CaveFungus = 6, CaveCrystalLight = 7, CaveSkylight = 8, CaveMineTimber = 9, CaveMineRail = 10, CaveMineCart = 11, CaveLava = 12;
        public const int CaveLightTries = 8;
        public const float CaveLightSideShare = 0.7f, CaveLightAlongMin = 0.15f, CaveLightAlongMax = 0.85f, SkylightJitterShare = 0.5f, SkylightFloorM = 40f;

        public struct CaveLightSpot
        {
            public int Kind, Edit, Tile;
            public float T, U;
            public ulong Seed;
        }

        public static bool IsCaveLight(int kind) => (kind >= CaveFungus && kind <= CaveSkylight) || kind == CaveLava;
        public static bool IsMineKind(int kind) => kind >= CaveMineTimber && kind <= CaveMineCart;

        static CaveLightSpot[] PlanCaveLights(GeneratedWorld w, FeatureEdit[] edits, ulong stream)
        {
            var spots = new List<CaveLightSpot>();
            var lib = w.Caves;
            if (lib == null || lib.LightMost <= 0) return spots.ToArray();
            float cm = w.Config.CellMeters;
            var tiles = new SortedDictionary<int, List<int>>();
            for (int e = 0; e < edits.Length; e++)
            {
                var c = edits[e];
                bool lava = LavaBasin(c);
                if (c.Kind != FeatureEdit.Carve || (c.Shape != FeatureEdit.Shaft && !FeatureEdit.IsRoom(c.Shape) && !lava)) continue;
                int t = lava ? Features.TileAt(c.Ax, c.Az, cm) : Features.TileOf(c, cm);
                if (t < 0) continue;
                if (!tiles.TryGetValue(t, out var list)) tiles[t] = list = new List<int>();
                list.Add(e);
            }
            var rooms = new List<int>();
            foreach (var kv in tiles)
            {
                var rng = new Rng(Rng.Mix(stream ^ (ulong)(uint)kv.Key));
                float roll = rng.NextFloat();
                int want = 1 + (int)(rng.NextU32() % (uint)lib.LightMost), made = 0;
                rooms.Clear();
                foreach (int e in kv.Value)
                {
                    bool lava = LavaBasin(edits[e]);
                    if (edits[e].Shape != FeatureEdit.Shaft && !lava) { rooms.Add(e); continue; }
                    if (made >= lib.LightMost) continue;
                    spots.Add(new CaveLightSpot { Kind = lava ? CaveLava : CaveSkylight, Edit = e, Tile = kv.Key, Seed = rng.NextU64() });
                    made++;
                }
                if (roll >= lib.LightChance || rooms.Count == 0) continue;
                for (int k = 0; k < want && made < lib.LightMost; k++, made++)
                {
                    int e = rooms[(int)(rng.NextU32() % (uint)rooms.Count)];
                    float t = rng.Range(CaveLightAlongMin, CaveLightAlongMax), u = rng.Range(-1f, 1f) * CaveLightSideShare;
                    spots.Add(new CaveLightSpot { Kind = edits[e].Shape == FeatureEdit.CrystalCell ? CaveCrystalLight : CaveFungus, Edit = e, Tile = kv.Key, T = t, U = u, Seed = rng.NextU64() });
                }
            }
            return spots.ToArray();
        }

        public static bool LavaBasin(FeatureEdit c) => c.Obsidian && c.Kind == FeatureEdit.Carve && c.Shape == FeatureEdit.Spring;

        public int CaveLightCount => caveLights.Length;
        public CaveLightSpot CaveLight(int i) => caveLights[i];

        int CaveDef(int kind)
        {
            for (int d = 0; d < lib.Objects.Count; d++) if (lib.Objects[d].Placement.Cave == kind) return d;
            return -1;
        }

        public bool CaveLightAt(int i, out ObjectInstance o)
        {
            o = caveLit[i];
            return o.Def >= 0;
        }

        ObjectInstance[] ResolveCaveLights(out Dictionary<int, List<int>> cells, out float most)
        {
            most = 0f;
            var lit = new ObjectInstance[caveLights.Length];
            cells = new Dictionary<int, List<int>>();
            for (int i = 0; i < lit.Length; i++)
            {
                var p = caveLights[i];
                int d = CaveDef(p.Kind);
                if (d < 0 || !CaveLightAt(p, d, out lit[i])) { lit[i] = new ObjectInstance { Def = -1 }; continue; }
                most = Math.Max(most, footprint[d] * lit[i].Size);
                int at = CaveCell(lit[i].Z) * n + CaveCell(lit[i].X);
                if (!cells.TryGetValue(at, out var list)) cells[at] = list = new List<int>();
                list.Add(i);
            }
            return lit;
        }

        int CaveCell(float m) => Math.Max(0, Math.Min(n - 1, (int)Math.Floor(m / cm)));

        string CrowdsCaveLight(float x, float z, float reach)
        {
            if (caveLitCells.Count == 0) return null;
            int span = (int)Math.Ceiling((caveLitReach + reach + CaveColumnKeepM) / cm), gx = CaveCell(x), gz = CaveCell(z);
            for (int az = Math.Max(0, gz - span); az <= Math.Min(n - 1, gz + span); az++)
                for (int ax = Math.Max(0, gx - span); ax <= Math.Min(n - 1, gx + span); ax++)
                {
                    if (!caveLitCells.TryGetValue(az * n + ax, out var list)) continue;
                    foreach (int i in list)
                    {
                        var o = caveLit[i];
                        float dx = o.X - x, dz = o.Z - z, keep = footprint[o.Def] * o.Size + reach + CaveColumnKeepM;
                        if (dx * dx + dz * dz < keep * keep) return "crowds a cave light";
                    }
                }
            return null;
        }

        bool CaveLightAt(CaveLightSpot p, int d, out ObjectInstance o)
        {
            o = default;
            var def = lib.Objects[d];
            var pl = def.Placement;
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM, foot = footprint[d];
            var c = edits[p.Edit];
            bool sky = p.Kind == CaveSkylight, lava = p.Kind == CaveLava;
            float ex = c.Bx - c.Ax, ez = c.Bz - c.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
            float px = len > 1e-3f ? -ez / len : 1f, pz = len > 1e-3f ? ex / len : 0f, open = c.HoleR;
            var rng = new Rng(p.Seed);
            for (int k = 0; k < CaveLightTries; k++)
            {
                float t = rng.Range(CaveLightAlongMin, CaveLightAlongMax), u = rng.Range(-1f, 1f), v = rng.Range(-1f, 1f), grow = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                int variant = (int)(rng.NextU32() % (uint)def.Variants);
                if (k == 0) { t = p.T; u = sky ? 0f : p.U; v = 0f; }
                float x, z, ay, low;
                if (lava)
                {
                    float jitter = k == 0 ? 0f : LavaGlowJitterM;
                    x = c.Ax + u * jitter;
                    z = c.Az + v * jitter;
                    ay = Underground.BasinLevel(c) + LavaGlowAboveM;
                    low = Underground.BasinLevel(c) - Underground.PoolMaxDepthM - LavaGlowAboveM;
                }
                else if (sky)
                {
                    x = c.Ax + u * SkylightJitterShare * open;
                    z = c.Az + v * SkylightJitterShare * open;
                    ay = c.Ay;
                    low = ay - SkylightFloorM;
                }
                else
                {
                    float side = (k == 0 ? u : u * CaveLightSideShare) * c.FloorHalf;
                    x = c.Ax + ex * t + px * side;
                    z = c.Az + ez * t + pz * side;
                    ay = c.Ay + (c.By - c.Ay) * t;
                    low = ay - open - 6f;
                    ay -= 0.2f * open;
                }
                var rock = RockAt(x, z);
                if (!sky && !lava && !Owns(rock, c, x, ay, z)) continue;
                if (Walk.Solid(ground, rock, s, x, ay, z) >= 0) continue;
                float floor = Walk.Surface(ground, rock, s, x, z, ay, low);
                if (floor <= low) continue;
                float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * grow;
                var q = new ObjectInstance { Def = d, Variant = variant, X = x, Z = z, Size = size, Y = floor - pl.Sink * size };
                Yaw(qa, qb, false, ref q);
                if (CaveLightFault(q, rock, foot * size) != null) continue;
                o = q;
                return true;
            }
            return false;
        }

        string CaveLightFault(ObjectInstance o, EditSlice rock, float reach)
        {
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM;
            if (Walk.Solid(ground, rock, s, o.X, o.Y, o.Z) < 0) return "floats above the cave floor";
            if (Walk.Solid(ground, rock, s, o.X, o.Y + 0.5f * o.Size + 0.3f, o.Z) >= 0) return "is buried in rock";
            var light = lib.Objects[o.Def].Light;
            float lift = light == null ? 0f : light.LiftM;
            if (lift > 0f && Walk.Solid(ground, RockAt(o.X, o.Z), s, o.X, o.Y + lift, o.Z) >= 0) return "lights from inside the rock";
            return Crowded(rock, o.X, o.Z, reach, false);
        }

        void EmitCaveLights(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list, ref int ordinal)
        {
            int kind = lib.Objects[d].Placement.Cave;
            for (int i = 0; i < caveLights.Length; i++)
            {
                var p = caveLights[i];
                if (p.Kind != kind || !CaveLightAt(i, out var o)) continue;
                if (o.X < x0 || o.Z < z0 || o.X >= x0 + sx || o.Z >= z0 + sz) continue;
                o.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(o);
            }
        }
        public const float LavaGlowJitterM = 0.3f, LavaGlowAboveM = 0.1f;
        public const float CrystalFloorShare = 0.4f, CrystalCeilingShare = 0.3f, CrystalJitter = 0.5f, CrystalStepM = 0.5f, CrystalLift = 0.6f;
        public const float CaveKeepShare = 2.2f, CaveColumnKeepM = 0.6f, CaveTipHeadroomM = 1f, CaveMiteHeadM = 1f, CaveWalkLineM = 0.9f, CavePairShare = 0.4f, CaveTwinScale = 0.6f;
        public const float CaveColumnGapMinM = 2.4f, CaveColumnGapMaxM = 8f, CaveColumnEmbedM = 0.4f, CaveDripPerHundredM = 10f, CaveDripSurveyM = 150f;

        [ThreadStatic] static List<float> caveSeenX, caveSeenZ;
        [ThreadStatic] static FeatureEdit[] caveRock;

        public static bool IsCave(ObjectDef def) => def.Placement.Cave > 0;

        static int[] CaveCarves(FeatureEdit[] edits)
        {
            var list = new List<int>();
            for (int e = 0; e < edits.Length; e++)
            {
                var c = edits[e];
                if (c.CaveCarve) list.Add(e);
            }
            return list.ToArray();
        }

        static ulong CarveKey(FeatureEdit c)
        {
            ulong a = (uint)(int)Math.Floor(c.Ax * 4f), b = (uint)(int)Math.Floor(c.Az * 4f), y = (uint)(int)Math.Floor(c.Ay * 4f);
            return Rng.Mix(a ^ (b << 21) ^ (y << 42) ^ 0xCA7E5EEDUL);
        }

        EditSlice RockAt(float x, float z)
        {
            int gx = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(x / cm))), gz = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(z / cm)));
            int i = gz * n + gx, from = blockStart[i], to = blockStart[i + 1];
            var buf = caveRock;
            if (buf == null || buf.Length < to - from) caveRock = buf = new FeatureEdit[Math.Max(64, to - from)];
            int count = 0;
            for (int k = from; k < to; k++)
                if (blockItems[k] < 0) buf[count++] = solid[~blockItems[k]];
            return new EditSlice { Items = buf, N = count };
        }

        static float Reach3(FeatureEdit c, float x, float y, float z)
        {
            float ex = c.Bx - c.Ax, ey = c.By - c.Ay, ez = c.Bz - c.Az, ll = ex * ex + ey * ey + ez * ez;
            float px = x - c.Ax, py = y - c.Ay, pz = z - c.Az;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
            float qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
            return (float)Math.Sqrt(qx * qx + qy * qy + qz * qz) - c.HoleR;
        }

        static bool Dripstone(int kind) => kind == CaveStalactite || kind == CaveStalagmite || kind == CaveColumn;

        static bool InObsidian(EditSlice rock, float x, float y, float z)
        {
            float best = float.MaxValue;
            bool glass = false;
            for (int q = 0; q < rock.N; q++)
            {
                var o = rock.Items[q];
                if (!o.CaveCarve) continue;
                float r = Reach3(o, x, y, z);
                if (r < best) { best = r; glass = o.Obsidian; }
            }
            return glass;
        }

        static bool Owns(EditSlice rock, FeatureEdit c, float x, float y, float z)
        {
            float own = Reach3(c, x, y, z);
            for (int q = 0; q < rock.N; q++)
            {
                var o = rock.Items[q];
                if (!o.CaveCarve) continue;
                if (o.Ax == c.Ax && o.Az == c.Az && o.Ay == c.Ay && o.Bx == c.Bx && o.Bz == c.Bz && o.By == c.By) continue;
                if (Reach3(o, x, y, z) < own) return false;
            }
            return true;
        }

        string Crowded(EditSlice rock, float x, float z, float reach, bool hangs, bool stands = false)
        {
            if (stands && CrowdsCaveLight(x, z, reach) is string lit) return lit;
            for (int q = 0; q < rock.N; q++)
            {
                var o = rock.Items[q];
                if (o.Shape == FeatureEdit.Bridge && Underground.Plan(o, x, z) < reach + o.R + Underground.BridgeKeepM) return "crowds a rock bridge";
                if (!hangs && o.Kind == FeatureEdit.Carve && o.Shape == FeatureEdit.Cave && Underground.Plan(o, x, z) < reach + Underground.PassM) return "blocks a cave passage";
                if (o.Shape == FeatureEdit.Speleothem && o.Kind == FeatureEdit.Add)
                {
                    float dx = o.Ax - x, dz = o.Az - z, keep = o.R + o.N + reach + CaveColumnKeepM;
                    if (dx * dx + dz * dz < keep * keep) return "crowds a column";
                }
            }
            return null;
        }

        static void Yaw(float qa, float qb, bool flip, ref ObjectInstance o)
        {
            float l = (float)Math.Sqrt(qa * qa + qb * qb);
            float cy = l > 1e-4f ? qa / l : 1f, sy = l > 1e-4f ? qb / l : 0f;
            if (flip) { o.Qx = cy; o.Qy = 0f; o.Qz = -sy; o.Qw = 0f; }
            else { o.Qx = 0f; o.Qy = sy; o.Qz = 0f; o.Qw = cy; }
        }

        ObjectInstance[][] caveChunks;

        public ObjectInstance[] CaveChunk(int cx, int cz)
        {
            var all = Volatile.Read(ref caveChunks) ?? Interlocked.CompareExchange(ref caveChunks, new ObjectInstance[across * across][], null) ?? caveChunks;
            int c = cz * across + cx;
            var s = Volatile.Read(ref all[c]);
            if (s != null) return s;
            var full = Volatile.Read(ref chunks[c]);
            var list = new List<ObjectInstance>();
            if (full != null) { foreach (var o in full) if (IsCave(lib.Objects[o.Def])) list.Add(o); }
            else for (int d = 0; d < lib.Objects.Count; d++) if (IsCave(lib.Objects[d])) PlaceInto(cx, cz, false, list, null, false, false, d);
            s = list.ToArray();
            Volatile.Write(ref all[c], s);
            return s;
        }

        void EmitCave(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list, ref int ordinal)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            int kind = pl.Cave;
            if (kind == CaveCrystal) { EmitCrystal(d, chunk, x0, z0, sx, sz, list, ref ordinal); return; }
            if (IsCaveLight(kind)) { EmitCaveLights(d, chunk, x0, z0, sx, sz, list, ref ordinal); return; }
            if (IsMineKind(kind)) { EmitMine(d, chunk, x0, z0, sx, sz, list, ref ordinal); return; }
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM, foot = footprint[d];
            var seenX = caveSeenX ?? (caveSeenX = new List<float>());
            var seenZ = caveSeenZ ?? (caveSeenZ = new List<float>());
            foreach (int e in caveCarves)
            {
                var c = solid[e];
                if (c.Shape == FeatureEdit.Mine || (c.Link && kind != CaveStalactite && kind != CaveRubble)) continue;
                if (c.MaxX < x0 || c.MinX >= x0 + sx || c.MaxZ < z0 || c.MinZ >= z0 + sz) continue;
                if (pl.CaveIce ? !c.Ice : c.Ice && (kind == CaveStalactite || kind == CaveStalagmite)) continue;
                if (c.Obsidian && Dripstone(kind)) continue;
                float ex = c.Bx - c.Ax, ez = c.Bz - c.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                if (len < 1f) continue;
                float px = -ez / len, pz = ex / len, wf = c.FloorHalf, hole = c.HoleR;
                int tries = Math.Max(1, (int)((len + (FeatureEdit.IsRoom(c.Shape) ? 2f * c.R : 0f)) * pl.PerHundredM / 100f));
                float keep = CaveKeepShare * foot * pl.SizeMaxM + pl.ClusterRadiusM;
                var rng = new Rng(Rng.Mix(caveStream ^ key[d] ^ CarveKey(c)));
                seenX.Clear(); seenZ.Clear();
                for (int k = 0; k < tries; k++)
                {
                    float t = rng.NextFloat(), u = rng.Range(-0.9f, 0.9f) * wf;
                    float x = c.Ax + ex * t + px * u, z = c.Az + ez * t + pz * u, ay = c.Ay + (c.By - c.Ay) * t;
                    bool near = false;
                    for (int j = 0; j < seenX.Count && !near; j++)
                    {
                        float dx = seenX[j] - x, dz = seenZ[j] - z;
                        near = dx * dx + dz * dz < keep * keep;
                    }
                    seenX.Add(x); seenZ.Add(z);
                    for (int m = 0; m < pl.ClusterCount; m++)
                    {
                        float ox = rng.Range(-1f, 1f), oz = rng.Range(-1f, 1f), grow = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f), pair = rng.NextFloat();
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        if (near) continue;
                        if (m > 0 && kind != CaveRubble && pair > CavePairShare) continue;
                        float mx = m == 0 ? x : x + ox * pl.ClusterRadiusM, mz = m == 0 ? z : z + oz * pl.ClusterRadiusM;
                        if (mx < x0 || mz < z0 || mx >= x0 + sx || mz >= z0 + sz) continue;
                        var rock = RockAt(mx, mz);
                        if (!Owns(rock, c, mx, ay, mz)) continue;
                        if (Walk.Solid(ground, rock, s, mx, ay - 0.2f * hole, mz) >= 0) continue;
                        float floor = Walk.Surface(ground, rock, s, mx, mz, ay - 0.2f * hole, ay - hole - 6f);
                        if (floor <= ay - hole - 6f) continue;
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * grow, reach = foot * size;
                        var o = new ObjectInstance { Def = d, Variant = variant, X = mx, Z = mz, Size = size };
                        if (kind == CaveRubble)
                        {
                            float lateral = Math.Abs((mx - c.Ax) * px + (mz - c.Az) * pz);
                            if (lateral < CaveWalkLineM + reach) continue;
                            o.Y = floor - pl.Sink * size;
                            Yaw(qa, qb, false, ref o);
                        }
                        else
                        {
                            float ceil = Underground.Ceiling(ground, rock, s, mx, floor + 1f, mz, floor + 3f * hole);
                            if (float.IsNaN(ceil)) continue;
                            float gap = ceil - floor;
                            bool hangs = kind == CaveStalactite;
                            if (gap < (hangs ? Walk.Height + CaveTipHeadroomM + pl.SizeMinM : kind == CaveColumn ? CaveColumnGapMinM : Underground.DripGapM)) continue;
                            if (kind == CaveColumn)
                            {
                                if (gap > CaveColumnGapMaxM) continue;
                                float side = Math.Abs((mx - c.Ax) * px + (mz - c.Az) * pz);
                                o.Size = size = gap + 2f * CaveColumnEmbedM;
                                if (side < CaveWalkLineM + foot * size) continue;
                                o.Y = floor - CaveColumnEmbedM;
                                if (Walk.Solid(ground, rock, s, mx, o.Y + size, mz) < 0) continue;
                            }
                            else if (hangs)
                            {
                                size = Math.Max(pl.SizeMinM, Math.Min(pl.SizeMaxM, gap * (0.15f + 0.3f * grow)));
                                if (gap - size < Walk.Height + CaveTipHeadroomM) continue;
                                o.Size = size;
                                o.Y = ceil + pl.Sink * size;
                                if (Walk.Solid(ground, rock, s, mx, o.Y + 0.05f * size, mz) < 0) continue;
                                float tip = o.Y - size;
                                if (tip - Walk.Surface(ground, rock, s, mx, mz, tip, tip - 40f) < Walk.Height + Underground.HeadroomM) continue;
                            }
                            else
                            {
                                size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * grow * grow * grow;
                                if (m > 0) size = Math.Max(pl.SizeMinM, size * CaveTwinScale);
                                o.Size = size;
                                if (gap - size < Walk.Height + CaveMiteHeadM) continue;
                                o.Y = floor - pl.Sink * size;
                            }
                            if (Crowded(rock, mx, mz, foot * size, hangs, kind == CaveStalagmite || kind == CaveColumn) != null) continue;
                            Yaw(qa, qb, hangs, ref o);
                        }
                        if (kind != CaveStalactite && (Walk.Solid(ground, rock, s, mx, o.Y, mz) < 0 || Walk.Solid(ground, rock, s, mx, o.Y + 0.5f * o.Size + 0.3f, mz) >= 0)) continue;
                        if (kind == CaveStalactite && Walk.Solid(ground, rock, s, mx, o.Y - o.Size, mz) >= 0) continue;
                        o.Id = ObjectIds.Make(chunk, d, ordinal++);
                        list.Add(o);
                    }
                }
            }
        }

        public string CaveFault(ObjectInstance o)
        {
            var def = lib.Objects[o.Def];
            int kind = def.Placement.Cave;
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM, reach = footprint[o.Def] * o.Size;
            var rock = RockAt(o.X, o.Z);
            string at = $"{def.Id} at {o.X:F0},{o.Z:F0}";
            if (kind == CaveCrystal)
            {
                string why = CrystalFault(o, reach);
                return why == null ? null : at + " " + why;
            }
            if (IsCaveLight(kind))
            {
                string why = CaveLightFault(o, rock, reach);
                return why == null ? null : at + " " + why;
            }
            if (IsMineKind(kind))
            {
                string why = MineFault(kind, o);
                return why == null ? null : at + " " + why;
            }
            if (Dripstone(kind) && InObsidian(rock, o.X, o.Y + (kind == CaveStalactite ? -0.5f : 0.5f) * o.Size, o.Z)) return at + " is dripstone in an obsidian cave";
            if (kind == CaveStalactite)
            {
                if (Walk.Solid(ground, rock, s, o.X, o.Y + 0.05f * o.Size, o.Z) < 0) return at + " floats free of the ceiling";
                float tip = o.Y - o.Size;
                if (Walk.Solid(ground, rock, s, o.X, tip, o.Z) >= 0) return at + " is buried in rock";
                if (tip - Walk.Surface(ground, rock, s, o.X, o.Z, tip, tip - 40f) < Walk.Height + Underground.HeadroomM) return at + " hangs into the walking headroom";
            }
            else
            {
                if (kind == CaveColumn && Walk.Solid(ground, rock, s, o.X, o.Y + o.Size, o.Z) < 0) return at + " does not reach the ceiling";
                if (Walk.Solid(ground, rock, s, o.X, o.Y, o.Z) < 0) return at + " floats above the cave floor";
                if (Walk.Solid(ground, rock, s, o.X, o.Y + 0.5f * o.Size + 0.3f, o.Z) >= 0) return at + " is buried in rock";
            }
            if (kind == CaveRubble) return null;
            string crowd = Crowded(rock, o.X, o.Z, reach, kind == CaveStalactite, kind == CaveStalagmite || kind == CaveColumn);
            return crowd == null ? null : at + " " + crowd;
        }

        static void CrystalAxis(ObjectInstance o, out float ax, out float ay, out float az)
        {
            float x = o.Qx, y = o.Qy, z = o.Qz, w = o.Qw;
            ax = 2f * (x * y - w * z);
            ay = 1f - 2f * (x * x + z * z);
            az = 2f * (y * z + w * x);
        }

        static void CrystalTurn(float ax, float ay, float az, float qa, float qb, ref ObjectInstance o)
        {
            float l = (float)Math.Sqrt(qa * qa + qb * qb);
            float cy = l > 1e-4f ? qa / l : 1f, sy = l > 1e-4f ? qb / l : 0f;
            float x = az, z = -ax, w = 1f + ay, n = (float)Math.Sqrt(x * x + z * z + w * w);
            if (n < 1e-4f) { x = 1f; z = 0f; w = 0f; }
            else { x /= n; z /= n; w /= n; }
            o.Qx = x * cy - z * sy;
            o.Qy = w * sy;
            o.Qz = z * cy + x * sy;
            o.Qw = w * cy;
        }

        void EmitCrystal(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list, ref int ordinal)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM, foot = footprint[d];
            foreach (int e in caveCarves)
            {
                var c = solid[e];
                if (c.Shape != FeatureEdit.CrystalCell) continue;
                if (c.MaxX < x0 || c.MinX >= x0 + sx || c.MaxZ < z0 || c.MinZ >= z0 + sz) continue;
                float ex = c.Bx - c.Ax, ey = c.By - c.Ay, ez = c.Bz - c.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                float px = len > 1e-3f ? -ez / len : 1f, pz = len > 1e-3f ? ex / len : 0f, hole = c.HoleR, far = c.R + 1f;
                int tries = Math.Max(1, (int)((len + 2f * hole) * pl.PerHundredM / 100f));
                var rng = new Rng(Rng.Mix(caveStream ^ key[d] ^ CarveKey(c)));
                for (int k = 0; k < tries; k++)
                {
                    float t = rng.NextFloat(), pick = rng.NextFloat(), da = rng.Range(-1f, 1f), db = rng.Range(-1f, 1f);
                    float cx = c.Ax + ex * t, cy = c.Ay + ey * t, cz = c.Az + ez * t;
                    float bx, by, bz;
                    if (pick < CrystalFloorShare) { bx = 0.3f * da; by = -1f; bz = 0.3f * db; }
                    else if (pick < CrystalFloorShare + CrystalCeilingShare) { bx = 0.3f * da; by = 1f; bz = 0.3f * db; }
                    else { bx = da; by = 0f; bz = db; }
                    for (int m = 0; m < pl.ClusterCount; m++)
                    {
                        float ja = rng.Range(-1f, 1f), jb = rng.Range(-1f, 1f), jc = rng.Range(-1f, 1f), grow = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        float dx = bx, dy = by, dz = bz;
                        if (m > 0) { dx += CrystalJitter * ja; dy += CrystalJitter * jb; dz += CrystalJitter * jc; }
                        float dl = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (dl < 1e-3f) continue;
                        dx /= dl; dy /= dl; dz /= dl;
                        var rock = RockAt(cx, cz);
                        if (Walk.Solid(ground, rock, s, cx, cy, cz) >= 0) continue;
                        float lo = 0f, hi = -1f;
                        for (float r = CrystalStepM; r <= far; r += CrystalStepM)
                        {
                            var at = RockAt(cx + dx * r, cz + dz * r);
                            if (Walk.Solid(ground, at, s, cx + dx * r, cy + dy * r, cz + dz * r) >= 0) { hi = r; break; }
                            lo = r;
                        }
                        if (hi < 0f) continue;
                        for (int b = 0; b < 5; b++)
                        {
                            float mid = 0.5f * (lo + hi);
                            var at = RockAt(cx + dx * mid, cz + dz * mid);
                            if (Walk.Solid(ground, at, s, cx + dx * mid, cy + dy * mid, cz + dz * mid) >= 0) hi = mid; else lo = mid;
                        }
                        float hx = cx + dx * hi, hy = cy + dy * hi, hz = cz + dz * hi;
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * grow * grow, reach = foot * size;
                        var o = new ObjectInstance { Def = d, Variant = variant, Size = size };
                        o.X = hx + dx * pl.Sink * size;
                        o.Y = hy + dy * pl.Sink * size;
                        o.Z = hz + dz * pl.Sink * size;
                        if (o.X < x0 || o.Z < z0 || o.X >= x0 + sx || o.Z >= z0 + sz) continue;
                        if (!Owns(RockAt(o.X, o.Z), c, o.X, o.Y, o.Z)) continue;
                        if (-dy > 0.7f && Math.Abs((o.X - c.Ax) * px + (o.Z - c.Az) * pz) < CaveWalkLineM + reach) continue;
                        CrystalTurn(-dx, -dy, -dz, qa, qb, ref o);
                        if (CrystalFault(o, reach) != null) continue;
                        o.Id = ObjectIds.Make(chunk, d, ordinal++);
                        list.Add(o);
                    }
                }
            }
        }

        string CrystalFault(ObjectInstance o, float reach)
        {
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM;
            CrystalAxis(o, out float ax, out float ay, out float az);
            var rock = RockAt(o.X, o.Z);
            if (Walk.Solid(ground, rock, s, o.X, o.Y, o.Z) < 0) return "floats free of the rock";
            float lift = CrystalLift * o.Size + 0.3f, mx = o.X + ax * lift, my = o.Y + ay * lift, mz = o.Z + az * lift;
            var near = RockAt(mx, mz);
            if (Walk.Solid(ground, near, s, mx, my, mz) >= 0) return "is buried in rock";
            bool inside = false;
            for (int q = 0; q < near.N && !inside; q++)
            {
                var c = near.Items[q];
                inside = c.Kind == FeatureEdit.Carve && c.Shape == FeatureEdit.CrystalCell && c.Reach(mx, my, mz, out float dist) && c.Hole(dist) < 0f;
            }
            if (!inside) return "strays outside its crystal cell";
            if (ay < -0.3f)
            {
                float tx = o.X + ax * o.Size, ty = o.Y + ay * o.Size, tz = o.Z + az * o.Size;
                if (ty - Walk.Surface(ground, RockAt(tx, tz), s, tx, tz, ty, ty - 40f) < Walk.Height + Underground.HeadroomM) return "hangs into the walking headroom";
            }
            return Crowded(RockAt(o.X, o.Z), o.X, o.Z, reach, ay < 0.7f);
        }
    }
}
