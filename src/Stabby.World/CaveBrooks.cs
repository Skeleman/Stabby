using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class CaveBrook
    {
        public const int Spring = 0, Pool = 1, Sink = 2, Stride = 6;
        public int Graph, End, Outlet = -1;
        public float Flow;
        public float[] Points;
        public int[] Nodes;

        public int Rows => Points.Length / Stride;
    }

    public static class CaveBrooks
    {
        public const float RowM = 1f, DepthM = 0.25f, MinRunM = 30f, MinRainMm = 300f, CatchmentM2 = 250000f, Infiltration = 0.3f;
        public const float HalfPerRootFlow = 12f, MinHalfM = 0.35f, MaxHalfM = 1.2f, FloorShare = 0.4f, PoolInsetM = 0.3f, PoolSkimM = 0.02f;
        public const float TaperM = 6f, TaperFloor = 0.2f, KnotM = 7f, WidthSwing = 0.35f, WanderShare = 0.6f, BuryMinM = 0.04f, BuryMaxM = 0.22f, DryM = 0.03f, HugM = 8f, ShrinkBy = 0.7f, Spread = 1.5f;
        const int Raw = 5, Shrinks = 3;
        const float SecondsPerYear = 31557600f;

        public static CaveBrook[] Build(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves)
        {
            var list = new List<CaveBrook>();
            if (caves == null || w.Climate == null) return list.ToArray();
            var floor = new Floor { Ground = new DetailGround { Detail = detail }, Grid = new EditGrid(edits, w.Config.Size * w.Config.CellMeters) };
            for (int gi = 0; gi < caves.Length; gi++)
            {
                var g = caves[gi];
                var order = new List<int>();
                for (int k = 0; k < g.Nodes.Count; k++) if (!g.Nodes[k].Entrance) order.Add(k);
                order.Sort((a, b) => g.Nodes[b].Y.CompareTo(g.Nodes[a].Y) != 0 ? g.Nodes[b].Y.CompareTo(g.Nodes[a].Y) : a.CompareTo(b));
                foreach (int src in order)
                {
                    var s = Trace(w, edits, g, gi, src);
                    if (s == null) continue;
                    var r = Rng.Stream(w.Config.Seed ^ Rng.Mix((ulong)gi), "cave-brooks");
                    s.Points = Shape(s.Points, s.End, floor, ref r);
                    list.Add(s);
                    break;
                }
            }
            return list.ToArray();
        }

        public static int CellOf(GeneratedWorld w, float x, float z)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            int i = Math.Min(n - 1, Math.Max(0, (int)(x / cm + 0.5f))), j = Math.Min(n - 1, Math.Max(0, (int)(z / cm + 0.5f)));
            return j * n + i;
        }

        public static bool Open(GeneratedWorld w, CaveNode n) => !n.Entrance || !w.Sea[CellOf(w, n.X, n.Z)];

        public static int Lower(GeneratedWorld w, CaveGraph g, int at, ICollection<int> seen, out CaveEdge via)
        {
            int best = -1;
            via = null;
            foreach (var e in g.Edges)
            {
                int o = e.A == at ? e.B : e.B == at ? e.A : -1;
                if (o < 0 || seen.Contains(o) || !(g.Nodes[o].Y < g.Nodes[at].Y) || !Open(w, g.Nodes[o])) continue;
                if (best < 0 || g.Nodes[o].Y < g.Nodes[best].Y) { best = o; via = e; }
            }
            return best;
        }

        static CaveBrook Trace(GeneratedWorld w, FeatureEdit[] edits, CaveGraph g, int gi, int src)
        {
            var start = g.Nodes[src];
            float rain = w.Climate.PrecipitationMm[CellOf(w, start.X, start.Z)];
            if (rain < MinRainMm) return null;
            float flow = rain / 1000f * CatchmentM2 * Infiltration / SecondsPerYear;
            float half = Math.Min(MaxHalfM, Math.Max(MinHalfM, HalfPerRootFlow * (float)Math.Sqrt(flow)));
            var pts = new List<float>();
            var nodes = new List<int> { src };
            float surface = float.MaxValue, run = 0f;
            int at = src, end = CaveBrook.Sink;
            FeatureEdit basin = default;
            bool pooled = false;
            while (true)
            {
                if (at != src && Basin(edits, g.Nodes[at], out basin)) { end = CaveBrook.Pool; pooled = true; break; }
                if (at != src && g.Nodes[at].Entrance) { end = CaveBrook.Spring; break; }
                int next = Lower(w, g, at, nodes, out var edge);
                if (next < 0) break;
                float room = 1.183f * Features.CaveHollow * edge.R;
                Leg(pts, Path(g, edge, at), Math.Min(half, FloorShare * room), room, ref surface, ref run);
                nodes.Add(next);
                at = next;
            }
            if (run < MinRunM || pts.Count < 2 * Raw) return null;
            if (pooled) Inlet(pts, basin, ref surface);
            return new CaveBrook
            {
                Graph = gi, End = end, Flow = flow, Points = pts.ToArray(), Nodes = nodes.ToArray(),
                Outlet = end == CaveBrook.Spring ? CellOf(w, g.Nodes[at].X, g.Nodes[at].Z) : -1,
            };
        }

        static bool Basin(FeatureEdit[] edits, CaveNode n, out FeatureEdit basin)
        {
            var rooms = new List<FeatureEdit>();
            foreach (var e in edits)
                if (e.Kind == FeatureEdit.Carve && FeatureEdit.IsRoom(e.Shape) && Underground.Same(e.Ax, e.Az, n.X, n.Z)) rooms.Add(e);
            basin = default;
            if (rooms.Count == 0) return false;
            foreach (var e in edits)
                if (Underground.PoolBasin(rooms, e)) { basin = e; return true; }
            return false;
        }

        static List<float> Path(CaveGraph g, CaveEdge e, int from)
        {
            var a = g.Nodes[from];
            var b = g.Nodes[e.A == from ? e.B : e.A];
            var p = new List<float> { a.X, a.Y, a.Z };
            var q = e.Points;
            if (q != null && q.Length >= 3)
            {
                bool flip = Plan(q[0], q[2], a) > Plan(q[q.Length - 3], q[q.Length - 1], a);
                for (int k = 0; k < q.Length / 3; k++)
                {
                    int i = flip ? q.Length - 3 - 3 * k : 3 * k;
                    p.Add(q[i]); p.Add(q[i + 1]); p.Add(q[i + 2]);
                }
            }
            p.Add(b.X); p.Add(b.Y); p.Add(b.Z);
            return p;
        }

        static float Plan(float x, float z, CaveNode n) => (x - n.X) * (x - n.X) + (z - n.Z) * (z - n.Z);

        static void Leg(List<float> pts, List<float> p, float half, float room, ref float surface, ref float run)
        {
            for (int k = 0; k + 3 < p.Count; k += 3)
            {
                float x0 = p[k], y0 = p[k + 1], z0 = p[k + 2], x1 = p[k + 3], y1 = p[k + 4], z1 = p[k + 5];
                float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                if (len < 1e-3f) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(len / RowM));
                for (int s = pts.Count == 0 ? 0 : 1; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    Row(pts, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t + DepthM, z0 + (z1 - z0) * t, half, room, ref surface);
                }
                run += len;
            }
        }

        static void Row(List<float> pts, float x, float y, float z, float half, float room, ref float surface)
        {
            surface = Math.Min(surface, y);
            pts.Add(x); pts.Add(surface); pts.Add(z); pts.Add(half); pts.Add(room);
        }

        static void Inlet(List<float> pts, FeatureEdit basin, ref float surface)
        {
            int last = pts.Count - Raw;
            float x0 = pts[last], z0 = pts[last + 2], half = pts[last + 3], room = pts[last + 4];
            float dx = basin.Ax - x0, dz = basin.Az - z0, d = (float)Math.Sqrt(dx * dx + dz * dz);
            float stop = Underground.BasinReach(basin) - PoolInsetM, go = d - stop;
            if (go <= 0f) return;
            float level = Math.Min(surface, Underground.BasinLevel(basin) + PoolSkimM), from = surface;
            int steps = Math.Max(1, (int)Math.Ceiling(go / RowM));
            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / steps;
                Row(pts, x0 + dx / d * go * t, from + (level - from) * t, z0 + dz / d * go * t, half, room, ref surface);
            }
        }

        struct Floor
        {
            public DetailGround Ground;
            public EditGrid Grid;

            public float At(float x, float z, float surface, float drop = DepthM + 2f * BuryMaxM)
            {
                return Walk.Surface(Ground, Grid.Cell(x, z), Features.LatticeStepM, x, z, surface + 1f, surface - drop);
            }
        }

        static float[] Knots(int n, ref Rng r)
        {
            var k = new float[n];
            for (int i = 0; i < n; i++) k[i] = r.Range(-1f, 1f);
            return k;
        }

        static float Smooth(float[] knots, float s)
        {
            float u = s / KnotM;
            int i = Math.Min(knots.Length - 2, (int)u);
            float t = Math.Min(1f, u - i);
            t = t * t * (3f - 2f * t);
            return knots[i] + (knots[i + 1] - knots[i]) * t;
        }

        static float Ease(float d)
        {
            float t = Math.Min(1f, d / TaperM);
            return t * t * (3f - 2f * t);
        }

        static float[] Shape(float[] raw, int end, Floor floor, ref Rng r)
        {
            int rows = raw.Length / Raw;
            var along = new float[rows];
            for (int k = 1; k < rows; k++)
            {
                float dx = raw[k * Raw] - raw[(k - 1) * Raw], dz = raw[k * Raw + 2] - raw[(k - 1) * Raw + 2];
                along[k] = along[k - 1] + (float)Math.Sqrt(dx * dx + dz * dz);
            }
            float total = along[rows - 1];
            int n = (int)(total / KnotM) + 2;
            var width = Knots(n, ref r);
            var wander = Knots(n, ref r);
            var pts = new float[rows * CaveBrook.Stride];
            for (int k = 0; k < rows; k++)
            {
                int o = k * Raw, a = Math.Max(k - 1, 0) * Raw, b = Math.Min(k + 1, rows - 1) * Raw;
                float tx = raw[b] - raw[a], tz = raw[b + 2] - raw[a + 2], tl = (float)Math.Sqrt(tx * tx + tz * tz);
                float px = tl > 0f ? -tz / tl : 0f, pz = tl > 0f ? tx / tl : 0f;
                float head = along[k], tail = end == CaveBrook.Pool ? TaperM : total - along[k];
                float ease = Ease(Math.Min(head, tail)), still = Ease(Math.Min(head, total - along[k]));
                float surface = raw[o + 1], room = raw[o + 4];
                float bed = floor.At(raw[o], raw[o + 2], surface, HugM);
                if (bed < surface - DepthM - BuryMaxM && end == CaveBrook.Spring) surface = bed + DepthM;
                if (k > 0) surface = Math.Min(surface, pts[(k - 1) * CaveBrook.Stride + 1]);
                float half = Spread * raw[o + 3] * (TaperFloor + (1f - TaperFloor) * ease) * (1f + WidthSwing * Smooth(width, along[k]));
                float shift = WanderShare * Math.Max(0f, room - raw[o + 3]) * Smooth(wander, along[k]) * still;
                float x = raw[o] + px * shift, z = raw[o + 2] + pz * shift, left = 0f, right = 0f;
                for (int q = 0; q <= Shrinks; q++)
                {
                    left = floor.At(x - px * half, z - pz * half, surface);
                    right = floor.At(x + px * half, z + pz * half, surface);
                    if (q == Shrinks || Math.Max(left, right) < surface - DryM) break;
                    half *= ShrinkBy;
                }
                float low = surface - DepthM - BuryMaxM;
                int p = k * CaveBrook.Stride;
                pts[p] = x; pts[p + 1] = surface; pts[p + 2] = z; pts[p + 3] = half;
                pts[p + 4] = Math.Max(low, Math.Min(left, surface) - r.Range(BuryMinM, BuryMaxM));
                pts[p + 5] = Math.Max(low, Math.Min(right, surface) - r.Range(BuryMinM, BuryMaxM));
            }
            return pts;
        }
    }
}
