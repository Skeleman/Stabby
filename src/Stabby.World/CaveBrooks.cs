using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class CaveBrook
    {
        public const int Spring = 0, Pool = 1, Sink = 2, Plunge = 3, Stride = 6;
        public int Graph, End, Outlet = -1, Fall = -1;
        public float Flow, BasinX, BasinZ, BasinReach, PoolR;
        public float[] Points, Tops;
        public int[] Nodes;

        public int Rows => Points.Length / Stride;
    }

    public static class CaveBrooks
    {
        public const float RowM = 1f, DepthM = 0.25f, MinRunM = 30f, MinRainMm = 300f, CatchmentM2 = 250000f, Infiltration = 0.3f;
        public const float HalfPerRootFlow = 12f, MinHalfM = 0.35f, MaxHalfM = 1.2f, FloorShare = 0.4f, PoolInsetM = 0.3f, PoolSkimM = 0.02f;
        public const float TaperM = 6f, TaperFloor = 0.2f, KnotM = 7f, WidthSwing = 0.35f, WanderShare = 0.6f, BuryMinM = 0.04f, BuryMaxM = 0.22f, HugM = 8f, Spread = 1.5f;
        public const float RecessM = 0.2f, BedM = 0.5f, BankPadM = 0.2f, BankRun = 1f, ChannelRound = 0.2f, EdgeInM = 0.05f, ChannelLumpM = 0.04f, BankOutM = 0.3f, FineStepM = 0.5f, MeshPadM = 0.02f;
        const int Raw = 5, ChannelRows = 2;
        const float SecondsPerYear = 31557600f;

        public const float SinkHoleM = 0.75f, SinkOffM = 1.7f, SinkNarrow = 0.6f, SinkClearM = Underground.ClearSide + SinkHoleM + 0.15f, SinkTopM = 0.15f, SinkLeanM = 1f, SinkDeepM = 3f, SinkLumps = 0.08f, SinkProbeM = 0.5f;
        public const float PlungeChance = 0.5f, PlungeRoomR = 20f, PlungeHoleM = 2.2f, PlungeOffM = 5f, PlungeDropMinM = 3.5f, PlungeDropMaxM = 4.5f, PlungeScanM = 3f, PlungeDeepM = 12f, PlungeStepM = 0.25f, PlungeLipM = 0.25f, PlungeLumpM = 0.15f, PlungeProbeM = 0.5f, PlungeFar = 0.6f, PlungeClearM = Underground.ClearSide + PlungeHoleM + 0.15f;

        public static CaveBrook[] Build(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, CaveGraph[] caves)
        {
            edits = Array.FindAll(edits, e => !Channel(e));
            var list = Traces(w, edits, caves);
            if (list.Count == 0) return list.ToArray();
            var floor = new Floor { Ground = new DetailGround { Detail = detail }, Grid = new EditGrid(edits, w.Config.Size * w.Config.CellMeters) };
            foreach (var s in list)
            {
                var r = Rng.Stream(w.Config.Seed ^ Rng.Mix((ulong)s.Graph), "cave-brooks");
                s.Points = Shape(s.Points, s.End, s.Fall, floor, ref r, out s.Tops);
            }
            return list.ToArray();
        }

        public static FeatureEdit[] Sinks(GeneratedWorld w, FeatureEdit[] edits, CaveGraph[] caves)
        {
            var holes = new List<FeatureEdit>();
            float cm = w.Config.CellMeters;
            foreach (var s in Traces(w, edits, caves))
            {
                var end = caves[s.Graph].Nodes[s.Nodes[s.Nodes.Length - 1]];
                if (s.End == CaveBrook.Plunge)
                {
                    int c = s.Fall * Raw;
                    holes.Add(Shaft(w, s.Graph, s.Points[c], s.Points[c + 2], s.Points[c + 1]));
                    continue;
                }
                if (s.End != CaveBrook.Sink) continue;
                int last = s.Points.Length - Raw;
                float x = s.Points[last], z = s.Points[last + 2], tx = x - end.X, tz = z - end.Z, tl = (float)Math.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-3f) continue;
                tx /= tl; tz /= tl;
                float r = SinkHoleM / Features.CaveHollow;
                var hole = new FeatureEdit
                {
                    Kind = FeatureEdit.Carve, Shape = FeatureEdit.Sink, R = r, N = SinkLumps * r,
                    Ax = x, Ay = end.Y - SinkTopM, Az = z,
                    Bx = x + tx * SinkLeanM, By = end.Y - SinkDeepM, Bz = z + tz * SinkLeanM,
                    S = Rng.Stream(w.Config.Seed ^ Rng.Mix((ulong)s.Graph), "cave-sinks").NextFloat(),
                }.Finer();
                if (Features.Inside(hole, cm)) holes.Add(hole);
            }
            return holes.ToArray();
        }

        static List<CaveBrook> Traces(GeneratedWorld w, FeatureEdit[] edits, CaveGraph[] caves)
        {
            var list = new List<CaveBrook>();
            if (caves == null || w.Climate == null) return list;
            for (int gi = 0; gi < caves.Length; gi++)
            {
                var g = caves[gi];
                if (Mines.IsMine(g) || DungeonLinks.IsLink(g)) continue;
                var order = new List<int>();
                for (int k = 0; k < g.Nodes.Count; k++) if (!g.Nodes[k].Entrance) order.Add(k);
                order.Sort((a, b) => g.Nodes[b].Y.CompareTo(g.Nodes[a].Y) != 0 ? g.Nodes[b].Y.CompareTo(g.Nodes[a].Y) : a.CompareTo(b));
                foreach (int src in order)
                {
                    var s = Trace(w, edits, caves, gi, src);
                    if (s == null) continue;
                    list.Add(s);
                    break;
                }
            }
            return list;
        }

        public static bool Channel(FeatureEdit e) => e.Kind == FeatureEdit.Carve && e.Shape == FeatureEdit.Spring && (e.Bx != e.Ax || e.Bz != e.Az);

        public static FeatureEdit[] Channels(GeneratedWorld w, CaveBrook[] brooks)
        {
            var list = new List<FeatureEdit>();
            foreach (var s in brooks)
            {
                var r = Rng.Stream(w.Config.Seed ^ Rng.Mix((ulong)s.Graph), "cave-brook-channels");
                float seed = r.NextFloat();
                var q = s.Points;
                int rows = s.Rows, st = CaveBrook.Stride;
                float pool = s.End == CaveBrook.Pool ? q[(rows - 1) * st + 1] + PoolSkimM : float.MinValue;
                int stop = s.End == CaveBrook.Plunge ? s.Fall : rows;
                for (int a = 0; a < stop - 1; a += ChannelRows)
                {
                    int b = Math.Min(a + ChannelRows, stop - 1), oa = a * st, ob = b * st;
                    float wa = q[oa + 1], wb = q[ob + 1];
                    float half = Math.Max(EdgeInM, Math.Min(q[oa + 3], q[ob + 3]) - EdgeInM);
                    float rise = Math.Max(s.Tops[a] - wa, s.Tops[b] - wb);
                    float ex = Math.Min(Math.Abs(q[oa] - s.BasinX), Math.Abs(q[ob] - s.BasinX)), ez = Math.Min(Math.Abs(q[oa + 2] - s.BasinZ), Math.Abs(q[ob + 2] - s.BasinZ));
                    float near = s.BasinReach + ObjectField.PoolPadM + half + BankRun * Math.Max(rise, 2f * FeatureEdit.MinMeshSteps * FineStepM) + ChannelRound;
                    float dry = ex < near && ez < near ? pool : float.MinValue;
                    float sink = Math.Max(0f, Math.Min(Math.Min(BedM, half), Math.Min(wa, wb) - dry));
                    rise = Math.Max(rise, 2f * FeatureEdit.MinMeshSteps * FineStepM + MeshPadM - sink);
                    float grow = ChannelRound * (1f - Features.CaveHollow);
                    var e = ObjectField.Disc(FeatureEdit.Carve, q[oa], q[oa + 2], wa + rise, wa - sink - grow, half + BankRun * rise, ChannelRound, -BankRun, 0f, ChannelLumpM, seed, 0f, 0f);
                    e.W += Math.Max(0f, FeatureEdit.MinMeshSteps * FineStepM + MeshPadM - e.Thick);
                    e.Bx = q[ob];
                    e.Bz = q[ob + 2];
                    e.By = e.Ay + wb - wa;
                    list.Add(e.Finer());
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

        static CaveBrook Trace(GeneratedWorld w, FeatureEdit[] edits, CaveGraph[] caves, int gi, int src)
        {
            var g = caves[gi];
            var start = g.Nodes[src];
            float rain = w.Climate.PrecipitationMm[CellOf(w, start.X, start.Z)];
            if (rain < MinRainMm) return null;
            float flow = rain / 1000f * CatchmentM2 * Infiltration / SecondsPerYear;
            float half = Math.Min(MaxHalfM, Math.Max(MinHalfM, HalfPerRootFlow * (float)Math.Sqrt(flow)));
            var pts = new List<float>();
            var nodes = new List<int> { src };
            float surface = float.MaxValue, run = 0f;
            int at = src, end = CaveBrook.Sink, fall = -1;
            FeatureEdit basin = default;
            bool pooled = false;
            while (true)
            {
                if (at != src && Ledge(w, edits, caves, gi, at, pts, ref surface, out fall)) { end = CaveBrook.Plunge; break; }
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
            if (end == CaveBrook.Sink && !Drain(pts, g, g.Nodes[at], ref surface)) return null;
            return new CaveBrook
            {
                Graph = gi, End = end, Flow = flow, Points = pts.ToArray(), Nodes = nodes.ToArray(), Fall = fall, PoolR = fall > 0 ? PlungeHoleM : 0f,
                Outlet = end == CaveBrook.Spring ? CellOf(w, g.Nodes[at].X, g.Nodes[at].Z) : -1,
                BasinX = basin.Ax, BasinZ = basin.Az, BasinReach = pooled ? Underground.BasinReach(basin) : 0f,
            };
        }

        static bool Ledge(GeneratedWorld w, FeatureEdit[] edits, CaveGraph[] caves, int gi, int at, List<float> pts, ref float surface, out int fall)
        {
            fall = -1;
            var g = caves[gi];
            var n = g.Nodes[at];
            float big = 0f, hx = 0f, hz = 0f;
            foreach (var e in edits)
                if (e.Kind == FeatureEdit.Carve && FeatureEdit.IsRoom(e.Shape) && Math.Abs(e.Ax - n.X) < 1e-2f && Math.Abs(e.Az - n.Z) < 1e-2f && e.R > big) { big = e.R; hx = e.Bx - e.Ax; hz = e.Bz - e.Az; }
            if (!n.Hall && big < PlungeRoomR) return false;
            var r = Rng.Stream(w.Config.Seed ^ Rng.Mix(((ulong)gi << 20) ^ (ulong)at), "cave-brook-ledges");
            if (r.NextFloat() >= PlungeChance) return false;
            float drop = r.Range(PlungeDropMinM, PlungeDropMaxM), hl = (float)Math.Sqrt(hx * hx + hz * hz);
            if (hl < 1e-3f) return false;
            hx /= hl; hz /= hl;
            const float Diag = 0.70710678f;
            float[] ways = { hx, hz, Diag * (hx - hz), Diag * (hz + hx), Diag * (hx + hz), Diag * (hz - hx), -hz, hx, hz, -hx };
            float ring = PlungeHoleM + PlungeProbeM, lip = PlungeOffM - PlungeHoleM - PlungeLipM;
            for (int k = 0; k < ways.Length; k += 2)
            {
                float cx = n.X + ways[k] * PlungeOffM, cz = n.Z + ways[k + 1] * PlungeOffM, lx = n.X + ways[k] * lip, lz = n.Z + ways[k + 1] * lip;
                if (Clear(caves, cx, cz) < PlungeClearM) continue;
                float lf = float.NaN;
                if (!Rock(edits, lx, n.Y + PlungeScanM, lz))
                    for (float y = n.Y + PlungeScanM; y >= n.Y - PlungeDeepM; y -= PlungeStepM)
                        if (Rock(edits, lx, y, lz)) { lf = y; break; }
                if (float.IsNaN(lf)) continue;
                float pool = lf - drop, low = pool - PlungeHoleM - PlungeProbeM;
                var sh = Shaft(w, gi, cx, cz, pool);
                if (!Features.Inside(sh, w.Config.CellMeters) || Crowded(edits, sh)) continue;
                bool solid = true;
                for (float y = pool + PlungeProbeM; y >= low && solid; y -= 1f)
                    solid = (y >= pool - PlungeHoleM || Rock(edits, cx, y, cz)) && Rock(edits, cx + ring, y, cz) && Rock(edits, cx - ring, y, cz) && Rock(edits, cx, y, cz + ring) && Rock(edits, cx, y, cz - ring);
                if (!solid) continue;
                int last = pts.Count - Raw;
                float x0 = pts[last], z0 = pts[last + 2], half = pts[last + 3], room = pts[last + 4], go = (float)Math.Sqrt((lx - x0) * (lx - x0) + (lz - z0) * (lz - z0));
                int steps = Math.Max(1, (int)Math.Ceiling(go / RowM));
                for (int t = 1; t <= steps; t++)
                {
                    float f = (float)t / steps;
                    Row(pts, x0 + (lx - x0) * f, lf + DepthM, z0 + (lz - z0) * f, half, room, ref surface);
                }
                fall = pts.Count / Raw;
                Row(pts, cx, pool, cz, half, room, ref surface);
                Row(pts, cx + ways[k] * PlungeFar * PlungeHoleM, pool, cz + ways[k + 1] * PlungeFar * PlungeHoleM, half, room, ref surface);
                return true;
            }
            return false;
        }

        static bool Rock(FeatureEdit[] edits, float x, float y, float z)
        {
            foreach (var e in edits)
                if (e.CaveCarve && e.Apply(1f, x, y, z) < 0f) return false;
            return true;
        }

        public static bool PlungeShaft(FeatureEdit e) => e.Kind == FeatureEdit.Carve && e.Shape == FeatureEdit.Sink && e.R == PlungeHoleM / Features.CaveHollow && e.N == PlungeLumpM;

        public static bool InShaft(IList<FeatureEdit> near, float x, float y, float z)
        {
            foreach (var e in near)
                if (PlungeShaft(e) && e.Apply(1f, x, y, z) < 0f) return true;
            return false;
        }

        static FeatureEdit Shaft(GeneratedWorld w, int gi, float x, float z, float pool)
        {
            float r = PlungeHoleM / Features.CaveHollow;
            return new FeatureEdit
            {
                Kind = FeatureEdit.Carve, Shape = FeatureEdit.Sink, R = r, N = PlungeLumpM,
                Ax = x, Ay = pool + PlungeDropMaxM + PlungeHoleM, Az = z, Bx = x, By = pool, Bz = z,
                S = Rng.Stream(w.Config.Seed ^ Rng.Mix((ulong)gi), "cave-sinks").NextFloat(),
            }.Finer();
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

        static bool Drain(List<float> pts, CaveGraph g, CaveNode n, ref float surface)
        {
            int last = pts.Count - Raw, prev = Math.Max(0, last - 2 * Raw);
            float tx = pts[last] - pts[prev], tz = pts[last + 2] - pts[prev + 2], tl = (float)Math.Sqrt(tx * tx + tz * tz);
            if (tl < 1e-3f) return false;
            tx /= tl; tz /= tl;
            const float Diag = 0.70710678f;
            float[] ways = { tx, tz, Diag * (tx - tz), Diag * (tz + tx), Diag * (tx + tz), Diag * (tz - tx), -tz, tx, tz, -tx };
            for (int k = 0; k < ways.Length; k += 2)
            {
                float hx = n.X + ways[k] * SinkOffM, hz = n.Z + ways[k + 1] * SinkOffM;
                if (Clearance(g, hx, hz) < SinkClearM) continue;
                float x0 = pts[last], z0 = pts[last + 2], half = pts[last + 3], room = pts[last + 4];
                int steps = Math.Max(1, (int)Math.Ceiling(SinkOffM / RowM));
                for (int t = 1; t <= steps; t++)
                {
                    float f = (float)t / steps;
                    Row(pts, x0 + (hx - x0) * f, n.Y + DepthM, z0 + (hz - z0) * f, half + (Math.Min(half, SinkNarrow * SinkHoleM) - half) * f, room, ref surface);
                }
                return true;
            }
            return false;
        }

        static float Clear(CaveGraph[] caves, float x, float z)
        {
            float best = float.MaxValue;
            foreach (var g in caves) best = Math.Min(best, Clearance(g, x, z));
            return best;
        }

        static bool Crowded(FeatureEdit[] edits, FeatureEdit sh)
        {
            float m = PlungeClearM;
            foreach (var e in edits)
                if (e.Kind == FeatureEdit.Carve && e.Shape == FeatureEdit.Spring && e.F == 0f && e.R == Underground.PoolRound && e.T == -Underground.PoolBank && e.MaxX >= sh.MinX - m && e.MinX <= sh.MaxX + m && e.MaxZ >= sh.MinZ - m && e.MinZ <= sh.MaxZ + m && e.MaxY >= sh.MinY - m && e.MinY <= sh.MaxY + m) return true;
            return false;
        }

        static float Clearance(CaveGraph g, float x, float z)
        {
            float best = float.MaxValue;
            foreach (var e in g.Edges)
            {
                var a = g.Nodes[e.A];
                var b = g.Nodes[e.B];
                float px = a.X, pz = a.Z;
                var q = e.Points;
                int m = q == null ? 0 : q.Length / 3;
                for (int k = 0; k <= m; k++)
                {
                    float nx = k < m ? q[3 * k] : b.X, nz = k < m ? q[3 * k + 2] : b.Z;
                    best = Math.Min(best, Segment(x, z, px, pz, nx, nz));
                    px = nx; pz = nz;
                }
            }
            return best;
        }

        static float Segment(float x, float z, float ax, float az, float bx, float bz)
        {
            float ex = bx - ax, ez = bz - az, ll = ex * ex + ez * ez;
            float t = ll > 0f ? Math.Max(0f, Math.Min(1f, ((x - ax) * ex + (z - az) * ez) / ll)) : 0f;
            float dx = x - ax - t * ex, dz = z - az - t * ez;
            return (float)Math.Sqrt(dx * dx + dz * dz);
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

        static float[] Shape(float[] raw, int end, int fall, Floor floor, ref Rng r, out float[] tops)
        {
            int rows = raw.Length / Raw;
            var along = new float[rows];
            for (int k = 1; k < rows; k++)
            {
                float dx = raw[k * Raw] - raw[(k - 1) * Raw], dz = raw[k * Raw + 2] - raw[(k - 1) * Raw + 2];
                along[k] = along[k - 1] + (float)Math.Sqrt(dx * dx + dz * dz);
            }
            float total = along[rows - 1], last = raw[(rows - 1) * Raw + 1], stop = fall > 0 ? along[fall - 1] : total;
            float cx = fall > 0 ? raw[fall * Raw] : 0f, cz = fall > 0 ? raw[fall * Raw + 2] : 0f, rim = PlungeHoleM;
            int n = (int)(total / KnotM) + 2;
            var width = Knots(n, ref r);
            var wander = Knots(n, ref r);
            var pts = new float[rows * CaveBrook.Stride];
            tops = new float[rows];
            for (int k = 0; k < rows; k++)
            {
                int o = k * Raw, a = Math.Max(k - 1, 0) * Raw, b = Math.Min(k + 1, rows - 1) * Raw;
                float tx = raw[b] - raw[a], tz = raw[b + 2] - raw[a + 2], tl = (float)Math.Sqrt(tx * tx + tz * tz);
                float px = tl > 0f ? -tz / tl : 0f, pz = tl > 0f ? tx / tl : 0f;
                float head = along[k], tail = end == CaveBrook.Pool ? TaperM : end == CaveBrook.Plunge ? stop - along[k] + 0.5f * TaperM : total - along[k];
                float ease = Ease(Math.Min(head, tail)), still = Ease(Math.Max(0f, Math.Min(head, stop - along[k])));
                float room = raw[o + 4];
                float half = Spread * raw[o + 3] * (TaperFloor + (1f - TaperFloor) * ease) * (1f + WidthSwing * Smooth(width, along[k]));
                float shift = WanderShare * Math.Max(0f, room - raw[o + 3]) * Smooth(wander, along[k]) * still;
                if (fall > 0)
                {
                    float dx = raw[o] - cx, dz = raw[o + 2] - cz;
                    half = k < fall ? Math.Min(half, 0.5f * rim + 0.5f * (stop - along[k])) : (float)Math.Sqrt(Math.Max(0f, PlungeHoleM * PlungeHoleM - dx * dx - dz * dz));
                }
                int p = k * CaveBrook.Stride;
                pts[p] = raw[o] + px * shift; pts[p + 2] = raw[o + 2] + pz * shift; pts[p + 3] = half;
            }
            for (int k = 0; k < rows; k++)
            {
                int p = k * CaveBrook.Stride, a = Math.Max(k - 1, 0) * CaveBrook.Stride, b = Math.Min(k + 1, rows - 1) * CaveBrook.Stride;
                float tx = pts[b] - pts[a], tz = pts[b + 2] - pts[a + 2], tl = (float)Math.Sqrt(tx * tx + tz * tz);
                float px = tl > 0f ? -tz / tl : 0f, pz = tl > 0f ? tx / tl : 0f;
                float x = pts[p], z = pts[p + 2], half = pts[p + 3], raised = raw[k * Raw + 1], low = float.MaxValue, high = float.MinValue;
                for (int j = 0; j < 5 && (fall < 0 || k < fall); j++)
                {
                    float off = j == 0 ? 0f : (j < 3 ? half : half + BankOutM) * (j % 2 == 1 ? -1f : 1f), sx = x + off * px, sz = z + off * pz;
                    if (fall > 0 && (sx - cx) * (sx - cx) + (sz - cz) * (sz - cz) < (rim + 0.15f) * (rim + 0.15f)) continue;
                    float f = floor.At(sx, sz, raised, HugM);
                    if (f <= raised - HugM || f >= raised + 1f) continue;
                    low = Math.Min(low, f);
                    high = Math.Max(high, f);
                }
                float surface = fall > 0 && k >= fall ? raised : low < float.MaxValue ? low - RecessM : raised - DepthM - RecessM;
                if (k > 0) surface = Math.Min(surface, pts[(k - 1) * CaveBrook.Stride + 1]);
                if (end == CaveBrook.Pool) surface = Math.Max(surface, last);
                pts[p + 1] = surface;
                pts[p + 4] = surface - r.Range(BuryMinM, BuryMaxM);
                pts[p + 5] = surface - r.Range(BuryMinM, BuryMaxM);
                tops[k] = Math.Max(surface + RecessM, high) + BankPadM;
            }
            return pts;
        }
    }
}
