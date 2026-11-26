using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float TempleU = 1 / 20f, TempleTop = 0.15f, TempleStepZ = 3f, TempleStepRun = 0.8f, TempleStepRise = 0.32f, TempleWaterline = 1.3f;
        public const int TempleSteps = 8;
        const float TempleFoot = -2.7f, TempleDrum = 0.9f;
        public const float TempleHalfX = 4.8f * TempleU, TempleFront = TempleStepZ * TempleU, TempleBack = -5.2f * TempleU;

        static readonly float[] TempleSeats = { 0, 0, 0.19f, -0.21f, -0.19f, -0.21f, TempleHalfX, TempleFront, -TempleHalfX, TempleFront, 0, -0.21f, 0, TempleFront, TempleHalfX, -0.05f, -TempleHalfX, -0.05f };

        static void SunkenTemple(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "sunken-temple");
            float hx = rng.Range(4.2f, 4.8f), back = -rng.Range(4.6f, 5.2f), front = TempleStepZ;
            int start = l.Pieces.Count;

            int nx = 6, nz = 5;
            float tx = 2 * hx / nx, tz = (front - back) / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x = -hx + (i + 0.5f) * tx, z = back + (j + 0.5f) * tz, sink = rng.NextFloat() < 0.2f ? rng.Range(0.04f, 0.12f) : rng.Range(0f, 0.03f);
                    Yaw(ref rng, 0.03f, out float c, out float s);
                    Hewn(l, rng.NextU64(), f, x, TempleFoot * 0.2f, z, tx - 0.04f, TempleTop - TempleFoot * 0.2f - sink, tz - 0.04f, c, s, 0, 1, rng.Range(0.93f, 0.97f), rng.Range(-0.02f, 0.02f));
                }
            Hewn(l, rng.NextU64(), f, 0, TempleFoot * 0.2f, 0.5f * (front + back), 2 * hx, TempleTop - TempleFoot * 0.2f, front - back, 1, 0, 2, 2, 0.95f);
            Flat(l, start);
            for (int k = 0; k < 4; k++) Touch(l, f, (k & 1) == 0 ? -hx : hx, 0, k < 2 ? back : front, 1.2f);
            Touch(l, f, 0, 0, back, 1.5f);
            Touch(l, f, -hx, 0, 0.5f * (front + back), 1.5f);
            Touch(l, f, hx, 0, 0.5f * (front + back), 1.5f);
            for (float x = -hx + 0.8f; x < hx; x += 1.6f)
                for (float z = back + 0.8f; z < front; z += 1.6f) Col(l, f, x, z, 0.85f, 0, TempleTop);

            int steps = l.Pieces.Count;
            float sw = rng.Range(2.4f, 3f);
            for (int k = 0; k < TempleSteps; k++)
            {
                float top = TempleTop - TempleStepRise * (k + 1), z = front + (k + 0.5f) * TempleStepRun, wear = k / (float)TempleSteps;
                int blocks = 3;
                float bw = 2 * sw / blocks;
                for (int b = 0; b < blocks; b++)
                {
                    if (k > 3 && rng.NextFloat() < 0.15f * wear) continue;
                    float drop = rng.NextFloat() < 0.3f * wear ? rng.Range(0.06f, 0.2f) : rng.Range(0f, 0.03f);
                    Yaw(ref rng, 0.02f + 0.06f * wear, out float c, out float s);
                    Hewn(l, rng.NextU64(), f, -sw + (b + 0.5f) * bw, TempleFoot, z, bw - 0.05f, top - drop - TempleFoot, TempleStepRun + 0.06f, c, s, 0, 1, rng.Range(0.9f, 0.96f), rng.Range(-0.03f, 0.03f) * wear);
                }
                Hewn(l, rng.NextU64(), f, 0, TempleFoot, z, 2 * sw, top - TempleFoot, TempleStepRun, 1, 0, 2, 2, 0.95f);
                Col(l, f, -sw * 0.5f, z, 0.6f, TempleFoot, top);
                Col(l, f, sw * 0.5f, z, 0.6f, TempleFoot, top);
                if ((k & 1) == 0) { Touch(l, f, -sw, top, z, 0.8f); Touch(l, f, sw, top, z, 0.8f); }
            }
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float len = TempleSteps * TempleStepRun * rng.Range(0.55f, 0.8f);
                Hewn(l, rng.NextU64(), f, sd * (sw + 0.35f), TempleFoot, front + 0.5f * len, 0.7f, TempleTop + 0.15f - TempleFoot - len * TempleStepRise / TempleStepRun * 0.5f, len, 1, 0, 0, 2, 0.93f, 0, 0, 0.04f);
                Touch(l, f, sd * (sw + 0.35f), 0, front + 0.5f * len, 0.9f);
            }
            Flat(l, steps);
            Drape(l, steps);

            float rowF = front - 0.7f, rowB = back + 0.7f, span = 2 * (hx - 0.9f) / 3;
            int broke = rng.Range(0, 4), lean = (broke + rng.Range(1, 4)) % 4, gap = rng.Range(0, 3);
            for (int i = 0; i < 4; i++)
            {
                float x = -hx + 0.9f + i * span;
                int drums = i == broke ? rng.Range(1, 3) : i == lean ? 4 : rng.NextFloat() < 0.4f ? rng.Range(2, 4) : 4;
                float tilt = i == lean ? rng.Range(0.07f, 0.12f) * (rng.NextFloat() < 0.5f ? -1 : 1) : rng.Range(-0.025f, 0.025f);
                TempleColumn(l, ref rng, f, x, TempleTop, rowF, drums, tilt, rng.Range(0, 16), drums == 4 && i != lean);
                if (drums < 4) TempleFallen(l, ref rng, f, x + rng.Range(-1.2f, 1.2f), rowF + rng.Range(1.6f, 3.4f), 4 - drums);
            }
            for (int i = 0; i < 4; i++)
                TempleColumn(l, ref rng, f, -hx + 0.9f + i * span, TempleTop, rowB, 4, rng.Range(-0.012f, 0.012f), rng.Range(0, 16), true);
            float top4 = TempleTop + 0.3f + 4 * TempleDrum + 0.3f;
            for (int i = 0; i < 3; i++)
            {
                float x = -hx + 0.9f + (i + 0.5f) * span;
                if (i == gap)
                {
                    Yaw(ref rng, 0.5f, out float fc, out float fs);
                    Hewn(l, rng.NextU64(), f, x + rng.Range(-0.6f, 0.6f), TempleTop - 0.05f, rowB + rng.Range(1.4f, 2.2f), span + 0.7f, 0.55f, 0.75f, fc, fs, 0, 1, rng.Range(0.9f, 0.95f), rng.Range(-0.06f, 0.06f));
                    continue;
                }
                Hewn(l, rng.NextU64(), f, x, top4, rowB, span + 0.85f, 0.55f, 0.8f, 1, 0, 0, 2, rng.Range(0.92f, 0.96f), rng.Range(-0.01f, 0.01f));
            }

            int wall = l.Pieces.Count;
            float wz = back - 0.35f;
            for (float x = -hx; x < hx - 0.1f;)
            {
                float bl = Math.Min(hx - x, rng.Range(1.1f, 1.6f)), h = top4 - TempleTop + 0.55f - rng.Range(0f, 1.6f) * (Math.Abs(x + 0.5f * bl) / hx);
                Hewn(l, rng.NextU64(), f, x + 0.5f * bl, TempleTop - 0.1f, wz, bl - 0.03f, h, 0.7f, 1, 0, 0, 2, rng.Range(0.88f, 0.94f), 0, 0, 0.02f);
                x += bl;
            }
            for (float x = -hx + 0.4f; x < hx; x += 0.8f) Col(l, f, x, wz, 0.4f, TempleTop, top4);

            float roofTop = top4 + 0.55f;
            int slabs = rng.Range(2, 4), from = gap == 0 ? 1 : 0;
            float roofW = (2 * hx) / 5;
            for (int k = 0; k < slabs; k++)
            {
                float x = -hx + (from + k + 0.5f) * roofW * (gap == 0 ? 1.2f : 1f);
                if (x > hx - 0.5f * roofW) break;
                float rz = 0.5f * (rowB + wz), rl = rowB - wz + 1.2f;
                Hewn(l, rng.NextU64(), f, x, roofTop - 0.05f, rz, roofW - 0.06f, 0.35f, rl, 1, 0, 0, 2, rng.Range(0.9f, 0.95f), rng.Range(-0.03f, 0.03f));
            }

            float drown = front + TempleSteps * TempleStepRun + rng.Range(0.3f, 0.7f), deep = TempleTop - TempleStepRise * TempleSteps - 0.45f;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                int drums = sd < 0 ? 3 : rng.Range(1, 3);
                float tilt = sd < 0 ? rng.Range(0.1f, 0.16f) * (rng.NextFloat() < 0.5f ? -1 : 1) : rng.Range(-0.04f, 0.04f);
                TempleColumn(l, ref rng, f, sd * (sw + rng.Range(0.3f, 1f)), deep, drown + rng.Range(-0.5f, 0.3f), drums, tilt, rng.Range(0, 16), false);
            }
            Rubble(l, ref rng, f, rng.Range(-hx, hx), drown + rng.Range(-0.4f, 0.4f), 0.3f);

            for (int k = start; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                if (p.Y < TempleWaterline * f.U) { p.Mossy = true; l.Pieces[k] = p; }
            }

            int tufts = rng.Range(5, 9);
            for (int k = 0; k < tufts; k++) Tuft(l, rng.NextU64(), ref rng, f, rng.Range(-hx, hx), TempleTop, rng.Range(back, front));
        }

        static void TempleColumn(StructureLayout l, ref Rng rng, in Frame f, float x, float y, float z, int drums, float tilt, int dir, bool capital)
        {
            var cf = f.Child(x, y, z, Cos16(dir), Sin16(dir));
            float r = rng.Range(0.68f, 0.76f), c2 = Cos16(2) , s2 = Sin16(2), yb = 0.3f;
            Hewn(l, rng.NextU64(), cf, 0, 0, 0, r + 0.32f, 0.3f, r + 0.32f, 1, 0, 0, 2, 0.93f);
            for (int d = 0; d < drums; d++)
            {
                bool last = d == drums - 1, broken = last && !capital && drums < 4;
                float h = broken ? TempleDrum * rng.Range(0.45f, 0.8f) : TempleDrum - 0.02f, q = broken ? rng.Range(0.86f, 0.89f) : rng.Range(0.92f, 0.95f), w = r * (1 - 0.03f * d);
                Hewn(l, rng.NextU64(), cf, tilt * yb, yb, 0, w, h, w, 1, 0, 0, 2, q, tilt);
                Hewn(l, rng.NextU64(), cf, tilt * yb, yb, 0, w, h, w, c2, s2, 0, 1, q, tilt * c2, -tilt * s2);
                yb += TempleDrum;
            }
            if (capital) Hewn(l, rng.NextU64(), cf, tilt * yb, yb - 0.02f, 0, r + 0.3f, 0.3f, r + 0.3f, 1, 0, 0, 2, 0.94f, tilt);
            Col(l, cf, tilt * 0.5f * yb, 0, 0.45f * r, 0, yb);
            if (y <= 0) Touch(l, cf, 0, 0, 0, 0.7f);
        }

        static void TempleFallen(StructureLayout l, ref Rng rng, in Frame f, float x, float z, int drums)
        {
            Yaw(ref rng, 0.9f, out float c, out float s);
            float r = rng.Range(0.62f, 0.7f);
            for (int d = 0; d < drums; d++)
            {
                float along = (d - 0.5f * (drums - 1)) * (TempleDrum + rng.Range(0.05f, 0.4f));
                Hewn(l, rng.NextU64(), f, x + c * along, rng.Range(-0.12f, -0.04f), z + s * along, TempleDrum - 0.04f, r, r, c, s, 0, 1, rng.Range(0.86f, 0.92f), rng.Range(-0.08f, 0.08f));
                Touch(l, f, x + c * along, 0, z + s * along, 0.6f);
            }
            Col(l, f, x, z, 0.5f * r, 0, r);
        }
    }

    public sealed partial class ObjectField
    {
        public const float TempleShoreMinM = 5f, TempleShoreM = 11f, TempleShoreAimM = 8f, TempleDropMinM = 0.6f, TempleDropM = 1.9f, TempleDropAimM = 1.2f, TempleDryM = 6f, TempleSeekStepM = 3f, TempleReachM = 14f, TempleCornerGapM = 0.15f;
        public const int TempleSeekRings = 4;

        public float TempleFit(float x, float z, out float vx, out float vz)
        {
            vx = 0; vz = 1;
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), best = float.MinValue;
            for (int k = 0; k < 16; k++)
            {
                float ux = Structures.Cos16(k), uz = Structures.Sin16(k);
                float shore = TempleShore(x, z, ux, uz, TempleShoreM, out float level);
                if (shore < TempleShoreMinM) continue;
                float drop = y - level;
                if (drop < TempleDropMinM || drop > TempleDropM || !TempleDry(x, z, -ux, -uz, level) || !TempleDry(x, z, -uz, ux, level) || !TempleDry(x, z, uz, -ux, level)) continue;
                float fit = -0.1f * Math.Abs(shore - TempleShoreAimM) - Math.Abs(drop - TempleDropAimM);
                if (fit > best) { best = fit; vx = ux; vz = uz; }
            }
            return best;
        }

        float TempleShore(float x, float z, float ux, float uz, float reach, out float level)
        {
            level = 0;
            for (float r = 1; r <= reach; r += 1)
            {
                float px = x + ux * r, pz = z + uz * r;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
                int j = Cell(pz) * n + Cell(px);
                if (w.Sea[j]) return float.MinValue;
                int lake = w.Water.LakeId[j];
                if (lake < 0) continue;
                level = w.Water.Lakes[lake].Level;
                if (GroundAt(detail, cm, px, pz) < level) return r;
            }
            return float.MinValue;
        }

        bool TempleDry(float x, float z, float ux, float uz, float level)
        {
            float px = x + ux * TempleDryM, pz = z + uz * TempleDryM;
            return px >= 0 && pz >= 0 && px < worldM && pz < worldM && GroundAt(detail, cm, px, pz) > level + ObjectField.ShoreClearM;
        }

        bool SeekTemple(int d, int variant, float size, ref float x, ref float z, float x0, float z0, float x1, float z1, out float qa, out float qb)
        {
            qa = 1; qb = 0;
            float bd = float.MaxValue, sx = x, sz = z;
            for (float pz = z0; pz <= z1; pz += cm)
                for (float px = x0; px <= x1; px += cm)
                {
                    int i = Cell(pz) * n + Cell(px);
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || !NearLake(Cell(px), Cell(pz))) continue;
                    float dd = (px - x) * (px - x) + (pz - z) * (pz - z);
                    if (dd < bd) { bd = dd; sx = px; sz = pz; }
                }
            if (bd == float.MaxValue) return false;
            float best = float.MinValue, bx = sx, bz = sz, vx = 0, vz = 1;
            for (int gz = -TempleSeekRings; gz <= TempleSeekRings; gz++)
                for (int gx = -TempleSeekRings; gx <= TempleSeekRings; gx++)
                {
                    float cx = Math.Min(x1, Math.Max(x0, sx + gx * TempleSeekStepM)), cz = Math.Min(z1, Math.Max(z0, sz + gz * TempleSeekStepM));
                    float fit = TempleFit(cx, cz, out float wx, out float wz);
                    if (fit <= best) continue;
                    Heading(wx, wz, out float ha, out float hb);
                    if (SiteFits(d, variant, cx, cz, size, ha, hb, out _)) { best = fit; bx = cx; bz = cz; vx = wx; vz = wz; }
                }
            x = bx; z = bz;
            Heading(vx, vz, out qa, out qb);
            return best > float.MinValue;
        }

        bool NearLake(int gx, int gz)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax >= 0 && az >= 0 && ax < n && az < n && w.Water.LakeId[az * n + ax] >= 0) return true;
                }
            return false;
        }

        public string TempleFault(ObjectInstance o)
        {
            Rotate(o, 0, 0, 1, out float bx, out _, out float bz);
            float bl = (float)Math.Sqrt(bx * bx + bz * bz);
            float shore = TempleShore(o.X, o.Z, bx / bl, bz / bl, TempleReachM, out float level);
            if (shore == float.MinValue) return "has no water beside it";
            float foot = o.Y + (Structures.TempleTop - Structures.TempleStepRise * Structures.TempleSteps) * Structures.TempleU * o.Size;
            if (level <= foot) return "has steps that never reach the water";
            float floor = o.Y + Structures.TempleTop * Structures.TempleU * o.Size;
            if (level >= floor) return "is drowned above its floor";
            for (int k = 0; k < 9; k++)
            {
                if (k == 4) continue;
                Rotate(o, (k % 3 - 1) * Structures.TempleHalfX * o.Size, 0, (k < 3 ? Structures.TempleFront : k < 6 ? 0.5f * (Structures.TempleFront + Structures.TempleBack) : Structures.TempleBack) * o.Size, out float cx, out _, out float cz);
                if (floor - GroundAt(detail, cm, o.X + cx, o.Z + cz) > TempleCornerGapM) return "has a platform edge above the ground";
            }
            return null;
        }
    }
}
