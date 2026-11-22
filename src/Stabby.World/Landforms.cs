using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class Landforms
    {
        public const float TorChance = 0.45f, HoodooChance = 0.55f;
        public const float TorMinR = 14f, TorMaxR = 18f, TorRound = 0.15f, TorWide = 0.62f, TorCore = 0.1f, TorDome = 0.06f, TorTaper = 0.4f, TorLump = 0.35f, TorShrink = 0.16f, TorShift = 0.26f, TorTurn = 0.3f, TorFuse = 0.3f, TorSink = 0.3f, TorJoint = 0.18f;
        public const float HoodooMinR = 6f, HoodooMaxR = 7.5f, HoodooMinRise = 8f, HoodooMaxRise = 14f, HoodooMinPinch = 0.2f, HoodooMaxPinch = 0.38f, HoodooCap = 1.15f, HoodooSpread = 5f, HoodooGap = 2.8f, HoodooCapR = 0.78f, HoodooMinBands = 2.2f, HoodooMaxBands = 3.4f, HoodooBand = 0.32f, HoodooLump = 0.12f, HoodooCapLump = 0.3f;
        public const float HoodooFoot = 1f / (1f - FeatureEdit.Flare);
        public const int TorLayers = 5, HoodooCount = 6;
        public const float MaxRiseM = 110f;
        public static readonly string[] TorBiomes = { "tundra", "boreal", "shrubland", "alpine" };
        public static readonly string[] HoodooBiomes = { "desert", "steppe" };
        public const float ButteChance = 0.5f, ButteMesa = 0.45f, ButteSink = 4f, ButteFlat = 0.35f, ButteSlack = 1.15f;
        public const float ButteMinRise = 30f, ButteMaxRise = 55f, ButteMinTop = 7f, ButteMaxTop = 12f, ButteMaxLen = 5f;
        public const float MesaMinRise = 24f, MesaMaxRise = 40f, MesaMinTop = 14f, MesaMaxTop = 22f, MesaMaxLen = 28f;
        public const float ButteCliff = 4f, ButteTalus = 0.75f, ButteMinApron = 0.3f, ButteMaxApron = 0.42f, ButteRound = 3f, ButteLump = 1.4f, ButteWobble = 0.1f;
        public const float TalusRound = 2f, TalusLump = 1.1f, TalusWobble = 0.08f, BlockSlope = 3f, BlockRound = 1f, BlockLump = 0.5f;
        public const int ButteBlocks = 4;
        public static readonly string[] ButteBiomes = { "desert", "cold-desert", "steppe", "shrubland" };
        public const float BasaltChance = 0.4f, BasaltMinW = 8f, BasaltMaxW = 15f, BasaltMaxLen = 12f, BasaltMinRise = 8f, BasaltMaxRise = 18f, BasaltSink = 3f;
        public const float BasaltMinP = 2.2f, BasaltMaxP = 3f, BasaltFall = 0.45f, BasaltRagged = 0.25f, BasaltLump = 0.16f, BasaltMinLog = 3f, BasaltMaxLog = 8f, BasaltBed = 0.3f;
        public const float BasaltHighM = 350f, BasaltCoastM = 160f, BasaltFlat = 0.6f;
        public const int BasaltLogs = 4;
        public static readonly string[] BasaltSkip = { "wetland", "mangrove" };

        public static bool Of(int shape) => shape == FeatureEdit.Tor || shape == FeatureEdit.Hoodoo || shape == FeatureEdit.Butte || shape == FeatureEdit.Basalt;

        public const int TileBudget = 12;

        public static string Name(int shape) => shape == FeatureEdit.Tor ? "tor" : shape == FeatureEdit.Hoodoo ? "hoodoo" : shape == FeatureEdit.Butte ? "butte" : "basalt";

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

        public static float Relief(GeneratedWorld w, int x, int y, int reach)
        {
            int n = w.Config.Size;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int py = Math.Max(0, y - reach); py <= Math.Min(n - 1, y + reach); py++)
                for (int px = Math.Max(0, x - reach); px <= Math.Min(n - 1, x + reach); px++)
                {
                    float h = w.Height[py * n + px];
                    lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
            return hi - lo;
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

        public static bool[] BasaltMask(GeneratedWorld w)
        {
            int n = w.Config.Size, reach = Math.Max(1, (int)Math.Ceiling(BasaltCoastM / w.Config.CellMeters));
            var skip = BiomeMask(w, BasaltSkip);
            var dist = new int[n * n];
            var queue = new Queue<int>();
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = w.Sea[i] ? 0 : int.MaxValue;
                if (w.Sea[i]) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % n, y = i / n;
                if (dist[i] >= reach) continue;
                for (int k = 0; k < 8; k++)
                {
                    int px = x + Grid.DX[k], py = y + Grid.DY[k];
                    if (px < 0 || py < 0 || px >= n || py >= n || dist[py * n + px] != int.MaxValue) continue;
                    dist[py * n + px] = dist[i] + 1;
                    queue.Enqueue(py * n + px);
                }
            }
            var mask = new bool[n * n];
            for (int i = 0; i < mask.Length; i++)
            {
                int b = w.Biome[i];
                mask[i] = b >= 0 && !skip[b] && Features.Dry(w, i) && (w.Height[i] >= BasaltHighM || dist[i] <= reach);
            }
            return mask;
        }

        public static int ButteSite(GeneratedWorld w, StrataField strata, bool[] biomes, int tx, int ty, float reachM, float rise)
        {
            int n = w.Config.Size, pad = Pad(w, reachM), best = -1, t = Features.TileCells;
            int reach = Math.Max(1, (int)Math.Ceiling(reachM / w.Config.CellMeters));
            float score = float.MinValue;
            for (int y = ty * t + pad; y < (ty + 1) * t - pad; y++)
                for (int x = tx * t + pad; x < (tx + 1) * t - pad; x++)
                {
                    int i = y * n + x, b = w.Biome[i];
                    if (b < 0 || !biomes[b] || !Features.Dry(w, i)) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    bool open = true;
                    for (int dy = -reach; dy <= reach && open; dy++)
                        for (int dx = -reach; dx <= reach && open; dx++)
                        {
                            int j = (y + dy) * n + x + dx, c = w.Biome[j];
                            open = c >= 0 && biomes[c] && Features.Dry(w, j);
                            lo = Math.Min(lo, w.Height[j]); hi = Math.Max(hi, w.Height[j]);
                        }
                    if (!open || hi - lo > ButteFlat * rise) continue;
                    float v = strata.Hardness(i, w.Height[i] + 0.85f * rise) - 0.5f * (hi - lo) / rise;
                    if (v > score) { score = v; best = i; }
                }
            return best;
        }

        public static int BasaltSite(GeneratedWorld w, StrataField strata, bool[] mask, int tx, int ty, float reachM, float rise)
        {
            int n = w.Config.Size, pad = Pad(w, reachM), best = -1, t = Features.TileCells;
            int reach = Math.Max(1, (int)Math.Ceiling(reachM / w.Config.CellMeters));
            float score = float.MinValue;
            for (int y = ty * t + pad; y < (ty + 1) * t - pad; y++)
                for (int x = tx * t + pad; x < (tx + 1) * t - pad; x++)
                {
                    int i = y * n + x;
                    if (!mask[i]) continue;
                    float v = strata.Hardness(i, w.Height[i] + 0.5f * rise);
                    if (v <= score || !DryAround(w, x, y, reach) || Relief(w, x, y, reach) > BasaltFlat * rise) continue;
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
            float roll = r.NextFloat(), r0 = r.Range(TorMinR, TorMaxR), layerRoll = r.NextFloat(), splitRoll = r.NextFloat(), dx = r.Range(-1f, 1f), dz = r.Range(-1f, 1f), seed = r.NextFloat();
            var shift = new float[TorLayers * 6];
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
                int o = k * 6;
                float size = r0 * (1f - TorShrink * k) * (1f + 0.08f * shift[o + 3]);
                float round = TorRound * size, len = size - round, wide = TorWide * size - round, core = TorCore * size, half = round + core;
                if (k == 0)
                {
                    float top = crest + 2f * half * (1f - TorSink), bottom = foot - round;
                    core = Math.Max(core, 0.5f * (top - bottom) - round);
                    half = round + core;
                    y = 0.5f * (top + bottom);
                }
                else y = y + last + half - TorFuse * round;
                if (k > 0) { px += Away(shift[o]) * TorShift * size; pz += Away(shift[o + 1]) * TorShift * size; }
                float ux = dx - dz * TorTurn * Away(shift[o + 2]), uz = dz + dx * TorTurn * Away(shift[o + 2]);
                Unit(ref ux, ref uz);
                var rock = new Rock { Round = round, Core = core, Dome = k == 0 ? 0f : Math.Min(TorDome * size, core), Taper = k == 0 ? 0f : TorTaper * shift[o + 4], Lump = TorLump * round, Seed = seed };
                bool split = k == 0 && splitRoll < 0.6f || k == 1 && splitRoll < 0.25f && layers < TorLayers;
                if (split)
                {
                    float joint = TorJoint * size * (1f + 0.6f * shift[o + 5]), piece = 0.5f * (len - joint), off = 0.5f * (len + joint) + 0.5f * round;
                    Slab(output, rock, wide, px - ux * off, y, pz - uz * off, ux, uz, piece);
                    rock.Taper = k == 0 ? 0f : -TorTaper * shift[o + 5];
                    Slab(output, rock, wide * (0.85f + 0.1f * shift[o + 3]), px + ux * off, y + 0.15f * core * shift[o], pz + uz * off, ux, uz, piece);
                }
                else Slab(output, rock, wide, px, y, pz, ux, uz, len);
                last = half;
            }
        }

        static float Away(float s) => s < 0f ? -0.4f + 0.6f * s : 0.4f + 0.6f * s;

        struct Rock
        {
            public float Round, Core, Dome, Taper, Lump, Seed;
        }

        static void Slab(List<FeatureEdit> output, Rock k, float wide, float x, float y, float z, float ux, float uz, float len)
            => output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Tor, R = k.Round, W = Math.Max(wide, 0.01f), H = k.Core, T = k.Taper, D = k.Dome, N = k.Lump, S = k.Seed, Ax = x - ux * len, Ay = y, Az = z - uz * len, Bx = x + ux * len, By = y, Bz = z + uz * len });

        public static void Hoodoo(GeneratedWorld w, TerrainDetail detail, StrataField strata, bool[] biomes, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat();
            int count = 3 + (int)(r.NextFloat() * (HoodooCount - 2));
            var pick = new float[HoodooCount * 12];
            for (int k = 0; k < pick.Length; k++) pick[k] = r.Range(-1f, 1f);
            if (roll >= HoodooChance) return;
            int site = HoodooSite(w, strata, biomes, tx, ty);
            if (site < 0) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, bx = site % n, bz = site / n;
            int start = output.Count;
            for (int q = 0; q < Math.Min(count, HoodooCount); q++)
            {
                int o = q * 12;
                float u = 0.5f + 0.5f * pick[o + 2], v = 0.5f + 0.5f * pick[o + 3], f = 0.5f + 0.5f * pick[o + 6];
                float rad = HoodooMinR + (HoodooMaxR - HoodooMinR) * u;
                float rise = Math.Min((HoodooMinRise + (HoodooMaxRise - HoodooMinRise) * v) * rad, MaxRiseM - rad);
                float spread = q == 0 ? 0f : HoodooSpread * HoodooMaxR / cm;
                float cx = bx + pick[o] * spread, cz = bz + pick[o + 1] * spread;
                bool clear = true;
                for (int p = start; p < output.Count && clear; p += 2)
                {
                    float gx = output[p].Ax - cx * cm, gz = output[p].Az - cz * cm, gap = HoodooGap * Math.Max(rad, output[p].R / HoodooFoot);
                    clear = gx * gx + gz * gz >= gap * gap;
                }
                if (!clear) continue;
                float pinch = HoodooMinPinch + (HoodooMaxPinch - HoodooMinPinch) * f, neck = 0.72f + 0.12f * pick[o + 7];
                float foot = Foot(detail, cx, cz, rad, cm), top = foot + rise;
                float capR = HoodooCapR * rad * (1f + 0.12f * pick[o + 10]), reach = HoodooCap * rad + 0.1f * rad, len = Math.Max(0f, reach - capR), mid = top - capR;
                float dx = pick[o + 4], dz = pick[o + 5];
                Unit(ref dx, ref dz);
                float x = cx * cm, z = cz * cm, shaft = rad * HoodooFoot, tilt = 0.25f * capR * pick[o + 11], lean = 0.3f * pick[o + 10], s = 0.5f + 0.5f * pick[o + 9];
                float bands = HoodooMinBands + (HoodooMaxBands - HoodooMinBands) * (0.5f + 0.5f * pick[o + 8]);
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Hoodoo, R = shaft, P = pinch * rad / shaft, T = neck, D = HoodooBand * rad / shaft, F = bands, N = HoodooLump * rad, S = s, Ax = x, Ay = foot - 0.5f * rad, Az = z, Bx = x, By = mid - capR, Bz = z });
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Hoodoo, R = capR, N = HoodooCapLump * capR, S = 1f - s, Ax = x - dx * len * (1f + lean), Ay = mid - tilt, Az = z - dz * len * (1f + lean), Bx = x + dx * len * (1f - lean), By = mid + tilt, Bz = z + dz * len * (1f - lean) });
            }
        }

        static float Mix(float a, float b, float t) => a + (b - a) * t;

        static float SegmentDistance(float x, float z, float ax, float az, float bx, float bz)
        {
            float ex = bx - ax, ez = bz - az, ll = ex * ex + ez * ez, qx = x - ax, qz = z - az;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (qx * ex + qz * ez) / ll)) : 0f;
            qx -= t * ex; qz -= t * ez;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        public static void Butte(GeneratedWorld w, TerrainDetail detail, StrataField strata, bool[] biomes, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), mesaRoll = r.NextFloat(), riseU = r.NextFloat(), topU = r.NextFloat(), lenU = r.NextFloat(), apronU = r.NextFloat(), slopeU = r.NextFloat(), dx = r.Range(-1f, 1f), dz = r.Range(-1f, 1f), seed = r.NextFloat();
            int blocks = 2 + (int)(r.NextFloat() * (ButteBlocks - 1));
            var pick = new float[ButteBlocks * 6];
            for (int k = 0; k < pick.Length; k++) pick[k] = r.Range(-1f, 1f);
            if (roll >= ButteChance) return;
            bool mesa = mesaRoll < ButteMesa;
            float rise = mesa ? Mix(MesaMinRise, MesaMaxRise, riseU) : Mix(ButteMinRise, ButteMaxRise, riseU);
            float top = mesa ? Mix(MesaMinTop, MesaMaxTop, topU) : Mix(ButteMinTop, ButteMaxTop, topU);
            float half = (mesa ? MesaMaxLen : ButteMaxLen) * lenU, pc = ButteCliff * (0.85f + 0.3f * slopeU), ha = Mix(ButteMinApron, ButteMaxApron, apronU) * rise;
            float plan = half + top + (rise + ButteSink) / pc + (ha + ButteSink) / ButteTalus + ButteRound;
            int site = ButteSite(w, strata, biomes, tx, ty, ButteSlack * plan, rise);
            if (site < 0) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, cx = site % n, cz = site / n;
            Unit(ref dx, ref dz);
            float crest = detail.HeightAt(cx, cz), foot = Math.Min(Foot(detail, cx, cz, plan, cm), Foot(detail, cx, cz, 0.5f * plan, cm));
            float bottom = foot - ButteSink, peak = Math.Max(crest, foot) + rise, hc = 0.5f * (peak - bottom), wc = top + 2f * hc / pc;
            float aTop = foot + ha, wa = wc - (aTop - bottom) / pc - 0.5f, hA = 0.5f * (aTop - bottom), wA = wa + 2f * hA / ButteTalus;
            float x = cx * cm, z = cz * cm, ax = x - dx * half, az = z - dz * half, bx = x + dx * half, bz = z + dz * half;
            output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Butte, R = ButteRound, W = wc, H = hc, P = pc, F = ButteWobble, N = ButteLump, S = seed, Ax = ax, Ay = bottom + hc, Az = az, Bx = bx, By = bottom + hc, Bz = bz });
            output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Butte, R = TalusRound, W = wA, H = hA, P = ButteTalus, F = TalusWobble, N = TalusLump, S = 1f - seed, Ax = ax, Ay = bottom + hA, Az = az, Bx = bx, By = bottom + hA, Bz = bz });
            for (int q = 0; q < Math.Min(blocks, ButteBlocks); q++)
            {
                int o = q * 6;
                float ux = pick[o], uz = pick[o + 1];
                Unit(ref ux, ref uz);
                float along = half * pick[o + 2], away = Mix(wa, wA, 0.35f + 0.15f * (pick[o + 3] + 1f));
                float px = x + dx * along + ux * away, pz = z + dz * along + uz * away, gap = SegmentDistance(px, pz, ax, az, bx, bz);
                float surface = Math.Max(bottom, Math.Min(aTop, bottom + (wA - gap) * ButteTalus));
                float tw = 2.6f + 0.9f * pick[o + 4], bh = 3.2f + 1.3f * pick[o + 5], hb = 0.5f * (surface + bh - bottom), wb = tw + 2f * hb / BlockSlope, lb = 0.6f + 0.6f * pick[o + 4];
                float vx = -uz, vz = ux;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Butte, R = BlockRound, W = wb, H = hb, P = BlockSlope, F = ButteWobble, N = BlockLump, S = 0.5f + 0.5f * pick[o + 5], Ax = px - vx * lb, Ay = bottom + hb, Az = pz - vz * lb, Bx = px + vx * lb, By = bottom + hb, Bz = pz + vz * lb });
            }
        }

        public static void Basalt(GeneratedWorld w, TerrainDetail detail, StrataField strata, bool[] mask, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), wU = r.NextFloat(), lenU = r.NextFloat(), riseU = r.NextFloat(), pU = r.NextFloat(), turn = r.Range(-1f, 1f), dx = r.Range(-1f, 1f), dz = r.Range(-1f, 1f), seed = r.NextFloat();
            int logs = 2 + (int)(r.NextFloat() * (BasaltLogs - 1));
            var pick = new float[BasaltLogs * 6];
            for (int k = 0; k < pick.Length; k++) pick[k] = r.Range(-1f, 1f);
            if (roll >= BasaltChance) return;
            float wide = Mix(BasaltMinW, BasaltMaxW, wU), half = 0.5f * BasaltMaxLen * lenU, rise = Mix(BasaltMinRise, BasaltMaxRise, riseU), p = Mix(BasaltMinP, BasaltMaxP, pU), round = 1.05f * p + 0.5f;
            float plan = wide + half + 2.6f * p + BasaltMaxLog + round;
            int site = BasaltSite(w, strata, mask, tx, ty, plan, rise);
            if (site < 0) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, cx = site % n, cz = site / n;
            Unit(ref dx, ref dz);
            float foot = Math.Min(Foot(detail, cx, cz, wide + half, cm), Foot(detail, cx, cz, 0.5f * wide, cm)), bottom = foot - BasaltSink, h = 0.5f * (foot + rise - bottom);
            float x = cx * cm, z = cz * cm;
            output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Basalt, R = round, W = wide, H = h, P = p, T = turn, D = BasaltFall, F = BasaltRagged, N = BasaltLump * p, S = seed, Ax = x - dx * half, Ay = bottom + h, Az = z - dz * half, Bx = x + dx * half, By = bottom + h, Bz = z + dz * half });
            for (int q = 0; q < Math.Min(logs, BasaltLogs); q++)
            {
                int o = q * 6;
                float ux = pick[o], uz = pick[o + 1];
                Unit(ref ux, ref uz);
                float len = Mix(BasaltMinLog, BasaltMaxLog, 0.5f + 0.5f * pick[o + 4]), lx = ux - 0.45f * pick[o + 5] * uz, lz = uz + 0.45f * pick[o + 5] * ux;
                Unit(ref lx, ref lz);
                float away = wide + (0.6f + 0.5f * (pick[o + 3] + 1f)) * p + 0.5f * len * Math.Abs(lx * ux + lz * uz), along = half * pick[o + 2];
                float mx = x + dx * along + ux * away, mz = z + dz * along + uz * away;
                float ax = mx - lx * 0.5f * len, az = mz - lz * 0.5f * len, bx = mx + lx * 0.5f * len, bz = mz + lz * 0.5f * len;
                float ay = detail.HeightAt(ax / cm, az / cm) + BasaltBed * p, by = detail.HeightAt(bx / cm, bz / cm) + BasaltBed * p;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Basalt, R = round, P = p, T = 0.5f * pick[o + 5], N = BasaltLump * p, S = 0.5f + 0.5f * pick[o + 3], Ax = ax, Ay = ay, Az = az, Bx = bx, By = by, Bz = bz });
            }
        }
    }
}
