#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Stabby.Game
{
    public partial class TerrainViewSystem
    {
        static readonly bool VerifyNodes = Environment.GetEnvironmentVariable("STABBY_LOD_VERIFY") == "1";
        readonly Dictionary<int, (int drawn, int cast, int stitch, float end)> verifyNodes = new Dictionary<int, (int, int, int, float)>();
        readonly HashSet<int> verifyEvicts = new HashSet<int>();
        readonly HashSet<int> verifyDrawn = new HashSet<int>();
        readonly List<string> verifyWrites = new List<string>();
        readonly int[] verifyStats = new int[TerrainNodesJob.StatCount];
        long verifyFrames, verifyKeys, verifyWriteCount, verifyMismatches;
        string verifyFirst = "";

        static string WriteKey(TerrainWrite w) => $"{w.Entity.Index}:{w.Entity.Version}:{w.Kind}:{w.Value}";

        partial void Expect(TerrainDrawJob walk)
        {
            if (!VerifyNodes) return;
            verifyDrawn.Clear();
            for (int r = 0; r < roots.Length; r++) Draw(roots[r]);
        }

        bool Built(int key) => nodes.TryGetValue(key, out var v) && v.Built != 0;

        bool Coverable(int key)
        {
            if (Built(key)) return true;
            if (!split.Contains(key)) return false;
            for (int q = 0; q < 4; q++)
            {
                int c = tree.Child(key, q);
                if (c >= 0 && !Coverable(c)) return false;
            }
            return true;
        }

        void Draw(int key)
        {
            if (split.Contains(key))
            {
                bool all = true;
                for (int q = 0; q < 4 && all; q++)
                {
                    int c = tree.Child(key, q);
                    all = c < 0 || Coverable(c);
                }
                if (all)
                {
                    for (int q = 0; q < 4; q++)
                    {
                        int c = tree.Child(key, q);
                        if (c >= 0) Draw(c);
                    }
                    return;
                }
            }
            if (Built(key)) verifyDrawn.Add(key);
        }

        partial void Expect(TerrainNodesJob job)
        {
            if (!VerifyNodes) return;
            verifyNodes.Clear();
            verifyEvicts.Clear();
            verifyWrites.Clear();
            Array.Clear(verifyStats, 0, verifyStats.Length);
            float3 eye = job.Eye;
            float floor = float.MaxValue;
            if (job.Reach > 0)
                foreach (int key in meshes.Keys)
                {
                    MinMaxAABB fb = tree.Box(key);
                    if (fb.Min.y < floor && math.length(math.max(0, math.max(fb.Min - eye, eye - fb.Max))) < job.Reach) floor = fb.Min.y;
                }
            foreach (int key in meshes.Keys)
            {
                var nd = nodes[key];
                bool want = verifyDrawn.Contains(key);
                if (!want && !InTree(key))
                {
                    verifyEvicts.Add(key);
                    continue;
                }
                bool show = want && !(HideSunk && tree.Sunk(key));
                verifyStats[TerrainNodesJob.Sunk] += want && !show ? 1 : 0;
                if (show != (nd.Drawn != 0)) verifyWrites.Add(WriteKey(new TerrainWrite { Entity = nd.Entity, Kind = 0, Value = show ? 1 : 0 }));
                MinMaxAABB nb = tree.Box(key);
                int cast = show && CastsNear(nb, eye, job.Reach, floor, job.Upsun, job.TanE) && Resolved(nb, eye, nd.Cast != 0) ? 1 : 0;
                if (cast != nd.Cast)
                {
                    verifyWrites.Add(WriteKey(new TerrainWrite { Entity = nd.Caster, Kind = 0, Value = cast != 0 ? 1 : 0 }));
                    verifyStats[TerrainNodesJob.Shadows]++;
                }
                verifyStats[TerrainNodesJob.Casting] += cast;
                verifyStats[TerrainNodesJob.CastVertices] += cast * nd.Vertices;
                int stitch = nd.Stitch;
                float end = nd.MorphEnd;
                if (show)
                {
                    int l = tree.Level(key);
                    verifyStats[TerrainNodesJob.Levels + l]++;
                    verifyStats[TerrainNodesJob.Vertices] += nd.Vertices;
                    int mask = StitchMask(key, l);
                    bool thin = Fine(key, l);
                    float e = thin ? TerrainTree.Step(0) / job.Allowed : l < TerrainTree.Root ? TerrainTree.Step(l + 1) / job.Allowed : 0;
                    if (mask != nd.Stitch || e != nd.MorphEnd)
                    {
                        var value = new float4(mask, TerrainNodesJob.MorphStart * e, e, thin ? TerrainTree.FineStep : TerrainTree.Step(l));
                        verifyWrites.Add(WriteKey(new TerrainWrite { Entity = nd.Entity, Kind = 1, Value = value }));
                        verifyWrites.Add(WriteKey(new TerrainWrite { Entity = nd.Caster, Kind = 1, Value = value }));
                        (stitch, end) = (mask, e);
                    }
                }
                verifyNodes[key] = (show ? 1 : 0, cast, stitch, end);
            }
        }

        partial void Compare()
        {
            if (!VerifyNodes) return;
            int bad = 0;
            void Miss(string what)
            {
                bad++;
                if (verifyFirst.Length == 0) verifyFirst = $" first: frame {verifyFrames} {what}";
            }
            foreach (var kv in verifyNodes)
            {
                var nd = nodes[kv.Key];
                var x = kv.Value;
                if (nd.Drawn != x.drawn || nd.Cast != x.cast || nd.Stitch != x.stitch || nd.MorphEnd != x.end)
                    Miss($"node {kv.Key} job {nd.Drawn}/{nd.Cast}/{nd.Stitch}/{nd.MorphEnd} old {x.drawn}/{x.cast}/{x.stitch}/{x.end}");
            }
            int walked = 0;
            foreach (int key in drawn)
                if (verifyDrawn.Contains(key)) walked++;
            if (walked != verifyDrawn.Count || drawn.Count != verifyDrawn.Count) Miss($"drawn job {drawn.Count} old {verifyDrawn.Count} shared {walked}");
            var evicts = new HashSet<int>();
            for (int i = 0; i < nodeEvicts.Length; i++) evicts.Add(nodeEvicts[i]);
            if (!evicts.SetEquals(verifyEvicts)) Miss($"evicts job {evicts.Count} old {verifyEvicts.Count}");
            foreach (int s in new[] { TerrainNodesJob.Sunk, TerrainNodesJob.Vertices, TerrainNodesJob.Casting, TerrainNodesJob.CastVertices, TerrainNodesJob.Shadows })
                if (nodeStats[s] != verifyStats[s]) Miss($"stat {s} job {nodeStats[s]} old {verifyStats[s]}");
            for (int l = 0; l < TerrainTree.Levels; l++)
                if (nodeStats[TerrainNodesJob.Levels + l] != verifyStats[TerrainNodesJob.Levels + l]) Miss($"level {l}");
            var hidden = new HashSet<Entity>();
            foreach (int key in verifyEvicts) { var nd = nodes[key]; hidden.Add(nd.Entity); hidden.Add(nd.Caster); }
            var got = new List<string>();
            int hides = 0;
            for (int i = 0; i < nodeWrites.Length; i++)
            {
                var w = nodeWrites[i];
                if (w.Kind == 0 && w.Value.x == 0 && hidden.Contains(w.Entity)) { hides++; continue; }
                got.Add(WriteKey(w));
            }
            if (hides != nodeStats[TerrainNodesJob.Hidden]) Miss($"hidden job {nodeStats[TerrainNodesJob.Hidden]} writes {hides}");
            if (!got.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(verifyWrites.OrderBy(s => s, StringComparer.Ordinal)))
            {
                string extra = string.Join(" ", got.Except(verifyWrites)), lost = string.Join(" ", verifyWrites.Except(got));
                Miss($"writes job {got.Count} old {verifyWrites.Count} job-only [{extra}] old-only [{lost}]");
            }
            verifyFrames++;
            verifyKeys += verifyNodes.Count + verifyEvicts.Count + verifyDrawn.Count;
            verifyWriteCount += verifyWrites.Count;
            verifyMismatches += bad;
            GameStatus.Lines["terrain~verify"] = $"terrain nodes job verify: {verifyFrames} frames, {verifyKeys} node checks, {verifyWriteCount} writes, {verifyMismatches} mismatches{verifyFirst}";
        }

        bool FineAt(float x, float z)
        {
            int k = tree.At(x, z, 0);
            return k >= 0 && fine.Contains(k);
        }

        bool InTree(int key)
        {
            int parent = tree.Parent(key);
            return parent < 0 || split.Contains(parent);
        }

        int DrawnLevel(float x, float z)
        {
            for (int l = TerrainTree.Root; l >= 0; l--)
            {
                int key = tree.At(x, z, l);
                if (key < 0) return -1;
                if (verifyDrawn.Contains(key)) return l;
            }
            return -1;
        }

        int StitchMask(int key, int l)
        {
            var c = tree.Cell(key, l);
            float s = TerrainTree.Size(l), x0 = c.x * s, z0 = c.y * s, x1 = x0 + s, z1 = z0 + s, o = 0.5f;
            float xa = x0 + 0.25f * s, xb = x0 + 0.75f * s, za = z0 + 0.25f * s, zb = z0 + 0.75f * s;
            int mask = 0;
            bool thin = Fine(key, l);
            mask |= Edge(x0 - o, za, x0 - o, zb, l, 1, thin);
            mask |= Edge(x1 + o, za, x1 + o, zb, l, 2, thin);
            mask |= Edge(xa, z0 - o, xb, z0 - o, l, 4, thin);
            mask |= Edge(xa, z1 + o, xb, z1 + o, l, 8, thin);
            for (int q = 0; q < 4; q++)
            {
                float xo = (q & 1) == 0 ? x0 - o : x1 + o, xi = (q & 1) == 0 ? x0 + o : x1 - o;
                float zo = (q & 2) == 0 ? z0 - o : z1 + o, zi = (q & 2) == 0 ? z0 + o : z1 - o;
                mask |= Edge(xo, zi, xi, zo, l, 16 << q, thin) | Edge(xo, zo, xo, zo, l, 16 << q, thin);
            }
            return mask;
        }

        int Edge(float xa, float za, float xb, float zb, int l, int bit, bool thin)
        {
            int a = DrawnLevel(xa, za), b = DrawnLevel(xb, zb), mask = 0;
            bool fa = a == 0 && FineAt(xa, za), fb = b == 0 && FineAt(xb, zb);
            if (a > l || b > l || thin && (a == 0 && !fa || b == 0 && !fb)) mask |= bit;
            if ((a >= 0 && a < l) || (b >= 0 && b < l) || l == 0 && !thin && (fa || fb)) mask |= bit << 8;
            return mask;
        }

        bool Resolved(MinMaxAABB nb, float3 eye, bool was)
        {
            float dist = math.length(math.max(0, math.max(nb.Min - eye, eye - nb.Max)));
            return ShadowViewSystem.Map.Holds(math.cmax(nb.Max - nb.Min), dist, viewQuality, was);
        }

        static bool CastsNear(MinMaxAABB nb, float3 eye, float reach, float floor, float2 upsun, float tanE)
        {
            float run = (nb.Max.y - math.min(floor, nb.Min.y)) / tanE;
            int steps = (int)math.clamp(math.ceil(run / math.max(math.cmin((nb.Max - nb.Min).xz), 1e-3f)), 1, 16);
            for (int k = 0; k <= steps; k++)
            {
                float2 o = -upsun * run * k / steps;
                float3 shift = new float3(o.x, 0, o.y);
                if (math.length(math.max(0, math.max(nb.Min + shift - eye, eye - nb.Max - shift))) < reach) return true;
            }
            return false;
        }
    }
}
#endif
