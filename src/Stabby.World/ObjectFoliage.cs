using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class ObjectFoliage
    {
        public const int CellsAcross = 2;
        public const int BroadLeaves = 0, Needles = 1, ShrubLeaves = 2, RoundLeaves = 3;
        public const float TrunkRadius = 0.022f;
        static readonly float[] SideCos = { -0.5f, 0f, 0.30901699f, 0.5f, 0.6234898f, 0.70710678f };
        static readonly float[] SideSin = { 0.8660254f, 1f, 0.95105652f, 0.8660254f, 0.7818315f, 0.70710678f };

        public struct V
        {
            public float X, Y, Z;
            public V(float x, float y, float z) { X = x; Y = y; Z = z; }
            public static V operator +(V a, V b) => new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V operator -(V a, V b) => new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V operator -(V a) => new V(-a.X, -a.Y, -a.Z);
            public static V operator *(V a, float s) => new V(a.X * s, a.Y * s, a.Z * s);
            public static float Dot(V a, V b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static V Cross(V a, V b) => new V(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            public V Norm() { float l = Length; return l > 1e-6f ? this * (1f / l) : new V(0, 1, 0); }
            public float Flat => (float)Math.Sqrt(X * X + Z * Z);
        }

        sealed class Limb
        {
            public readonly List<V> P = new List<V>();
            public readonly List<float> R = new List<float>();
            public float Bend0, Bend1, Phase;
            public int Depth;
            public float Bend(float t) => Bend0 + (Bend1 - Bend0) * t * (float)Math.Sqrt(t);
            public V At(float t, out V dir)
            {
                float f = t * (P.Count - 1);
                int i = Math.Min(P.Count - 2, (int)f);
                dir = (P[i + 1] - P[i]).Norm();
                return P[i] + (P[i + 1] - P[i]) * (f - i);
            }
            public float Radius(float t)
            {
                float f = t * (R.Count - 1);
                int i = Math.Min(R.Count - 2, (int)f);
                return R[i] + (R[i + 1] - R[i]) * (f - i);
            }
            public float Length()
            {
                float l = 0;
                for (int i = 1; i < P.Count; i++) l += (P[i] - P[i - 1]).Length;
                return l;
            }
        }

        sealed class Style
        {
            public int Cell;
            public float Card, Along, Lift, Jitter, From, AoLow, Width = 1;
            public int MinLeafDepth;
            public bool Uniform, DarkWood;
            public int[] Counts;
            public int[][] Sides;
        }

        static readonly Style ShrubStyle = new Style
        {
            Cell = ShrubLeaves, Card = 0.34f, Along = 0.4f, Lift = 0.3f, Jitter = 0.5f, From = 0.12f, AoLow = 0.3f,
            Counts = new[] { 140, 52, 18 }, Sides = new[] { new[] { 5, 3 }, new[] { 3, 0 }, new[] { 0, 0 } },
        };

        static readonly Style BroadleafStyle = new Style
        {
            Cell = BroadLeaves, Card = 0.075f, Along = 0.3f, Lift = 0.25f, Jitter = 0.6f, From = 0.05f, AoLow = 0.22f, MinLeafDepth = 1, Uniform = true,
            Counts = new[] { 1500, 440, 120 }, Sides = new[] { new[] { 7, 5, 4, 3 }, new[] { 5, 3, 0, 0 }, new[] { 4, 0, 0, 0 } },
        };

        static readonly Style ConiferStyle = new Style
        {
            Cell = Needles, Card = 0.072f, Along = 0.7f, Lift = -0.3f, Jitter = 0.2f, From = 0.22f, AoLow = 0.15f, MinLeafDepth = 1, Width = 1.4f, Uniform = true, DarkWood = true,
            Counts = new[] { 1300, 380, 110 }, Sides = new[] { new[] { 7, 3, 0 }, new[] { 5, 0, 0 }, new[] { 4, 0, 0 } },
        };

        sealed class Out
        {
            public readonly List<float> P = new List<float>(), N = new List<float>(), U = new List<float>(), W = new List<float>();
            public readonly List<int> I = new List<int>();
            public int Count => P.Count / 3;

            public void Add(V p, V n, float u, float v, float ao, float leaf, float bend, float flutter, float phase, float flutterPhase)
            {
                P.Add(p.X); P.Add(p.Y); P.Add(p.Z);
                N.Add(n.X); N.Add(n.Y); N.Add(n.Z);
                U.Add(u); U.Add(v); U.Add(ao); U.Add(leaf);
                W.Add(bend); W.Add(flutter); W.Add(phase); W.Add(flutterPhase);
            }

            public void Tri(int a, int b, int c) { I.Add(a); I.Add(b); I.Add(c); }

            public ObjectMesh Finish(float scale)
            {
                var pos = P.ToArray();
                for (int i = 0; i < pos.Length; i++) pos[i] *= scale;
                var uv = U.ToArray();
                for (int i = 0; i < uv.Length; i += 4) if (uv[i + 3] < 0.5f) { uv[i] *= scale; uv[i + 1] *= scale; }
                return new ObjectMesh { Positions = pos, Normals = N.ToArray(), Indices = I.ToArray(), Uvs = uv, Winds = W.ToArray() };
            }
        }

        static V Unit(ref Rng rng)
        {
            while (true)
            {
                var v = new V(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                float l = V.Dot(v, v);
                if (l > 1e-4f && l <= 1f) return v * (1f / (float)Math.Sqrt(l));
            }
        }

        static V Flat(ref Rng rng)
        {
            var u = Unit(ref rng);
            return new V(u.X, 0, u.Z).Norm();
        }

        static V Side(V d)
        {
            var a = Math.Abs(d.Y) < 0.9f ? new V(0, 1, 0) : new V(1, 0, 0);
            return V.Cross(d, a).Norm();
        }

        static V Around(V a, float f)
        {
            var up = new V(0, 1, 0);
            float x = f * 4f;
            int q = ((int)x % 4 + 4) % 4;
            float r = x - (float)Math.Floor(x);
            var d = (a * (1 - r) + V.Cross(up, a) * r).Norm();
            for (int i = 0; i < q; i++) d = V.Cross(up, d);
            return d;
        }

        static void Tube(Out o, Limb l, int sides, bool dark)
        {
            if (sides < 3) return;
            int s = Math.Min(8, sides) - 3;
            float cs = SideCos[s], sn = SideSin[s];
            int rings = l.P.Count, first = o.Count;
            var a = Side((l.P[1] - l.P[0]).Norm());
            float along = 0;
            for (int r = 0; r < rings; r++)
            {
                var d = r == 0 ? (l.P[1] - l.P[0]).Norm() : r == rings - 1 ? (l.P[r] - l.P[r - 1]).Norm() : (l.P[r + 1] - l.P[r - 1]).Norm();
                a = (a - d * V.Dot(a, d)).Norm();
                var b = V.Cross(d, a);
                if (r > 0) along += (l.P[r] - l.P[r - 1]).Length;
                float t = r / (float)(rings - 1), rad = l.R[r], c = 1, si = 0;
                float bend = l.Bend(t), ao = !dark || l.Depth == 0 ? 0.35f + 0.5f * t : 0.15f + 0.3f * t;
                for (int k = 0; k <= sides; k++)
                {
                    var n = a * c + b * si;
                    o.Add(l.P[r] + n * rad, n, k / (float)sides * 6.2831853f * rad, along, ao, 0, bend, 0, l.Phase, 0);
                    float nc = c * cs - si * sn;
                    si = c * sn + si * cs;
                    c = nc;
                }
            }
            int ring = sides + 1;
            for (int r = 0; r + 1 < rings; r++)
                for (int k = 0; k < sides; k++)
                {
                    int i0 = first + r * ring + k, i1 = i0 + 1, j0 = i0 + ring, j1 = j0 + 1;
                    o.Tri(i0, j0, j1);
                    o.Tri(i0, j1, i1);
                }
        }

        static void Card(Out o, V at, V up, V face, float size, float width, int cell, bool flip, V center, float crownR, float bend, float phase, float flutterPhase, float aoLow)
        {
            var right = V.Cross(up, face).Norm() * (0.5f * size * width);
            var lift = up * size;
            var foot = at - up * (0.12f * size);
            float du = 1f / CellsAcross, e = 0.004f;
            float u0 = (cell % CellsAcross) * du + e, u1 = u0 + du - 2 * e;
            float v0 = (cell / CellsAcross) * du + e, v1 = v0 + du - 2 * e;
            if (flip) { float tmp = u0; u0 = u1; u1 = tmp; }
            var corners = new[] { foot - right, foot + right, foot + right + lift, foot - right + lift };
            float[] us = { u0, u1, u1, u0 }, vs = { v0, v0, v1, v1 }, fl = { 0.15f, 0.15f, 1f, 1f };
            int first = o.Count;
            for (int k = 0; k < 4; k++)
            {
                var p = corners[k];
                var radial = (p - center).Norm();
                var f = V.Dot(face, radial) < 0 ? -face : face;
                var n = (f * 0.35f + radial * 0.65f).Norm();
                float depth = Math.Min(1f, (p - center).Length / Math.Max(1e-3f, crownR));
                o.Add(p, n, us[k], vs[k], aoLow + (1f - aoLow) * depth * depth, 1, bend, fl[k], phase, flutterPhase);
            }
            o.Tri(first, first + 1, first + 2);
            o.Tri(first, first + 2, first + 3);
        }

        static Limb Grow(ref Rng rng, V start, V dir, float length, int points, float r0, float r1, float curl, V pull, float bend0, float bend1, float phase, int depth)
        {
            var l = new Limb { Bend0 = bend0, Bend1 = bend1, Phase = phase, Depth = depth };
            var p = start;
            var d = dir.Norm();
            float step = length / (points - 1);
            for (int i = 0; i < points; i++)
            {
                l.P.Add(p);
                l.R.Add(r0 + (r1 - r0) * (i / (float)(points - 1)));
                d = (d + pull * (step * 1.2f) + Unit(ref rng) * curl).Norm();
                p = p + d * step;
            }
            return l;
        }

        static void Twigs(ref Rng rng, List<Limb> limbs, Limb parent, int count, float tMin, float tMax, float lenMin, float lenMax, float spread, V pull, float tipBend)
        {
            for (int k = 0; k < count; k++)
            {
                float t = rng.Range(tMin, tMax);
                var at = parent.At(t, out var pd);
                var side = V.Cross(pd, Unit(ref rng)).Norm();
                var d = (pd * (1 - spread) + side * spread).Norm();
                limbs.Add(Grow(ref rng, at, d, rng.Range(lenMin, lenMax), 3, parent.Radius(t) * 0.65f, 0.0025f, 0.15f, pull, parent.Bend(t), tipBend, parent.Phase, parent.Depth + 1));
            }
        }

        public static ObjectMesh Shrub(ulong key, int lod)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x6C8E9CF570932BD5UL));
            var limbs = new List<Limb>();
            int stems = rng.Range(6, 10);
            float spread = rng.Range(0.55f, 0.9f);
            for (int s = 0; s < stems; s++)
            {
                var u = Unit(ref rng);
                var d = new V(u.X * spread, Math.Abs(u.Y) * 0.5f + 0.55f, u.Z * spread);
                var start = new V(rng.Range(-0.05f, 0.05f), 0, rng.Range(-0.05f, 0.05f));
                var stem = Grow(ref rng, start, d, rng.Range(0.6f, 1f), 5, 0.022f, 0.006f, 0.12f, new V(0, 0.15f, 0), 0, 1, rng.NextFloat(), 0);
                limbs.Add(stem);
                Twigs(ref rng, limbs, stem, rng.Range(2, 4), 0.35f, 0.8f, 0.22f, 0.4f, 0.6f, new V(0, 0.2f, 0), 1.25f);
            }
            return Leaves(key, lod, limbs, ShrubStyle);
        }

        public static ObjectMesh Broadleaf(ulong key, int lod)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x9B05688C2B3E6C1FUL));
            var limbs = new List<Limb>();
            float bole = rng.Range(0.3f, 0.42f);
            var trunk = Grow(ref rng, new V(0, 0, 0), new V(0, 1, 0), bole, 5, TrunkRadius * 1.25f, TrunkRadius * 0.8f, 0.04f, new V(0, 0.4f, 0), 0, 0.05f, rng.NextFloat(), 0);
            limbs.Add(trunk);
            var top = trunk.P[trunk.P.Count - 1];
            int leaders = rng.Range(2, 4);
            var spin = Flat(ref rng);
            for (int k = 0; k < leaders; k++)
            {
                var side = Around(spin, (k + rng.Range(-0.15f, 0.15f)) / leaders);
                var d = (new V(0, 1, 0) + side * rng.Range(0.5f, 0.9f)).Norm();
                var leader = Grow(ref rng, top, d, rng.Range(0.34f, 0.46f), 5, TrunkRadius * 0.75f, 0.004f, 0.08f, new V(0, 0.25f, 0), 0.05f, 0.35f, rng.NextFloat(), 1);
                limbs.Add(leader);
                int branches = rng.Range(8, 12);
                for (int b = 0; b < branches; b++)
                {
                    float t = 0.12f + 0.85f * (b + rng.NextFloat()) / branches;
                    var at = leader.At(t, out _);
                    var outward = new V(at.X, 0, at.Z);
                    outward = outward.Flat > 0.02f ? outward.Norm() : Flat(ref rng);
                    var bd = (outward + Flat(ref rng) * 0.7f + new V(0, rng.Range(-0.1f, 0.8f) + 0.6f * t, 0)).Norm();
                    float len = rng.Range(0.18f, 0.3f) * (1.2f - 0.6f * t);
                    var branch = Grow(ref rng, at, bd, len, 4, leader.Radius(t) * 0.7f, 0.003f, 0.12f, new V(0, -0.15f, 0), leader.Bend(t), 0.85f, rng.NextFloat(), 2);
                    limbs.Add(branch);
                    Twigs(ref rng, limbs, branch, 5, 0.15f, 0.95f, 0.06f, 0.12f, 0.75f, new V(0, 0.1f, 0), 1.1f);
                }
                Twigs(ref rng, limbs, leader, 4, 0.3f, 0.95f, 0.06f, 0.1f, 0.8f, new V(0, 0.1f, 0), 0.9f);
            }
            for (int b = 0; b < 2; b++)
            {
                float t = rng.Range(0.6f, 0.9f);
                var at = trunk.At(t, out _);
                var bd = (Flat(ref rng) + new V(0, 0.3f, 0)).Norm();
                var low = Grow(ref rng, at, bd, rng.Range(0.18f, 0.26f), 4, TrunkRadius * 0.45f, 0.003f, 0.1f, new V(0, -0.1f, 0), 0.02f, 0.8f, rng.NextFloat(), 2);
                limbs.Add(low);
                Twigs(ref rng, limbs, low, 2, 0.5f, 0.9f, 0.07f, 0.11f, 0.7f, new V(0, 0.1f, 0), 1.1f);
            }
            return Leaves(key, lod, limbs, BroadleafStyle);
        }

        public static ObjectMesh Conifer(ulong key, int lod)
        {
            var rng = new Rng(Rng.Mix(key ^ 0xD1B54A32D192ED03UL));
            var limbs = new List<Limb>();
            var trunk = Grow(ref rng, new V(0, 0, 0), new V(0, 1, 0), 1f, 8, TrunkRadius * 1.1f, 0.003f, 0.012f, new V(0, 0.5f, 0), 0, 0.3f, rng.NextFloat(), 0);
            limbs.Add(trunk);
            float crown = rng.Range(0.26f, 0.33f), start = rng.Range(0.08f, 0.16f);
            int whorls = rng.Range(15, 19);
            var spin = Flat(ref rng);
            for (int w = 0; w < whorls; w++)
            {
                float t = start + (0.97f - start) * (w + rng.Range(-0.25f, 0.25f)) / (whorls - 1);
                t = Math.Max(start, Math.Min(0.97f, t));
                var at = trunk.At(t, out _);
                float left = 1.02f - t;
                float reach = crown * left / (1.02f - start) + 0.015f;
                int count = rng.Range(5, 8);
                spin = Around(spin, rng.Range(0.1f, 0.4f));
                for (int k = 0; k < count; k++)
                {
                    var dir = Around(spin, (k + rng.Range(-0.2f, 0.2f)) / count);
                    float droop = 0.35f + 0.25f * t;
                    var bd = (dir + new V(0, -0.15f - 0.35f * (1 - t), 0)).Norm();
                    var branch = Grow(ref rng, at, bd, reach * rng.Range(0.85f, 1.1f), 4, TrunkRadius * (0.15f + 0.4f * (1 - t)), 0.002f, 0.06f, new V(0, droop, 0), trunk.Bend(t), 0.8f, rng.NextFloat(), 1);
                    limbs.Add(branch);
                    if (reach > 0.05f) Twigs(ref rng, limbs, branch, 3, 0.3f, 0.9f, reach * 0.3f, reach * 0.5f, 0.85f, new V(0, -0.5f, 0), 1f);
                }
            }
            return Leaves(key, lod, limbs, ConiferStyle);
        }

        static ObjectMesh Leaves(ulong key, int lod, List<Limb> limbs, Style st)
        {
            lod = Math.Max(0, Math.Min(st.Counts.Length - 1, lod));
            var weights = new float[limbs.Count];
            float total = 0, maxY = 0, maxR = 0;
            V center = default;
            int samples = 0;
            for (int i = 0; i < limbs.Count; i++)
            {
                foreach (var p in limbs[i].P)
                {
                    maxY = Math.Max(maxY, p.Y);
                    maxR = Math.Max(maxR, p.Flat);
                }
                if (limbs[i].Depth < st.MinLeafDepth) continue;
                weights[i] = limbs[i].Length() * (limbs[i].Depth == 0 ? 0.7f : limbs[i].Depth < st.MinLeafDepth + 1 && st.Uniform ? 0.6f : 1f);
                total += weights[i];
                foreach (var p in limbs[i].P) { center = center + p; samples++; }
            }
            center = center * (1f / Math.Max(1, samples));
            float crownR = 0;
            for (int i = 0; i < limbs.Count; i++)
                if (limbs[i].Depth >= st.MinLeafDepth)
                    foreach (var p in limbs[i].P) crownR = Math.Max(crownR, (p - center).Length);
            crownR += 0.5f * st.Card;
            var o = new Out();
            var sides = st.Sides[lod];
            foreach (var l in limbs) Tube(o, l, sides[Math.Min(sides.Length - 1, l.Depth)], st.DarkWood);
            var rng = new Rng(Rng.Mix(key ^ 0x3C6EF372FE94F82BUL));
            float grow = (float)Math.Sqrt(st.Counts[0] / (float)st.Counts[lod]);
            for (int c = 0; c < st.Counts[0]; c++)
            {
                float pick = rng.NextFloat() * total;
                int li = 0;
                while (li < limbs.Count - 1 && (weights[li] <= 0 || pick > weights[li])) { pick -= weights[li]; li++; }
                var l = limbs[li];
                float t = st.From + (1f - st.From) * (st.Uniform ? rng.NextFloat() : (float)Math.Sqrt(rng.NextFloat()));
                var at = l.At(t, out var dir);
                var outward = new V(at.X - center.X, (at.Y - center.Y) * 0.5f, at.Z - center.Z).Norm();
                var up = (dir * st.Along + outward * (1f - st.Along) + new V(0, st.Lift, 0) + Unit(ref rng) * st.Jitter).Norm();
                var face = V.Cross(up, Unit(ref rng)).Norm();
                float size = st.Card * rng.Range(0.8f, 1.2f) * grow, width = st.Width * rng.Range(0.85f, 1.1f);
                bool flip = rng.NextFloat() < 0.5f;
                float phase = rng.NextFloat();
                if (c >= st.Counts[lod]) continue;
                Card(o, at, up, face, size, width, st.Cell, flip, center, crownR, l.Bend(t), l.Phase, phase, st.AoLow);
            }
            float scale = Math.Min(1f / (maxY + 1.2f * st.Card), 0.5f / (maxR + 1.2f * st.Card));
            return o.Finish(scale);
        }
    }
}
