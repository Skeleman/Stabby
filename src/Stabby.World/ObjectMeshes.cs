using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class ObjectMesh
    {
        public float[] Positions, Normals;
        public int[] Indices;
        public float[] Uvs, Winds, Tones;
        public int VertexCount => Positions.Length / 3;
        public int TriangleCount => Indices.Length / 3;
    }

    public static class ObjectMeshes
    {
        public const int MaxLods = 4;
        static readonly int[] RockLevels = { 3, 2, 1, 0 };
        static readonly int[] RingSegments = { 12, 8, 6, 6 };
        static readonly float[] Cos = { 0.8660254f, 0.70710678f, 0.5f };
        static readonly float[] Sin = { 0.5f, 0.70710678f, 0.8660254f };

        public static ulong Key(string defId, int part, int variant)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in defId) { h ^= ch; h *= 1099511628211UL; }
            h ^= (ulong)part * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)variant * 0xC2B2AE3D27D4EB4FUL;
            return Rng.Mix(h);
        }

        public static ObjectMesh Build(ObjectPart p, ulong key, int lod)
        {
            float aspect = 0;
            if (p.Shape == ObjectPart.Log && p.ScaleX > 0) aspect = 0.5f * (p.ScaleY + p.ScaleZ) / p.ScaleX;
            if (p.Shape == ObjectPart.Stump && p.ScaleY > 0) aspect = 0.5f * (p.ScaleX + p.ScaleZ) / p.ScaleY;
            if (p.Shape == ObjectPart.Dripstone && p.ScaleY > 0) aspect = 0.5f * (p.ScaleX + p.ScaleZ) / p.ScaleY;
            return Build(p.Shape, key, lod, aspect, p.Ends, p.Trim);
        }

        public static ObjectMesh Build(int shape, ulong key, int lod, float aspect = 0, int ends = 0, int trim = 0)
        {
            lod = Math.Max(0, Math.Min(MaxLods - 1, lod));
            if (ObjectPart.IsStructure(shape)) return Structures.Build(shape, key, lod, trim);
            if (shape == ObjectPart.Shrub) return ObjectFoliage.Shrub(key, lod);
            if (shape == ObjectPart.Reed) return ObjectFoliage.Reed(key, lod);
            if (shape == ObjectPart.Broadleaf) return ObjectFoliage.Broadleaf(key, lod);
            if (shape == ObjectPart.Conifer) return ObjectFoliage.Conifer(key, lod);
            if (shape == ObjectPart.Log) return Log(key, lod, aspect > 0 ? aspect : 0.11f, ends);
            if (shape == ObjectPart.Stump) return Stump(key, lod, aspect > 0 ? aspect : 1.11f, ends);
            if (shape == ObjectPart.BasaltColumns) return Columns(key, lod);
            if (shape == ObjectPart.Crystals) return Crystals(key, lod);
            if (shape == ObjectPart.Dripstone) return Dripstone(key, lod, aspect > 0 ? aspect : 0.3f);
            if (shape == ObjectPart.DripColumn) return DripColumn(key, lod);
            return Rock(key, RockLevels[lod]);
        }

        internal struct Builder
        {
            public List<float> P;
            public List<int> I;
            public static Builder New() => new Builder { P = new List<float>(), I = new List<int>() };
            public int Add(float x, float y, float z) { P.Add(x); P.Add(y); P.Add(z); return P.Count / 3 - 1; }
            public void Tri(int a, int b, int c) { I.Add(a); I.Add(b); I.Add(c); }
            public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

            public ObjectMesh Finish()
            {
                var pos = P.ToArray();
                var nor = new float[pos.Length];
                var idx = I.ToArray();
                for (int t = 0; t < idx.Length; t += 3)
                {
                    int a = idx[t] * 3, b = idx[t + 1] * 3, c = idx[t + 2] * 3;
                    float ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
                    float vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = idx[t + k] * 3;
                        nor[v] += nx; nor[v + 1] += ny; nor[v + 2] += nz;
                    }
                }
                for (int v = 0; v < nor.Length; v += 3)
                {
                    float l = (float)Math.Sqrt(nor[v] * nor[v] + nor[v + 1] * nor[v + 1] + nor[v + 2] * nor[v + 2]);
                    if (l > 0) { nor[v] /= l; nor[v + 1] /= l; nor[v + 2] /= l; }
                    else nor[v + 1] = 1;
                }
                return new ObjectMesh { Positions = pos, Normals = nor, Indices = idx };
            }
        }

        static readonly float[] HexX = { 0.8660254f, 0f, -0.8660254f, -0.8660254f, 0f, 0.8660254f };
        static readonly float[] HexZ = { 0.5f, 1f, 0.5f, -0.5f, -1f, -0.5f };
        static readonly int[] HexQ = { 0, 1, 0, -1, -1, 0, 1, 2, 1, 0, -1, -2, -2, -2, -1, 0, 1, 2, 2 };
        static readonly int[] HexR = { 0, 0, 1, 1, 0, -1, -1, 0, 1, 2, 2, 2, 1, 0, -1, -2, -2, -2, -1 };
        static readonly float[] ColumnOdds = { 1f, 0.85f, 0.35f };
        public const float PrismSkirt = 0.12f, PrismFlare = 1.12f, PrismBevel = 0.82f;

        struct Prism
        {
            public float Cx, Cz, Ax, Ay, Az, Ux, Uy, Uz, Vx, Vy, Vz, R, TiltU, TiltV;

            public static Prism Along(float cx, float cz, float ax, float ay, float az, float r)
            {
                float l = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
                ax /= l; ay /= l; az /= l;
                float ox = Math.Abs(az) < 0.9f ? 0f : 1f, oz = 1f - ox;
                float ux = ay * oz, uy = az * ox - ax * oz, uz = -ay * ox;
                float ul = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
                ux /= ul; uy /= ul; uz /= ul;
                return new Prism { Cx = cx, Cz = cz, Ax = ax, Ay = ay, Az = az, Ux = ux, Uy = uy, Uz = uz, Vx = uy * az - uz * ay, Vy = uz * ax - ux * az, Vz = ux * ay - uy * ax, R = r };
            }

            public void Ring(float t, float s, float lift, float[] ring)
            {
                for (int k = 0; k < 6; k++)
                {
                    float hx = HexX[k] * R * s, hz = HexZ[k] * R * s, tk = t + lift * (TiltU * HexX[k] + TiltV * HexZ[k]);
                    ring[k * 3] = Cx + Ax * tk + Ux * hx + Vx * hz;
                    ring[k * 3 + 1] = Ay * tk + Uy * hx + Vy * hz;
                    ring[k * 3 + 2] = Cz + Az * tk + Uz * hx + Vz * hz;
                }
            }
        }

        static void Band(ref Builder b, float[] lo, float[] hi)
        {
            for (int k = 0; k < 6; k++)
            {
                int j = (k + 1) % 6;
                int a = b.Add(lo[k * 3], lo[k * 3 + 1], lo[k * 3 + 2]), c = b.Add(hi[j * 3], hi[j * 3 + 1], hi[j * 3 + 2]);
                int d = b.Add(hi[k * 3], hi[k * 3 + 1], hi[k * 3 + 2]), e = b.Add(lo[j * 3], lo[j * 3 + 1], lo[j * 3 + 2]);
                b.Quad(a, d, c, e);
            }
        }

        static void Fan(ref Builder b, float[] ring, float x, float y, float z)
        {
            for (int k = 0; k < 6; k++)
            {
                int j = (k + 1) % 6;
                b.Tri(b.Add(x, y, z), b.Add(ring[j * 3], ring[j * 3 + 1], ring[j * 3 + 2]), b.Add(ring[k * 3], ring[k * 3 + 1], ring[k * 3 + 2]));
            }
        }

        static void Shaft(ref Builder b, in Prism p, int lod, float bottom, float top, float topScale, bool apex, float tipT)
        {
            float[] lo = new float[18], hi = new float[18];
            p.Ring(bottom, PrismFlare, 0f, lo);
            if (lod == 0)
            {
                p.Ring(0f, 1f, 0f, hi);
                Band(ref b, lo, hi);
                (lo, hi) = (hi, lo);
            }
            if (lod < 2)
            {
                p.Ring(top, 1f, p.R, hi);
                Band(ref b, lo, hi);
                (lo, hi) = (hi, lo);
                top += 0.12f * p.R;
            }
            p.Ring(top, topScale, p.R, hi);
            Band(ref b, lo, hi);
            float tt = apex ? tipT : top + 0.04f * p.R;
            Fan(ref b, hi, p.Cx + p.Ax * tt, p.Ay * tt, p.Cz + p.Az * tt);
        }

        static ObjectMesh Normalised(Builder b)
        {
            int count = b.P.Count / 3;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, maxY = 1e-3f;
            for (int v = 0; v < count; v++)
            {
                float x = b.P[v * 3], z = b.P[v * 3 + 2];
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z); maxY = Math.Max(maxY, b.P[v * 3 + 1]);
            }
            float sh = 1f / Math.Max(1e-3f, Math.Max(maxX - minX, maxZ - minZ)), cx = 0.5f * (minX + maxX), cz = 0.5f * (minZ + maxZ);
            for (int v = 0; v < count; v++)
            {
                b.P[v * 3] = (b.P[v * 3] - cx) * sh;
                b.P[v * 3 + 1] /= maxY;
                b.P[v * 3 + 2] = (b.P[v * 3 + 2] - cz) * sh;
            }
            return b.Finish();
        }

        internal static ObjectMesh Columns(ulong key, int lod)
        {
            var rng = new Rng(Rng.Mix(key ^ 0xBA5A17C0FFEE1234UL));
            var b = Builder.New();
            float peak = rng.Range(3f, 6f), tu = rng.Range(-0.12f, 0.12f), tv = rng.Range(-0.12f, 0.12f), d = 2f * 0.8660254f;
            for (int c = 0; c < HexQ.Length; c++)
            {
                int ring = c == 0 ? 0 : c < 7 ? 1 : 2;
                float keep = rng.NextFloat(), h = peak * (1f - 0.18f * ring) * rng.Range(0.7f, 1f), r = rng.Range(0.88f, 0.96f);
                float du = rng.Range(-0.05f, 0.05f), dv = rng.Range(-0.05f, 0.05f);
                if (keep >= ColumnOdds[ring]) continue;
                var p = Prism.Along(d * (HexQ[c] + 0.5f * HexR[c]), d * 0.8660254f * HexR[c], 0f, 1f, 0f, r);
                p.TiltU = tu + du;
                p.TiltV = tv + dv;
                Shaft(ref b, p, lod, -PrismSkirt * peak, h, PrismBevel, false, 0f);
            }
            return Normalised(b);
        }

        internal static ObjectMesh Crystals(ulong key, int lod)
        {
            var rng = new Rng(Rng.Mix(key ^ 0xC7157A1D00D5EEDUL));
            var b = Builder.New();
            int count = rng.Range(4, 9);
            for (int c = 0; c < count; c++)
            {
                Unit(ref rng, out float x, out float y, out float z);
                float lean = c == 0 ? 0.15f : rng.Range(0.3f, 0.9f), r = c == 0 ? 0.5f : rng.Range(0.22f, 0.42f), len = r * rng.Range(3f, 6f), off = c == 0 ? 0f : rng.Range(0.15f, 0.45f), tip = rng.Range(0.8f, 1.4f);
                float hl = Math.Max(1e-3f, (float)Math.Sqrt(x * x + z * z));
                var p = Prism.Along(off * x / hl, off * z / hl, lean * x / hl, 1f, lean * z / hl, r);
                float side = (float)Math.Sqrt(Math.Max(0f, 1f - p.Ay * p.Ay));
                Shaft(ref b, p, lod, -(PrismSkirt * len + r * side) / p.Ay, len, 1f, true, len + r * tip);
            }
            return Normalised(b);
        }

        static readonly int[] DripSegments = { 18, 12, 8, 6 };
        static readonly int[] DripRings = { 12, 7, 4, 3 };
        public const float DripSkirt = 0.12f;

        static void Turn(int n, out float c, out float s)
        {
            float a = 6.2831853f / n, a2 = a * a;
            c = 1f - a2 / 2f * (1f - a2 / 12f * (1f - a2 / 30f * (1f - a2 / 56f)));
            s = a * (1f - a2 / 6f * (1f - a2 / 20f * (1f - a2 / 42f * (1f - a2 / 72f))));
        }

        static void Lobe(ref Rng rng, out float x, out float z)
        {
            Unit(ref rng, out x, out _, out z);
            float l = (float)Math.Sqrt(x * x + z * z);
            if (l < 1e-3f) { x = 1f; z = 0f; } else { x /= l; z /= l; }
        }

        internal static ObjectMesh Dripstone(ulong key, int lod, float aspect)
        {
            var rng = new Rng(Rng.Mix(key ^ 0xD81B57011E5EEDUL));
            int n = DripSegments[lod], rings = DripRings[lod];
            float baseR = rng.Range(0.3f, 0.62f), bulge = Math.Max(-0.4f, Math.Min(0.8f, 2f * (aspect - 0.3f) - 0.3f)), flare = rng.Range(0.35f, 0.9f);
            Lobe(ref rng, out float l1x, out float l1z);
            Lobe(ref rng, out float l2x, out float l2z);
            float a1 = rng.Range(0.14f, 0.26f), a2 = rng.Range(0.1f, 0.2f), b1 = rng.Range(0.04f, 0.12f), b2 = rng.Range(0.03f, 0.09f);
            float dx = rng.Range(-0.12f, 0.12f), dz = rng.Range(-0.12f, 0.12f);
            Turn(n, out float tc, out float ts);
            var dirX = new float[n];
            var dirZ = new float[n];
            float cx = 1f, cz = 0f;
            for (int k = 0; k < n; k++)
            {
                dirX[k] = cx; dirZ[k] = cz;
                float nx = cx * tc - cz * ts;
                cz = cx * ts + cz * tc;
                cx = nx;
            }
            var b = Builder.New();
            int first = 0;
            for (int j = 0; j <= rings; j++)
            {
                float u = j == 0 ? 0f : (j - 1f) / rings, t = u * (0.55f + 0.45f * u), y = j == 0 ? -DripSkirt : t, low = (1f - t) * (1f - t) * (1f - t);
                float sl = 1f - t, taper = sl + 0.35f * ((float)Math.Sqrt(sl) - sl), r = j == 0 ? baseR * (1f + flare) * 1.1f : baseR * (taper * (1f + bulge * t) + flare * low);
                float ox = dx * t * t + rng.Range(-0.03f, 0.03f) * baseR, oz = dz * t * t + rng.Range(-0.03f, 0.03f) * baseR;
                float w1 = b1 + (a1 - b1) * low, w2 = b2 + (a2 - b2) * low;
                int ring = b.P.Count / 3;
                if (j == 0) first = ring;
                for (int k = 0; k < n; k++)
                {
                    float c1 = dirX[k] * l1x + dirZ[k] * l1z, c2 = dirX[k] * l2x + dirZ[k] * l2z;
                    float m = 1f + w1 * c1 + w2 * (2f * c2 * c2 - 1f) + rng.Range(-0.015f, 0.015f);
                    b.Add(ox + dirX[k] * r * m, y, oz + dirZ[k] * r * m);
                }
                if (j == 0) continue;
                int lo = ring - n;
                for (int k = 0; k < n; k++)
                {
                    int q = (k + 1) % n;
                    b.Quad(lo + k, ring + k, ring + q, lo + q);
                }
            }
            int top = first + rings * n, apex = b.Add(dx + rng.Range(-0.02f, 0.02f), 1f, dz + rng.Range(-0.02f, 0.02f));
            for (int k = 0; k < n; k++) b.Tri(apex, top + (k + 1) % n, top + k);
            return b.Finish();
        }

        internal static ObjectMesh DripColumn(ulong key, int lod)
        {
            var rng = new Rng(Rng.Mix(key ^ 0xC01D5701E5EEDUL));
            int n = DripSegments[lod], rings = 2 * DripRings[lod];
            float baseR = rng.Range(0.22f, 0.32f), waist = rng.Range(0.3f, 0.55f), mid = rng.Range(0.4f, 0.6f);
            float foot = rng.Range(0.5f, 1.1f), crown = rng.Range(0.3f, 0.8f);
            Lobe(ref rng, out float l1x, out float l1z);
            Lobe(ref rng, out float l2x, out float l2z);
            float a1 = rng.Range(0.12f, 0.24f), a2 = rng.Range(0.08f, 0.18f), dx = rng.Range(-0.6f, 0.6f) * baseR, dz = rng.Range(-0.6f, 0.6f) * baseR;
            Turn(n, out float tc, out float ts);
            var b = Builder.New();
            for (int j = 0; j <= rings + 2; j++)
            {
                float t = j == 0 ? 0f : j == rings + 2 ? 1f : (j - 1f) / rings, y = j == 0 ? -DripSkirt : j == rings + 2 ? 1f + DripSkirt : t;
                float e = t < mid ? (mid - t) / mid : (t - mid) / (1f - mid), lo = (1f - t) * (1f - t) * (1f - t), hi = t * t * t;
                float r = baseR * (waist + (1f - waist) * e * e + foot * lo + crown * hi) * (j == 0 || j == rings + 2 ? 1.1f : 1f);
                float bend = 4f * t * (1f - t), ox = dx * bend, oz = dz * bend, w1 = a1 * (0.5f + lo + hi), w2 = a2 * (0.5f + 0.5f * e);
                int ring = b.P.Count / 3;
                float cx = 1f, cz = 0f;
                for (int k = 0; k < n; k++)
                {
                    float c1 = cx * l1x + cz * l1z, c2 = cx * l2x + cz * l2z;
                    float m = 1f + w1 * c1 + w2 * (2f * c2 * c2 - 1f) + rng.Range(-0.02f, 0.02f);
                    b.Add(ox + cx * r * m, y, oz + cz * r * m);
                    float nx = cx * tc - cz * ts;
                    cz = cx * ts + cz * tc;
                    cx = nx;
                }
                if (j == 0) continue;
                int below = ring - n;
                for (int k = 0; k < n; k++)
                {
                    int q = (k + 1) % n;
                    b.Quad(below + k, ring + k, ring + q, below + q);
                }
            }
            return b.Finish();
        }

        internal static ObjectMesh Rock(ulong key, int levels)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x51ED270B27D4EB4FUL));
            int planes = rng.Range(5, 10);
            var pn = new float[planes * 4];
            for (int k = 0; k < planes; k++)
            {
                Unit(ref rng, out float x, out float y, out float z);
                pn[k * 4] = x; pn[k * 4 + 1] = y; pn[k * 4 + 2] = z; pn[k * 4 + 3] = rng.Range(0.62f, 0.9f);
            }
            int bumps = rng.Range(2, 5);
            var bn = new float[bumps * 4];
            for (int k = 0; k < bumps; k++)
            {
                Unit(ref rng, out float x, out float y, out float z);
                bn[k * 4] = x; bn[k * 4 + 1] = y; bn[k * 4 + 2] = z; bn[k * 4 + 3] = rng.Range(-0.12f, 0.12f);
            }
            float squash = rng.Range(0.55f, 0.85f), stretch = rng.Range(0.8f, 1f), floor = rng.Range(-0.45f, -0.25f);
            var b = Icosphere(levels);
            int count = b.P.Count / 3;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int v = 0; v < count; v++)
            {
                float dx = b.P[v * 3], dy = b.P[v * 3 + 1], dz = b.P[v * 3 + 2];
                float r = 1f;
                for (int k = 0; k < planes; k++)
                {
                    float dot = dx * pn[k * 4] + dy * pn[k * 4 + 1] + dz * pn[k * 4 + 2];
                    if (dot > 1e-3f) r = Math.Min(r, pn[k * 4 + 3] / dot);
                }
                r = 0.7f * r + 0.3f;
                for (int k = 0; k < bumps; k++)
                {
                    float dot = dx * bn[k * 4] + dy * bn[k * 4 + 1] + dz * bn[k * 4 + 2] - 0.7f;
                    if (dot > 0) r += bn[k * 4 + 3] * dot * dot * 11f;
                }
                float x = dx * r * stretch, y = Math.Max(floor, dy * r) * squash, z = dz * r;
                b.P[v * 3] = x; b.P[v * 3 + 1] = y; b.P[v * 3 + 2] = z;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            }
            float span = Math.Max(maxX - minX, maxZ - minZ), sh = 1f / Math.Max(1e-3f, span), sv = 1f / Math.Max(1e-3f, maxY - minY);
            float cx = 0.5f * (minX + maxX), cz = 0.5f * (minZ + maxZ);
            float height = (maxY - minY) * sh;
            for (int v = 0; v < count; v++)
            {
                b.P[v * 3] = (b.P[v * 3] - cx) * sh;
                b.P[v * 3 + 1] = (b.P[v * 3 + 1] - minY) * sv * height;
                b.P[v * 3 + 2] = (b.P[v * 3 + 2] - cz) * sh;
            }
            return b.Finish();
        }

        public static float RockHeight(ulong key)
        {
            var m = Rock(key, 1);
            float hi = 0;
            for (int v = 1; v < m.Positions.Length; v += 3) hi = Math.Max(hi, m.Positions[v]);
            return hi;
        }

        static void Unit(ref Rng rng, out float x, out float y, out float z)
        {
            while (true)
            {
                x = rng.Range(-1f, 1f); y = rng.Range(-1f, 1f); z = rng.Range(-1f, 1f);
                float l = x * x + y * y + z * z;
                if (l < 1e-4f || l > 1f) continue;
                l = (float)Math.Sqrt(l);
                x /= l; y /= l; z /= l;
                return;
            }
        }

        static readonly float[][] IcoP = new float[RockLevels.Length][];
        static readonly int[][] IcoI = new int[RockLevels.Length][];

        static ObjectMeshes()
        {
            for (int l = 0; l < IcoP.Length; l++)
            {
                var b = BuildIcosphere(l);
                IcoP[l] = b.P.ToArray();
                IcoI[l] = b.I.ToArray();
            }
        }

        static Builder Icosphere(int levels) => new Builder { P = new List<float>(IcoP[levels]), I = new List<int>(IcoI[levels]) };

        static Builder BuildIcosphere(int levels)
        {
            var b = Builder.New();
            float t = 1.618034f;
            float[] v =
            {
                -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0,
                0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t,
                t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1,
            };
            for (int k = 0; k < 12; k++) AddUnit(ref b, v[k * 3], v[k * 3 + 1], v[k * 3 + 2]);
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var tris = new List<int>(f);
            for (int l = 0; l < levels; l++)
            {
                var mid = new Dictionary<long, int>();
                var next = new List<int>(tris.Count * 4);
                for (int k = 0; k < tris.Count; k += 3)
                {
                    int a = tris[k], c = tris[k + 1], d = tris[k + 2];
                    int ab = Mid(ref b, mid, a, c), bc = Mid(ref b, mid, c, d), ca = Mid(ref b, mid, d, a);
                    next.AddRange(new[] { a, ab, ca, c, bc, ab, d, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            b.I.AddRange(tris);
            return b;
        }

        static int AddUnit(ref Builder b, float x, float y, float z)
        {
            float l = (float)Math.Sqrt(x * x + y * y + z * z);
            return b.Add(x / l, y / l, z / l);
        }

        static int Mid(ref Builder b, Dictionary<long, int> cache, int a, int c)
        {
            long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
            if (cache.TryGetValue(key, out int m)) return m;
            m = AddUnit(ref b, b.P[a * 3] + b.P[c * 3], b.P[a * 3 + 1] + b.P[c * 3 + 1], b.P[a * 3 + 2] + b.P[c * 3 + 2]);
            cache[key] = m;
            return m;
        }

        static void Ring(int segments, int k, out float c, out float s)
        {
            int step = segments == 12 ? 0 : segments == 8 ? 1 : 2;
            float cs = Cos[step], sn = Sin[step];
            c = 1; s = 0;
            for (int i = 0; i < k; i++)
            {
                float nc = c * cs - s * sn;
                s = c * sn + s * cs;
                c = nc;
            }
        }

        const float StumpR0 = 0.5f / 1.35f;
        const float CodeOak = 0, CodeSpruce = 1, CodeGrain = 2, CodeSoil = 3, CodeRoot = 4, CodeStone = 5;

        struct WoodBuilder
        {
            public Builder B;
            public List<float> T;
            public List<ObjectFoliage.V> R;
            public float Sx, Sy, Sz, Oy;

            public static WoodBuilder New(float sx, float sy, float sz, float oy) => new WoodBuilder { B = Builder.New(), T = new List<float>(), R = new List<ObjectFoliage.V>(), Sx = sx, Sy = sy, Sz = sz, Oy = oy };

            public int Add(ObjectFoliage.V p, float code, float y, float z, float w)
            {
                R.Add(p);
                T.Add(code); T.Add(y); T.Add(z); T.Add(w);
                return B.Add(p.X * Sx, Oy + p.Y * Sy, p.Z * Sz);
            }

            public void Tri(int a, int b, int c, ObjectFoliage.V hint)
            {
                var n = ObjectFoliage.V.Cross(R[b] - R[a], R[c] - R[a]);
                if (ObjectFoliage.V.Dot(n, hint) >= 0) B.Tri(a, b, c);
                else B.Tri(a, c, b);
            }

            public void Quad(int a, int b, int c, int d, ObjectFoliage.V hint) { Tri(a, b, c, hint); Tri(a, c, d, hint); }

            public ObjectMesh Finish()
            {
                var m = B.Finish();
                m.Tones = T.ToArray();
                return m;
            }
        }

        struct Harmonics
        {
            public float A1, B1, A2, B2, A3, B3, A4, B4, A5, B5;

            public static Harmonics Draw(ref Rng rng, float amp, bool first)
            {
                var h = new Harmonics();
                if (first) { h.A1 = rng.Range(-amp, amp); h.B1 = rng.Range(-amp, amp); }
                h.A2 = rng.Range(-amp, amp); h.B2 = rng.Range(-amp, amp);
                h.A3 = rng.Range(-amp, amp) * 0.8f; h.B3 = rng.Range(-amp, amp) * 0.8f;
                h.A4 = rng.Range(-amp, amp) * 0.6f; h.B4 = rng.Range(-amp, amp) * 0.6f;
                h.A5 = rng.Range(-amp, amp) * 0.5f; h.B5 = rng.Range(-amp, amp) * 0.5f;
                return h;
            }

            public float At(float c, float s)
            {
                float c2 = c * c - s * s, s2 = 2 * s * c;
                float c3 = c2 * c - s2 * s, s3 = s2 * c + c2 * s;
                float c4 = c3 * c - s3 * s, s4 = s3 * c + c3 * s;
                float c5 = c4 * c - s4 * s, s5 = s4 * c + c4 * s;
                return A1 * c + B1 * s + A2 * c2 + B2 * s2 + A3 * c3 + B3 * s3 + A4 * c4 + B4 * s4 + A5 * c5 + B5 * s5;
            }
        }

        struct Teeth
        {
            public Harmonics Jag;
            public float Base, Amp, Core, Spike0, Spike1, Spike2, Height0, Height1, Height2;
            public int Spikes;
            public bool Hollow, Sawn;
        }

        static Teeth DrawTeeth(ref Rng rng, bool sawn, float age, float amp, float spikeMin, float spikeMax)
        {
            var t = new Teeth { Sawn = sawn, Jag = Harmonics.Draw(ref rng, 1, true), Amp = amp };
            t.Base = rng.Range(0.05f, 0.3f);
            t.Core = rng.Range(-0.2f, 0.6f);
            t.Hollow = !sawn && age > 0.6f && rng.NextFloat() < 0.5f;
            if (t.Hollow) t.Core = -rng.Range(1f, 2.5f);
            t.Spikes = rng.Range(2, 4);
            t.Spike0 = rng.NextFloat(); t.Spike1 = rng.NextFloat(); t.Spike2 = rng.NextFloat();
            t.Height0 = rng.Range(spikeMin, spikeMax); t.Height1 = rng.Range(spikeMin, spikeMax) * 0.7f; t.Height2 = rng.Range(spikeMin, spikeMax) * 0.5f;
            return t;
        }

        static float SpikeAt(in Teeth t, int k, int segments)
        {
            float h = 0;
            if (t.Spikes > 0) h = Math.Max(h, t.Height0 * Falloff(Nearest(t.Spike0, segments), k, segments));
            if (t.Spikes > 1) h = Math.Max(h, t.Height1 * Falloff(Nearest(t.Spike1, segments), k, segments));
            if (t.Spikes > 2) h = Math.Max(h, t.Height2 * Falloff(Nearest(t.Spike2, segments), k, segments));
            return h;
        }

        static float Falloff(int at, int k, int segments)
        {
            int d = Math.Abs(at - k);
            d = Math.Min(d, segments - d);
            return d == 0 ? 1f : d == 1 ? 0.45f : 0f;
        }

        static float TornRim(ref Rng jr, in Teeth t, int k, int segments, float c, float s) =>
            0.35f * t.Amp * Math.Max(0f, 0.5f + 0.5f * t.Jag.At(c, s)) + 0.3f * SpikeAt(t, k, segments) + jr.Range(0f, 0.3f);

        static float WornRim(ref Rng jr, float tiltY, float tiltZ, float c, float s) => 0.25f * (tiltY * c + tiltZ * s) + jr.Range(0f, 0.12f);

        static void Dome(ref WoodBuilder w, ref Rng jr, int[] rim, int segments, ObjectFoliage.V centre, ObjectFoliage.V axis, ObjectFoliage.V e1, ObjectFoliage.V e2, float radius, float age)
        {
            float[] fracs = { 0.78f, 0.42f }, lifts = { 0.32f, 0.58f };
            var prev = new int[segments];
            for (int k = 0; k < segments; k++) prev[k] = w.Add(w.R[rim[k]], CodeGrain, 0, 0, age);
            float rimAt = 0;
            for (int k = 0; k < segments; k++) rimAt += ObjectFoliage.V.Dot(w.R[rim[k]] - centre, axis) / segments;
            for (int r = 0; r < fracs.Length; r++)
            {
                var next = new int[segments];
                for (int k = 0; k < segments; k++)
                {
                    Ring(segments, k, out float c, out float s);
                    float f = fracs[r] * (1 + jr.Range(-0.06f, 0.06f));
                    next[k] = w.Add(centre + (e1 * c + e2 * s) * (radius * f) + axis * (rimAt + radius * lifts[r] * (1 + jr.Range(-0.15f, 0.15f))), CodeGrain, 0, 0, age);
                }
                for (int k = 0; k < segments; k++)
                {
                    int k1 = (k + 1) % segments;
                    w.Quad(prev[k], prev[k1], next[k1], next[k], axis);
                }
                prev = next;
            }
            int mid = w.Add(centre + axis * (rimAt + radius * jr.Range(0.62f, 0.72f)), CodeGrain, 0, 0, age);
            for (int k = 0; k < segments; k++) w.Tri(mid, prev[k], prev[(k + 1) % segments], axis);
        }

        static int Nearest(float frac, int segments) => (int)(frac * segments + 0.5f) % segments;

        static void Cap(ref WoodBuilder w, ref Rng jr, int[] rim, int segments, in Teeth t, ObjectFoliage.V centre, ObjectFoliage.V axis, ObjectFoliage.V e1, ObjectFoliage.V e2, float radius, float moss, float age)
        {
            float extra = t.Sawn ? 0 : 1;
            var outer = new int[segments];
            for (int k = 0; k < segments; k++) outer[k] = w.Add(w.R[rim[k]], CodeGrain, moss, extra, age);
            int mid = w.Add(centre + axis * (t.Sawn ? 0 : t.Core), CodeGrain, moss, extra, age);
            if (t.Sawn)
            {
                for (int k = 0; k < segments; k++) w.Tri(mid, outer[k], outer[(k + 1) % segments], axis);
                return;
            }
            var inner = new int[segments];
            for (int k = 0; k < segments; k++)
            {
                Ring(segments, k, out float c, out float s);
                float spike = SpikeAt(t, k, segments);
                float jag = Math.Max(0f, 0.5f + 0.5f * t.Jag.At(c, s));
                float tooth = (k & 1) == 0 ? jr.Range(0.15f, 0.6f) * (0.4f + t.Amp) * Math.Min(radius, 1.2f) : 0f;
                float frac = t.Hollow ? jr.Range(0.74f, 0.86f) : spike > 0 ? jr.Range(0.72f, 0.84f) : jr.Range(0.5f, 0.72f);
                float lift = t.Hollow ? 0.4f * t.Amp * jag + jr.Range(-0.1f, 0.3f) + 0.6f * spike + tooth : t.Base + t.Amp * jag + jr.Range(0f, 0.4f) + spike + tooth;
                float rim0 = ObjectFoliage.V.Dot(w.R[rim[k]] - centre, axis);
                inner[k] = w.Add(centre + (e1 * c + e2 * s) * (radius * frac) + axis * (lift + rim0 * frac), CodeGrain, moss, extra, age);
            }
            for (int k = 0; k < segments; k++)
            {
                int k1 = (k + 1) % segments;
                w.Quad(outer[k], outer[k1], inner[k1], inner[k], axis);
                w.Tri(mid, inner[k], inner[k1], axis);
            }
        }

        public const float PitRimY = -1.1f, PitSkirt = 1.6f;

        public static void PitReach(float rp, float thick, out float centreX, out float radius)
        {
            centreX = 0.3f - thick - 0.55f * rp;
            radius = 1.5f * rp;
        }

        public static void RootPlate(ulong key, out float radius, out float centreY, out float thickness)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x3C6EF372FE94F82BUL));
            radius = rng.Range(3.2f, 4.5f);
            centreY = -1 + 0.75f * radius;
            thickness = rng.Range(0.8f, 1.4f);
        }

        static ObjectMesh Log(ulong key, int lod, float aspect, int ends)
        {
            int seg = RingSegments[lod];
            var rng = new Rng(Rng.Mix(key ^ 0x2545F4914F6CDD1DUL));
            var jr = new Rng(Rng.Mix(key ^ 0x7A3D5E1F0C2B4968UL ^ (ulong)seg));
            float h = 0.5f * Math.Max(aspect, 0.01f), half = 0.5f / h;
            var w = WoodBuilder.New(h, 0.5f, 0.5f, 0.5f);
            bool rooted = ends == ObjectPart.EndsRooted, sawn = ends == ObjectPart.EndsSawn, worn = ends == ObjectPart.EndsWorn;
            float code = rng.NextFloat() < 0.5f ? CodeOak : CodeSpruce;
            float moss = rng.NextFloat() < 0.65f ? rng.Range(0.4f, 1f) : 0f, age = rng.NextFloat();
            if (worn) { code = CodeGrain; moss = 0; age = 0.15f + 0.35f * age; }
            float taper = rng.Range(0.72f, 0.92f), sag = rng.Range(-0.1f, 0.25f), bow = rng.Range(-0.8f, 0.8f), wave = rng.Range(-1f, 1f);
            float lift = rooted ? rng.Range(0.25f, 0.5f) : 0f, flare = rooted ? rng.Range(0.3f, 0.55f) : 0f;
            var section = Harmonics.Draw(ref rng, 0.06f, false);
            float tiltY0 = rng.Range(-0.3f, 0.3f), tiltZ0 = rng.Range(-0.3f, 0.3f), tiltY1 = rng.Range(-0.3f, 0.3f), tiltZ1 = rng.Range(-0.3f, 0.3f);
            var teeth0 = DrawTeeth(ref rng, sawn, age, rng.Range(0.3f, 0.9f), 0.8f, 2.2f);
            var teeth1 = DrawTeeth(ref rng, sawn, age, rng.Range(0.3f, 0.9f), 0.8f, 2.2f);
            RootPlate(key, out float rp, out float yc, out float thick);
            float liftY = lift * yc;
            int rings = lod == 0 ? 7 : lod == 1 ? 5 : 3;
            var centres = new ObjectFoliage.V[rings];
            var radii = new float[rings];
            var ringIdx = new int[rings][];
            for (int r = 0; r < rings; r++)
            {
                float u = r / (float)(rings - 1), b = 4 * u * (1 - u), back = 1 - u;
                centres[r] = new ObjectFoliage.V(-half + 2 * half * u, sag * b + liftY * back * back * back, bow * b + wave * b * (2 * u - 1));
                radii[r] = (1 + (taper - 1) * u) * (1 + flare * back * back * back * back);
                ringIdx[r] = new int[seg];
                for (int k = 0; k < seg; k++)
                {
                    Ring(seg, k, out float c, out float s);
                    float rr = radii[r] * (1 + section.At(c, s) + jr.Range(-0.02f, 0.02f));
                    float ax = 0;
                    if (r == 0) ax = worn ? -WornRim(ref jr, tiltY0, tiltZ0, c, s) : -(tiltY0 * c + tiltZ0 * s) * (sawn ? 0.3f : 1f) - (sawn || rooted ? 0 : TornRim(ref jr, teeth0, k, seg, c, s));
                    if (r == rings - 1) ax = worn ? WornRim(ref jr, tiltY1, tiltZ1, c, s) : (tiltY1 * c + tiltZ1 * s) * (sawn ? 0.3f : 1f) + (sawn ? 0 : TornRim(ref jr, teeth1, k, seg, c, s));
                    ringIdx[r][k] = w.Add(centres[r] + new ObjectFoliage.V(ax, c * rr, s * rr), code, moss, 0, age);
                }
            }
            for (int r = 0; r + 1 < rings; r++)
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    Ring(seg, k, out float c0, out float s0);
                    Ring(seg, k1, out float c1, out float s1);
                    w.Quad(ringIdx[r][k], ringIdx[r][k1], ringIdx[r + 1][k1], ringIdx[r + 1][k], new ObjectFoliage.V(0, c0 + c1, s0 + s1));
                }
            var ex = new ObjectFoliage.V(1, 0, 0);
            var ey = new ObjectFoliage.V(0, 1, 0);
            var ez = new ObjectFoliage.V(0, 0, 1);
            if (worn)
            {
                Dome(ref w, ref jr, ringIdx[0], seg, centres[0], -ex, ey, ez, radii[0], age);
                Dome(ref w, ref jr, ringIdx[rings - 1], seg, centres[rings - 1], ex, ey, ez, radii[rings - 1], age);
            }
            else
            {
                if (!rooted) Cap(ref w, ref jr, ringIdx[0], seg, teeth0, centres[0], -ex, ey, ez, radii[0], moss, age);
                Cap(ref w, ref jr, ringIdx[rings - 1], seg, teeth1, centres[rings - 1], ex, ey, ez, radii[rings - 1], moss, age);
            }
            int stubs = rng.Range(3, 8), limbs = rng.Range(0, 3);
            var stubU = new float[stubs + limbs];
            var stubDir = new ObjectFoliage.V[stubs + limbs];
            var stubLen = new float[stubs + limbs];
            var stubRad = new float[stubs + limbs];
            for (int i = 0; i < stubs + limbs; i++)
            {
                bool limb = i >= stubs;
                float dy, dz;
                while (true)
                {
                    dy = rng.Range(-1f, 1f); dz = rng.Range(-1f, 1f);
                    float l = dy * dy + dz * dz;
                    if (l < 0.04f || l > 1f) continue;
                    l = (float)Math.Sqrt(l);
                    dy /= l; dz /= l;
                    if (dy >= -0.342f) break;
                }
                stubU[i] = limb ? rng.Range(0.35f, 0.92f) : rng.Range(0.15f, 0.9f);
                stubDir[i] = new ObjectFoliage.V(rng.Range(0.2f, 0.8f), dy, dz).Norm();
                stubLen[i] = limb ? rng.Range(1.5f, 3f) : rng.Range(0.5f, 1.4f);
                stubRad[i] = limb ? rng.Range(0.3f, 0.45f) : rng.Range(0.16f, 0.34f);
            }
            if (lod < 2)
            {
                int first = -1, second = -1;
                for (int i = 0; i < stubs; i++)
                {
                    if (first < 0 || stubRad[i] > stubRad[first]) { second = first; first = i; }
                    else if (second < 0 || stubRad[i] > stubRad[second]) second = i;
                }
                for (int i = 0; i < stubs + limbs; i++)
                {
                    if (lod == 1 && i != first && i != second) continue;
                    float u = stubU[i], back = 1 - u, b = 4 * u * (1 - u);
                    var c0 = new ObjectFoliage.V(-half + 2 * half * u, sag * b + liftY * back * back * back, bow * b + wave * b * (2 * u - 1));
                    float rad = (1 + (taper - 1) * u);
                    var d = stubDir[i];
                    var side = new ObjectFoliage.V(0, d.Y, d.Z).Norm();
                    var start = c0 + side * (0.8f * rad);
                    bool limb = i >= stubs;
                    var tip = start + d * stubLen[i];
                    if (limb) tip = tip + new ObjectFoliage.V(0, -0.35f * stubLen[i], 0);
                    if (tip.Y < -0.9f + stubRad[i]) tip = new ObjectFoliage.V(tip.X, -0.9f + stubRad[i], tip.Z);
                    Branch(ref w, ref jr, start, tip, stubRad[i], limb ? 0.35f : 0.75f, code, moss, age, limb);
                }
            }
            if (rooted) Plate(ref w, ref rng, ref jr, seg, lod, -half, rp, yc, thick, moss, age);
            return w.Finish();
        }

        static void Branch(ref WoodBuilder w, ref Rng jr, ObjectFoliage.V start, ObjectFoliage.V tip, float radius, float tipScale, float code, float moss, float age, bool limb)
        {
            var d = (tip - start).Norm();
            var e1 = ObjectFoliage.V.Cross(d, new ObjectFoliage.V(1, 0, 0));
            if (e1.Length < 0.1f) e1 = ObjectFoliage.V.Cross(d, new ObjectFoliage.V(0, 1, 0));
            e1 = e1.Norm();
            var e2 = ObjectFoliage.V.Cross(d, e1);
            const int seg = 6;
            var ringsAt = new[] { -0.15f, 0.5f, 1f };
            var idx = new int[3][];
            float len = (tip - start).Length;
            for (int r = 0; r < 3; r++)
            {
                idx[r] = new int[seg];
                float rr = radius * (r == 0 ? 1f : r == 1 ? 0.5f * (1 + tipScale) : tipScale);
                var c0 = start + d * (len * ringsAt[r]);
                if (limb && r == 1) c0 = c0 + new ObjectFoliage.V(0, 0.15f * len, 0);
                for (int k = 0; k < seg; k++)
                {
                    Ring(seg, k, out float c, out float s);
                    float ax = r == 2 ? jr.Range(-0.12f, 0.12f) * radius : 0;
                    idx[r][k] = w.Add(c0 + (e1 * c + e2 * s) * (rr * jr.Range(0.9f, 1.08f)) + d * ax, code, moss * 0.6f, 0, age);
                }
            }
            for (int r = 0; r < 2; r++)
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    Ring(seg, k, out float c0, out float s0);
                    Ring(seg, k1, out float c1, out float s1);
                    w.Quad(idx[r][k], idx[r][k1], idx[r + 1][k1], idx[r + 1][k], e1 * (c0 + c1) + e2 * (s0 + s1));
                }
            var teeth = new Teeth { Base = 0.02f, Amp = 0.1f * radius, Core = jr.Range(-0.05f, 0.2f) * radius, Jag = new Harmonics { A3 = 1 } };
            var tipCentre = start + d * len;
            Cap(ref w, ref jr, idx[2], seg, teeth, tipCentre, d, e1, e2, radius * tipScale, moss * 0.6f, age);
        }

        static void Plate(ref WoodBuilder w, ref Rng rng, ref Rng jr, int seg, int lod, float baseX, float rp, float yc, float thick, float moss, float age)
        {
            float tilt = rng.Range(0.1f, 0.3f), xp = baseX + 0.3f, bulge = rng.Range(0.1f, 0.35f);
            var rim = Harmonics.Draw(ref rng, 0.16f, true);
            float darkB = rng.Range(0.3f, 0.6f);
            float pitScale = rng.Range(0.8f, 1f), rimRise = rng.Range(0.5f, 0.9f);
            var pitRim = Harmonics.Draw(ref rng, 0.15f, true);
            int roots = rng.Range(5, 10);
            var rootFrac = new float[roots];
            var rootLen = new float[roots];
            var rootRad = new float[roots];
            for (int i = 0; i < roots; i++) { rootFrac[i] = rng.NextFloat(); rootLen[i] = rng.Range(1f, 3f); rootRad[i] = rng.Range(0.15f, 0.32f); }
            int strands = rng.Range(14, 22);
            var sc = new float[strands];
            var ss = new float[strands];
            var sr0 = new float[strands];
            var sr1 = new float[strands];
            var srad = new float[strands];
            for (int i = 0; i < strands; i++) { Dir2(ref rng, out sc[i], out ss[i]); sr0[i] = rng.Range(0.15f, 0.45f); sr1[i] = rng.Range(0.95f, 1.3f); srad[i] = rng.Range(0.07f, 0.17f); }
            int lumps = rng.Range(16, 26);
            var lc = new float[lumps];
            var ls = new float[lumps];
            var lrho = new float[lumps];
            var lsize = new float[lumps];
            var lside = new int[lumps];
            var lstone = new bool[lumps];
            for (int i = 0; i < lumps; i++)
            {
                Dir2(ref rng, out lc[i], out ls[i]);
                lrho[i] = rng.Range(0.2f, 0.92f);
                lsize[i] = rng.Range(0.15f, 0.45f);
                float u = rng.NextFloat();
                lside[i] = u < 0.35f ? 0 : u < 0.8f ? 1 : 2;
                lstone[i] = rng.NextFloat() < 0.35f;
            }
            var ex = new ObjectFoliage.V(1, 0, 0);
            var ey = new ObjectFoliage.V(0, 1, 0);
            var rhoAt = lod == 0 ? new[] { 0.3f, 0.55f, 0.8f, 1f } : lod == 1 ? new[] { 0.4f, 0.75f, 1f } : new[] { 0.45f, 1f };
            int rings = rhoAt.Length, last = rings - 1;
            var rimS = new float[seg];
            for (int k = 0; k < seg; k++)
            {
                Ring(seg, k, out float c, out float s);
                rimS[k] = Math.Max(0.7f, Math.Min(1.25f, RimAt(rim, c, s) + jr.Range(-0.08f, 0.08f)));
            }
            var front = new int[rings][];
            var back = new int[rings][];
            for (int q = 0; q < rings; q++)
            {
                front[q] = new int[seg];
                back[q] = new int[seg];
                float rho = rhoAt[q];
                bool outer = q == last;
                for (int k = 0; k < seg; k++)
                {
                    Ring(seg, k, out float c, out float s);
                    float rr = rp * rho * (1 + (rimS[k] - 1) * rho * rho) * (outer ? 1f : 1 + jr.Range(-0.05f, 0.05f));
                    var d = new ObjectFoliage.V(xp - tilt * c * rr, yc + c * rr, s * rr);
                    float bumpF = outer ? jr.Range(-0.12f, 0.12f) : jr.Range(-0.12f, 0.25f);
                    float bumpB = outer ? jr.Range(-0.12f, 0.12f) : jr.Range(-0.2f, 0.15f);
                    float con = Contact(d.Y);
                    front[q][k] = w.Add(d + ex * (bulge * (1 - rho * rho) + bumpF), CodeSoil, 0.1f + 0.3f * rho * rho * rho, Math.Max(0.1f, con), age);
                    back[q][k] = w.Add(d - ex * (thick * (1 - 0.7f * rho * rho) + bumpB), CodeSoil, 0.08f, Math.Max(darkB, con), age);
                }
            }
            int fc = w.Add(new ObjectFoliage.V(xp + bulge, yc, 0), CodeSoil, 0.1f, 0.1f, age);
            int bc = w.Add(new ObjectFoliage.V(xp - thick, yc, 0), CodeSoil, 0.08f, darkB, age);
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                w.Tri(fc, front[0][k], front[0][k1], ex);
                w.Tri(bc, back[0][k], back[0][k1], -ex);
                for (int q = 0; q < last; q++)
                {
                    w.Quad(front[q][k], front[q][k1], front[q + 1][k1], front[q + 1][k], ex);
                    w.Quad(back[q][k], back[q][k1], back[q + 1][k1], back[q + 1][k], -ex);
                }
            }
            var band0 = new int[seg];
            var band1 = new int[seg];
            for (int k = 0; k < seg; k++)
            {
                float con0 = Contact(w.R[front[last][k]].Y), con1 = Contact(w.R[back[last][k]].Y);
                band0[k] = w.Add(w.R[front[last][k]], CodeSoil, 0.3f, Math.Max(0.2f, con0), age);
                band1[k] = w.Add(w.R[back[last][k]], CodeSoil, 0.1f, Math.Max(darkB, con1), age);
            }
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                Ring(seg, k, out float c0, out float s0);
                Ring(seg, k1, out float c1, out float s1);
                w.Quad(band0[k], band0[k1], band1[k1], band1[k], new ObjectFoliage.V(0, c0 + c1, s0 + s1));
            }
            if (lod < 2)
            {
                for (int i = 0; i < roots; i++)
                {
                    if (lod == 1 && (i & 1) == 1) continue;
                    int k = Nearest(rootFrac[i], seg);
                    Ring(seg, k, out float c, out float s);
                    float rr = 0.7f * rp;
                    var start = new ObjectFoliage.V(xp - tilt * c * rr - thick * (1 - 0.7f * 0.49f) + 0.15f, yc + c * rr, s * rr);
                    var d = new ObjectFoliage.V(-0.6f, c - 0.3f, s).Norm();
                    var tip = start + d * rootLen[i];
                    if (tip.Y < -1.05f) tip = new ObjectFoliage.V(tip.X, -1.05f, tip.Z);
                    Root(ref w, ref jr, start, tip, rootRad[i], age, start + (tip - start) * 0.5f + new ObjectFoliage.V(0, -0.1f * (tip - start).Length, 0));
                }
                for (int i = 0; i < strands; i++)
                {
                    if (lod == 1 && (i & 1) == 1) continue;
                    float c = sc[i], s = ss[i], rimR = RimAt(rim, c, s);
                    bool onFront = i % 3 == 0;
                    float rho0 = sr0[i], rhoM = 0.5f * (rho0 + 1), r0 = rp * rho0, rm = rp * rhoM, r1 = rp * Math.Min(1.22f, sr1[i] * rimR);
                    ObjectFoliage.V start, mid, tip;
                    if (onFront)
                    {
                        start = new ObjectFoliage.V(xp - tilt * c * r0 + bulge * (1 - rho0 * rho0), yc + c * r0, s * r0);
                        mid = new ObjectFoliage.V(xp - tilt * c * rm + bulge * (1 - rhoM * rhoM) + 0.5f * srad[i], yc + c * rm, s * rm);
                        tip = new ObjectFoliage.V(xp - tilt * c * r1 - 0.3f * thick, yc + c * r1, s * r1);
                    }
                    else
                    {
                        start = new ObjectFoliage.V(xp - tilt * c * r0 - thick * (1 - 0.7f * rho0 * rho0) + 0.05f, yc + c * r0, s * r0);
                        mid = new ObjectFoliage.V(xp - tilt * c * rm - thick * (1 - 0.7f * rhoM * rhoM) - 0.5f * srad[i], yc + c * rm, s * rm);
                        tip = new ObjectFoliage.V(xp - tilt * c * r1 - 0.5f * thick - 0.3f * sr1[i], yc + c * r1, s * r1);
                    }
                    if (start.Y < -0.95f) continue;
                    if (tip.Y < -1.05f) tip = new ObjectFoliage.V(tip.X, -1.05f, tip.Z);
                    Root(ref w, ref jr, start, tip, srad[i], age, mid);
                }
                for (int i = 0; i < lumps; i++)
                {
                    if (lod == 1 && (i & 1) == 1) continue;
                    float c = lc[i], s = ls[i], rho = lrho[i], rimR = RimAt(rim, c, s);
                    float rr = lside[i] == 2 ? rp * rimR - 0.7f * lsize[i] : rp * rho * (1 + (rimR - 1) * rho * rho);
                    float x = lside[i] == 0 ? bulge * (1 - rho * rho) : lside[i] == 1 ? -thick * (1 - 0.7f * rho * rho) : -0.3f * thick * rho;
                    var pos = new ObjectFoliage.V(xp - tilt * c * rr + x, yc + c * rr, s * rr);
                    if (pos.Y < -0.9f) continue;
                    var up = lside[i] == 0 ? ex : lside[i] == 1 ? -ex : new ObjectFoliage.V(0, c, s);
                    Lump(ref w, ref jr, pos, up, lsize[i], lstone[i] ? CodeStone : CodeSoil, lstone[i] ? 0.05f : 0.15f, Math.Max(0.1f, Contact(pos.Y)), age);
                }
            }
            float rpit = rp * pitScale, pcx = xp - thick - 0.55f * rpit;
            var pitRho = new[] { 0.55f, 1f, 1.5f, 1.45f };
            var pitY = new[] { -0.94f, 0f, PitRimY, PitRimY - PitSkirt };
            var pit = new int[4][];
            for (int q = 0; q < 4; q++)
            {
                pit[q] = new int[seg];
                for (int k = 0; k < seg; k++)
                {
                    Ring(seg, k, out float c, out float s);
                    float rr = rpit * pitRho[q] * (1 + 0.5f * pitRim.At(c, s));
                    float y = q == 1 ? -1 + rimRise * (1 + pitRim.At(s, c)) + jr.Range(-0.05f, 0.05f) : pitY[q];
                    float blend = q == 0 ? 0.25f : q == 1 ? 0.55f : 1f, dark = q == 0 ? 1f : q == 1 ? 0.35f : q == 2 ? 0f : Contact(y);
                    pit[q][k] = w.Add(new ObjectFoliage.V(pcx + 0.7f * rr * c, y, rr * s), CodeSoil, blend, dark, age);
                }
            }
            int pc = w.Add(new ObjectFoliage.V(pcx, -0.96f, 0), CodeSoil, 0.25f, 1f, age);
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                w.Tri(pc, pit[0][k], pit[0][k1], ey);
                w.Quad(pit[0][k], pit[0][k1], pit[1][k1], pit[1][k], ey);
                w.Quad(pit[1][k], pit[1][k1], pit[2][k1], pit[2][k], ey);
                Ring(seg, k, out float c0, out float s0);
                Ring(seg, k1, out float c1, out float s1);
                w.Quad(pit[2][k], pit[2][k1], pit[3][k1], pit[3][k], new ObjectFoliage.V(c0 + c1, 0, s0 + s1));
            }
        }

        static float RimAt(in Harmonics rim, float c, float s) => Math.Max(0.7f, Math.Min(1.2f, 1 + rim.At(c, s)));

        static float Contact(float y) => 0.9f * Math.Max(0f, Math.Min(1f, (-0.35f - y) / 0.65f));

        static void Dir2(ref Rng rng, out float c, out float s)
        {
            while (true)
            {
                c = rng.Range(-1f, 1f);
                s = rng.Range(-1f, 1f);
                float l = c * c + s * s;
                if (l < 0.04f || l > 1f) continue;
                l = (float)Math.Sqrt(l);
                c /= l;
                s /= l;
                return;
            }
        }

        static void Lump(ref WoodBuilder w, ref Rng jr, ObjectFoliage.V centre, ObjectFoliage.V up, float size, float code, float y, float z, float age)
        {
            up = up.Norm();
            var a = ObjectFoliage.V.Cross(up, Math.Abs(up.Y) < 0.9f ? new ObjectFoliage.V(0, 1, 0) : new ObjectFoliage.V(1, 0, 0)).Norm();
            var b = ObjectFoliage.V.Cross(up, a);
            var dirs = new[] { a, -a, b, -b, up * 0.7f, -up * 0.5f };
            var idx = new int[6];
            for (int i = 0; i < 6; i++) idx[i] = w.Add(centre + dirs[i] * (size * jr.Range(0.65f, 1.3f)), code, y, z, age);
            for (int i = 0; i < 2; i++)
                for (int j = 2; j < 4; j++)
                    for (int p = 4; p < 6; p++)
                        w.Tri(idx[i], idx[j], idx[p], dirs[i] + dirs[j] + dirs[p]);
        }

        static void Root(ref WoodBuilder w, ref Rng jr, ObjectFoliage.V start, ObjectFoliage.V tip, float radius, float age, ObjectFoliage.V mid)
        {
            var d = (tip - start).Norm();
            var e1 = ObjectFoliage.V.Cross(d, new ObjectFoliage.V(0, 1, 0));
            if (e1.Length < 0.1f) e1 = ObjectFoliage.V.Cross(d, new ObjectFoliage.V(1, 0, 0));
            e1 = e1.Norm();
            var e2 = ObjectFoliage.V.Cross(d, e1);
            var tc = new[] { 1f, -0.5f, -0.5f };
            var ts = new[] { 0f, 0.8660254f, -0.8660254f };
            var a = new int[3];
            var b = new int[3];
            float soil = jr.Range(0.3f, 0.8f);
            for (int k = 0; k < 3; k++)
            {
                var off = e1 * tc[k] + e2 * ts[k];
                a[k] = w.Add(start + off * radius, CodeRoot, soil, 0, age);
                b[k] = w.Add(mid + off * (0.5f * radius), CodeRoot, soil, 0, age);
            }
            int t = w.Add(tip, CodeRoot, soil, 0, age);
            for (int k = 0; k < 3; k++)
            {
                int k1 = (k + 1) % 3;
                var hint = e1 * (tc[k] + tc[k1]) + e2 * (ts[k] + ts[k1]);
                w.Quad(a[k], a[k1], b[k1], b[k], hint);
                w.Tri(b[k], b[k1], t, hint + d * 0.3f);
            }
        }

        static ObjectMesh Stump(ulong key, int lod, float aspect, int ends)
        {
            int seg = RingSegments[lod];
            var rng = new Rng(Rng.Mix(key ^ 0x9FB21C651E98DF25UL));
            var jr = new Rng(Rng.Mix(key ^ 0x1B873593CC9E2D51UL ^ (ulong)seg));
            float hgt = 1f / (StumpR0 * Math.Max(aspect, 0.05f));
            var w = WoodBuilder.New(StumpR0, 1f / hgt, StumpR0, 0);
            bool sawn = ends == ObjectPart.EndsSawn, snapped = ends == ObjectPart.EndsSnapped;
            float code = rng.NextFloat() < 0.5f ? CodeOak : CodeSpruce;
            float moss = rng.NextFloat() < 0.5f ? rng.Range(0.4f, 1f) : 0f, age = rng.NextFloat();
            var lobes = Harmonics.Draw(ref rng, 0.5f, false);
            var section = Harmonics.Draw(ref rng, 0.05f, false);
            float tc = rng.Range(-1f, 1f), ts = rng.Range(-1f, 1f);
            float tl = (float)Math.Sqrt(tc * tc + ts * ts);
            if (tl < 1e-3f) { tc = 1; ts = 0; tl = 1; }
            tc /= tl; ts /= tl;
            float top, tilt, toothAmp;
            if (sawn) { top = rng.Range(0.8f, 0.95f) * hgt; tilt = rng.Range(0f, 0.12f); toothAmp = 0; }
            else if (snapped) { top = 0.5f * hgt; tilt = 0.15f * hgt; toothAmp = rng.Range(0.1f, 0.2f) * hgt; }
            else { top = rng.Range(0.6f, 0.75f) * hgt; tilt = rng.Range(0f, 0.08f) * hgt; toothAmp = rng.Range(0.1f, 0.25f) * hgt; }
            var teeth = DrawTeeth(ref rng, sawn, age, toothAmp, snapped ? 0.14f * hgt : 0.1f * hgt, snapped ? 0.28f * hgt : 0.25f * hgt);
            if (snapped)
            {
                float h0 = rng.Range(-0.15f, 0.15f);
                teeth.Spike0 = Frac(Angle(tc, ts) + h0);
                teeth.Spike1 = Frac(Angle(tc, ts) + rng.Range(-0.2f, 0.2f));
                teeth.Spike2 = Frac(Angle(tc, ts) + rng.Range(-0.25f, 0.25f));
                teeth.Spikes = 3;
            }
            var rimJag = Harmonics.Draw(ref rng, 1, true);
            var us = new[] { 0f, 0.2f, 0.5f, 1f };
            var idx = new int[4][];
            var rimH = new float[seg];
            for (int k = 0; k < seg; k++)
            {
                Ring(seg, k, out float c, out float s);
                float jag = sawn ? 0 : 0.35f * toothAmp * (0.5f + 0.5f * rimJag.At(c, s)) + jr.Range(0f, 0.1f) * toothAmp;
                rimH[k] = top + tilt * (c * tc + s * ts) + jag;
            }
            for (int r = 0; r < 4; r++)
            {
                idx[r] = new int[seg];
                float u = us[r], f3 = (1 - u) * (1 - u) * (1 - u);
                for (int k = 0; k < seg; k++)
                {
                    Ring(seg, k, out float c, out float s);
                    float f = 1 + 0.35f * Math.Max(0, Math.Min(1, 0.5f + lobes.At(c, s)));
                    float rad = (1 + (f - 1) * f3) * (1 + section.At(c, s) * u + jr.Range(-0.015f, 0.015f));
                    idx[r][k] = w.Add(new ObjectFoliage.V(c * rad, u * rimH[k], s * rad), code, moss * (0.3f + 0.7f * u), 0, age);
                }
            }
            for (int r = 0; r < 3; r++)
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    Ring(seg, k, out float c0, out float s0);
                    Ring(seg, k1, out float c1, out float s1);
                    w.Quad(idx[r][k], idx[r][k1], idx[r + 1][k1], idx[r + 1][k], new ObjectFoliage.V(c0 + c1, 0, s0 + s1));
                }
            var centre = new ObjectFoliage.V(0, top, 0);
            Cap(ref w, ref jr, idx[3], seg, teeth, centre, new ObjectFoliage.V(0, 1, 0), new ObjectFoliage.V(1, 0, 0), new ObjectFoliage.V(0, 0, 1), 1f, moss, age);
            return w.Finish();
        }

        static float Angle(float c, float s)
        {
            float a = Math.Abs(c) > Math.Abs(s) ? 0.125f * s / Math.Abs(c) : 0.25f - 0.125f * c / Math.Abs(s);
            if (Math.Abs(c) > Math.Abs(s) && c < 0) a = 0.5f - a;
            if (Math.Abs(c) <= Math.Abs(s) && s < 0) a = -a;
            return Frac(a);
        }

        static float Frac(float x) => x - (float)Math.Floor(x);
    }
}
