using System;
using System.Collections.Generic;
using V = Stabby.World.ObjectFoliage.V;

namespace Stabby.World
{
    public static partial class ObjectMeshes
    {
        public const int SweptMinPoints = 3, SweptMaxPoints = 6;
        public const float SweptBump = 0.08f, SweptJitter = 0.03f, SweptTipRound = 1.2f, SweptBaseDome = 0.15f, LensBump = 0.06f, LensLobe = 0.4f;
        static readonly int[] SweptSub = { 4, 3, 2, 1 };

        public static ObjectMesh Swept(ulong key, float[] path, float baseR, float tipR, int lod, float kink = 0, float twist = 0, float tone = CodeOak)
        {
            int n = path.Length / 3;
            if (path.Length % 3 != 0 || n < SweptMinPoints || n > SweptMaxPoints) throw new ArgumentException("swept path needs 3 to 6 points");
            lod = Math.Max(0, Math.Min(MaxLods - 1, lod));
            var ctl = new V[n];
            for (int i = 0; i < n; i++) ctl[i] = new V(path[3 * i], path[3 * i + 1], path[3 * i + 2]);
            var kr = Rng.Stream(key, "kit-swept-kink");
            var axis = (ctl[n - 1] - ctl[0]).Norm();
            var side = Perp(axis);
            var lift = V.Cross(axis, side);
            for (int i = 1; i < n - 1; i++)
            {
                float span = 0.5f * ((ctl[i] - ctl[i - 1]).Length + (ctl[i + 1] - ctl[i]).Length);
                float u = kr.Range(-1f, 1f), w = kr.Range(-1f, 1f);
                ctl[i] = ctl[i] + (side * u + lift * w) * (kink * span);
            }
            float spin = twist * Rng.Stream(key, "kit-swept-twist").Range(-1f, 1f);
            int sub = SweptSub[lod];
            var c = new List<V>((n - 1) * sub + 1);
            for (int i = 0; i < n - 1; i++)
            {
                V p0 = ctl[Math.Max(0, i - 1)], p1 = ctl[i], p2 = ctl[i + 1], p3 = ctl[Math.Min(n - 1, i + 2)];
                for (int j = 0; j < sub; j++) c.Add(CatmullRom(p0, p1, p2, p3, j / (float)sub));
            }
            c.Add(ctl[n - 1]);
            int rings = c.Count;
            var along = new float[rings];
            for (int r = 1; r < rings; r++) along[r] = along[r - 1] + (c[r] - c[r - 1]).Length;
            float total = Math.Max(1e-4f, along[rings - 1]);
            var br = Rng.Stream(key, "kit-swept-bark");
            var harm = new float[n * 4];
            for (int i = 0; i < harm.Length; i++) harm[i] = br.Range(-1f, 1f);
            var jr = Rng.Stream(key, "kit-swept-grain");
            int seg = RingSegments[lod];
            var b = Builder.New();
            var a = Perp((c[1] - c[0]).Norm());
            V d = default;
            for (int r = 0; r < rings; r++)
            {
                d = (r == 0 ? c[1] - c[0] : r == rings - 1 ? c[r] - c[r - 1] : c[r + 1] - c[r - 1]).Norm();
                a = (a - d * V.Dot(a, d)).Norm();
                if (r > 0 && spin != 0) a = (a + V.Cross(d, a) * (spin * (along[r] - along[r - 1]))).Norm();
                var e = V.Cross(d, a);
                float t = along[r] / total, radius = baseR + (tipR - baseR) * t, f = t * (n - 1);
                int k0 = Math.Min(n - 2, (int)f);
                float m = f - k0;
                m = m * m * (3 - 2 * m);
                float a2 = Lerp(harm[k0 * 4], harm[k0 * 4 + 4], m), b2 = Lerp(harm[k0 * 4 + 1], harm[k0 * 4 + 5], m);
                float a3 = Lerp(harm[k0 * 4 + 2], harm[k0 * 4 + 6], m), b3 = Lerp(harm[k0 * 4 + 3], harm[k0 * 4 + 7], m);
                for (int k = 0; k < seg; k++)
                {
                    Ring(seg, k, out float cs, out float sn);
                    float c2 = cs * cs - sn * sn, s2 = 2 * cs * sn, c3 = cs * c2 - sn * s2, s3 = sn * c2 + cs * s2;
                    float rr = radius * (1 + SweptBump * (a2 * c2 + b2 * s2 + 0.6f * (a3 * c3 + b3 * s3)) + jr.Range(-SweptJitter, SweptJitter));
                    var p = c[r] + (a * cs + e * sn) * rr;
                    b.Add(p.X, p.Y, p.Z);
                }
            }
            for (int r = 0; r + 1 < rings; r++)
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg, i0 = r * seg + k, i1 = r * seg + k1;
                    b.Tri(i0, i1, i0 + seg);
                    b.Tri(i1, i1 + seg, i0 + seg);
                }
            var d0 = (c[1] - c[0]).Norm();
            var foot = c[0] - d0 * (SweptBaseDome * baseR);
            int centre = b.Add(foot.X, foot.Y, foot.Z);
            for (int k = 0; k < seg; k++) b.Tri(centre, (k + 1) % seg, k);
            var tip = c[rings - 1] + d * (SweptTipRound * Math.Max(tipR, 1e-3f));
            int apex = b.Add(tip.X, tip.Y, tip.Z), last = (rings - 1) * seg;
            for (int k = 0; k < seg; k++) b.Tri(last + k, last + (k + 1) % seg, apex);
            var mesh = b.Finish();
            mesh.Tones = new float[4 * mesh.VertexCount];
            for (int v = 0; v < mesh.VertexCount; v++) mesh.Tones[4 * v] = tone;
            return mesh;
        }

        public static ObjectMesh Lens(ulong key, float rx, float ry, float rz, int lod)
        {
            lod = Math.Max(0, Math.Min(MaxLods - 1, lod));
            var rng = Rng.Stream(key, "kit-lens");
            var lobes = new float[3 * 4];
            for (int k = 0; k < 3; k++)
            {
                Unit(ref rng, out lobes[k * 4], out lobes[k * 4 + 1], out lobes[k * 4 + 2]);
                lobes[k * 4 + 3] = rng.Range(-1f, 1f);
            }
            var b = Icosphere(RockLevels[lod]);
            int count = b.P.Count / 3;
            float norm = 1f / ((1 - LensLobe) * (1 - LensLobe));
            for (int v = 0; v < count; v++)
            {
                float x = b.P[v * 3], y = b.P[v * 3 + 1], z = b.P[v * 3 + 2], r = 1;
                for (int k = 0; k < 3; k++)
                {
                    float dot = x * lobes[k * 4] + y * lobes[k * 4 + 1] + z * lobes[k * 4 + 2] - LensLobe;
                    if (dot > 0) r += LensBump * lobes[k * 4 + 3] * dot * dot * norm;
                }
                b.P[v * 3] = x * r * rx; b.P[v * 3 + 1] = y * r * ry; b.P[v * 3 + 2] = z * r * rz;
            }
            return b.Finish();
        }

        static V Perp(V d) => V.Cross(d, Math.Abs(d.Y) < 0.9f ? new V(0, 1, 0) : new V(1, 0, 0)).Norm();

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static V CatmullRom(V p0, V p1, V p2, V p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (p1 * 2 + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t2 + (p1 * 3 - p0 - p2 * 3 + p3) * t3) * 0.5f;
        }
    }
}
