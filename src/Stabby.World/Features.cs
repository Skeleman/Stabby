using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct FeatureEdit
    {
        public const int Add = 0, Carve = 1;
        public const int Overhang = 0, Arch = 1, Cave = 2, CaveRoom = 3, Boulder = 4, Tor = 5, Hoodoo = 6, Spring = 7, Lip = 8, Bridge = 9, Speleothem = 10, CrystalCell = 11, Sink = 12, Shaft = 14, Vault = 15, Mine = 16, Shapes = 17;

        public static bool IsCaveCarve(int shape) => shape == Cave || shape == CaveRoom || shape == CrystalCell || shape == Mine;

        public static bool IsRoom(int shape) => shape == CaveRoom || shape == CrystalCell;

        public bool CaveCarve => Kind == Carve && IsCaveCarve(Shape);

        public const float Blend = 0.5f;

        public const float MinMeshSteps = 0.75f, FadeSteps = 0.25f;

        public bool MeshedIn(float stepM, float x0, float z0, float x1, float z1)
        {
            var e = AtStep(stepM);
            return e.Thick >= MinMeshSteps * stepM && e.MaxX > x0 - stepM && e.MinX < x1 + stepM && e.MaxZ > z0 - stepM && e.MinZ < z1 + stepM;
        }

        public FeatureEdit AtStep(float stepM)
        {
            if (CaveCarve)
            {
                var c = this;
                c.Coarse = Math.Max(0f, Math.Min(1f, (stepM - RockFineM) / RockFineM));
                return c;
            }
            if (Shape != Boulder || R >= MinMeshSteps * stepM || R < FadeSteps * stepM) return this;
            var e = this;
            float grow = MinMeshSteps * stepM - R;
            e.R += grow;
            e.Ay -= grow;
            e.By -= grow;
            e.N = 0f;
            return e;
        }

        public int Kind, Shape;
        public float Ax, Ay, Az, Bx, By, Bz, R;
        public float W, H, P, T;
        public float D, F, N, S;
        public float Coarse;

        public const float Flare = 0.3f, FlareSpan = 0.16f, Wave = 1.2f, LobeM = 2.5f, LumpM = 1.8f, Riser = 0.55f, Grain = 4.7f, GrainShare = 0.2f;

        public float Thick => R + Math.Min(W, H);

        public bool Fine, Ice, Obsidian, Link;

        public FeatureEdit Finer()
        {
            var e = this;
            e.Fine = true;
            return e;
        }

        float Spread => Shape == Spring && T < 0f ? W - 2f * H * T : W;

        public float MinX => Math.Min(Ax, Bx) - R - Spread;
        public float MaxX => Math.Max(Ax, Bx) + R + Spread;
        public float MinY => Math.Min(Ay, By) - R - H;
        public float MaxY => Math.Max(Ay, By) + R + H;
        public float MinZ => Math.Min(Az, Bz) - R - Spread;
        public float MaxZ => Math.Max(Az, Bz) + R + Spread;

        public bool Contains(float x, float y, float z) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;

        public float Apply(float d, float x, float y, float z)
        {
            if (!Reach(x, y, z, out float dist)) return d;
            return Kind == Add ? Math.Max(d, R - dist) : Cut(d, Hole(dist), R * Blend);
        }

        public bool Reach(float x, float y, float z, out float dist) => Reach(x, y, z, out dist, out _);

        public bool Reach(float x, float y, float z, out float dist, out float floor)
        {
            dist = R;
            floor = float.MaxValue;
            if (!Contains(x, y, z)) return false;
            if (Shape == Spring) return Basin(x, y, z, out dist);
            if (W > 0f) return Slab(x, y, z, out dist);
            float ex = Bx - Ax, ey = By - Ay, ez = Bz - Az, px = x - Ax, py = y - Ay, pz = z - Az;
            float ll = ex * ex + ey * ey + ez * ez;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
            float qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
            if (CaveCarve)
            {
                if (qx * qx + qy * qy + qz * qz >= R * R) return false;
                dist = Rock(x, y, z, qx, qy, qz, out floor);
                return dist < R;
            }
            dist = Shell(x, y, z, qx, qy, qz, t);
            return dist < R;
        }

        float Shell(float x, float y, float z, float qx, float qy, float qz, float t)
        {
            float dist = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
            if (P > 0f) dist += R * Profile(t);
            if (N > 0f) dist += Weathering(x, y, z);
            return dist;
        }

        public float BoulderDistance(float x, float y, float z)
        {
            float ex = Bx - Ax, ey = By - Ay, ez = Bz - Az, px = x - Ax, py = y - Ay, pz = z - Az;
            float ll = ex * ex + ey * ey + ez * ez;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
            return Shell(x, y, z, px - t * ex, py - t * ey, pz - t * ez, t);
        }

        public const float Fill = 0.35f, RockBand = 6f, RockFade = 3f, DebrisBand = 4.5f, RockRefM = 10f, RockFineM = 2f;

        public float HoleR => R - 1.25f * R * Blend;

        public float FloorHalf
        {
            get
            {
                float h = HoleR, r = h + Fill * h, c = h - Fill * h;
                return (float)Math.Sqrt(r * r - c * c);
            }
        }

        public const float ReliefLo = 2.2f, ReliefHi = 2.6f;

        public void RockSpan(out float lo, out float hi)
        {
            lo = hi = 0f;
            if (!CaveCarve) return;
            float h = HoleR, k = Math.Min(1f, h / RockRefM);
            lo = 2f * Fill * h + k * ReliefLo;
            hi = k * ReliefHi;
            if (!Natural) return;
            float m = Math.Min(1f, h / ProfileRefM);
            lo += m * (UndercutM + 0.5f * LedgeStepM + CeilDomeM) + LeanMax * (2f * h + 1f) + (CeilShift + WideW + RiftH) * h;
            hi += m * (CeilBumpM + CeilDropM + MoundM + BankM) + (WideH + RiftTopW) * 2f * h;
        }

        uint RockSeed => (uint)(F * 16777216f) * 0x9E3779B1u + 0x7F4A7C15u;

        float Rock(float x, float y, float z, float qx, float qy, float qz, out float floor)
        {
            if (Natural) return Passage(x, y, z, qx, qy, qz, out floor);
            float hole = HoleR, sy = qy + Fill * hole;
            bool cell = Shape == CrystalCell;
            float wall = (cell ? Facets(qx, sy, qz) : (float)Math.Sqrt(qx * qx + sy * sy + qz * qz)) - Fill * hole;
            if (N > 0f) wall += N * Lump(x, y, z);
            float k = Math.Min(1f, hole / RockRefM), band = Math.Abs(wall - hole);
            if (band < RockBand * k) wall += (cell ? CellRelief : Shape == Mine ? MineRelief : 1f) * k * Relief(x, y, z) * Math.Min(1f, (RockBand * k - band) / (RockFade * k));
            floor = -qy;
            if (Math.Abs(floor - hole) < DebrisBand * k) floor += k * Debris(x, z);
            return Math.Max(wall, floor);
        }

        bool Natural => Shape == Cave || Shape == CaveRoom;

        public const float ProfileRefM = 2.2f, LeanMax = 0.35f, LeanSpanM = 9f, SideSpanM = 11f, CeilShift = 0.45f, ShiftSpanM = 13f, SectionSpanM = 12f, WideW = 0.25f, WideH = 0.2f, RiftFloorW = 0.05f, RiftTopW = 0.3f, RiftH = 0.7f;
        public const float LedgeStepM = 1.1f, UndercutM = 0.6f, UndercutAtM = 0.8f, UndercutSpanM = 0.55f, CeilBumpM = 0.35f, CeilDropM = 0.9f, CeilDomeM = 1.0f, CeilSpanM = 21f, MoundM = 0.3f, BankM = 0.5f;
        const uint ProfileSeed = 0x3C6EF372u;

        float Passage(float x, float y, float z, float qx, float qy, float qz, out float floor)
        {
            float hole = HoleR, m = Math.Min(1f, hole / ProfileRefM), shell = 2f * (float)Math.Sqrt(qx * qx + qy * qy + qz * qz) - R;
            float ex = Bx - Ax, ez = Bz - Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
            float px = len > 1e-3f ? -ez / len : 1f, pz = len > 1e-3f ? ex / len : 0f;
            float u = qx * px + qz * pz, a2 = Math.Max(0f, qx * qx + qz * qz - u * u), half = FloorHalf;
            float vf = qy + hole, side = 2f * Value(ProfileSeed ^ 0xA54FF53Au, x / SideSpanM, 0f, z / SideSpanM) - 1f;
            float tilt = CeilShift * hole * (2f * Value(ProfileSeed ^ 0x6A09E667u, x / ShiftSpanM, 0f, z / ShiftSpanM) - 1f) * Smooth(0.3f * hole, 1.3f * hole, vf);
            float su = u - LeanMax * (2f * Value(ProfileSeed ^ 0x510E527Fu, x / LeanSpanM, 0f, z / LeanSpanM) - 1f) * vf - tilt;
            float sec = Value(ProfileSeed ^ 0xBB67AE85u, x / SectionSpanM, 0f, z / SectionSpanM), wide = Smooth(0.35f, 0.15f, sec), rift = Smooth(0.65f, 0.85f, sec);
            float sw = 1f + WideW * wide - rift * (RiftFloorW + RiftTopW * Smooth(0f, 1.5f * hole, vf)), sh = 1f - WideH * wide + RiftH * rift;
            float ledge = Smooth(0.2f, 0.7f, side * su / hole) * Smooth(0.6f, 1.4f, vf), cut = Smooth(0.2f, 0.7f, -side * su / hole);
            float g = vf / LedgeStepM, gf = g - (float)Math.Floor(g), sy = qy + Fill * hole + ledge * LedgeStepM * (gf * gf * gf * (gf * (6f * gf - 15f) + 10f) - gf);
            float wall = hole + ((float)Math.Sqrt(Sq(su / sw) + a2 + Sq(sy / sh)) - (1f + Fill) * hole) * Math.Min(sw, sh);
            if (N > 0f) wall += N * Lump(x, y, z);
            float k = Math.Min(1f, hole / RockRefM), band = Math.Abs(wall - hole);
            if (band < RockBand * k) wall += k * Relief(x, y, z) * Math.Min(1f, (RockBand * k - band) / (RockFade * k));
            float under = Math.Max(0f, 1f - Sq((vf - UndercutAtM) / UndercutSpanM));
            wall -= m * UndercutM * cut * under * under;
            float c = Value(ProfileSeed ^ 0x9B05688Cu, x / CeilSpanM, 0f, z / CeilSpanM);
            float roof = CeilBumpM * (2f * Value(ProfileSeed ^ 0x1F83D9ABu, x / 3.5f, y / 3.5f, z / 3.5f) - 1f) + CeilDropM * Smooth(0.6f, 0.85f, c) - CeilDomeM * Smooth(0.6f, 0.85f, 1f - c);
            wall += m * roof * Smooth(0.2f * hole, 0.9f * hole, qy);
            float off = Math.Abs(u) / half;
            floor = -qy + m * (MoundM * Value(ProfileSeed ^ 0x5BE0CD19u, x / 4.5f, 0f, z / 4.5f) * Smooth(0.75f, 0.95f, off) + BankM * Smooth(0.75f, 1.05f, off));
            if (Math.Abs(floor - hole) < DebrisBand * k) floor += k * Debris(x, z);
            return Math.Max(Math.Max(wall, floor), shell);
        }

        static float Sq(float v) => v * v;

        static float Smooth(float a, float b, float v)
        {
            float t = Math.Max(0f, Math.Min(1f, (v - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        public const float CellRelief = 0.3f, MineRelief = 0.3f, FacetJitter = 0.2f;

        float Facets(float x, float y, float z)
        {
            uint seed = RockSeed ^ 0x2545F491u;
            float ca = Unit(Hash(seed, 1, 0, 0)) - 0.5f, sa = Unit(Hash(seed, 2, 0, 0)) - 0.5f, n = (float)Math.Sqrt(ca * ca + sa * sa);
            if (n < 1e-3f) { ca = 1f; sa = 0f; } else { ca /= n; sa /= n; }
            float rx = ca * x - sa * z, rz = sa * x + ca * z, best = float.MinValue;
            float p = 1.618034f, l = (float)Math.Sqrt(1f + p * p), a = 1f / l, b = p / l;
            for (int k = 0; k < 12; k++)
            {
                int s = k / 3, r = k - 3 * s;
                float u = (s & 1) == 0 ? a : -a, v = (s & 2) == 0 ? b : -b;
                float fx = r == 0 ? 0f : r == 1 ? u : v, fy = r == 0 ? u : r == 1 ? v : 0f, fz = r == 0 ? v : r == 1 ? 0f : u;
                float d = (rx * fx + y * fy + rz * fz) * (1f + FacetJitter * Unit(Hash(seed, k, 3, 5)));
                if (d > best) best = d;
            }
            return best;
        }

        float Relief(float x, float y, float z)
        {
            uint seed = RockSeed;
            float w = 3f * (Value(seed ^ 0x1B56C4E9u, x / 9f, y / 9f, z / 9f) - 0.5f);
            float wx = x + w, wy = y + 0.5f * w, wz = z - w;
            float lump = Value(seed, wx / 6f, wy / 5f, wz / 6f);
            float facet = 1f - Math.Abs(2f * Value(seed ^ 0x5851F42Du, wx / 2.6f, wy / 2.2f, wz / 2.6f) - 1f);
            float s = wy / 1.8f + 1.5f * lump, f = s - (float)Math.Floor(s);
            return 2.6f * (lump - 0.5f) + (1f - Coarse) * (1.2f * (facet - 0.5f) + 0.9f * (f * f * f - 0.25f));
        }

        float Debris(float x, float z) => 0.35f * Value(RockSeed ^ 0x9E3779B9u, x / 8f, 0f, z / 8f);

        float Profile(float t)
        {
            float p = P * Pinch(t);
            if (D <= 0f) return p;
            uint seed = Seed;
            float b = Math.Max(0f, 1f - t / FlareSpan), s = F * t + 0.45f * (Value(seed, 1.7f * F * t, 0f, 0f) - 0.5f);
            float cell = (float)Math.Floor(s), f = s - cell;
            float band = 16f * f * f * (1f - f) * (1f - f) * (0.35f + 0.65f * Unit(Hash(seed, (int)cell, 7, 11)));
            return p + Flare * (1f - b * b) + D * band * (1f - b);
        }

        uint Seed => (uint)(S * 16777216f) * 0x9E3779B1u + 0x632BE5ABu;

        float Lump(float x, float y, float z)
        {
            float k = 1f / (Wave * R);
            uint seed = Seed;
            return 0.65f * Value(seed, x * k, y * k, z * k) + 0.35f * Value(seed ^ 0x68E31DA4u, 2.1f * k * x, 2.1f * k * y, 2.1f * k * z);
        }

        public float Weathering(float x, float y, float z)
        {
            if (N <= 0f) return 0f;
            if (Shape != Boulder) return N * Lump(x, y, z);
            float k = Grain / (Wave * R);
            return N * ((1f - GrainShare) * Lump(x, y, z) + GrainShare * Value(Seed ^ 0x2545F491u, k * x, k * y, k * z));
        }

        static uint Hash(uint seed, int x, int y, int z)
        {
            uint h = seed ^ ((uint)x * 0x9E3779B1u) ^ ((uint)y * 0x85EBCA77u) ^ ((uint)z * 0xC2B2AE3Du);
            h ^= h >> 16; h *= 0x7FEB352Du;
            h ^= h >> 15; h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }

        static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

        static float Value(uint seed, float x, float y, float z)
        {
            float fx0 = (float)Math.Floor(x), fy0 = (float)Math.Floor(y), fz0 = (float)Math.Floor(z);
            int ix = (int)fx0, iy = (int)fy0, iz = (int)fz0;
            float fx = x - fx0, fy = y - fy0, fz = z - fz0;
            float u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy), w = fz * fz * (3f - 2f * fz);
            float a = Lerp(Unit(Hash(seed, ix, iy, iz)), Unit(Hash(seed, ix + 1, iy, iz)), u);
            float b = Lerp(Unit(Hash(seed, ix, iy + 1, iz)), Unit(Hash(seed, ix + 1, iy + 1, iz)), u);
            float c = Lerp(Unit(Hash(seed, ix, iy, iz + 1)), Unit(Hash(seed, ix + 1, iy, iz + 1)), u);
            float d = Lerp(Unit(Hash(seed, ix, iy + 1, iz + 1)), Unit(Hash(seed, ix + 1, iy + 1, iz + 1)), u);
            return Lerp(Lerp(a, b, v), Lerp(c, d, v), w);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        float Pinch(float t)
        {
            float span = Math.Max(T, 1f - T), u = (t - T) / span;
            return Math.Max(0f, 1f - u * u);
        }

        bool Slab(float x, float y, float z, out float dist)
        {
            float ex = Bx - Ax, ez = Bz - Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
            float ux = len > 0 ? ex / len : 1f, uz = len > 0 ? ez / len : 0f;
            float px = x - 0.5f * (Ax + Bx), pz = z - 0.5f * (Az + Bz), half = 0.5f * len;
            float along = px * ux + pz * uz, across = pz * ux - px * uz, sn = Math.Max(-1f, Math.Min(1f, along / (half + W)));
            float wide = W * (1f - Math.Abs(T) * (0.5f - 0.5f * (T > 0f ? sn : -sn)));
            float a = Math.Max(Math.Abs(along) - half, 0f);
            float b = Math.Max(Math.Abs(across) - wide, 0f);
            float dy = y - 0.5f * (Ay + By), h = H;
            if (dy > 0f && D > 0f)
            {
                float ea = along / (half + W + R), eb = across / (W + R);
                h -= D * Math.Min(1f, ea * ea + eb * eb);
            }
            float c = Math.Max(Math.Abs(dy) - h, 0f);
            dist = (float)Math.Sqrt(a * a + b * b + c * c);
            if (N > 0f) dist += N * Lump(x, y, z);
            return dist < R;
        }

        bool Basin(float x, float y, float z, out float dist)
        {
            float dx = x - Ax, dz = z - Az, ay = Ay;
            if (Bx != Ax || Bz != Az)
            {
                float fx = Bx - Ax, fz = Bz - Az, t = Math.Max(0f, Math.Min(1f, (dx * fx + dz * fz) / (fx * fx + fz * fz)));
                dx -= t * fx;
                dz -= t * fz;
                ay += t * (By - Ay);
            }
            float h = Math.Max(y - ay + H, 0f);
            if (D > 0f)
            {
                float j = 0.6f * Value(Seed ^ 0x3C6EF372u, x / 3f, 0f, z / 3f), s = h / D + j, i = (float)Math.Floor(s), f = s - i;
                float f2 = f * f;
                h = Math.Max(0f, D * (i + Lerp(f, f2 * f2, Riser) - j));
            }
            float rad = T != 0f ? W - T * h : W;
            float wob = P > 0f ? P * rad * Value(Seed, x / 1.5f, 0f, z / 1.5f) : 0f;
            if (P < 0f)
            {
                float l = Math.Max(W, LobeM);
                wob += P * W * (1f - Value(Seed ^ 0xA54FF53Au, x / l, 0f, z / l));
            }
            float a = (float)Math.Sqrt(dx * dx + dz * dz) - rad - wob;
            if (T != 0f) a /= (float)Math.Sqrt(1f + T * T);
            a = Math.Max(a, 0f);
            float c = Math.Max(Math.Abs(y - ay) - H, 0f);
            dist = (float)Math.Sqrt(a * a + c * c);
            if (N > 0f)
            {
                float sc = Math.Max(1f, LumpM / (Wave * R));
                dist += N * Lump(x / sc, y / sc, z / sc);
            }
            return dist < R;
        }

        public const float CrustReach = 3f, CrustWarp = 0.4f;

        public bool Crusts => Shape == Spring && Kind == Add && F < 0.5f;

        public float CrustR => R + W + Math.Abs(T) * H;

        public float CrustExtent => CrustR * (1f + (CrustReach - 1f) * (1f + CrustWarp));

        public float Crust(float x, float z)
        {
            float rf = CrustR, dx = x - Ax, dz = z - Az, ext = CrustExtent;
            if (Math.Abs(dx) >= ext || Math.Abs(dz) >= ext) return 0f;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist >= ext) return 0f;
            uint seed = Seed ^ 0x2C1B3C6Du;
            float k = 1f / rf;
            float n = 0.5f * Value(seed, x * k, 0f, z * k) + 0.3f * Value(seed ^ 0x297A2D39u, 2.3f * k * x, 0f, 2.3f * k * z) + 0.2f * Value(seed ^ 0x1B873593u, 5.1f * k * x, 0f, 5.1f * k * z);
            float t = (dist - rf) / ((CrustReach - 1f) * rf) + 2f * CrustWarp * (n - 0.5f);
            t = Math.Max(0f, Math.Min(1f, t));
            return 1f - t * t * (3f - 2f * t);
        }

        public const float FertileWarp = 0.45f;

        public bool Oasis => Shape == Spring && Kind == Carve && F <= -ObjectField.OasisTag;

        public float OasisR => -F - ObjectField.OasisTag;

        public float FertileExtent => ObjectField.OasisMargin(OasisR) + ObjectField.OasisRingM * FertileWarp;

        public float Fertile(float x, float z)
        {
            float r = OasisR, dx = x - Ax, dz = z - Az, ext = FertileExtent;
            if (Math.Abs(dx) >= ext || Math.Abs(dz) >= ext) return 0f;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist >= ext) return 0f;
            uint seed = Seed ^ 0x5F356495u;
            float k = 1f / ObjectField.OasisRingM;
            float n = 0.5f * Value(seed, x * k, 0f, z * k) + 0.3f * Value(seed ^ 0x297A2D39u, 2.3f * k * x, 0f, 2.3f * k * z) + 0.2f * Value(seed ^ 0x1B873593u, 5.1f * k * x, 0f, 5.1f * k * z);
            float t = (dist - r) / ObjectField.OasisRingM + 2f * FertileWarp * (n - 0.5f);
            t = Math.Max(0f, Math.Min(1f, t));
            return 1f - t * t * (3f - 2f * t);
        }

        public bool Under(float x, float z)
        {
            if (Shape == Spring)
            {
                float sx = x - Ax, sz = z - Az, rr = Spread + R, gx = Bx - Ax, gz = Bz - Az, gg = gx * gx + gz * gz;
                if (gg > 0f)
                {
                    float u = Math.Max(0f, Math.Min(1f, (sx * gx + sz * gz) / gg));
                    sx -= u * gx;
                    sz -= u * gz;
                }
                return sx * sx + sz * sz <= rr * rr;
            }
            if (W > 0f)
            {
                float ex = Bx - Ax, ez = Bz - Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                float ux = len > 0 ? ex / len : 1f, uz = len > 0 ? ez / len : 0f;
                float px = x - 0.5f * (Ax + Bx), pz = z - 0.5f * (Az + Bz);
                float a = Math.Max(Math.Abs(px * ux + pz * uz) - 0.5f * len, 0f), b = Math.Max(Math.Abs(pz * ux - px * uz) - W, 0f);
                return a * a + b * b <= R * R;
            }
            float fx = Bx - Ax, fz = Bz - Az, ll = fx * fx + fz * fz, qx0 = x - Ax, qz0 = z - Az;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (qx0 * fx + qz0 * fz) / ll)) : 0f;
            float qx = qx0 - t * fx, qz = qz0 - t * fz;
            return qx * qx + qz * qz <= R * R;
        }

        public float Hole(float dist) => dist - (R - 1.25f * R * Blend);

        public static float Cut(float d, float c, float k)
        {
            float h = Math.Max(0f, Math.Min(1f, 0.5f + 0.5f * (c - d) / k));
            return c + (d - c) * h - k * h * (1 - h);
        }
    }

    public interface IEditList
    {
        int Count { get; }
        FeatureEdit At(int k);
    }

    public struct NoEdits : IEditList
    {
        public int Count => 0;
        public FeatureEdit At(int k) => default;
    }

    public struct EditArray : IEditList
    {
        public FeatureEdit[] Items;
        public int Count => Items == null ? 0 : Items.Length;
        public FeatureEdit At(int k) => Items[k];
    }

    public struct EditedSolid<TCols, TEdits> : ISolidField where TCols : struct, IColumnField where TEdits : struct, IEditList
    {
        public TCols Columns;
        public TEdits Edits;
        public float StepM;

        public float Density(int i, int j, int k) => Features.Apply(Columns, Edits, Columns.Height(i, k) - j * StepM, i, j, k, StepM);
        public bool Sharp(int i, int j, int k) => Features.Covers(Edits, i * StepM, j * StepM, k * StepM, StepM);
        public bool Round(int i, int j, int k) => Features.Rounds(Edits, i * StepM, j * StepM, k * StepM, StepM);
        public float Base(int i, int j, int k) => Features.Level(Columns, Columns.Height(i, k) - j * StepM, i, k, StepM);

        public float Edit(float d, float x, float y, float z) => Features.Edit(Edits, d, x, y, z, true);
    }

    public static class Features
    {
        public const int TileCells = 32;
        public const int MarginCells = 3;
        public const float OverhangChance = 0.6f, ArchChance = 0.3f;
        public const float OverhangMinDrop = 50f, ReferenceCellM = 45f;
        public const float CaveMinDrop = 30f, CaveRoof = 12f, CaveGrade = 0.18f, RoofSampleM = 4f;
        public const float CaveHollow = 1f - 1.25f * FeatureEdit.Blend, JoinBlend = 0.25f;
        public const float LatticeStepM = 11.25f, MinRadiusSteps = 1.25f;
        public const float UndercutChance = 0.5f, UndercutMinDrop = 30f, UndercutContrast = 0.12f, UndercutStep = 15f;
        public const int UndercutReach = 3;

        public static float Apply<TCols, TEdits>(TCols columns, TEdits edits, float d, int i, int j, int k, float s) where TCols : struct, IColumnField where TEdits : struct, IEditList
            => Edit(edits, Level(columns, d, i, k, s), i * s, j * s, k * s);

        public static float Edit<TEdits>(TEdits edits, float d, float x, float y, float z, bool smooth = false) where TEdits : struct, IEditList
        {
            float hole = float.MaxValue, low = float.MaxValue, k = 0f;
            for (int q = 0, n = edits.Count; q < n; q++)
            {
                var e = edits.At(q);
                if (!e.Reach(x, y, z, out float dist, out float floor))
                {
                    if (smooth && hole == float.MaxValue && e.Kind == FeatureEdit.Add && e.Shape == FeatureEdit.Boulder) d = Math.Max(d, e.R - e.BoulderDistance(x, y, z));
                    continue;
                }
                if (e.Kind == FeatureEdit.Add)
                {
                    if (hole < float.MaxValue) { d = FeatureEdit.Cut(d, Math.Max(hole, low), k); hole = low = float.MaxValue; k = 0f; }
                    d = Math.Max(d, e.R - dist);
                }
                else
                {
                    float c = e.Hole(dist), kc = e.R * FeatureEdit.Blend;
                    hole = hole < float.MaxValue ? FeatureEdit.Cut(hole, c, JoinBlend * Math.Min(k, kc)) : c;
                    low = Math.Min(low, floor < float.MaxValue ? e.Hole(floor) : c);
                    k = Math.Max(k, kc);
                }
            }
            return hole < float.MaxValue ? FeatureEdit.Cut(d, Math.Max(hole, low), k) : d;
        }

        public static float Level<TCols>(TCols columns, float d, int i, int k, float s) where TCols : struct, IColumnField
            => d / Slope(columns.Height(i + 1, k), columns.Height(i - 1, k), columns.Height(i, k + 1), columns.Height(i, k - 1), s);

        public static float Slope(float xp, float xm, float zp, float zm, float s)
        {
            float gx = (xp - xm) / (2 * s), gz = (zp - zm) / (2 * s);
            return (float)Math.Sqrt(1 + gx * gx + gz * gz);
        }

        public static bool Covers<TEdits>(TEdits edits, float x, float y, float z, float s) where TEdits : struct, IEditList
        {
            bool any = false;
            for (int q = 0, n = edits.Count; q < n; q++)
            {
                var e = edits.At(q);
                if (e.MinX <= x + s && e.MaxX >= x && e.MinY <= y + s && e.MaxY >= y && e.MinZ <= z + s && e.MaxZ >= z)
                {
                    if (e.Fine && ((e.Shape != FeatureEdit.Cave && e.Shape != FeatureEdit.Mine) || s < NearField.NodeStep(0))) return false;
                    if (e.Shape == FeatureEdit.Boulder && s <= NearField.NodeStep(0)) return false;
                    any = true;
                }
            }
            return any;
        }

        public static bool Rounds<TEdits>(TEdits edits, float x, float y, float z, float s) where TEdits : struct, IEditList
        {
            if (s > NearField.NodeStep(0)) return false;
            for (int q = 0, n = edits.Count; q < n; q++)
            {
                var e = edits.At(q);
                if (e.Shape == FeatureEdit.Boulder && !e.Fine && e.MinX <= x + s && e.MaxX >= x && e.MinY <= y + s && e.MaxY >= y && e.MinZ <= z + s && e.MaxZ >= z) return true;
            }
            return false;
        }

        public static int TileAt(float x, float z, float cellMeters)
        {
            float span = TileCells * cellMeters;
            return (int)Math.Floor(z / span) * 65536 + (int)Math.Floor(x / span);
        }

        public static int TileOf(FeatureEdit e, float cellMeters)
        {
            float span = TileCells * cellMeters;
            int tx = (int)Math.Floor(e.MinX / span), tz = (int)Math.Floor(e.MinZ / span);
            return (int)Math.Floor(e.MaxX / span) == tx && (int)Math.Floor(e.MaxZ / span) == tz ? tz * 65536 + tx : -1;
        }

        public static bool Inside(FeatureEdit e, float cellMeters)
        {
            float span = TileCells * cellMeters, m = MarginCells * cellMeters;
            float x0 = (float)Math.Floor(e.MinX / span) * span, z0 = (float)Math.Floor(e.MinZ / span) * span;
            return e.MinX >= x0 + m && e.MaxX <= x0 + span - m && e.MinZ >= z0 + m && e.MaxZ <= z0 + span - m;
        }

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail) => Place(w, detail, out _);

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail, out CaveGraph[] caves) => Place(w, detail, out caves, out _);

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail, out CaveGraph[] caves, out ObjectInstance[] discoveries) => Place(w, detail, out caves, out discoveries, out _);

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail, out CaveGraph[] caves, out ObjectInstance[] discoveries, out RoadSegment[] roads)
        {
            roads = Array.Empty<RoadSegment>();
            var graphs = new List<CaveGraph>();
            var terrain = Terrain(w, detail, graphs);
            caves = graphs.ToArray();
            terrain = Sunk(terrain, CaveBrooks.Sinks(w, detail, terrain, caves));
            discoveries = null;
            if (w.Objects == null || w.Biome == null) return Brooked(w, detail, terrain, caves);
            var field = new ObjectField(w, detail, terrain, w.Objects);
            field.QuicksandPools();
            var boulders = field.Boulders();
            var springs = field.SpringEdits();
            var bog = field.BogEdits();
            var sand = field.QuicksandEdits();
            var bogs = new FeatureEdit[bog.Length + sand.Length];
            bog.CopyTo(bogs, 0);
            sand.CopyTo(bogs, bog.Length);
            var oases = field.OasisEdits();
            var dens = field.DenEdits();
            var vaults = field.DungeonEdits();
            discoveries = field.Discoveries();
            roads = field.Roads();
            var tunnels = field.TunnelEdits(out var bores);
            graphs.AddRange(bores);
            caves = graphs.ToArray();
            if (boulders.Length + springs.Length + bogs.Length + oases.Length + dens.Length + vaults.Length + tunnels.Length == 0) return Brooked(w, detail, terrain, caves);
            var all = new FeatureEdit[terrain.Length + boulders.Length + springs.Length + bogs.Length + oases.Length + dens.Length + vaults.Length + tunnels.Length];
            terrain.CopyTo(all, 0);
            boulders.CopyTo(all, terrain.Length);
            springs.CopyTo(all, terrain.Length + boulders.Length);
            bogs.CopyTo(all, terrain.Length + boulders.Length + springs.Length);
            oases.CopyTo(all, terrain.Length + boulders.Length + springs.Length + bogs.Length);
            dens.CopyTo(all, terrain.Length + boulders.Length + springs.Length + bogs.Length + oases.Length);
            vaults.CopyTo(all, terrain.Length + boulders.Length + springs.Length + bogs.Length + oases.Length + dens.Length);
            tunnels.CopyTo(all, terrain.Length + boulders.Length + springs.Length + bogs.Length + oases.Length + dens.Length + vaults.Length);
            all = DungeonLinks.Place(w, detail, field, discoveries, all, graphs);
            caves = graphs.ToArray();
            return Brooked(w, detail, all, caves);
        }

        static FeatureEdit[] Brooked(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves)
        {
            var channels = CaveBrooks.Channels(w, CaveBrooks.Build(w, detail, edits, caves));
            if (channels.Length == 0) return edits;
            var all = new FeatureEdit[edits.Length + channels.Length];
            edits.CopyTo(all, 0);
            channels.CopyTo(all, edits.Length);
            return all;
        }

        static FeatureEdit[] Sunk(FeatureEdit[] terrain, FeatureEdit[] holes)
        {
            if (holes.Length == 0) return terrain;
            var kept = new List<FeatureEdit>(terrain.Length + holes.Length);
            foreach (var e in terrain)
            {
                bool drop = false;
                if (e.Shape == FeatureEdit.Speleothem)
                    foreach (var h in holes)
                        if (e.MinX < h.MaxX && e.MaxX > h.MinX && e.MinZ < h.MaxZ && e.MaxZ > h.MinZ && e.MinY < h.MaxY && e.MaxY > h.MinY) drop = true;
                if (!drop) kept.Add(e);
            }
            kept.AddRange(holes);
            return kept.ToArray();
        }

        static FeatureEdit[] Terrain(GeneratedWorld w, TerrainDetail detail, List<CaveGraph> caves)
        {
            var list = new List<FeatureEdit>();
            if (w.Biome == null || 16f < MinRadiusSteps * LatticeStepM) return list.ToArray();
            if (w.Materials == null) throw new InvalidOperationException("feature placement needs the material library");
            var strata = new StrataField(w.Config, w.Materials);
            int tiles = w.Config.Size / TileCells;
            ulong ro = Rng.Stream(w.Config.Seed, "overhangs").NextU64(), ra = Rng.Stream(w.Config.Seed, "arches").NextU64(), rc = Rng.Stream(w.Config.Seed, "cave-graph").NextU64();
            ulong rk = Rng.Stream(w.Config.Seed, "cave-rock").NextU64(), rd = Rng.Stream(w.Config.Seed, "cave-speleothems").NextU64(), rf = Rng.Stream(w.Config.Seed, "cave-flowstone").NextU64();
            ulong ru = Rng.Stream(w.Config.Seed, "undercuts").NextU64();
            ulong rw = Rng.Stream(w.Config.Seed, "cave-width").NextU64(), rp = Rng.Stream(w.Config.Seed, "cave-pools").NextU64(), rg = Rng.Stream(w.Config.Seed, "cave-crystal").NextU64(), ri = Rng.Stream(w.Config.Seed, "cave-ice").NextU64(), rv = Rng.Stream(w.Config.Seed, "cave-obsidian").NextU64();
            ulong rt = Rng.Stream(w.Config.Seed, "tors").NextU64(), rh = Rng.Stream(w.Config.Seed, "hoodoos").NextU64();
            ulong rl = Rng.Stream(w.Config.Seed, "mesa-lips").NextU64(), rs = Rng.Stream(w.Config.Seed, "cave-skylights").NextU64();
            var lips = Landforms.BiomeMask(w, Mesas.Biomes);
            var tors = Landforms.BiomeMask(w, Landforms.TorBiomes);
            var hoodoos = Landforms.BiomeMask(w, Landforms.HoodooBiomes);
            int from = 0;
            var tile = new List<FeatureEdit>();
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    ulong t = (ulong)(ty * tiles + tx);
                    var r = new Rng(Rng.Mix(ro + t));
                    tile.Clear();
                    Overhang(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                    r = new Rng(Rng.Mix(ra + t));
                    tile.Clear();
                    Arch(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                    r = new Rng(Rng.Mix(rc + t));
                    tile.Clear();
                    var width = new Rng(Rng.Mix(rw + t));
                    Underground.Tile(w, detail, w.Caves, tx, ty, ref r, ref width, Rng.Mix(rk + t), Rng.Mix(rd + t), Rng.Mix(rf + t), Rng.Mix(rp + t), Rng.Mix(rg + t), Rng.Mix(ri + t), Rng.Mix(rv + t), Rng.Mix(rs + t), list.GetRange(from, list.Count - from), tile, out var graph);
                    if (Keep(w, tile, list) && graph != null) caves.Add(graph);
                    r = new Rng(Rng.Mix(ru + t));
                    tile.Clear();
                    Undercut(w, detail, strata, tx, ty, ref r, tile);
                    if (tile.Count == 0 || !Overlaps(tile[0], list, from)) Keep(w, tile, list);
                    r = new Rng(Rng.Mix(rt + t));
                    tile.Clear();
                    Landforms.Tor(w, detail, strata, tors, tx, ty, ref r, tile);
                    if (Landforms.Fits(tile, list, from)) Keep(w, tile, list);
                    r = new Rng(Rng.Mix(rh + t));
                    tile.Clear();
                    Landforms.Hoodoo(w, detail, strata, hoodoos, tx, ty, ref r, tile);
                    if (Landforms.Fits(tile, list, from)) Keep(w, tile, list);
                    r = new Rng(Rng.Mix(rl + t));
                    tile.Clear();
                    Mesas.Lip(w, detail, lips, tx, ty, ref r, tile);
                    if (tile.Count == 0 || !Overlaps(tile[0], list, from)) Keep(w, tile, list);
                    from = list.Count;
                }
            Mines.Place(w, detail, strata, list, caves);
            return list.ToArray();
        }

        static bool Keep(GeneratedWorld w, List<FeatureEdit> tile, List<FeatureEdit> list)
        {
            foreach (var e in tile) if (!Inside(e, w.Config.CellMeters)) return false;
            list.AddRange(tile);
            return true;
        }

        internal static bool Overlaps(FeatureEdit e, List<FeatureEdit> list, int from)
        {
            for (int q = from; q < list.Count; q++)
            {
                var o = list[q];
                if (e.MinX < o.MaxX && e.MaxX > o.MinX && e.MinY < o.MaxY && e.MaxY > o.MinY && e.MinZ < o.MaxZ && e.MaxZ > o.MinZ) return true;
            }
            return false;
        }

        internal static bool Dry(GeneratedWorld w, int i) => !w.Sea[i] && w.Water.LakeId[i] < 0 && !w.Water.IsRiver[i] && w.Height[i] > 0;

        static void Overhang(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), radius = r.Range(28f, 40f), length = r.Range(60f, 120f), lift = r.Range(0.2f, 0.5f);
            if (roll >= OverhangChance) return;
            int n = w.Config.Size, best = -1, bestDir = 0;
            float bestDrop = Math.Min(OverhangMinDrop, OverhangMinDrop / ReferenceCellM * w.Config.CellMeters);
            for (int y = ty * TileCells + MarginCells; y < (ty + 1) * TileCells - MarginCells; y++)
                for (int x = tx * TileCells + MarginCells; x < (tx + 1) * TileCells - MarginCells; x++)
                {
                    int i = y * n + x;
                    if (!Dry(w, i)) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = i + Grid.DY[k] * n + Grid.DX[k];
                        float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                        if (drop > bestDrop && Dry(w, j)) { bestDrop = drop; best = i; bestDir = k; }
                    }
                }
            if (best < 0) return;
            float cm = w.Config.CellMeters, inv = 1f / Grid.Dist[bestDir];
            float gx = Grid.DX[bestDir] * inv, gz = Grid.DY[bestDir] * inv;
            float fx = best % n + 0.7f * gx, fz = best / n + 0.7f * gz;
            float foot = detail.HeightAt(fx, fz);
            float inset = radius * 0.6f / cm, cx = fx - gx * inset, cz = fz - gz * inset;
            float y0 = foot + radius * lift;
            if (detail.HeightAt(cx, cz) < y0 + radius * 0.3f) return;
            if (detail.HeightAt(cx - gx * radius / cm, cz - gz * radius / cm) < y0 + radius + 8f) return;
            float half = length * 0.5f;
            output.Add(new FeatureEdit
            {
                Kind = FeatureEdit.Carve, Shape = FeatureEdit.Overhang, R = radius,
                Ax = cx * cm - gz * half, Az = cz * cm + gx * half, Ay = y0,
                Bx = cx * cm + gz * half, Bz = cz * cm - gx * half, By = y0,
            });
        }

        static void Undercut(GeneratedWorld w, TerrainDetail detail, StrataField strata, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), radius = r.Range(16f, 22f), length = r.Range(80f, 140f);
            if (roll >= UndercutChance) return;
            int n = w.Config.Size, best = -1, bestDir = 0;
            float bestDrop = Math.Min(UndercutMinDrop, UndercutMinDrop / ReferenceCellM * w.Config.CellMeters);
            for (int y = ty * TileCells + MarginCells; y < (ty + 1) * TileCells - MarginCells; y++)
                for (int x = tx * TileCells + MarginCells; x < (tx + 1) * TileCells - MarginCells; x++)
                {
                    int i = y * n + x;
                    if (!Dry(w, i)) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = i + Grid.DY[k] * n + Grid.DX[k];
                        float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                        if (drop > bestDrop && Dry(w, j)) { bestDrop = drop; best = i; bestDir = k; }
                    }
                }
            if (best < 0) return;
            float cm = w.Config.CellMeters, inv = 1f / Grid.Dist[bestDir];
            float gx = Grid.DX[bestDir] * inv, gz = Grid.DY[bestDir] * inv;
            float bx = best % n, bz = best / n, foot = w.Height[best], top = foot;
            for (int s = 1; s <= UndercutReach; s++)
            {
                float h = detail.HeightAt(bx + gx * s, bz + gz * s);
                if (foot - h < UndercutStep) break;
                foot = h;
            }
            for (int s = 1; s <= UndercutReach; s++)
            {
                float h = detail.HeightAt(bx - gx * s, bz - gz * s);
                if (h - top < UndercutStep) break;
                top = h;
            }
            float y0 = 0, score = UndercutContrast;
            for (float y = foot + 0.5f * radius; y <= top - 1.2f * radius; y += 1f)
            {
                float cap = 0;
                for (int s = 0; s <= 4; s++) cap += strata.Hardness(best, y + radius * (0.6f + 0.25f * s));
                float v = cap / 5f - strata.Hardness(best, y);
                if (v > score) { score = v; y0 = y; }
            }
            if (score <= UndercutContrast) return;
            float fx = bx + gx * UndercutReach, fz = bz + gz * UndercutReach;
            for (int s = 0; s < 20 * UndercutReach && detail.HeightAt(fx, fz) < y0; s++) { fx -= gx * 0.1f; fz -= gz * 0.1f; }
            float inset = radius * 0.6f / cm, cx = fx - gx * inset, cz = fz - gz * inset;
            if (detail.HeightAt(cx, cz) < y0 + radius * 0.3f) return;
            if (detail.HeightAt(cx - gx * radius / cm, cz - gz * radius / cm) < y0 + radius + 8f) return;
            float half = length * 0.5f;
            output.Add(new FeatureEdit
            {
                Kind = FeatureEdit.Carve, Shape = FeatureEdit.Overhang, R = radius,
                Ax = cx * cm - gz * half, Az = cz * cm + gx * half, Ay = y0,
                Bx = cx * cm + gz * half, Bz = cz * cm - gx * half, By = y0,
            });
        }

        public const float DryBedShare = 0.25f, ArchClearM = 25f, ArchAcross = 0.5f;
        public const int FlowSteps = 3;

        public static bool Channel(GeneratedWorld w, int i) => w.Water.IsRiver[i] || (!w.Sea[i] && w.Water.LakeId[i] < 0 && w.Height[i] > 0 && w.Water.DischargeM3s[i] >= DryBedShare * w.Config.RiverMinDischarge);

        public static bool FlowDir(GeneratedWorld w, int i, out float fx, out float fz)
        {
            int n = w.Config.Size, j = i;
            for (int k = 0; k < FlowSteps; k++)
            {
                int r = w.Water.Receiver[j];
                if (r < 0 || r == j) break;
                j = r;
            }
            fx = j % n - i % n; fz = j / n - i / n;
            float len = (float)Math.Sqrt(fx * fx + fz * fz);
            if (len <= 0) return false;
            fx /= len; fz /= len;
            return true;
        }

        internal static bool DryAt(GeneratedWorld w, float x, float z)
        {
            int n = w.Config.Size, ix = (int)Math.Round(x), iz = (int)Math.Round(z);
            return ix >= 0 && iz >= 0 && ix < n && iz < n && Dry(w, iz * n + ix);
        }

        static void Arch(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), span = r.Range(110f, 200f), radius = r.Range(16f, 22f), rise = r.Range(0.45f, 0.7f);
            if (roll >= ArchChance) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, half = span * 0.5f / cm, leg = radius / cm;
            float pad = MarginCells + radius / cm, x0 = tx * TileCells + pad, x1 = (tx + 1) * TileCells - pad, z0 = ty * TileCells + pad, z1 = (ty + 1) * TileCells - pad;
            float best = 0, ax = 0, az = 0, bx = 0, bz = 0, ha = 0, hb = 0;
            for (int y = (int)Math.Ceiling(z0); y <= (int)z1; y++)
                for (int x = (int)Math.Ceiling(x0); x <= (int)x1; x++)
                {
                    int c = y * n + x;
                    if (!Channel(w, c) || !FlowDir(w, c, out float fx, out float fz)) continue;
                    float sx = -fz, sz = fx, pax = x - sx * half, paz = y - sz * half, pbx = x + sx * half, pbz = y + sz * half;
                    if (Math.Min(pax, pbx) < x0 || Math.Max(pax, pbx) > x1 || Math.Min(paz, pbz) < z0 || Math.Max(paz, pbz) > z1) continue;
                    if (!DryAt(w, pax, paz) || !DryAt(w, pbx, pbz) || !DryAt(w, pax + sx * leg, paz + sz * leg) || !DryAt(w, pbx - sx * leg, pbz - sz * leg)) continue;
                    float qa = detail.HeightAt(pax, paz), qb = detail.HeightAt(pbx, pbz), score = Math.Min(qa, qb) - detail.HeightAt(x, y);
                    if (score <= best) continue;
                    float ground = Math.Max(qa, qb);
                    for (int s = 1; s < 8; s++) ground = Math.Max(ground, detail.HeightAt(pax + (pbx - pax) * s / 8f, paz + (pbz - paz) * s / 8f));
                    if (Math.Max(qa, qb) + span * rise - radius < ground + ArchClearM) continue;
                    best = score; ax = pax; az = paz; bx = pbx; bz = pbz; ha = qa; hb = qb;
                }
            if (!(best > 0)) return;
            float apex = Math.Max(ha, hb) + span * rise, y0 = ha - radius * 0.5f, y1 = hb - radius * 0.5f, ctrl = 2f * apex - 0.5f * (y0 + y1);
            const int Pieces = 5;
            float lx = ax, lz = az, ly = y0;
            for (int s = 1; s <= Pieces; s++)
            {
                float t = s / (float)Pieces, u = 1 - t;
                float nx = ax + (bx - ax) * t, nz = az + (bz - az) * t, ny = u * u * y0 + 2 * u * t * ctrl + t * t * y1;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Arch, R = radius, Ax = lx * cm, Ay = ly, Az = lz * cm, Bx = nx * cm, By = ny, Bz = nz * cm });
                lx = nx; ly = ny; lz = nz;
            }
        }

        internal static int Steepest(GeneratedWorld w, int i)
        {
            if (!Dry(w, i)) return -1;
            int n = w.Config.Size, dir = -1;
            float most = Math.Min(CaveMinDrop, CaveMinDrop / ReferenceCellM * w.Config.CellMeters);
            for (int k = 0; k < 8; k++)
            {
                int j = i + Grid.DY[k] * n + Grid.DX[k];
                float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                if (drop > most && Dry(w, j)) { most = drop; dir = k; }
            }
            return dir;
        }
    }
}
