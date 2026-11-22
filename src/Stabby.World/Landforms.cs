using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class Landforms
    {
        public const float TorChance = 0.45f, HoodooChance = 0.55f;
        public const float TorMinR = 14f, TorMaxR = 18f, TorRound = 0.09f, TorWide = 0.62f, TorCore = 0.17f, TorShrink = 0.16f, TorShift = 0.26f, TorTurn = 0.3f, TorFuse = 0.3f, TorSink = 0.3f, TorJoint = 0.18f;
        public const float HoodooMinR = 6f, HoodooMaxR = 7.5f, HoodooMinRise = 8f, HoodooMaxRise = 14f, HoodooMinPinch = 0.2f, HoodooMaxPinch = 0.38f, HoodooCap = 1.15f, HoodooSpread = 5f, HoodooGap = 2.8f;
        public const int TorLayers = 5, HoodooCount = 6;
        public const float MaxRiseM = 110f;
        public static readonly string[] TorBiomes = { "tundra", "boreal", "shrubland", "alpine" };
        public static readonly string[] HoodooBiomes = { "desert", "steppe" };

        public static bool Of(int shape) => shape == FeatureEdit.Tor || shape == FeatureEdit.Hoodoo;

        public const int TileBudget = 12;

        public static string Name(int shape) => shape == FeatureEdit.Tor ? "tor" : "hoodoo";

        public static bool Fits(List<FeatureEdit> tile, List<FeatureEdit> list, int from)
        {
            int used = tile.Count;
            for (int q = from; q < list.Count; q++) if (Of(list[q].Shape)) used++;
            if (used > TileBudget) return false;
            foreach (var e in tile) if (Features.Overlaps(e, list, from)) return false;
            return true;
        }

        public static bool[] BiomeMask(GeneratedWorld w, string[] ids)
        {
            var mask = new bool[w.BiomeIds == null ? 0 : w.BiomeIds.Length];
            for (int b = 0; b < mask.Length; b++) mask[b] = Array.IndexOf(ids, w.BiomeIds[b]) >= 0;
            return mask;
        }

        static int Pad(GeneratedWorld w, float reachM) => Features.MarginCells + (int)Math.Ceiling(reachM / w.Config.CellMeters);

        static bool DryAround(GeneratedWorld w, int x, int y, int reach)
        {
            int n = w.Config.Size;
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= n || py >= n || !Features.Dry(w, py * n + px)) return false;
                }
            return true;
        }

        public static int TorSite(GeneratedWorld w, StrataField strata, bool[] biomes, int tx, int ty)
        {
            int n = w.Config.Size, pad = Pad(w, (1.5f + TorJoint) * TorMaxR), best = -1, t = Features.TileCells;
            float score = float.MinValue;
            for (int y = ty * t + pad; y < (ty + 1) * t - pad; y++)
                for (int x = tx * t + pad; x < (tx + 1) * t - pad; x++)
                {
                    int i = y * n + x, b = w.Biome[i];
                    if (b < 0 || !biomes[b] || !Features.Dry(w, i)) continue;
                    bool crest = true;
                    for (int k = 0; k < 8 && crest; k++) crest = w.Height[i + Grid.DY[k] * n + Grid.DX[k]] <= w.Height[i];
                    if (!crest || !DryAround(w, x, y, 1)) continue;
                    float v = strata.Hardness(i, w.Height[i]);
                    if (v > score) { score = v; best = i; }
                }
            return best;
        }

        public static int HoodooSite(GeneratedWorld w, StrataField strata, bool[] biomes, int tx, int ty)
        {
            int n = w.Config.Size, pad = Pad(w, (HoodooSpread + 1.5f * HoodooCap) * HoodooMaxR), best = -1, t = Features.TileCells;
            float score = float.MinValue, rise = 0.5f * (HoodooMinRise + HoodooMaxRise) * HoodooMaxR;
            int reach = Math.Max(1, (int)Math.Ceiling(HoodooSpread * HoodooMaxR / w.Config.CellMeters));
            for (int y = ty * t + pad; y < (ty + 1) * t - pad; y++)
                for (int x = tx * t + pad; x < (tx + 1) * t - pad; x++)
                {
                    int i = y * n + x, b = w.Biome[i];
                    if (b < 0 || !biomes[b] || !Features.Dry(w, i)) continue;
                    float h = w.Height[i], v = strata.Hardness(i, h + rise) - strata.Hardness(i, h + 0.5f * rise);
                    if (v <= score || !DryAround(w, x, y, reach)) continue;
                    score = v; best = i;
                }
            return best;
        }

        static float Foot(TerrainDetail detail, float cx, float cz, float r, float cm)
        {
            float lo = detail.HeightAt(cx, cz), q = r / cm;
            for (int k = 0; k < 8; k++) lo = Math.Min(lo, detail.HeightAt(cx + Grid.DX[k] * q, cz + Grid.DY[k] * q));
            return lo;
        }

        static void Unit(ref float x, ref float z)
        {
            float l = (float)Math.Sqrt(x * x + z * z);
            if (l < 0.2f) { x = 1f; z = 0f; return; }
            x /= l; z /= l;
        }

        public static void Tor(GeneratedWorld w, TerrainDetail detail, StrataField strata, bool[] biomes, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), r0 = r.Range(TorMinR, TorMaxR), layerRoll = r.NextFloat(), splitRoll = r.NextFloat(), dx = r.Range(-1f, 1f), dz = r.Range(-1f, 1f);
            var shift = new float[TorLayers * 4];
            for (int k = 0; k < shift.Length; k++) shift[k] = r.Range(-1f, 1f);
            if (roll >= TorChance) return;
            int site = TorSite(w, strata, biomes, tx, ty);
            if (site < 0) return;
            int n = w.Config.Size, layers = 3 + (int)(layerRoll * (TorLayers - 2));
            float cm = w.Config.CellMeters, cx = site % n, cz = site / n;
            Unit(ref dx, ref dz);
            float foot = Foot(detail, cx, cz, r0, cm), crest = detail.HeightAt(cx, cz), px = cx * cm, pz = cz * cm, y = foot, last = 0f;
            for (int k = 0; k < layers; k++)
            {
                float size = r0 * (1f - TorShrink * k) * (1f + 0.08f * shift[k * 4 + 3]);
                float round = TorRound * size, len = size - round, wide = TorWide * size - round, core = TorCore * size, half = round + core;
                if (k == 0)
                {
                    float top = crest + 2f * half * (1f - TorSink), bottom = foot - round;
                    core = Math.Max(core, 0.5f * (top - bottom) - round);
                    half = round + core;
                    y = 0.5f * (top + bottom);
                }
                else y = y + last + half - TorFuse * round;
                if (k > 0) { px += shift[k * 4] * TorShift * size; pz += shift[k * 4 + 1] * TorShift * size; }
                float ux = dx - dz * TorTurn * shift[k * 4 + 2], uz = dz + dx * TorTurn * shift[k * 4 + 2];
                Unit(ref ux, ref uz);
                bool split = k == 0 && splitRoll < 0.6f || k == 1 && splitRoll < 0.25f && layers < TorLayers;
                if (split)
                {
                    float joint = TorJoint * size, piece = 0.5f * (len - joint), off = 0.5f * (len + joint) + 0.5f * round;
                    Slab(output, round, wide, core, px - ux * off, y, pz - uz * off, ux, uz, piece);
                    Slab(output, round, wide * (0.85f + 0.1f * shift[k * 4 + 3]), core, px + ux * off, y + 0.15f * core * shift[k * 4], pz + uz * off, ux, uz, piece);
                }
                else Slab(output, round, wide, core, px, y, pz, ux, uz, len);
                last = half;
            }
        }

        static void Slab(List<FeatureEdit> output, float round, float wide, float core, float x, float y, float z, float ux, float uz, float len)
            => output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Tor, R = round, W = Math.Max(wide, 0.01f), H = core, Ax = x - ux * len, Ay = y, Az = z - uz * len, Bx = x + ux * len, By = y, Bz = z + uz * len });

        public static void Hoodoo(GeneratedWorld w, TerrainDetail detail, StrataField strata, bool[] biomes, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat();
            int count = 3 + (int)(r.NextFloat() * (HoodooCount - 2));
            var pick = new float[HoodooCount * 8];
            for (int k = 0; k < pick.Length; k++) pick[k] = r.Range(-1f, 1f);
            if (roll >= HoodooChance) return;
            int site = HoodooSite(w, strata, biomes, tx, ty);
            if (site < 0) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, bx = site % n, bz = site / n;
            int start = output.Count;
            for (int q = 0; q < Math.Min(count, HoodooCount); q++)
            {
                int o = q * 8;
                float u = 0.5f + 0.5f * pick[o + 2], v = 0.5f + 0.5f * pick[o + 3], f = 0.5f + 0.5f * pick[o + 6];
                float rad = HoodooMinR + (HoodooMaxR - HoodooMinR) * u;
                float rise = Math.Min((HoodooMinRise + (HoodooMaxRise - HoodooMinRise) * v) * rad, MaxRiseM - rad);
                float spread = q == 0 ? 0f : HoodooSpread * HoodooMaxR / cm;
                float cx = bx + pick[o] * spread, cz = bz + pick[o + 1] * spread;
                bool clear = true;
                for (int p = start; p < output.Count && clear; p += 2)
                {
                    float gx = output[p].Ax - cx * cm, gz = output[p].Az - cz * cm, gap = HoodooGap * Math.Max(rad, output[p].R);
                    clear = gx * gx + gz * gz >= gap * gap;
                }
                if (!clear) continue;
                float pinch = HoodooMinPinch + (HoodooMaxPinch - HoodooMinPinch) * f, neck = 0.72f + 0.12f * pick[o + 7];
                float round = 0.35f * rad, core = 0.15f * rad, wide = HoodooCap * rad * (1f - pinch) + 0.15f * rad - round, len = wide + 0.1f * rad;
                float foot = Foot(detail, cx, cz, rad, cm), top = foot + rise, mid = top - round - core;
                float dx = pick[o + 4], dz = pick[o + 5];
                Unit(ref dx, ref dz);
                float x = cx * cm, z = cz * cm;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Hoodoo, R = rad, P = pinch, T = neck, Ax = x, Ay = foot - 0.5f * rad, Az = z, Bx = x, By = mid - 0.6f * rad, Bz = z });
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Hoodoo, R = round, W = wide, H = core, Ax = x - dx * len, Ay = mid, Az = z - dz * len, Bx = x + dx * len, By = mid, Bz = z + dz * len });
            }
        }
    }
}
