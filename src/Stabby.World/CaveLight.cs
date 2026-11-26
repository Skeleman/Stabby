using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class CaveLight
    {
        public const float Unlit = 1e6f;

        public static bool Lit(FeatureEdit e) => e.CaveCarve;

        public static float[] Distances(FeatureEdit[] edits, CaveGraph[] caves)
        {
            var keys = new Dictionary<long, int>();
            var ends = new int[2 * edits.Length];
            for (int i = 0; i < edits.Length; i++)
            {
                var e = edits[i];
                if (!Lit(e)) { ends[2 * i] = ends[2 * i + 1] = -1; continue; }
                ends[2 * i] = Node(keys, e.Ax, e.Az);
                ends[2 * i + 1] = Node(keys, e.Bx, e.Bz);
            }
            int n = keys.Count;
            var links = new List<KeyValuePair<int, float>>[n];
            for (int k = 0; k < n; k++) links[k] = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < edits.Length; i++)
            {
                int a = ends[2 * i], b = ends[2 * i + 1];
                if (a < 0 || a == b) continue;
                var e = edits[i];
                float dx = e.Bx - e.Ax, dy = e.By - e.Ay, dz = e.Bz - e.Az, len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                links[a].Add(new KeyValuePair<int, float>(b, len));
                links[b].Add(new KeyValuePair<int, float>(a, len));
            }
            var dist = new float[n];
            for (int k = 0; k < n; k++) dist[k] = Unlit;
            var open = new SortedSet<(float, int)>();
            if (caves != null)
                foreach (var g in caves)
                    if (!DungeonLinks.IsLink(g))
                        foreach (var node in g.Nodes)
                            if (node.Entrance && keys.TryGetValue(Key(node.X, node.Z), out int s) && dist[s] > 0)
                            {
                                dist[s] = 0;
                                open.Add((0f, s));
                            }
            while (open.Count > 0)
            {
                var top = open.Min;
                open.Remove(top);
                int u = top.Item2;
                foreach (var l in links[u])
                {
                    float d = dist[u] + l.Value;
                    if (d >= dist[l.Key]) continue;
                    if (dist[l.Key] < Unlit) open.Remove((dist[l.Key], l.Key));
                    dist[l.Key] = d;
                    open.Add((d, l.Key));
                }
            }
            var result = new float[2 * edits.Length];
            for (int i = 0; i < edits.Length; i++)
            {
                if (ends[2 * i] < 0) continue;
                result[2 * i] = dist[ends[2 * i]];
                result[2 * i + 1] = dist[ends[2 * i + 1]];
            }
            return result;
        }

        static long Key(float x, float z) => ((long)Math.Round(x * 16f) << 32) ^ (uint)(int)Math.Round(z * 16f);

        static int Node(Dictionary<long, int> keys, float x, float z)
        {
            long k = Key(x, z);
            if (!keys.TryGetValue(k, out int i)) keys[k] = i = keys.Count;
            return i;
        }
    }
}
