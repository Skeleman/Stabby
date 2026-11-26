using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class Mines
    {
        public const string Class = "mine";
        public const int Most = 3, Tries = 16;
        public const float Radius = 5f, ShaftRadius = 4f, RoofM = 6f, ShaftMostM = 90f, Lumps = 0.03f, MouthDipM = 0.3f;

        public static bool IsMine(CaveGraph g) => g.Class == Class;

        public static void Place(GeneratedWorld w, TerrainDetail detail, StrataField strata, List<FeatureEdit> list, List<CaveGraph> caves)
        {
            if (detail == null || w.Height == null) return;
            var r = Rng.Stream(w.Config.Seed, "mines");
            ulong seed = r.NextU64();
            int n = w.Config.Size, tc = Features.TileCells, tm = Features.MarginCells, tiles = n / tc;
            float cm = w.Config.CellMeters;
            var taken = new HashSet<int>();
            foreach (var e in list) if (e.CaveCarve) taken.Add(Features.TileOf(e, cm));
            var picks = new List<KeyValuePair<float, int>>();
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    if (taken.Contains(ty * 65536 + tx)) continue;
                    float best = float.NegativeInfinity;
                    int code = -1;
                    for (int y = ty * tc + tm; y < (ty + 1) * tc - tm; y++)
                        for (int x = tx * tc + tm; x < (tx + 1) * tc - tm; x++)
                        {
                            int i = y * n + x, dir = Features.Steepest(w, i);
                            if (dir < 0 || (dir & 1) != 0) continue;
                            float score = strata.Hardness(i, w.Height[i]) - RangeGap(w, x, y);
                            if (score > best) { best = score; code = i * 8 + dir; }
                        }
                    if (code >= 0) picks.Add(new KeyValuePair<float, int>(best, code));
                }
            picks.Sort((a, b) => b.Key.CompareTo(a.Key) != 0 ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value));
            int placed = 0;
            for (int k = 0; k < picks.Count && k < Tries && placed < Most; k++)
            {
                var q = new Rng(Rng.Mix(seed + (ulong)picks[k].Value));
                if (Build(w, detail, list, caves, picks[k].Value, ref q)) placed++;
            }
        }

        static float RangeGap(GeneratedWorld w, int x, int y)
        {
            if (w.Sketch == null || w.Sketch.Ranges == null || w.Sketch.Ranges.Count == 0) return 0f;
            float best = float.MaxValue;
            foreach (var g in w.Sketch.Ranges)
            {
                if (g.X == null || g.X.Length == 0) continue;
                for (int k = 0; k < g.X.Length; k++)
                {
                    int j = Math.Min(g.X.Length - 1, k + 1);
                    float ax = g.X[k], ay = g.Y[k], ex = g.X[j] - ax, ey = g.Y[j] - ay, ll = ex * ex + ey * ey;
                    float t = ll > 0f ? Math.Max(0f, Math.Min(1f, ((x - ax) * ex + (y - ay) * ey) / ll)) : 0f;
                    float dx = x - ax - ex * t, dy = y - ay - ey * t, d = (float)Math.Sqrt(dx * dx + dy * dy) / (4f * Math.Max(1f, g.Width));
                    if (d < best) best = d;
                }
            }
            return best == float.MaxValue ? 0f : best;
        }

        static float Snap(float v) => (float)Math.Round(v / Structures.KitCellM) * Structures.KitCellM;

        static bool Build(GeneratedWorld w, TerrainDetail detail, List<FeatureEdit> list, List<CaveGraph> caves, int code, ref Rng r)
        {
            int n = w.Config.Size, i = code / 8, dir = code % 8;
            float cm = w.Config.CellMeters, hx = -Grid.DX[dir], hz = -Grid.DY[dir];
            float back = Math.Max(0.5f, 0.5f * Features.ReferenceCellM / cm);
            float mx = Snap((i % n - back * hx) * cm), mz = Snap((i / n - back * hz) * cm);
            float floor = Ground(detail, cm, mx, mz) - MouthDipM, grain = r.NextFloat();
            var g = new CaveGraph { Class = Class };
            g.Nodes.Add(new CaveNode { X = mx, Y = floor, Z = mz, Hx = hx, Hz = hz, Entrance = true });
            var edits = new List<FeatureEdit>();
            float open = Radius + RoofM, main = Structures.KitCellM * r.Range(8, 13);
            float lead = (float)Math.Ceiling((open + Underground.MouthM) / Structures.KitCellM) * Structures.KitCellM;
            if (lead >= main) main = lead + 2f * Structures.KitCellM;
            float ax = mx + hx * main, az = mz + hz * main;
            g.Nodes.Add(new CaveNode { X = ax, Y = floor, Z = az, Hx = hx, Hz = hz });
            if (!Run(w, detail, list, edits, g, 0, 1, lead, open, floor + MouthDipM, grain, ref r)) return false;
            float side = r.NextFloat() < 0.5f ? -1f : 1f, sx = -hz * side, sz = hx * side, turn = Structures.KitCellM * r.Range(4, 9);
            g.Nodes.Add(new CaveNode { X = ax + sx * turn, Y = floor, Z = az + sz * turn, Hx = sx, Hz = sz });
            if (!Run(w, detail, list, edits, g, 1, 2, 0f, 0f, 0f, grain, ref r)) return false;
            if (r.NextFloat() < 0.5f)
            {
                float drift = Structures.KitCellM * r.Range(3, 7);
                g.Nodes.Add(new CaveNode { X = ax + hx * drift, Y = floor, Z = az + hz * drift, Hx = hx, Hz = hz });
                int mark = edits.Count;
                if (!Run(w, detail, list, edits, g, 1, 3, 0f, 0f, 0f, grain, ref r)) { g.Nodes.RemoveAt(3); edits.RemoveRange(mark, edits.Count - mark); }
            }
            int tile = Features.TileOf(edits[0], cm);
            if (tile < 0) return false;
            foreach (var e in edits) if (Features.TileOf(e, cm) != tile) return false;
            float h = ShaftRadius - 1.25f * ShaftRadius * FeatureEdit.Blend, shaftSeed = r.NextFloat();
            var shaft = new FeatureEdit();
            bool dug = false;
            for (int k = 1; k < g.Nodes.Count; k++)
            {
                float sx0 = g.Nodes[k].X, sz0 = g.Nodes[k].Z, surface = Ground(detail, cm, sx0, sz0);
                if (surface - floor > ShaftMostM || (dug && surface + ShaftRadius >= shaft.By)) continue;
                var s = new FeatureEdit { Kind = FeatureEdit.Carve, Shape = FeatureEdit.Mine, R = ShaftRadius, Ax = sx0, Ay = floor + h, Az = sz0, Bx = sx0, By = surface + ShaftRadius, Bz = sz0, N = Lumps * ShaftRadius, S = shaftSeed, F = grain }.Finer();
                if (Features.Inside(s, cm) && Features.TileOf(s, cm) == tile && !Underground.Wet(w, s) && Clear(list, s)) { shaft = s; dug = true; }
            }
            if (dug) edits.Add(shaft);
            var near = new List<FeatureEdit>();
            foreach (var e in list) if (Features.TileOf(e, cm) == tile) near.Add(e);
            near.AddRange(edits);
            var all = near.ToArray();
            foreach (var e in edits) if (!Vertical(e) && Underground.PathFault(detail, all, e) != null) return false;
            foreach (var edge in g.Edges) if (Underground.Cramped(detail, all, g, edge) != null) return false;
            if (Underground.Unreached(detail, all, g) != null) return false;
            g.Tile = tile;
            list.AddRange(edits);
            caves.Add(g);
            return true;
        }

        public static bool Vertical(FeatureEdit e) => Underground.Same(e.Ax, e.Az, e.Bx, e.Bz);

        static float Ground(TerrainDetail detail, float cm, float x, float z) => detail.HeightAt(x / cm, z / cm);

        static bool Run(GeneratedWorld w, TerrainDetail detail, List<FeatureEdit> list, List<FeatureEdit> edits, CaveGraph g, int ia, int ib, float lead, float open, float mouth, float grain, ref Rng r)
        {
            var a = g.Nodes[ia];
            var b = g.Nodes[ib];
            float cm = w.Config.CellMeters, dx = b.X - a.X, dz = b.Z - a.Z, len = (float)Math.Sqrt(dx * dx + dz * dz);
            var ts = new List<float> { 0f };
            if (lead > 0f) ts.Add(lead / len);
            float t0 = ts[ts.Count - 1];
            int pieces = Math.Max(1, (int)Math.Ceiling((1f - t0) * len / Underground.PieceM));
            for (int s = 1; s <= pieces; s++) ts.Add(t0 + (1f - t0) * s / pieces);
            var pts = new float[ts.Count * 3];
            for (int s = 0; s < ts.Count; s++)
            {
                pts[s * 3] = a.X + dx * ts[s];
                pts[s * 3 + 1] = a.Y + (b.Y - a.Y) * ts[s];
                pts[s * 3 + 2] = a.Z + dz * ts[s];
            }
            float lift = Radius * Features.CaveHollow;
            for (int s = 0; s + 1 < ts.Count; s++)
            {
                var e = new FeatureEdit { Kind = FeatureEdit.Carve, Shape = FeatureEdit.Mine, R = Radius, Ax = pts[s * 3], Ay = pts[s * 3 + 1] + lift, Az = pts[s * 3 + 2], Bx = pts[s * 3 + 3], By = pts[s * 3 + 4] + lift, Bz = pts[s * 3 + 5], N = Lumps * Radius, S = r.NextFloat(), F = grain }.Finer();
                if (!Fits(w, detail, list, e, s == 0 ? open : 0f, mouth)) return false;
                edits.Add(e);
            }
            g.Edges.Add(new CaveEdge { A = ia, B = ib, R = Radius, Points = pts });
            return true;
        }

        static bool Fits(GeneratedWorld w, TerrainDetail detail, List<FeatureEdit> list, FeatureEdit e, float open, float mouth)
        {
            float cm = w.Config.CellMeters, ex = e.Bx - e.Ax, ez = e.Bz - e.Az, len = (float)Math.Sqrt(ex * ex + ez * ez);
            int steps = Math.Max(2, (int)Math.Ceiling(len / Features.RoofSampleM));
            for (int t = 1; t <= steps; t++)
            {
                float f = t / (float)steps, x = e.Ax + ex * f, z = e.Az + ez * f, ground = Ground(detail, cm, x, z);
                if (f * len < open + Underground.MouthM && ground >= mouth) continue;
                if (ground < e.Ay + (e.By - e.Ay) * f + e.R + RoofM) return false;
            }
            return Features.Inside(e, cm) && !Underground.Wet(w, e) && Clear(list, e);
        }

        static bool Clear(List<FeatureEdit> list, FeatureEdit e)
        {
            foreach (var o in list)
                if (e.MinX < o.MaxX && e.MaxX > o.MinX && e.MinY < o.MaxY && e.MaxY > o.MinY && e.MinZ < o.MaxZ && e.MaxZ > o.MinZ) return false;
            return true;
        }
    }
}
