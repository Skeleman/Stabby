using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const int OasisPalmsMin = 3;

        static void CheckOases(GeneratedWorld w, ObjectField field, List<string> e)
        {
            string fault = OasisFault(w, field);
            if (fault != null) e.Add(fault);
        }

        public static string OasisFault(GeneratedWorld w, ObjectField field)
        {
            var b = field.OasisPools();
            if (b.Length == 0) return null;
            foreach (var x in field.OasisEdits())
                if (!x.MeshedIn(NearStepM, x.MinX, x.MinZ, x.MaxX, x.MaxZ)) return $"oasis pool at {x.Ax:F0},{x.Az:F0} is too thin to mesh near the player";
            int n = w.Config.Size, hot = Array.IndexOf(w.BiomeIds, "desert"), cold = Array.IndexOf(w.BiomeIds, "cold-desert"), steppe = Array.IndexOf(w.BiomeIds, "steppe");
            float cm = w.Config.CellMeters;
            var pools = new List<LiquidPool>();
            foreach (var p in field.LiquidPools()) if (p.Kind == ObjectField.OasisPool) pools.Add(p);
            if (pools.Count != b.Length / ObjectField.OasisStride) return $"{pools.Count} oasis pools hold water but {b.Length / ObjectField.OasisStride} oases were placed";
            int across = field.ChunksAcross;
            float span = field.ChunkMeters;
            for (int k = 0; k < b.Length; k += ObjectField.OasisStride)
            {
                float x = b[k], z = b[k + 1], r = b[k + 2], level = b[k + 3];
                string at = $"oasis at {x:F0},{z:F0}";
                int c = Math.Min(n - 1, (int)(z / cm + 0.5f)) * n + Math.Min(n - 1, (int)(x / cm + 0.5f)), biome = w.Biome[c];
                if (biome < 0 || (biome != hot && biome != cold && biome != steppe)) return at + $" lies in {(biome < 0 ? "no biome" : w.BiomeIds[biome])}, not desert or steppe";
                if (!(b[k + 4] < level && level < b[k + 5])) return at + " has its floor, surface and bank out of order";
                LiquidPool pool = null;
                foreach (var p in pools) if (Math.Abs(p.X - x) < 0.01f && Math.Abs(p.Z - z) < 0.01f) pool = p;
                if (pool == null || pool.Leaks || pool.WetCount == 0 || !(pool.Level - pool.Floor >= ObjectField.OasisMinDepthM * 0.5f)) return at + " holds no water";
                float reach = ObjectField.OasisMargin(ObjectField.OasisReach(r, b[k + 3], b[k + 5] + ObjectField.OasisHeadM)) + 4f;
                int palms = 0;
                int x0 = Math.Max(0, (int)((x - reach) / span)), x1 = Math.Min(across - 1, (int)((x + reach) / span));
                int z0 = Math.Max(0, (int)((z - reach) / span)), z1 = Math.Min(across - 1, (int)((z + reach) / span));
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            float dx = o.X - x, dz = o.Z - z, d2 = dx * dx + dz * dz;
                            if (d2 >= reach * reach) continue;
                            var pl = w.Objects.Objects[o.Def].Placement;
                            string id = w.Objects.Objects[o.Def].Id;
                            if (pl.Oasis == null)
                            {
                                if (!ObjectField.IsCave(w.Objects.Objects[o.Def]) && d2 < (reach - 4f) * (reach - 4f)) return $"{id} at {o.X:F0},{o.Z:F0} stands inside the {at}";
                                continue;
                            }
                            if (ObjectField.OasisKind(pl.Oasis) != ObjectField.OasisPalm) continue;
                            palms++;
                            float foot = field.OasisFoot(o.X, o.Z);
                            if (foot < level + ObjectField.OasisPalmRiseM - 0.01f) return $"{id} at {o.X:F0},{o.Z:F0} stands in the water of the {at} (ground {foot:F2} m, water {level:F2} m)";
                            if (d2 > (r + ObjectField.OasisRingM) * (r + ObjectField.OasisRingM)) return $"{id} at {o.X:F0},{o.Z:F0} stands more than {ObjectField.OasisRingM:F0} m from the shore of the {at}";
                        }
                if (palms < OasisPalmsMin) return at + $" has {palms} date palms, fewer than {OasisPalmsMin}";
            }
            return null;
        }
    }
}
