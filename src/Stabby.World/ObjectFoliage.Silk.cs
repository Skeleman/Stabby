using System;

namespace Stabby.World
{
    public static partial class ObjectFoliage
    {
        static readonly int[] WebGrid = { 6, 4, 2, 2 }, WebTube = { 12, 8, 6, 6 };
        static readonly float[] WebKeep = { 1f, 1f, 0.6f, 0.35f };

        public static ObjectMesh SpiderWebs(ulong key, int lod)
        {
            lod = Math.Max(0, Math.Min(WebGrid.Length - 1, lod));
            ulong site = key;
            for (int v = 0; v < 64; v++)
                if (ObjectMeshes.Key("spider-nest", 1, v) == key) { site = ObjectMeshes.Key("spider-nest", 0, v); break; }
            var s = Structures.SpiderBones(site);
            var rng = Rng.Stream(site, "spider-webs");
            var o = Out.Shared();
            int grid = WebGrid[lod];
            float keep = WebKeep[lod];

            for (int k = 0; k < s.Trunks; k++)
            {
                float tr = 0.55f * s.Girth[k];
                int skirts = rng.Range(2, 4);
                for (int j = 0; j < skirts; j++)
                {
                    float phi = s.Ta[k] + rng.Range(-3.1416f, 3.1416f), half = rng.Range(0.5f, 0.9f), spread = rng.Range(0.6f, 1.05f);
                    float h = rng.Range(1f, 3.2f), out0 = rng.Range(2.3f, 4.4f), out1 = out0 * rng.Range(0.75f, 1.25f);
                    var a0 = Around(s.Tx[k], s.Tz[k], phi - half, tr, h + rng.Range(-0.25f, 0.25f));
                    var a1 = Around(s.Tx[k], s.Tz[k], phi + half, tr, h + rng.Range(-0.25f, 0.25f));
                    var b0 = Around(s.Tx[k], s.Tz[k], phi - spread, out0, rng.Range(-0.1f, 0.02f));
                    var b1 = Around(s.Tx[k], s.Tz[k], phi + spread, out1, rng.Range(-0.1f, 0.02f));
                    float sag = rng.Range(0.12f, 0.3f) * h;
                    if (rng.NextFloat() < keep) Web(o, ref rng, a0, a1, b0, b1, sag, grid);
                    else Skip(ref rng);
                }
                int r = Nearest(s, k);
                float gx = s.Rx[r] - s.Tx[k], gz = s.Rz[r] - s.Tz[k], gap = (float)Math.Sqrt(gx * gx + gz * gz), rr = 0.25f * (s.Rw[r] + s.Rdep[r]);
                if (gap - tr - rr < 4.5f)
                {
                    float toward = Turn(gx, gz), back = toward + 3.1416f, half = rng.Range(0.45f, 0.8f), h = rng.Range(1.6f, 3f);
                    var a0 = Around(s.Tx[k], s.Tz[k], toward - half, tr, h);
                    var a1 = Around(s.Tx[k], s.Tz[k], toward + half, tr, h + rng.Range(-0.3f, 0.3f));
                    var b0 = Around(s.Rx[r], s.Rz[r], back + 0.6f, rr * 0.8f, s.Rt[r] * rng.Range(0.6f, 0.9f));
                    var b1 = Around(s.Rx[r], s.Rz[r], back - 0.6f, rr * 0.8f, s.Rt[r] * rng.Range(0.6f, 0.9f));
                    float sag = rng.Range(0.3f, 0.55f);
                    if (rng.NextFloat() < keep) Web(o, ref rng, a0, a1, b0, b1, sag, grid);
                    else Skip(ref rng);
                }
            }

            for (int k = 0; k < s.Rocks; k++)
            {
                float rr = 0.25f * (s.Rw[k] + s.Rdep[k]);
                int drapes = rng.Range(1, 3);
                for (int j = 0; j < drapes; j++)
                {
                    float phi = rng.Range(0, 6.2832f), half = rng.Range(0.5f, 0.85f), spread = rng.Range(0.7f, 1.15f), out0 = rr + rng.Range(1.8f, 3.1f);
                    var a0 = Around(s.Rx[k], s.Rz[k], phi - half, rr * 0.75f, s.Rt[k] * rng.Range(0.75f, 1f));
                    var a1 = Around(s.Rx[k], s.Rz[k], phi + half, rr * 0.75f, s.Rt[k] * rng.Range(0.75f, 1f));
                    var b0 = Around(s.Rx[k], s.Rz[k], phi - spread, out0, rng.Range(-0.1f, 0.02f));
                    var b1 = Around(s.Rx[k], s.Rz[k], phi + spread, out0 * rng.Range(0.8f, 1.2f), rng.Range(-0.1f, 0.02f));
                    if (rng.NextFloat() < keep) Web(o, ref rng, a0, a1, b0, b1, rng.Range(0.1f, 0.25f), grid);
                    else Skip(ref rng);
                }
            }

            for (int k = 0; k < s.Trunks; k++)
            {
                int n = (k + 1) % s.Trunks;
                float dx = s.Tx[n] - s.Tx[k], dz = s.Tz[n] - s.Tz[k], len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len > 6.5f) { Skip(ref rng); continue; }
                float t0 = Turn(dx, dz), h0 = rng.Range(0.9f, 1.9f), h1 = rng.Range(0.9f, 1.9f), w = rng.Range(0.6f, 1.05f);
                var a0 = Around(s.Tx[k], s.Tz[k], t0 + 1.5708f, 0.55f * s.Girth[k], h0);
                var a1 = Around(s.Tx[k], s.Tz[k], t0 + 1.5708f, 0.55f * s.Girth[k], h0 + w);
                var b0 = Around(s.Tx[n], s.Tz[n], t0 + 1.5708f, 0.55f * s.Girth[n], h1);
                var b1 = Around(s.Tx[n], s.Tz[n], t0 + 1.5708f, 0.55f * s.Girth[n], h1 + w);
                float sag = rng.Range(0.35f, 0.6f) + 0.05f * len;
                if (rng.NextFloat() < keep) Web(o, ref rng, a0, b0, a1, b1, sag, grid);
                else Skip(ref rng);
            }

            int funnels = rng.Range(2, 5);
            for (int j = 0; j < funnels; j++)
            {
                int k = j % s.Trunks;
                float phi = s.Ta[k] + 3.1416f + rng.Range(-1.4f, 1.4f), tr = 0.5f * s.Girth[k];
                var mouth = Around(s.Tx[k], s.Tz[k], phi, tr, rng.Range(0.2f, 0.45f));
                var lip = Around(s.Tx[k], s.Tz[k], phi + rng.Range(-0.2f, 0.2f), tr + rng.Range(1.4f, 2.1f), rng.Range(0.18f, 0.3f));
                float apron = rng.Range(1.6f, 2.4f);
                if (rng.NextFloat() < keep) Funnel(o, ref rng, mouth, lip, apron, WebTube[lod], Math.Max(2, grid - 1));
                else Skip(ref rng);
            }

            return o.Finish(Structures.SpiderU);
        }

        static void Skip(ref Rng rng) => rng.NextU64();

        static int Nearest(Structures.SpiderFrame s, int k)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int r = 0; r < s.Rocks; r++)
            {
                float dx = s.Rx[r] - s.Tx[k], dz = s.Rz[r] - s.Tz[k], d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        static float Turn(float x, float z)
        {
            float best = 0, bd = float.MinValue, len = Math.Max((float)Math.Sqrt(x * x + z * z), 1e-5f);
            for (int i = 0; i < 64; i++)
            {
                float a = i * (6.2832f / 64f), d = (x * Structures.TurnCos(a) + z * Structures.TurnSin(a)) / len;
                if (d > bd) { bd = d; best = a; }
            }
            return best;
        }

        static V Around(float cx, float cz, float a, float r, float y) => new V(cx + r * Structures.TurnCos(a), y, cz + r * Structures.TurnSin(a));

        static void Cell(ref Rng rng, out float u0, out float v0, out float us, out float vs)
        {
            float du = 1f / CellsAcross, e = 0.004f, span = du - 2 * e;
            u0 = (Silk % CellsAcross) * du + e;
            v0 = (Silk / CellsAcross) * du + e;
            us = span; vs = span;
            if (rng.NextFloat() < 0.5f) { u0 += span; us = -span; }
            if (rng.NextFloat() < 0.5f) { v0 += span; vs = -span; }
        }

        static void Web(Out o, ref Rng rng, V a0, V a1, V b0, V b1, float sag, int grid)
        {
            Cell(ref rng, out float u0, out float v0, out float us, out float vs);
            float billow = rng.Range(-0.12f, 0.12f), twist = rng.Range(-0.08f, 0.08f), phase = rng.NextFloat(), flutterPhase = rng.NextFloat();
            float tag = 1f + rng.Range(-0.05f, 0.05f), ao = rng.Range(0.6f, 0.72f);
            int nu = grid, nv = Math.Max(2, grid - 1), first = o.Count;
            var pts = new V[(nu + 1) * (nv + 1)];
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float su = i / (float)nu, tv = j / (float)nv, mid = 4 * su * (1 - su), along = 4 * tv * (1 - tv);
                    var top = a0 + (a1 - a0) * su;
                    var bot = b0 + (b1 - b0) * su;
                    var p = top + (bot - top) * tv;
                    p = p + new V(0, -sag * along * (0.55f + 0.45f * mid), 0);
                    var across = (a1 - a0) + (b1 - b0);
                    var side = V.Cross(across, bot - top).Norm();
                    p = p + side * (billow * mid * along + twist * (su - 0.5f) * along);
                    pts[j * (nu + 1) + i] = p;
                }
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    var du = pts[j * (nu + 1) + Math.Min(nu, i + 1)] - pts[j * (nu + 1) + Math.Max(0, i - 1)];
                    var dv = pts[Math.Min(nv, j + 1) * (nu + 1) + i] - pts[Math.Max(0, j - 1) * (nu + 1) + i];
                    var n = V.Cross(du, dv).Norm();
                    if (n.Y < 0) n = -n;
                    n = (n + new V(0, 0.4f, 0)).Norm();
                    float su = i / (float)nu, tv = j / (float)nv, free = 4 * su * (1 - su) * 4 * tv * (1 - tv);
                    o.Add(pts[j * (nu + 1) + i], n, u0 + us * su, v0 + vs * tv, ao, tag, 0.12f * free, 0.15f + 0.5f * free, phase, flutterPhase);
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = first + j * (nu + 1) + i;
                    o.Tri(a, a + 1, a + nu + 2);
                    o.Tri(a, a + nu + 2, a + nu + 1);
                }
        }

        static void Funnel(Out o, ref Rng rng, V mouth, V lip, float apron, int segs, int rings)
        {
            Cell(ref rng, out float u0, out float v0, out float us, out float vs);
            float phase = rng.NextFloat(), flutterPhase = rng.NextFloat(), tag = 1f + rng.Range(-0.05f, 0.05f), ao = rng.Range(0.6f, 0.72f);
            var axis = lip - mouth;
            var fwd = new V(axis.X, 0, axis.Z).Norm();
            var side = new V(-fwd.Z, 0, fwd.X);
            int total = 2 * rings, first = o.Count;
            float r0 = rng.Range(0.05f, 0.08f), r1 = rng.Range(0.45f, 0.65f);
            for (int j = 0; j <= total; j++)
            {
                float t = j / (float)total, r, squash;
                V c;
                if (j <= rings)
                {
                    float w = j / (float)rings;
                    c = mouth + axis * w;
                    r = r0 + (r1 - r0) * w * w;
                    squash = 1f;
                }
                else
                {
                    float w = (j - rings) / (float)rings;
                    c = lip + fwd * (0.4f * apron * w) + new V(0, -lip.Y * w, 0);
                    r = r1 + (apron - r1) * w;
                    squash = 1f - 0.85f * w;
                }
                for (int i = 0; i <= segs; i++)
                {
                    int q = (i * 16 / segs) & 15;
                    float cs = Structures.Cos16(q), sn = Structures.Sin16(q);
                    var p = c + side * (r * cs) + new V(0, r * sn * squash, 0);
                    if (p.Y < -0.08f) p = new V(p.X, -0.08f, p.Z);
                    var n = (side * cs + new V(0, sn + 0.4f, 0)).Norm();
                    float free = j <= rings ? 0.3f * t : t;
                    o.Add(p, n, u0 + us * (i / (float)segs), v0 + vs * t, ao, tag, 0.08f * free, 0.15f + 0.4f * free, phase, flutterPhase);
                }
            }
            for (int j = 0; j < total; j++)
                for (int i = 0; i < segs; i++)
                {
                    int a = first + j * (segs + 1) + i;
                    o.Tri(a, a + 1, a + segs + 2);
                    o.Tri(a, a + segs + 2, a + segs + 1);
                }
        }
    }
}
