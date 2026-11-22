using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class Landforms
    {
        public const float TorChance = 0.45f, HoodooChance = 0.55f;
        public const float TorMinR = 18.5f, TorMaxR = 22f, TorShrink = 0.1f, TorStack = 0.7f, TorSink = 0.25f, TorSideGap = 1.5f;
        public const float HoodooMinR = 14.5f, HoodooMaxR = 17f, HoodooMinRise = 3f, HoodooMaxRise = 4.5f, HoodooCap = 1.35f, HoodooSpread = 3.5f, HoodooGap = 2.4f;
        public const int TorBlocks = 3, HoodooCount = 5;
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
            int n = w.Config.Size, pad = Pad(w, (1 + TorSideGap) * TorMaxR), best = -1, t = Features.TileCells;
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
            int n = w.Config.Size, pad = Pad(w, HoodooSpread * HoodooMaxR + HoodooCap * HoodooMaxR), best = -1, t = Features.TileCells;
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
            float roll = r.NextFloat(), r0 = r.Range(TorMinR, TorMaxR), sideRoll = r.NextFloat(), sx = r.Range(-1f, 1f), sz = r.Range(-1f, 1f);
            var ax = new float[TorBlocks + 2];
            var az = new float[TorBlocks + 2];
            var ox = new float[TorBlocks + 2];
            var oz = new float[TorBlocks + 2];
            for (int k = 0; k < ax.Length; k++) { ax[k] = r.Range(-1f, 1f); az[k] = r.Range(-1f, 1f); ox[k] = r.Range(-0.2f, 0.2f); oz[k] = r.Range(-0.2f, 0.2f); }
            if (roll >= TorChance) return;
            int site = TorSite(w, strata, biomes, tx, ty);
            if (site < 0) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            Unit(ref sx, ref sz);
            int blocks = 2 + (sideRoll < 0.6f ? 1 : 0);
            Stack(detail, cm, site % n, site / n, r0, blocks, ax, az, ox, oz, 0, output);
            if (sideRoll < 0.5f) Stack(detail, cm, site % n + sx * TorSideGap * r0 / cm, site / n + sz * TorSideGap * r0 / cm, r0 * 0.85f, 2, ax, az, ox, oz, TorBlocks, output);
        }

        static void Stack(TerrainDetail detail, float cm, float cx, float cz, float r0, int blocks, float[] ax, float[] az, float[] ox, float[] oz, int at, List<FeatureEdit> output)
        {
            float foot = Foot(detail, cx, cz, r0, cm), y = foot + TorSink * r0, last = 0, px = cx * cm, pz = cz * cm;
            for (int k = 0; k < blocks; k++)
            {
                float rk = r0 * (1 - TorShrink * k);
                if (k > 0) { y += TorStack * (last + rk); px += ox[at + k] * rk; pz += oz[at + k] * rk; }
                float dx = ax[at + k], dz = az[at + k];
                Unit(ref dx, ref dz);
                float half = 0.9f * rk;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Tor, R = rk, Ax = px - dx * half, Ay = y, Az = pz - dz * half, Bx = px + dx * half, By = y, Bz = pz + dz * half });
                last = rk;
            }
        }

        public static void Hoodoo(GeneratedWorld w, TerrainDetail detail, StrataField strata, bool[] biomes, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat();
            int count = 3 + (int)(r.NextFloat() * (HoodooCount - 2));
            var pick = new float[HoodooCount * 6];
            for (int k = 0; k < pick.Length; k++) pick[k] = r.Range(-1f, 1f);
            if (roll >= HoodooChance) return;
            int site = HoodooSite(w, strata, biomes, tx, ty);
            if (site < 0) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, bx = site % n, bz = site / n;
            int start = output.Count;
            for (int q = 0; q < Math.Min(count, HoodooCount); q++)
            {
                float u = 0.5f + 0.5f * pick[q * 6 + 2], v = 0.5f + 0.5f * pick[q * 6 + 3];
                float rad = HoodooMinR + (HoodooMaxR - HoodooMinR) * u, cap = HoodooCap * rad;
                float rise = (HoodooMinRise + (HoodooMaxRise - HoodooMinRise) * v) * rad;
                float spread = q == 0 ? 0f : HoodooSpread * HoodooMaxR / cm;
                float cx = bx + pick[q * 6] * spread, cz = bz + pick[q * 6 + 1] * spread;
                bool clear = true;
                for (int p = start; p < output.Count && clear; p += 2)
                {
                    float gx = output[p].Ax - cx * cm, gz = output[p].Az - cz * cm, gap = HoodooGap * Math.Max(cap, output[p + 1].R);
                    clear = gx * gx + gz * gz >= gap * gap;
                }
                if (!clear) continue;
                float foot = Foot(detail, cx, cz, rad, cm), top = foot + rise;
                float dx = pick[q * 6 + 4], dz = pick[q * 6 + 5];
                Unit(ref dx, ref dz);
                float x = cx * cm, z = cz * cm, half = 0.5f * cap;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Hoodoo, R = rad, Ax = x, Ay = foot - 0.5f * rad, Az = z, Bx = x, By = top - 0.6f * cap, Bz = z });
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Hoodoo, R = cap, Ax = x - dx * half, Ay = top - 0.4f * cap, Az = z - dz * half, Bx = x + dx * half, By = top - 0.4f * cap, Bz = z + dz * half });
            }
        }
    }
}
