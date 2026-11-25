using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class CaveClass
    {
        public string Id = "", Fallback = "";
        public float Weight = 1f;
        public int MinNodes = 1, MaxNodes = 1, Loops, Entrances = 1, Bridges;
        public float RadiusMin = 30f, RadiusMax = 38f, EdgeMin = 70f, EdgeMax = 120f, Turn = 0.5f;
        public float ChamberChance, ChamberMin = 1.3f, ChamberMax = 1.6f;
        public float HallMin, HallMax, BridgeMin = 9f, BridgeMax = 12f;
        public float Warp = 0.1f, Lumps = 0.03f, RoomLumps = 0.06f;

        public bool Hall => HallMax > 0f;
    }

    public sealed class CaveLibrary
    {
        public float Chance = 0.35f;
        public List<CaveClass> Classes = new List<CaveClass>();

        public CaveClass Find(string id)
        {
            foreach (var c in Classes) if (c.Id == id) return c;
            return null;
        }

        public static List<string> Validate(CaveLibrary l)
        {
            var e = new List<string>();
            if (!(l.Chance >= 0f && l.Chance <= 1f)) e.Add("chance must be within 0..1");
            if (l.Classes.Count == 0) e.Add("classes must not be empty");
            var ids = new HashSet<string>();
            foreach (var c in l.Classes)
            {
                string at = $"cave class '{c.Id}'";
                if (string.IsNullOrEmpty(c.Id) || !ids.Add(c.Id)) e.Add($"{at} needs a unique id");
                if (!(c.Weight > 0f)) e.Add($"{at} weight must be positive");
                if (c.MinNodes < 1 || c.MaxNodes < c.MinNodes) e.Add($"{at} nodes must be 1 <= min <= max");
                if (c.Entrances < 1 || c.Loops < 0 || c.Bridges < 0) e.Add($"{at} needs entrances >= 1 and loops, bridges >= 0");
                if (!(c.RadiusMin >= Underground.MinRadius && c.RadiusMax >= c.RadiusMin)) e.Add($"{at} radius must be {Underground.MinRadius} <= min <= max");
                if (!(c.EdgeMin > 0f && c.EdgeMax >= c.EdgeMin)) e.Add($"{at} edge length must be 0 < min <= max");
                if (!(c.ChamberChance >= 0f && c.ChamberChance <= 1f && c.ChamberMin > c.RadiusMax && c.ChamberMax >= c.ChamberMin)) e.Add($"{at} chamber needs chance 0..1 and tunnel radius < min <= max");
                if (c.Hall && !(c.HallMin > c.RadiusMax && c.HallMax >= c.HallMin)) e.Add($"{at} hall radius must exceed the tunnel radius and min <= max");
                if (c.Bridges > 0 && (!c.Hall || !(c.BridgeMin > 0f && c.BridgeMax >= c.BridgeMin))) e.Add($"{at} bridges need a hall and 0 < bridge min <= max");
                if (!(c.Warp >= 0f && c.Warp <= 0.3f && c.Lumps >= 0f && c.Lumps <= 0.05f && c.RoomLumps >= 0f && c.RoomLumps <= 0.1f)) e.Add($"{at} warp must be 0..0.3, lumps 0..0.05, room lumps 0..0.1");
            }
            foreach (var c in l.Classes)
            {
                if (c.Fallback.Length == 0) continue;
                var f = l.Find(c.Fallback);
                if (f == null || f == c || f.Fallback.Length > 0) e.Add($"cave class '{c.Id}' fallback must name another class without a fallback");
            }
            return e;
        }
    }

    public sealed class CaveNode
    {
        public float X, Y, Z, Hx, Hz;
        public bool Entrance, Hall;
    }

    public sealed class CaveEdge
    {
        public int A, B;
        public float R;
        public float[] Points;
    }

    public sealed class CaveGraph
    {
        public string Class;
        public int Tile;
        public List<CaveNode> Nodes = new List<CaveNode>();
        public List<CaveEdge> Edges = new List<CaveEdge>();
    }

    public static class Underground
    {
        public const float MinRadius = 5f, PieceM = 40f, Grade = 0.17f, PorchM = 45f, MouthM = 15f, BridgeClearM = 2f, BridgeReachM = 150f;
        public const int Tries = 6;

        public static void Tile(GeneratedWorld w, TerrainDetail detail, CaveLibrary lib, int tx, int ty, ref Rng r, ref Rng width, ulong rock, ulong drip, List<FeatureEdit> prior, List<FeatureEdit> output, out CaveGraph graph)
        {
            graph = null;
            if (lib == null || lib.Classes.Count == 0) return;
            float roll = r.NextFloat(), pick = r.NextFloat();
            if (roll >= lib.Chance) return;
            float total = 0f;
            foreach (var c in lib.Classes) total += c.Weight;
            CaveClass cls = lib.Classes[lib.Classes.Count - 1];
            float acc = 0f;
            foreach (var c in lib.Classes)
            {
                acc += c.Weight;
                if (pick * total < acc) { cls = c; break; }
            }
            var mouths = Mouths(w, tx, ty);
            if (mouths.Count == 0) return;
            int start = output.Count;
            float grain = new Rng(rock).NextFloat();
            while (cls != null)
            {
                for (int a = cls.Fallback.Length > 0 ? Tries : 1; a > 0; a--)
                {
                    var b = new Builder { W = w, Detail = detail, Cm = w.Config.CellMeters, Out = output, Start = start, C = cls, R = r, Width = width, Mouths = mouths, Grain = grain };
                    b.G = new CaveGraph { Class = cls.Id, Tile = ty * 65536 + tx };
                    bool ok = b.Build() && Sound(detail, Near(prior, output, start), b.G);
                    r = b.R;
                    width = b.Width;
                    if (ok)
                    {
                        graph = b.G;
                        var d = new Rng(drip);
                        Speleothems(detail, w.Config.CellMeters, ref d, output, start);
                        return;
                    }
                    output.RemoveRange(start, output.Count - start);
                }
                cls = cls.Fallback.Length > 0 ? lib.Find(cls.Fallback) : null;
            }
        }

        static FeatureEdit[] Near(List<FeatureEdit> prior, List<FeatureEdit> output, int start)
        {
            var all = new List<FeatureEdit>(prior);
            all.AddRange(output.GetRange(start, output.Count - start));
            return all.ToArray();
        }

        static List<int> Mouths(GeneratedWorld w, int tx, int ty)
        {
            var list = new List<int>();
            int n = w.Config.Size, t = Features.TileCells, m = Features.MarginCells;
            for (int y = ty * t + m; y < (ty + 1) * t - m; y++)
                for (int x = tx * t + m; x < (tx + 1) * t - m; x++)
                {
                    int i = y * n + x, dir = Features.Steepest(w, i);
                    if (dir >= 0) list.Add(i * 8 + dir);
                }
            return list;
        }

        sealed class Builder
        {
            public GeneratedWorld W;
            public TerrainDetail Detail;
            public float Cm;
            public List<FeatureEdit> Out;
            public int Start;
            public CaveClass C;
            public Rng R, Width;
            public List<int> Mouths;
            public CaveGraph G;
            public float Grain;
            readonly List<int> parents = new List<int>();

            float Ground(float x, float z) => Detail.HeightAt(x / Cm, z / Cm);

            bool Covered(float x, float cy, float z, float r) => Ground(x, z) >= cy + r + Features.CaveRoof;

            public bool Build()
            {
                if (Mouths.Count == 0) return false;
                int first = (int)(R.NextFloat() * Mouths.Count);
                if (!Mouth(Mouths[first], out var m)) return false;
                Mouths.RemoveAt(first);
                G.Nodes.Add(m);
                float len = R.Range(C.EdgeMin, C.EdgeMax), grade = R.Range(-0.3f, 1f) * Grade;
                len = Math.Max(len, C.RadiusMax + Features.CaveRoof + MouthM + PieceM * 0.5f);
                var n1 = new CaveNode { X = m.X + m.Hx * len, Z = m.Z + m.Hz * len, Y = m.Y - len * grade, Hx = m.Hx, Hz = m.Hz };
                if (!Link(0, n1, true)) return false;
                if (C.Hall) return BuildHall();
                int want = C.MinNodes + (int)(R.NextFloat() * (C.MaxNodes - C.MinNodes + 1));
                var kids = new List<int> { 0, 0 };
                for (int tries = want * Tries; G.Nodes.Count - 1 < want && tries > 0; tries--)
                {
                    int p = 1 + (int)(R.NextFloat() * (G.Nodes.Count - 1));
                    var a = G.Nodes[p];
                    float hx = a.Hx, hz = a.Hz;
                    Turn(ref hx, ref hz, R.Range(-1f, 1f) * C.Turn * (kids[p] > 0 ? 2.5f : 1f));
                    float l = R.Range(C.EdgeMin, C.EdgeMax), g = R.Range(-0.6f, 1f) * Grade;
                    var b = new CaveNode { X = a.X + hx * l, Z = a.Z + hz * l, Y = a.Y - l * g, Hx = hx, Hz = hz };
                    if (Near(b, 0.6f * C.EdgeMin)) continue;
                    if (!Link(p, b, false)) continue;
                    kids[p]++;
                    kids.Add(0);
                }
                if (G.Nodes.Count - 1 < C.MinNodes) return false;
                for (int k = 1; k < G.Nodes.Count; k++)
                    if (R.NextFloat() < C.ChamberChance) Chamber(G.Nodes[k]);
                for (int tries = C.Loops * Tries, made = 0; made < C.Loops && tries > 0; tries--)
                {
                    int i = 1 + (int)(R.NextFloat() * (G.Nodes.Count - 1)), j = 1 + (int)(R.NextFloat() * (G.Nodes.Count - 1));
                    if (i == j || Linked(i, j)) continue;
                    var a = G.Nodes[i];
                    var b = G.Nodes[j];
                    float d = Plan(a, b);
                    if (d < 0.6f * C.EdgeMin || d > 1.6f * C.EdgeMax || Math.Abs(a.Y - b.Y) > 0.9f * Grade * d) continue;
                    if (Edge(i, j, false)) made++;
                }
                for (int tries = (C.Entrances - 1) * Tries, made = 1; made < C.Entrances && tries > 0 && Mouths.Count > 0; tries--)
                    if (Entrance(false)) made++;
                return true;
            }

            bool BuildHall()
            {
                float rh = R.Range(C.HallMin, C.HallMax), hole = rh * Features.CaveHollow;
                var porch = G.Nodes[1];
                float l = rh + PorchM, g = R.Range(0f, 1f) * Grade;
                var hall = new CaveNode { X = porch.X + porch.Hx * l, Z = porch.Z + porch.Hz * l, Y = porch.Y - l * g, Hx = porch.Hx, Hz = porch.Hz, Hall = true };
                float hx = hall.Hx, hz = hall.Hz, px = -hz, pz = hx;
                if (!Covered(hall.X, hall.Y + hole, hall.Z, rh)) return false;
                if (!Link(1, hall, false)) return false;
                int mark = Out.Count;
                if (!Room(hall, hx, hz, 0.9f * rh, rh) || !Room(hall, -hx, -hz, 0.9f * rh, rh) || !Room(hall, px, pz, 0.4f * rh, rh) || !Room(hall, -px, -pz, 0.4f * rh, rh)) return false;
                int made = 1;
                for (int tries = (C.Entrances - 1) * Tries; made < C.Entrances && tries > 0 && Mouths.Count > 0; tries--)
                    if (Entrance(true)) made++;
                if (made < 2) return false;
                int bridges = 0;
                for (int tries = C.Bridges * Tries; bridges < C.Bridges && tries > 0; tries--)
                    if (Bridge(hall, rh, hole, mark)) bridges++;
                return true;
            }

            bool Bridge(CaveNode hall, float rh, float hole, int hallFrom)
            {
                float rb = R.Range(C.BridgeMin, C.BridgeMax), o = R.Range(0.45f, 0.85f) * rh, lift = R.Range(0.85f, 1.1f), sag = R.Range(-0.08f, 0.08f) * rb;
                float half = hole + 2f * rb, hx = hall.Hx, hz = hall.Hz, px = -hz, pz = hx;
                float cx = hall.X + hx * o, cz = hall.Z + hz * o, cy = hall.Y + hole * lift;
                var e = new FeatureEdit
                {
                    Kind = FeatureEdit.Add, Shape = FeatureEdit.Bridge, R = rb, N = 0.1f * rb, S = R.NextFloat(),
                    Ax = cx - px * half, Ay = cy + sag, Az = cz - pz * half,
                    Bx = cx + px * half, By = cy - sag, Bz = cz + pz * half,
                };
                if (!Features.Inside(e, Cm)) return false;
                float clear = rb + Walk.Height + BridgeClearM;
                for (int q = Start; q < Out.Count; q++)
                {
                    var c = Out[q];
                    if (c.Kind != FeatureEdit.Carve || (q >= hallFrom && q < hallFrom + 4)) continue;
                    for (int t = 1; t < 8; t++)
                    {
                        float f = t / 8f;
                        if (SegmentDistance(c, e.Ax + (e.Bx - e.Ax) * f, e.Ay + (e.By - e.Ay) * f, e.Az + (e.Bz - e.Az) * f) < clear + c.R * Features.CaveHollow) return false;
                    }
                }
                Out.Add(e);
                var items = Out.GetRange(Start, Out.Count - Start).ToArray();
                if (BridgeFault(Detail, items, e) == null) return true;
                Out.RemoveAt(Out.Count - 1);
                return false;
            }

            bool Entrance(bool hall)
            {
                float rh = 0f;
                if (hall) for (int q = Start; q < Out.Count; q++) if (Out[q].Shape == FeatureEdit.CaveRoom) rh = Math.Max(rh, Out[q].R);
                var fit = new List<int>();
                for (int q = 0; q < Mouths.Count; q++)
                    if (Aim(Mouths[q], hall, rh, out _, out _, out _)) fit.Add(q);
                if (fit.Count == 0) { Mouths.Clear(); return false; }
                int k = fit[(int)(R.NextFloat() * fit.Count)];
                Aim(Mouths[k], hall, rh, out var m, out int target, out var porch);
                Mouths.RemoveAt(k);
                var to = G.Nodes[target];
                if (hall)
                {
                    int mark = Out.Count, nodes = G.Nodes.Count, edges = G.Edges.Count;
                    G.Nodes.Add(porch);
                    if (!Edge(nodes, target, false) || !EntranceEdge(m, nodes))
                    {
                        Out.RemoveRange(mark, Out.Count - mark);
                        G.Nodes.RemoveRange(nodes, G.Nodes.Count - nodes);
                        G.Edges.RemoveRange(edges, G.Edges.Count - edges);
                        return false;
                    }
                    return true;
                }
                return EntranceEdge(m, target);
            }

            bool Aim(int code, bool hall, float rh, out CaveNode m, out int target, out CaveNode porch)
            {
                Mouth(code, out m);
                porch = null;
                target = -1;
                float best = float.MaxValue;
                for (int q = 1; q < G.Nodes.Count; q++)
                {
                    var t = G.Nodes[q];
                    if (hall != t.Hall) continue;
                    float d = Plan(m, t);
                    if (d < best) { best = d; target = q; }
                }
                if (target < 0) return false;
                var to = G.Nodes[target];
                if (!hall) return Reachable(m, to);
                float dx = m.X - to.X, dz = m.Z - to.Z;
                if (best <= 0f) return false;
                float l = rh + PorchM;
                porch = new CaveNode { X = to.X + dx / best * l, Z = to.Z + dz / best * l, Y = to.Y, Hx = -dx / best, Hz = -dz / best };
                return Reachable(m, porch);
            }

            bool Reachable(CaveNode m, CaveNode t)
            {
                float d = Plan(m, t), dx = t.X - m.X, dz = t.Z - m.Z;
                if (d < C.EdgeMin || d > 3f * C.EdgeMax) return false;
                if (dx * m.Hx + dz * m.Hz < 0.3f * d) return false;
                return Math.Abs(m.Y - t.Y) <= 0.9f * Grade * d;
            }

            bool EntranceEdge(CaveNode m, int target)
            {
                int at = G.Nodes.Count;
                G.Nodes.Add(m);
                if (Edge(at, target, true)) return true;
                G.Nodes.RemoveAt(at);
                return false;
            }

            bool Mouth(int code, out CaveNode m)
            {
                int n = W.Config.Size, i = code / 8, dir = code % 8;
                float inv = 1f / Grid.Dist[dir], hx = -Grid.DX[dir] * inv, hz = -Grid.DY[dir] * inv;
                float back = Math.Max(0.5f, 0.5f * Features.ReferenceCellM / Cm);
                float x = (i % n - back * hx) * Cm, z = (i / n - back * hz) * Cm;
                m = new CaveNode { X = x, Z = z, Y = Ground(x, z) - 0.05f * (C.RadiusMin + C.RadiusMax) * Features.CaveHollow, Hx = hx, Hz = hz, Entrance = true };
                return true;
            }

            bool Near(CaveNode b, float d)
            {
                foreach (var o in G.Nodes) if (Plan(o, b) < d) return true;
                return false;
            }

            bool Linked(int i, int j)
            {
                foreach (var e in G.Edges) if ((e.A == i && e.B == j) || (e.A == j && e.B == i)) return true;
                return false;
            }

            static float Plan(CaveNode a, CaveNode b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

            bool Link(int from, CaveNode b, bool mouth)
            {
                int at = G.Nodes.Count;
                G.Nodes.Add(b);
                if (Edge(from, at, mouth)) return true;
                G.Nodes.RemoveAt(at);
                return false;
            }

            bool Edge(int ia, int ib, bool mouth)
            {
                var a = G.Nodes[ia];
                var b = G.Nodes[ib];
                float dx = b.X - a.X, dz = b.Z - a.Z, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len <= 1f || Math.Abs(b.Y - a.Y) > Grade * len) return false;
                float r = Width.Range(C.RadiusMin, C.RadiusMax), px = -dz / len, pz = dx / len, open = r + Features.CaveRoof, ground = mouth ? Ground(a.X, a.Z) : 0f;
                var ts = new List<float> { 0f };
                float t0 = 0f;
                if (mouth)
                {
                    t0 = Math.Min(1f, (open + MouthM) / len);
                    ts.Add(t0);
                }
                int pieces = Math.Max(t0 < 1f ? 1 : 0, (int)Math.Ceiling((1f - t0) * len / PieceM));
                if (!mouth) pieces = Math.Max(2, pieces);
                for (int s = 1; s <= pieces; s++) ts.Add(t0 + (1f - t0) * s / pieces);
                var pts = new float[ts.Count * 3];
                for (int s = 0; s < ts.Count; s++)
                {
                    float t = ts[s], j = s == 0 || s == ts.Count - 1 || (mouth && s == 1) ? 0f : R.Range(-1f, 1f) * C.Warp * PieceM;
                    pts[s * 3] = a.X + dx * t + px * j;
                    pts[s * 3 + 1] = a.Y + (b.Y - a.Y) * t;
                    pts[s * 3 + 2] = a.Z + dz * t + pz * j;
                }
                int mark = Out.Count;
                for (int s = 0; s + 1 < ts.Count; s++)
                {
                    var e = Carve(FeatureEdit.Cave, r, pts[s * 3], pts[s * 3 + 1], pts[s * 3 + 2], pts[s * 3 + 3], pts[s * 3 + 4], pts[s * 3 + 5], C.Lumps * r).Finer();
                    if (!Fits(e, mouth && s == 0 ? open : 0f, ground)) { Out.RemoveRange(mark, Out.Count - mark); return false; }
                    Out.Add(e);
                }
                G.Edges.Add(new CaveEdge { A = ia, B = ib, R = r, Points = pts });
                return true;
            }

            void Chamber(CaveNode at)
            {
                float big = R.Range(C.ChamberMin, C.ChamberMax), reach = big * 0.6f;
                Room(at, at.Hx, at.Hz, reach, big);
            }

            bool Room(CaveNode at, float hx, float hz, float reach, float r)
            {
                var e = Carve(FeatureEdit.CaveRoom, r, at.X, at.Y, at.Z, at.X + hx * reach, at.Y, at.Z + hz * reach, C.RoomLumps * r);
                if (!Covered(at.X, e.Ay, at.Z, r) || !Fits(e, 0f, 0f)) return false;
                Out.Add(e);
                return true;
            }

            FeatureEdit Carve(int shape, float r, float ax, float af, float az, float bx, float bf, float bz, float lumps)
            {
                float lift = r * Features.CaveHollow;
                return new FeatureEdit { Kind = FeatureEdit.Carve, Shape = shape, R = r, Ax = ax, Ay = af + lift, Az = az, Bx = bx, By = bf + lift, Bz = bz, N = lumps, S = R.NextFloat(), F = Grain };
            }

            bool Fits(FeatureEdit e, float open, float ground)
            {
                float ex = e.Bx - e.Ax, ez = e.Bz - e.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                int steps = Math.Max(2, (int)Math.Ceiling(len / Features.RoofSampleM));
                for (int t = 1; t <= steps; t++)
                {
                    float f = t / (float)steps, x = e.Ax + ex * f, z = e.Az + ez * f;
                    if (f * len < open && Ground(x, z) >= ground) continue;
                    if (!Covered(x, e.Ay + (e.By - e.Ay) * f, z, e.R)) return false;
                }
                if (!Features.Inside(e, Cm) || Wet(W, e)) return false;
                for (int q = Start; q < Out.Count; q++)
                {
                    var o = Out[q];
                    if (o.Kind != FeatureEdit.Carve || Joined(o, e)) continue;
                    if (Over(o, e) || Over(e, o)) return false;
                }
                return true;
            }
        }

        public const float DripPerM = 1f / 5f, DripClearM = 2.5f, PassM = 1.5f, BridgeKeepM = 6f, DripGapM = 4f, ColumnGapM = 14f, HeadroomM = 0.7f;
        public const float ColumnChance = 0.12f;

        static void Speleothems(TerrainDetail detail, float cm, ref Rng r, List<FeatureEdit> output, int start)
        {
            var ground = new DetailGround { Detail = detail };
            var rock = new EditArray { Items = output.GetRange(start, output.Count - start).ToArray() };
            float s = Features.LatticeStepM;
            int end = output.Count;
            var made = new List<FeatureEdit>();
            for (int q = start; q < end; q++)
            {
                var c = output[q];
                if (c.Kind != FeatureEdit.Carve) continue;
                float ex = c.Bx - c.Ax, ez = c.Bz - c.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                if (len < 1f) continue;
                float px = -ez / len, pz = ex / len, wf = c.FloorHalf, hole = c.HoleR;
                int tries = Math.Max(1, (int)((len + (c.Shape == FeatureEdit.CaveRoom ? 2f * c.R : 0f)) * DripPerM));
                for (int k = 0; k < tries; k++)
                {
                    float t = r.NextFloat(), u = r.Range(-0.9f, 0.9f) * wf, kind = r.NextFloat(), girth = r.NextFloat(), seed = r.NextFloat();
                    if (kind >= ColumnChance) continue;
                    float x = c.Ax + ex * t + px * u, z = c.Az + ez * t + pz * u, ay = c.Ay + (c.By - c.Ay) * t;
                    if (Walk.Solid(ground, rock, s, x, ay - 0.2f * hole, z) >= 0) continue;
                    float floor = Walk.Surface(ground, rock, s, x, z, ay - 0.2f * hole, ay - hole - 6f);
                    if (floor <= ay - hole - 6f) continue;
                    float ceil = Ceiling(ground, rock, s, x, floor + 1f, z, floor + 3f * hole);
                    if (float.IsNaN(ceil)) continue;
                    float gap = ceil - floor;
                    if (gap < DripGapM || gap >= ColumnGapM) continue;
                    var e = new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Speleothem, Ax = x, Az = z, Bx = x, Bz = z, S = seed };
                    e.R = 1.4f + 1.2f * girth;
                    e.Ay = ceil + 1.5f; e.By = floor - 1f; e.T = 0.5f; e.P = 0.55f; e.N = 0.15f * e.R;
                    Drop(e, made, output, ground, rock, s, cm, start, end);
                }
            }
            output.AddRange(made);
        }

        static bool Drop(FeatureEdit e, List<FeatureEdit> made, List<FeatureEdit> output, DetailGround ground, EditArray rock, float s, float cm, int start, int end)
        {
            if (!Features.Inside(e, cm) || DripFault(ground, rock, s, e, output, start, end, DripClearM) != null) return false;
            foreach (var o in made)
            {
                float dx = o.Ax - e.Ax, dz = o.Az - e.Az, keep = o.R + e.R;
                if (dx * dx + dz * dz < keep * keep) return false;
            }
            made.Add(e);
            return true;
        }

        internal static float Ceiling<TE>(DetailGround g, TE list, float s, float x, float y, float z, float top) where TE : struct, IEditList
        {
            const float probe = 0.5f;
            for (; y < top; y += probe)
            {
                if (Walk.Solid(g, list, s, x, y + probe, z) < 0) continue;
                float lo = y, hi = y + probe;
                for (int i = 0; i < 6; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Walk.Solid(g, list, s, x, mid, z) < 0) lo = mid; else hi = mid;
                }
                return hi;
            }
            return float.NaN;
        }

        public static string DripFault(DetailGround g, EditArray rock, float s, FeatureEdit e, IList<FeatureEdit> items, int from, int to, float clear)
        {
            string at = $"speleothem at {e.Ax:F0},{e.Az:F0}";
            if (Walk.Solid(g, rock, s, e.Ax, e.Ay, e.Az) < 0) return at + " floats free of the rock";
            if (e.T == 1f && Walk.Solid(g, rock, s, e.Bx, e.By, e.Bz) >= 0) return at + " is buried in rock";
            bool hangs = e.T == 1f && e.Ay > e.By;
            if (hangs && e.By - Walk.Surface(g, rock, s, e.Bx, e.Bz, e.By, e.By - 40f) < Walk.Height + HeadroomM) return at + " hangs into the walking headroom";
            float reach = e.R + e.N;
            for (int q = from; q < to; q++)
            {
                var c = items[q];
                if (!hangs && c.Shape == FeatureEdit.Cave && Plan(c, e.Ax, e.Az) < reach + clear) return at + " blocks a cave passage";
                if (c.Shape == FeatureEdit.Bridge && Plan(c, e.Ax, e.Az) < reach + c.R + BridgeKeepM) return at + " crowds a rock bridge";
            }
            return null;
        }

        internal static float Plan(FeatureEdit c, float x, float z)
        {
            float ex = c.Bx - c.Ax, ez = c.Bz - c.Az, ll = ex * ex + ez * ez, px = x - c.Ax, pz = z - c.Az;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + pz * ez) / ll)) : 0f;
            float qx = px - t * ex, qz = pz - t * ez;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        public const float SettledM = 0.5f, SettledShare = 0.5f, SettledLateral = 0.5f;

        public static void SettledFloor(TerrainDetail detail, FeatureEdit[] rock, ref int probes, ref int settled)
        {
            var g = new DetailGround { Detail = detail };
            var list = new EditArray { Items = rock };
            float s = Features.LatticeStepM;
            foreach (var c in rock)
            {
                if (c.Shape != FeatureEdit.Cave) continue;
                float ex = c.Bx - c.Ax, ez = c.Bz - c.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
                if (len < 1f) continue;
                float px = -ez / len, pz = ex / len, hole = c.HoleR;
                for (int i = 1; i <= 3; i++)
                {
                    float t = 0.25f * i, cx = c.Ax + ex * t, cz = c.Az + ez * t;
                    float ay = c.Ay + (c.By - c.Ay) * t, top = ay - 0.5f * hole, bottom = ay - hole - 6f;
                    if (Walk.Solid(g, list, s, cx, top, cz) >= 0) continue;
                    float mid = Walk.Surface(g, list, s, cx, cz, top, bottom);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = cx + px * side * SettledLateral * hole, z = cz + pz * side * SettledLateral * hole;
                        probes++;
                        if (Walk.Solid(g, list, s, x, top, z) >= 0) continue;
                        if (Math.Abs(Walk.Surface(g, list, s, x, z, top, bottom) - mid) < SettledM) settled++;
                    }
                }
            }
        }

        static void Turn(ref float hx, ref float hz, float t)
        {
            float ox = -hz, oz = hx;
            hx += ox * t; hz += oz * t;
            float l = (float)Math.Sqrt(hx * hx + hz * hz);
            hx /= l; hz /= l;
        }

        static bool Same(float ax, float az, float bx, float bz) => Math.Abs(ax - bx) < 1e-2f && Math.Abs(az - bz) < 1e-2f;

        static bool Joined(FeatureEdit o, FeatureEdit e)
            => Same(o.Ax, o.Az, e.Ax, e.Az) || Same(o.Ax, o.Az, e.Bx, e.Bz) || Same(o.Bx, o.Bz, e.Ax, e.Az) || Same(o.Bx, o.Bz, e.Bx, e.Bz);

        static bool Over(FeatureEdit o, FeatureEdit e)
        {
            float ex = e.Bx - e.Ax, ez = e.Bz - e.Az, ox = o.Bx - o.Ax, oz = o.Bz - o.Az, ll = ox * ox + oz * oz;
            float reach = (1f - FeatureEdit.Blend * 0.25f) * o.R, tol = 0.25f * Math.Min(o.R, e.R);
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(ex * ex + ez * ez) / Features.RoofSampleM));
            for (int t = 0; t <= steps; t++)
            {
                float f = t / (float)steps, px = e.Ax + ex * f, pz = e.Az + ez * f;
                float u = ll > 0 ? Math.Max(0f, Math.Min(1f, ((px - o.Ax) * ox + (pz - o.Az) * oz) / ll)) : 0f;
                float dx = px - o.Ax - ox * u, dz = pz - o.Az - oz * u;
                if (dx * dx + dz * dz >= reach * reach) continue;
                float fe = e.Ay + (e.By - e.Ay) * f - e.R * Features.CaveHollow, fo = o.Ay + (o.By - o.Ay) * u - o.R * Features.CaveHollow;
                if (Math.Abs(fe - fo) > tol) return true;
            }
            return false;
        }

        static float SegmentDistance(FeatureEdit c, float x, float y, float z)
        {
            float ex = c.Bx - c.Ax, ey = c.By - c.Ay, ez = c.Bz - c.Az, px = x - c.Ax, py = y - c.Ay, pz = z - c.Az;
            float ll = ex * ex + ey * ey + ez * ez;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
            float qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
            return (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
        }

        public static bool Wet(GeneratedWorld w, FeatureEdit e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, reach = e.R + 0.71f * cm;
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(e.Ax, e.Bx) - reach) / cm)), i1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(e.Ax, e.Bx) + reach) / cm));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(e.Az, e.Bz) - reach) / cm)), j1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(e.Az, e.Bz) + reach) / cm));
            float fx = e.Bx - e.Ax, fz = e.Bz - e.Az, ll = fx * fx + fz * fz;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int c = j * n + i;
                    if (!w.Sea[c] && w.Water.LakeId[c] < 0) continue;
                    float qx0 = i * cm - e.Ax, qz0 = j * cm - e.Az;
                    float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (qx0 * fx + qz0 * fz) / ll)) : 0f;
                    float qx = qx0 - t * fx, qz = qz0 - t * fz;
                    if (qx * qx + qz * qz <= reach * reach) return true;
                }
            return false;
        }

        public const float FloorSlope = 0.6f;

        static bool Sound(TerrainDetail detail, FeatureEdit[] edits, CaveGraph g)
        {
            foreach (var p in edits) if (p.Shape == FeatureEdit.Cave && PathFault(detail, edits, p) != null) return false;
            foreach (var edge in g.Edges) if (Cramped(detail, edits, g, edge) != null) return false;
            return Unreached(detail, edits, g) == null;
        }

        public static string PathFault(TerrainDetail detail, FeatureEdit[] near, FeatureEdit p)
        {
            float s = NearField.NodeStep(1);
            var cols = new DetailColumns { Detail = detail, StepM = s };
            float run = (float)Math.Sqrt((p.Bx - p.Ax) * (p.Bx - p.Ax) + (p.Bz - p.Az) * (p.Bz - p.Az));
            var solid = new EditedSolid<DetailColumns, EditArray> { Columns = cols, Edits = new EditArray { Items = near }, StepM = s };
            int steps = Math.Max(1, (int)Math.Ceiling(run / s)), li = int.MinValue, lk = 0;
            float last = 0;
            for (int t = 0; t <= steps; t++)
            {
                float f = t / (float)steps;
                int i = (int)Math.Round((p.Ax + (p.Bx - p.Ax) * f) / s), k = (int)Math.Round((p.Az + (p.Bz - p.Az) * f) / s);
                if (i == li && k == lk) continue;
                float floor = CaveFloor(solid, i, k, p.Ay + (p.By - p.Ay) * f, s);
                if (float.IsNaN(floor)) return $"cave path at {p.Ax:F0},{p.Az:F0} is blocked at {i * s:F0},{k * s:F0}";
                if (li != int.MinValue)
                {
                    float d = (float)Math.Sqrt((i - li) * (i - li) + (k - lk) * (k - lk)) * s;
                    if (Math.Abs(floor - last) > FloorSlope * d) return $"cave path at {p.Ax:F0},{p.Az:F0} has a {Math.Abs(floor - last):F1} m step at {i * s:F0},{k * s:F0}";
                }
                li = i; lk = k; last = floor;
            }
            return null;
        }

        static float CaveFloor<TSolid>(TSolid solid, int i, int k, float centre, float s) where TSolid : struct, ISolidField
        {
            int j = (int)Math.Round(centre / s);
            if (solid.Density(i, j, k) >= 0) return float.NaN;
            for (int n = 0; n < 64; n++, j--)
            {
                float above = solid.Density(i, j, k), below = solid.Density(i, j - 1, k);
                if (below >= 0) return (j - 1 + below / (below - above)) * s;
            }
            return float.NaN;
        }

        public const float WalkStride = 0.5f, ClearSide = 0.75f, ClearStep = 0.4f, ClearProbe = 0.25f;

        public static string Cramped(TerrainDetail detail, FeatureEdit[] edits, CaveGraph g, CaveEdge edge)
        {
            var ground = new DetailGround { Detail = detail };
            var list = new EditArray { Items = edits };
            float s = Features.LatticeStepM;
            var p = edge.Points;
            for (int q = 3; q + 2 < p.Length; q += 3)
            {
                float ex = p[q] - p[q - 3], ey = p[q + 1] - p[q - 2], ez = p[q + 2] - p[q - 1], run = (float)Math.Sqrt(ex * ex + ez * ez);
                if (run <= 0f) continue;
                float sx = -ez / run * ClearSide, sz = ex / run * ClearSide;
                int steps = Math.Max(1, (int)Math.Ceiling(run));
                for (int t = q == 3 ? 0 : 1; t <= steps; t++)
                {
                    float f = t / (float)steps, x = p[q - 3] + ex * f, af = p[q - 2] + ey * f, z = p[q - 1] + ez * f;
                    float floor = af, y = af + Walk.StepUp;
                    if (Walk.Solid(ground, list, s, x, y, z) >= 0) return $"{g.Class} cave tunnel is too narrow for the player at {x:F0},{y:F0},{z:F0}";
                    for (; y >= af - 2f; y -= ClearProbe)
                        if (Walk.Solid(ground, list, s, x, y, z) >= 0) { floor = y + ClearProbe; break; }
                    for (float h = floor + ClearStep * 1.5f; h <= floor + Walk.Height + 0.2f + 1e-3f; h += ClearStep)
                        if (Walk.Solid(ground, list, s, x, h, z) >= 0 || Walk.Solid(ground, list, s, x + sx, h, z + sz) >= 0 || Walk.Solid(ground, list, s, x - sx, h, z - sz) >= 0)
                            return $"{g.Class} cave tunnel is too narrow for the player at {x:F0},{h:F0},{z:F0}";
                }
            }
            return null;
        }

        public static string Unreached(TerrainDetail detail, FeatureEdit[] edits, CaveGraph g)
        {
            var ground = new DetailGround { Detail = detail };
            var list = new EditArray { Items = edits };
            var open = new List<int>[g.Nodes.Count];
            for (int k = 0; k < open.Length; k++) open[k] = new List<int>();
            string stop = null;
            foreach (var edge in g.Edges)
            {
                string why = WalkEdge(ground, list, Features.LatticeStepM, edge);
                if (why == null) { open[edge.A].Add(edge.B); open[edge.B].Add(edge.A); }
                else stop = stop ?? why;
            }
            var reached = new bool[g.Nodes.Count];
            var queue = new Queue<int>();
            for (int k = 0; k < g.Nodes.Count; k++) if (g.Nodes[k].Entrance) { reached[k] = true; queue.Enqueue(k); }
            while (queue.Count > 0)
                foreach (int k in open[queue.Dequeue()])
                    if (!reached[k]) { reached[k] = true; queue.Enqueue(k); }
            for (int k = 0; k < g.Nodes.Count; k++)
            {
                if (reached[k]) continue;
                var n = g.Nodes[k];
                return $"{g.Class} cave chamber at {n.X:F0},{n.Z:F0} is not walkable from an entrance ({stop ?? "no tunnel reaches it"})";
            }
            return null;
        }

        static string WalkEdge(DetailGround ground, EditArray list, float s, CaveEdge edge)
        {
            var p = edge.Points;
            float x = p[0], y = p[1] + 0.5f, z = p[2], fall = Walk.FallSpeed * WalkStride / 6f;
            Walk.Step(ground, list, s, ref x, ref y, ref z, 0, 0, Walk.Reach);
            for (int q = 3; q + 2 < p.Length; q += 3)
            {
                float ex = p[q] - p[q - 3], ez = p[q + 2] - p[q - 1];
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ex * ex + ez * ez) / WalkStride));
                float dx = ex / steps, dz = ez / steps;
                for (int t = 0; t < steps; t++)
                {
                    float px = x, pz = z;
                    Walk.Step(ground, list, s, ref x, ref y, ref z, dx, dz, fall);
                    if (Math.Abs(x - px - dx) > 1e-3f || Math.Abs(z - pz - dz) > 1e-3f) return $"walker stopped at {x:F0},{y:F0},{z:F0}";
                }
            }
            float floor = p[p.Length - 2];
            return Math.Abs(y - floor) > Math.Max(0.25f * edge.R, Walk.Height) ? $"walker ends at height {y:F1}, floor is {floor:F1}" : null;
        }

        public static string BridgeFault(TerrainDetail detail, FeatureEdit[] edits, FeatureEdit b)
        {
            var g = new DetailGround { Detail = detail };
            var list = new EditArray { Items = edits };
            float s = Features.LatticeStepM, ex = b.Bx - b.Ax, ey = b.By - b.Ay, ez = b.Bz - b.Az, len = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
            string at = $"rock bridge at {b.Ax:F0},{b.Az:F0}";
            if (len <= 0f) return at + " has no span";
            float ux = ex / len, uy = ey / len, uz = ez / len;
            for (int t = 3; t <= 17; t++)
            {
                float f = t / 20f;
                if (Walk.Solid(g, list, s, b.Ax + ex * f, b.Ay + ey * f, b.Az + ez * f) < 0) return at + " has a gap in its deck";
            }
            float k = b.R + 2f;
            if (Walk.Solid(g, list, s, b.Ax - ux * k, b.Ay - uy * k, b.Az - uz * k) < 0 || Walk.Solid(g, list, s, b.Bx + ux * k, b.By + uy * k, b.Bz + uz * k) < 0) return at + " is not anchored in rock";
            float mx = b.Ax + 0.5f * ex, my = b.Ay + 0.5f * ey, mz = b.Az + 0.5f * ez;
            if (!Clear(g, list, s, mx, my, mz, -1f)) return at + " has no open floor under it";
            if (!Clear(g, list, s, mx, my, mz, 1f)) return at + " has no rock roof over it";
            return null;
        }

        static bool Clear(DetailGround g, EditArray list, float s, float x, float y, float z, float dir)
        {
            const float probe = 0.25f;
            float d = 0f;
            while (d < BridgeReachM && Walk.Solid(g, list, s, x, y + dir * d, z) >= 0) d += probe;
            float gap = d;
            while (d < BridgeReachM && Walk.Solid(g, list, s, x, y + dir * d, z) < 0) d += probe;
            return d < BridgeReachM && d - gap >= Walk.Height;
        }
    }
}
