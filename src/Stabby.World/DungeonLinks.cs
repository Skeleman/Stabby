using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class DungeonLinks
    {
        public const string Class = "dungeon-link", Basis = "branching";
        public const float RadiusMin = 5.4f, RadiusMax = 6f, AheadM = 2.5f, SealM = 6.3f, WidthMin = 4f, WidthMax = 6.5f;
        public const int MostNodes = 4;

        public static bool IsLink(CaveGraph g) => g.Class == Class;

        public static CaveClass Shape(CaveLibrary lib)
        {
            var b = lib?.Find(Basis);
            if (b == null) return null;
            return new CaveClass
            {
                Id = Class, MinNodes = 1, MaxNodes = Math.Min(b.MaxNodes, MostNodes), Entrances = 1,
                RadiusMin = RadiusMin, RadiusMax = RadiusMax, EdgeMin = b.EdgeMin, EdgeMax = b.EdgeMax, Turn = b.Turn,
                ChamberChance = b.ChamberChance, ChamberMin = b.ChamberMin, ChamberMax = b.ChamberMax,
                Warp = b.Warp, Lumps = b.Lumps, RoomLumps = b.RoomLumps,
            };
        }

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail, ObjectField field, ObjectInstance[] discoveries, FeatureEdit[] edits, List<CaveGraph> caves)
        {
            var c = Shape(w.Caves);
            if (c == null || discoveries == null || detail == null) return edits;
            float cm = w.Config.CellMeters;
            var taken = new HashSet<int>();
            foreach (var e in edits) if (e.CaveCarve) taken.Add(Features.TileOf(e, cm));
            ulong seed = Rng.Stream(w.Config.Seed, "dungeon-links").NextU64();
            var list = new List<FeatureEdit>(edits);
            for (int k = 0; k < discoveries.Length; k++)
            {
                var o = discoveries[k];
                if (!field.IsDungeon(o) || !field.BreachOf(o, out var m)) continue;
                var r = new Rng(Rng.Mix(seed + (ulong)k));
                if (Build(w, detail, field, o, c, m, list, caves, taken, ref r)) taken.Add(caves[caves.Count - 1].Tile);
            }
            return list.Count == edits.Length ? edits : list.ToArray();
        }

        public static bool Fits(float width) => width >= WidthMin && width <= WidthMax;

        public static float Width(TerrainDetail detail, FeatureEdit[] items, CaveGraph g)
        {
            var ground = new DetailGround { Detail = detail };
            var list = new EditArray { Items = items };
            float sum = 0f;
            int count = 0;
            foreach (var edge in g.Edges)
            {
                var p = edge.Points;
                for (int q = 3; q + 2 < p.Length; q += 3)
                {
                    float ex = p[q] - p[q - 3], ez = p[q + 2] - p[q - 1], run = (float)Math.Sqrt(ex * ex + ez * ez);
                    if (run <= 0f) continue;
                    float x = p[q - 3] + 0.5f * ex, z = p[q - 1] + 0.5f * ez, y = 0.5f * (p[q - 2] + p[q + 1]) + 1f, sx = -ez / run, sz = ex / run;
                    if (Walk.Density(list, Walk.At(ground, list, x, z, Features.LatticeStepM), y) >= 0) continue;
                    sum += Reach(ground, list, x, z, y, sx, sz) + Reach(ground, list, x, z, y, -sx, -sz);
                    count++;
                }
            }
            return count == 0 ? 0f : sum / count;
        }

        static float Reach(DetailGround ground, EditArray list, float x, float z, float y, float sx, float sz)
        {
            float d = 0f;
            while (d < 2f * WidthMax && Walk.Density(list, Walk.At(ground, list, x + sx * (d + 0.1f), z + sz * (d + 0.1f), Features.LatticeStepM), y) < 0) d += 0.1f;
            return d;
        }

        static bool Build(GeneratedWorld w, TerrainDetail detail, ObjectField field, ObjectInstance o, CaveClass c, CaveNode m, List<FeatureEdit> list, List<CaveGraph> caves, HashSet<int> taken, ref Rng r)
        {
            float cm = w.Config.CellMeters;
            var width = new Rng(r.NextU64());
            var drip = new Rng(r.NextU64());
            var flow = new Rng(r.NextU64());
            float grain = r.NextFloat();
            var edits = new List<FeatureEdit>();
            if (!Underground.Breach(w, detail, c, m, m.Y + SealM, Mines.RoofM, grain, ref r, ref width, ref drip, ref flow, edits, out var g)) return false;
            int tile = Features.TileOf(edits[0], cm);
            if (tile < 0 || taken.Contains(tile)) return false;
            foreach (var e in edits) if (Features.TileOf(e, cm) != tile) return false;
            foreach (var e in edits) if (field.InTownGround(0.5f * (e.MinX + e.MaxX), 0.5f * (e.MinZ + e.MaxZ), 0.5f * (float)Math.Sqrt((e.MaxX - e.MinX) * (e.MaxX - e.MinX) + (e.MaxZ - e.MinZ) * (e.MaxZ - e.MinZ)), true)) return false;
            foreach (var e in edits) if (field.OnRoad(0.5f * (e.MinX + e.MaxX), 0.5f * (e.MinZ + e.MaxZ), 0.5f * (float)Math.Sqrt((e.MaxX - e.MinX) * (e.MaxX - e.MinX) + (e.MaxZ - e.MinZ) * (e.MaxZ - e.MinZ)))) return false;
            var own = new List<FeatureEdit>();
            field.DungeonEditsOf(o, own);
            float ox0 = float.MaxValue, ox1 = float.MinValue, oz0 = float.MaxValue, oz1 = float.MinValue;
            foreach (var v in own) { ox0 = Math.Min(ox0, v.MinX); ox1 = Math.Max(ox1, v.MaxX); oz0 = Math.Min(oz0, v.MinZ); oz1 = Math.Max(oz1, v.MaxZ); }
            foreach (var e in edits)
                foreach (var x in list)
                    if (!(x.Shape == FeatureEdit.Vault && x.MinX >= ox0 && x.MaxX <= ox1 && x.MinZ >= oz0 && x.MaxZ <= oz1) && e.MinX < x.MaxX && e.MaxX > x.MinX && e.MinY < x.MaxY && e.MaxY > x.MinY && e.MinZ < x.MaxZ && e.MaxZ > x.MinZ) return false;
            for (int i = 0; i < edits.Count; i++) { var e = edits[i]; e.Link = true; edits[i] = e; }
            var near = new List<FeatureEdit>();
            foreach (var e in list) if (Features.TileOf(e, cm) == tile) near.Add(e);
            var bare = near.FindAll(q => q.Shape != FeatureEdit.Speleothem);
            bare.AddRange(edits.FindAll(q => q.Shape != FeatureEdit.Speleothem));
            var rock = bare.ToArray();
            var ground = new DetailGround { Detail = detail };
            var solid = new EditArray { Items = rock };
            edits.RemoveAll(q => q.Shape == FeatureEdit.Speleothem && Underground.DripFault(ground, solid, Features.LatticeStepM, q, rock, 0, rock.Length, Underground.PassM) != null);
            near.AddRange(edits);
            var all = near.ToArray();
            foreach (var e in edits) if ((e.Shape == FeatureEdit.Cave && detail.HeightAt(e.Bx / cm, e.Bz / cm) < e.By + e.R) || Underground.PathFault(detail, all, e) != null) return false;
            foreach (var edge in g.Edges) if (Underground.Cramped(detail, all, g, edge) != null) return false;
            if (Underground.Unreached(detail, all, g) != null || !Fits(Width(detail, all, g))) return false;
            g.Tile = tile;
            list.AddRange(edits);
            caves.Add(g);
            return true;
        }
    }

    public sealed partial class ObjectField
    {
        public bool IsDungeon(ObjectInstance o) => IsDungeon(lib, o);

        public bool BreachOf(ObjectInstance o, out CaveNode m) => BreachOf(lib, o, out m);

        public static bool IsDungeon(ObjectLibrary lib, ObjectInstance o) => lib.Objects[o.Def].Placement.Discovery == Dungeon;

        public static bool BreachOf(ObjectLibrary lib, ObjectInstance o, out CaveNode m)
        {
            m = null;
            if (!Structures.DungeonBreach(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant), out float x, out float y, out float z, out float hx, out float hz)) return false;
            float u = o.Size / Structures.DungeonM;
            Rotate(o, x * u, y * u, z * u, out float px, out float py, out float pz);
            Rotate(o, hx, 0f, hz, out float dx, out _, out float dz);
            float l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l <= 0f) return false;
            dx /= l;
            dz /= l;
            m = new CaveNode { X = o.X + px + dx * DungeonLinks.AheadM, Y = o.Y + py, Z = o.Z + pz + dz * DungeonLinks.AheadM, Hx = dx, Hz = dz, Entrance = true };
            return true;
        }
    }

    public static partial class Structures
    {
        static void Breach(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "dungeon-breach-mesh");
            float h = 0.5f * PortalWidthM, c = 0.5f * KitCellM, e = c - KitFrameM, top = PortalHeightM + KitSlabM, cut = rng.Range(-0.3f, 0.3f);
            Flags(l, key, ref rng, f, -h, h, -c, c);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var g = f.Child(0, 0, 0, sg);
                float z0 = sg > 0 ? -e : -cut, z1 = sg > 0 ? cut : e;
                Masonry(l, key, g, h, c, -KitFloorM, top, z0, z1);
                Line(l, g, h + LineR, z0, h + LineR, z1, -KitFloorM, top);
            }
            Edge(l, key, f.Child(0, 0, 0, -1), c, top, KitFrameM);
            Dressed(l, key, f, -h, h, PortalHeightM, top, -e, cut - 0.4f);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float z = cut;
                for (int s = 0; z < c + VaultReach && s < 4; s++)
                {
                    float d = rng.Range(0.35f, 0.6f), y = top * (1f - 0.22f * (s + 1)) * rng.Range(0.8f, 1f);
                    Hewn(l, rng.NextU64(), f, sg * (h + 0.3f), 0.5f * y - KitFloorM, z + 0.5f * d, 0.6f, y, d, 1, 0, 0, 1, rng.Range(0.88f, 0.95f), rng.Range(-0.08f, 0.08f));
                    z += d;
                }
            }
            int n = rng.Range(10, 15);
            for (int i = 0; i < n; i++)
            {
                int d = rng.Range(0, 16);
                float side = i % 2 == 0 ? -1 : 1, sz = rng.Range(0.18f, 0.45f), x = side * (h - rng.Range(0.15f, 0.55f)), z = rng.Range(-e, c + VaultReach), y = -0.06f + rng.Range(0f, 0.25f) * (z + c) / (2f * c);
                if (rng.NextFloat() < 0.35f) Hewn(l, rng.NextU64(), f, x, y - 0.05f, z, sz * 1.5f, sz * 0.7f, sz * 1.1f, Cos16(d), Sin16(d), 0, 1, rng.Range(0.9f, 0.95f), rng.Range(-0.15f, 0.15f));
                else Rock(l, rng.NextU64(), f, x, y, z, sz * 1.25f, sz * 0.75f, sz, Cos16(d), Sin16(d), 0, 1, rng.Range(-0.1f, 0.1f));
            }
            for (int sg = -1; sg <= 1; sg += 2)
                Rock(l, rng.NextU64(), f, sg * (h - 0.25f), -0.1f, c - 0.2f, 0.9f, 0.55f, 1.1f, 1, 0, 0, 1);
        }
    }
}
