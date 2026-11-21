using System;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game
{
    public static class LeafAtlas
    {
        public const int Size = 1024, Cell = Size / ObjectFoliage.CellsAcross;
        public const float ShadeRange = 1.5f;

        public sealed class Pixels
        {
            public Color32[][] Mips;
        }

        sealed class Canvas
        {
            public readonly float[] Shade = new float[Size * Size], Hue = new float[Size * Size], Twig = new float[Size * Size], Alpha = new float[Size * Size];
            public int Ox, Oy;
            public uint Seed;

            public void Stroke(float x0, float y0, float x1, float y1, float r0, float r1, float shade)
            {
                float lx = x1 - x0, ly = y1 - y0, len2 = Math.Max(lx * lx + ly * ly, 1e-8f);
                float pad = Math.Max(r0, r1) + 2f / Cell;
                Box(Math.Min(x0, x1) - pad, Math.Min(y0, y1) - pad, Math.Max(x0, x1) + pad, Math.Max(y0, y1) + pad, (x, y, i) =>
                {
                    float s = Math.Max(0, Math.Min(1, ((x - x0) * lx + (y - y0) * ly) / len2));
                    float dx = x - x0 - lx * s, dy = y - y0 - ly * s;
                    float r = r0 + (r1 - r0) * s;
                    float off = (float)Math.Sqrt(dx * dx + dy * dy);
                    float c = Coverage(off - r);
                    if (c <= 0) return;
                    float across = r > 0 ? dx * -ly + dy * lx : 0;
                    float lit = shade * (across > 0 ? 1.08f : 0.86f);
                    Put(i, c, lit, 0.5f, 1);
                });
            }

            public void Leaf(float bx, float by, float dirX, float dirY, float length, float halfWidth, float skew, float round, float serrate, float shade, float hue, float veins) =>
                Leaf(bx, by, dirX, dirY, length, halfWidth, skew, round, serrate, shade, hue, veins, 0, 0);

            public void Leaf(float bx, float by, float dirX, float dirY, float length, float halfWidth, float skew, float round, float serrate, float shade, float hue, float veins, float lobes, float lobeDepth)
            {
                float il = 1f / Math.Max((float)Math.Sqrt(dirX * dirX + dirY * dirY), 1e-6f);
                float fx = dirX * il, fy = dirY * il;
                float pad = halfWidth + 2f / Cell;
                float tx = bx + fx * length, ty = by + fy * length;
                uint seed = Seed++;
                Box(Math.Min(bx, tx) - pad, Math.Min(by, ty) - pad, Math.Max(bx, tx) + pad, Math.Max(by, ty) + pad, (x, y, i) =>
                {
                    float px = x - bx, py = y - by;
                    float s = (px * fx + py * fy) / length;
                    float t = px * -fy + py * fx;
                    if (s < -0.02f || s > 1.02f) return;
                    float sc = Math.Max(0, Math.Min(1, s));
                    float w = halfWidth * (float)Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(sc, skew))), round);
                    if (serrate > 0) w *= 1f - serrate * Math.Abs((float)Math.Sin(sc * 38f));
                    if (lobes > 0)
                    {
                        float ph = (float)Math.PI * (lobes * sc + (t > 0 ? 0.5f : 0f) + 0.25f);
                        float sinus = (float)Math.Pow(Math.Abs(Math.Cos(ph)), 5);
                        w *= 1f - lobeDepth * sinus * Math.Min(1, sc * 5f) * Math.Min(1, (1 - sc) * 4f);
                    }
                    float edge = Math.Abs(t) - w;
                    float ends = Math.Max(-s, s - 1) * length;
                    float c = Coverage(Math.Max(edge, ends));
                    if (c <= 0) return;
                    float across = Math.Abs(t) / Math.Max(w, 1e-5f);
                    float rib = (float)Math.Exp(-across * across * 90f);
                    float lateral = 0;
                    if (veins > 0)
                    {
                        float ph = sc * 9f - across * 2.2f;
                        float f = ph - (float)Math.Floor(ph);
                        lateral = (float)Math.Exp(-(f - 0.5f) * (f - 0.5f) * 140f) * (1 - across) * veins;
                    }
                    float grain = Noise.Gradient(seed, x * 90f, y * 90f) * 0.08f + Noise.Gradient(seed ^ 0x9E37u, x * 260f, y * 260f) * 0.05f;
                    float rim = 0.82f + 0.18f * Math.Min(1, (1 - across) * 4f);
                    float fold = t > 0 ? 1.06f : 0.9f;
                    float lit = shade * fold * rim * (0.84f + 0.2f * sc) * (1 + grain) + 0.1f * rib + 0.06f * lateral;
                    Put(i, c, lit, hue + 0.04f * rib, 0);
                });
            }

            static float Coverage(float edgeCells) => Math.Max(0, Math.Min(1, 0.5f - edgeCells * Cell));

            void Put(int i, float c, float shade, float hue, float twig)
            {
                float keep = 1 - c;
                Shade[i] = Shade[i] * keep + shade * c;
                Hue[i] = Hue[i] * keep + hue * c;
                Twig[i] = Twig[i] * keep + twig * c;
                Alpha[i] = c + Alpha[i] * keep;
            }

            void Box(float x0, float y0, float x1, float y1, Action<float, float, int> paint)
            {
                int ix0 = Math.Max(0, (int)(x0 * Cell)), iy0 = Math.Max(0, (int)(y0 * Cell));
                int ix1 = Math.Min(Cell - 1, (int)(x1 * Cell) + 1), iy1 = Math.Min(Cell - 1, (int)(y1 * Cell) + 1);
                for (int y = iy0; y <= iy1; y++)
                    for (int x = ix0; x <= ix1; x++)
                        paint((x + 0.5f) / Cell, (y + 0.5f) / Cell, (Oy + y) * Size + Ox + x);
            }
        }

        public static Pixels Build()
        {
            var cv = new Canvas();
            for (int cell = 0; cell < ObjectFoliage.CellsAcross * ObjectFoliage.CellsAcross; cell++)
            {
                cv.Ox = (cell % ObjectFoliage.CellsAcross) * Cell;
                cv.Oy = (cell / ObjectFoliage.CellsAcross) * Cell;
                cv.Seed = (uint)(cell * 977 + 13);
                var rng = new Rng(Rng.Mix(0xA7F00D5EEDUL + (ulong)cell));
                if (cell == ObjectFoliage.BroadLeaves) Broad(cv, ref rng);
                else if (cell == ObjectFoliage.Needles) Needles(cv, ref rng);
                else if (cell == ObjectFoliage.ShrubLeaves) Shrub(cv, ref rng);
                else Reeds(cv, ref rng);
            }
            return new Pixels { Mips = Mips(cv) };
        }

        static void Twig(Canvas cv, ref Rng rng, float x0, float y0, float x1, float y1, float bow, float r0, float r1, int pieces, float[] xs, float[] ys)
        {
            float nx = -(y1 - y0), ny = x1 - x0;
            float px = x0, py = y0;
            for (int k = 1; k <= pieces; k++)
            {
                float s = k / (float)pieces;
                float b = bow * 4 * s * (1 - s);
                float qx = x0 + (x1 - x0) * s + nx * b, qy = y0 + (y1 - y0) * s + ny * b;
                cv.Stroke(px, py, qx, qy, r0 + (r1 - r0) * (s - 1f / pieces), r0 + (r1 - r0) * s, rng.Range(0.55f, 0.7f));
                px = qx; py = qy;
            }
            for (int k = 0; k < xs.Length; k++)
            {
                float s = k / (float)(xs.Length - 1);
                float b = bow * 4 * s * (1 - s);
                xs[k] = x0 + (x1 - x0) * s + nx * b;
                ys[k] = y0 + (y1 - y0) * s + ny * b;
            }
        }

        static void Broad(Canvas cv, ref Rng rng)
        {
            var xs = new float[9];
            var ys = new float[9];
            Twig(cv, ref rng, 0.5f, 0.0f, 0.5f, 0.56f, 0.05f, 0.012f, 0.005f, 8, xs, ys);
            for (int b = 0; b < 2; b++)
            {
                var sx = new float[5];
                var sy = new float[5];
                float dir = b == 0 ? -1 : 1;
                int k = 3 + b;
                Twig(cv, ref rng, xs[k], ys[k], xs[k] + dir * 0.18f, ys[k] + 0.2f, -dir * 0.06f, 0.007f, 0.004f, 4, sx, sy);
                OakCluster(cv, ref rng, sx[4], sy[4], dir * 0.75f, 0.75f, 4);
                OakLeaf(cv, ref rng, sx[2], sy[2], -dir * 0.3f, 1, 0.16f);
            }
            for (int k = 1; k < 7; k += 2)
                OakLeaf(cv, ref rng, xs[k], ys[k], (k % 4 == 1 ? -1 : 1) * 0.9f, 0.6f, 0.17f);
            OakCluster(cv, ref rng, xs[8], ys[8], 0.05f, 1, 5);
        }

        static void OakCluster(Canvas cv, ref Rng rng, float x, float y, float dirX, float dirY, int count)
        {
            float baseAng = (float)Math.Atan2(dirX, dirY);
            for (int n = 0; n < count; n++)
            {
                float a = baseAng + (n - (count - 1) * 0.5f) * 0.62f + rng.Range(-0.12f, 0.12f);
                float len = rng.Range(0.19f, 0.24f) * (n == count / 2 ? 1.1f : 1f);
                OakLeaf(cv, ref rng, x, y, (float)Math.Sin(a), (float)Math.Cos(a), len);
            }
        }

        static void OakLeaf(Canvas cv, ref Rng rng, float x, float y, float dx, float dy, float len)
        {
            float il = 1f / (float)Math.Sqrt(dx * dx + dy * dy);
            dx *= il; dy *= il;
            float stalk = 0.018f;
            cv.Stroke(x, y, x + dx * stalk, y + dy * stalk, 0.003f, 0.0025f, 0.6f);
            cv.Leaf(x + dx * stalk, y + dy * stalk, dx, dy, len, len * rng.Range(0.34f, 0.4f), 1.35f, 0.7f, 0, rng.Range(0.76f, 1.04f), rng.Range(0.47f, 0.53f), 0.8f, rng.Range(3.6f, 4.6f), rng.Range(0.5f, 0.62f));
        }

        static void Needles(Canvas cv, ref Rng rng)
        {
            var xs = new float[33];
            var ys = new float[33];
            Twig(cv, ref rng, 0.5f, 0.0f, 0.5f, 0.92f, 0.03f, 0.011f, 0.004f, 10, xs, ys);
            for (int side = 0; side < 2; side++)
            {
                var sx = new float[5];
                var sy = new float[5];
                float at = 0.3f + 0.25f * side;
                int k0 = (int)(at * 32);
                float dir = side == 0 ? -1 : 1;
                Twig(cv, ref rng, xs[k0], ys[k0], xs[k0] + dir * 0.3f, ys[k0] + 0.38f, -dir * 0.05f, 0.006f, 0.003f, 5, sx, sy);
                for (int n = 0; n < 60; n++)
                {
                    float s = rng.Range(0.05f, 1f) * 4f;
                    int a = Math.Min(3, (int)s);
                    float f = s - a;
                    float bx = sx[a] + (sx[a + 1] - sx[a]) * f, by = sy[a] + (sy[a + 1] - sy[a]) * f;
                    float lr = rng.NextFloat() < 0.5f ? -1 : 1;
                    float ang = rng.Range(0.5f, 1.1f);
                    float tx = sx[4] - sx[0], ty = sy[4] - sy[0];
                    float tl = (float)Math.Sqrt(tx * tx + ty * ty);
                    tx /= tl; ty /= tl;
                    float c = (float)Math.Cos(ang), sn = (float)Math.Sin(ang) * lr;
                    cv.Leaf(bx, by, tx * c - ty * sn, tx * sn + ty * c, rng.Range(0.07f, 0.11f), 0.0055f, 1, 0.25f, 0, rng.Range(0.7f, 1.05f), Hue(ref rng) - 0.05f, 0);
                }
            }
            for (int n = 0; n < 150; n++)
            {
                float s = rng.Range(0.04f, 1f);
                float fk = s * 32;
                int a = Math.Min(31, (int)fk);
                float f = fk - a;
                float bx = xs[a] + (xs[a + 1] - xs[a]) * f, by = ys[a] + (ys[a + 1] - ys[a]) * f;
                float lr = rng.NextFloat() < 0.5f ? -1 : 1;
                float ang = rng.Range(0.45f, 1.15f);
                float len = rng.Range(0.1f, 0.15f) * (1 - 0.35f * s);
                cv.Leaf(bx, by, lr * (float)Math.Sin(ang), (float)Math.Cos(ang), len, 0.0065f, 1, 0.25f, 0, rng.Range(0.68f, 1.05f), Hue(ref rng) - 0.05f, 0);
            }
        }

        static void Shrub(Canvas cv, ref Rng rng)
        {
            var main = new float[9];
            var mainY = new float[9];
            Twig(cv, ref rng, 0.5f, 0.0f, 0.48f, 0.78f, -0.05f, 0.011f, 0.004f, 8, main, mainY);
            for (int b = 0; b < 2; b++)
            {
                var bx = new float[6];
                var by = new float[6];
                float dir = b == 0 ? -1 : 1;
                int k = 3 + b;
                Twig(cv, ref rng, main[k], mainY[k], main[k] + dir * 0.3f, mainY[k] + 0.4f, dir * 0.04f, 0.007f, 0.003f, 5, bx, by);
                ShrubLeaves(cv, ref rng, bx, by, 9);
            }
            ShrubLeaves(cv, ref rng, main, mainY, 14);
        }

        static void ShrubLeaves(Canvas cv, ref Rng rng, float[] xs, float[] ys, int count)
        {
            for (int n = 0; n < count; n++)
            {
                float s = rng.Range(0.15f, 1f) * (xs.Length - 1);
                int a = Math.Min(xs.Length - 2, (int)s);
                float f = s - a;
                float bx = xs[a] + (xs[a + 1] - xs[a]) * f, by = ys[a] + (ys[a + 1] - ys[a]) * f;
                float lr = n % 2 == 0 ? -1 : 1;
                float ang = rng.Range(0.35f, 1.1f);
                float len = rng.Range(0.12f, 0.17f);
                cv.Leaf(bx, by, lr * (float)Math.Sin(ang), (float)Math.Cos(ang), len, len * rng.Range(0.26f, 0.32f), 0.85f, 0.75f, 0, rng.Range(0.7f, 1.05f), Hue(ref rng), 0.6f);
            }
        }

        static void Reeds(Canvas cv, ref Rng rng)
        {
            for (int k = 0; k < 15; k++)
            {
                float bx = rng.Range(0.12f, 0.88f), lean = (bx - 0.5f) * rng.Range(0.25f, 0.6f) + rng.Range(-0.08f, 0.08f);
                float len = rng.Range(0.55f, 0.97f);
                cv.Leaf(bx, 0f, lean, 1f, len, rng.Range(0.009f, 0.017f), 0.45f, 0.35f, 0, rng.Range(0.62f, 1f), Hue(ref rng), 0.4f);
            }
            for (int k = 0; k < 3; k++)
            {
                float bx = 0.3f + 0.2f * k + rng.Range(-0.06f, 0.06f), lean = rng.Range(-0.06f, 0.06f), top = rng.Range(0.78f, 0.95f);
                float hx = bx + lean * top, head = rng.Range(0.1f, 0.15f), r = rng.Range(0.014f, 0.019f);
                cv.Leaf(bx, 0f, lean, 1f, top, 0.0045f, 1f, 0.05f, 0, rng.Range(0.7f, 0.9f), Hue(ref rng), 0);
                cv.Stroke(hx - lean * head, top - head, hx, top - 0.01f, r * 0.85f, r, rng.Range(0.5f, 0.68f));
                cv.Stroke(hx, top - 0.01f, hx + lean * 0.05f, Math.Min(0.995f, top + 0.05f), 0.003f, 0.0015f, 0.6f);
            }
        }

        static float Hue(ref Rng rng)
        {
            float h = rng.Range(0.36f, 0.64f);
            return rng.NextFloat() < 0.07f ? rng.Range(0.75f, 0.9f) : h;
        }

        static Color32[][] Mips(Canvas cv)
        {
            int levels = 1;
            for (int s = Size; s > 1; s >>= 1) levels++;
            var mips = new Color32[levels][];
            int n = Size;
            float[] r = cv.Shade, g = cv.Hue, b = cv.Twig, a = cv.Alpha;
            var target = new float[ObjectFoliage.CellsAcross * ObjectFoliage.CellsAcross];
            for (int level = 0; level < levels; level++)
            {
                if (level > 0)
                {
                    int m = n / 2;
                    var r2 = new float[m * m];
                    var g2 = new float[m * m];
                    var b2 = new float[m * m];
                    var a2 = new float[m * m];
                    for (int y = 0; y < m; y++)
                        for (int x = 0; x < m; x++)
                        {
                            int o = y * m + x;
                            float sa = 0, sr = 0, sg = 0, sb = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int i = (2 * y + (k >> 1)) * n + 2 * x + (k & 1);
                                sa += a[i]; sr += r[i] * a[i]; sg += g[i] * a[i]; sb += b[i] * a[i];
                            }
                            float inv = sa > 1e-5f ? 1f / sa : 0;
                            r2[o] = sr * inv; g2[o] = sg * inv; b2[o] = sb * inv; a2[o] = sa * 0.25f;
                        }
                    r = r2; g = g2; b = b2; a = a2; n = m;
                }
                var px = new Color32[n * n];
                int across = ObjectFoliage.CellsAcross, cellN = Math.Max(1, n / across);
                for (int c = 0; c < across * across; c++)
                {
                    int ox = (c % across) * cellN, oy = (c / across) * cellN;
                    if (n < across) { ox = 0; oy = 0; cellN = n; }
                    float scale = 1;
                    if (level == 0) target[c] = Covered(a, n, ox, oy, cellN, 1);
                    else
                    {
                        float lo = 0.25f, hi = 8f;
                        for (int it = 0; it < 18; it++)
                        {
                            float mid = 0.5f * (lo + hi);
                            if (Covered(a, n, ox, oy, cellN, mid) < target[c]) lo = mid; else hi = mid;
                        }
                        scale = 0.5f * (lo + hi);
                    }
                    for (int y = oy; y < oy + cellN; y++)
                        for (int x = ox; x < ox + cellN; x++)
                        {
                            int i = y * n + x;
                            px[i] = new Color32(Byte(r[i] / ShadeRange), Byte(g[i]), Byte(b[i]), Byte(a[i] * scale));
                        }
                    if (n < across) break;
                }
                mips[level] = px;
            }
            return mips;
        }

        static float Covered(float[] a, int n, int ox, int oy, int cellN, float scale)
        {
            int count = 0;
            for (int y = oy; y < oy + cellN; y++)
                for (int x = ox; x < ox + cellN; x++)
                    if (a[y * n + x] * scale >= 0.5f) count++;
            return count / (float)(cellN * cellN);
        }

        static byte Byte(float v) => (byte)Math.Max(0, Math.Min(255, (int)(v * 255f + 0.5f)));

        public static Texture2D Create(Pixels p)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, p.Mips.Length, true)
            {
                name = "Stabby leaf atlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            for (int m = 0; m < p.Mips.Length; m++) t.SetPixels32(p.Mips[m], m);
            t.Apply(false, true);
            return t;
        }
    }
}
