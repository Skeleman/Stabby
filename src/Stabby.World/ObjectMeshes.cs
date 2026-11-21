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

        public static ObjectMesh Build(int shape, ulong key, int lod)
        {
            lod = Math.Max(0, Math.Min(MaxLods - 1, lod));
            if (ObjectPart.IsStructure(shape)) return Structures.Build(shape, key, lod);
            if (shape == ObjectPart.Shrub) return ObjectFoliage.Shrub(key, lod);
            if (shape == ObjectPart.Broadleaf) return ObjectFoliage.Broadleaf(key, lod);
            if (shape == ObjectPart.Conifer) return ObjectFoliage.Conifer(key, lod);
            if (shape == ObjectPart.Log) return Log(key, RingSegments[lod]);
            if (shape == ObjectPart.Stump) return Stump(key, RingSegments[lod]);
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

        static Builder Icosphere(int levels)
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

        static void Tone(List<float> t, float moss, float broken, float age) { t.Add(0); t.Add(moss); t.Add(broken); t.Add(age); }

        static ObjectMesh Log(ulong key, int segments)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x2545F4914F6CDD1DUL));
            var b = Builder.New();
            var tones = new List<float>();
            const int rings = 5;
            float bend = rng.Range(-0.03f, 0.03f), taper = rng.Range(0.8f, 0.95f);
            var wob = new float[segments];
            for (int k = 0; k < segments; k++) wob[k] = rng.Range(0.92f, 1.06f);
            float moss = rng.NextFloat() < 0.55f ? rng.Range(0.5f, 1f) : 0f, age = rng.NextFloat();
            var broken = new bool[] { rng.NextFloat() < 0.5f, rng.NextFloat() < 0.65f };
            var jag = new float[2 * segments];
            for (int end = 0; end < 2; end++)
                for (int k = 0; k < segments; k++)
                    jag[end * segments + k] = broken[end] ? rng.Range(0f, 0.04f) * ((k & 1) == 0 ? 1f : 0.3f) : 0f;
            var tip = new float[] { rng.Range(-0.015f, 0.02f), rng.Range(-0.015f, 0.02f) };
            for (int r = 0; r < rings; r++)
            {
                float u = r / (float)(rings - 1), x = u - 0.5f, rad = 0.5f * (1 + (taper - 1) * u);
                float yo = bend * 4 * u * (1 - u);
                int end = r == 0 ? 0 : r == rings - 1 ? 1 : -1;
                for (int k = 0; k < segments; k++)
                {
                    Ring(segments, k, out float c, out float s);
                    float j = end < 0 ? 0 : jag[end * segments + k], rr = rad * wob[k] * (1 - 6f * j);
                    b.Add(x + (end == 0 ? -j : j), 0.5f + yo + c * rr, s * rr);
                    Tone(tones, moss, 0, age);
                }
            }
            for (int r = 0; r + 1 < rings; r++)
                for (int k = 0; k < segments; k++)
                {
                    int k1 = (k + 1) % segments;
                    b.Quad(r * segments + k, r * segments + k1, (r + 1) * segments + k1, (r + 1) * segments + k);
                }
            for (int end = 0; end < 2; end++)
            {
                int ring = end == 0 ? 0 : rings - 1;
                float u = end, cap = broken[end] ? 1 : 0;
                int center = b.Add(u - 0.5f + (end == 0 ? -tip[end] : tip[end]) * cap, 0.5f, 0);
                Tone(tones, moss, cap, age);
                int first = b.P.Count / 3;
                for (int k = 0; k < segments; k++)
                {
                    int src = (ring * segments + k) * 3;
                    b.Add(b.P[src], b.P[src + 1], b.P[src + 2]);
                    Tone(tones, moss, cap, age);
                }
                for (int k = 0; k < segments; k++)
                {
                    int k1 = (k + 1) % segments;
                    if (end == 0) b.Tri(center, first + k1, first + k);
                    else b.Tri(center, first + k, first + k1);
                }
            }
            var mesh = b.Finish();
            mesh.Tones = tones.ToArray();
            return mesh;
        }

        static ObjectMesh Stump(ulong key, int segments)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x9FB21C651E98DF25UL));
            var b = Builder.New();
            var tones = new List<float>();
            const int rings = 4;
            var top = new float[segments];
            var flare = new float[segments];
            bool broken = rng.NextFloat() < 0.5f;
            float moss = rng.NextFloat() < 0.4f ? rng.Range(0.4f, 1f) : 0f, age = rng.NextFloat();
            float h0 = broken ? rng.Range(0.55f, 0.7f) : rng.Range(0.75f, 0.95f), sx = rng.Range(-0.12f, 0.12f), sz = rng.Range(-0.12f, 0.12f);
            for (int k = 0; k < segments; k++)
            {
                Ring(segments, k, out float c, out float s);
                top[k] = broken ? ((k & 1) == 0 ? rng.Range(0.85f, 1.3f) : rng.Range(0.6f, 0.85f)) : h0 + c * sx + s * sz;
                flare[k] = rng.Range(1f, 1.35f);
            }
            for (int r = 0; r < rings; r++)
            {
                float u = r / (float)(rings - 1);
                for (int k = 0; k < segments; k++)
                {
                    Ring(segments, k, out float c, out float s);
                    float f = 1 + (flare[k] - 1) * (1 - u) * (1 - u) * (1 - u);
                    float rad = 0.5f * f / 1.35f * (broken && r == rings - 1 && (k & 1) == 0 ? 0.88f : 1f);
                    b.Add(c * rad, u * top[k], s * rad);
                    Tone(tones, moss * u, 0, age);
                }
            }
            for (int r = 0; r + 1 < rings; r++)
                for (int k = 0; k < segments; k++)
                {
                    int k1 = (k + 1) % segments;
                    b.Quad(r * segments + k, (r + 1) * segments + k, (r + 1) * segments + k1, r * segments + k1);
                }
            float cap = broken ? 1 : 0;
            int center = b.Add(0, h0, 0);
            Tone(tones, moss, cap, age);
            int first = b.P.Count / 3;
            for (int k = 0; k < segments; k++)
            {
                int src = ((rings - 1) * segments + k) * 3;
                b.Add(b.P[src], b.P[src + 1], b.P[src + 2]);
                Tone(tones, moss, cap, age);
            }
            for (int k = 0; k < segments; k++) b.Tri(center, first + (k + 1) % segments, first + k);
            var mesh = b.Finish();
            mesh.Tones = tones.ToArray();
            return mesh;
        }
    }
}
