using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Stabby.Game.Editor
{
    static class ProfileDump
    {
        public static void Run()
        {
            string outPath = System.IO.Path.GetFullPath("../out/unity/spike-dump.txt");
            var sb = new StringBuilder();
            var gc = new Dictionary<string, long>();
            var seen = new Dictionary<string, int>();
            var stacks = new Dictionary<string, long>();
            int counted = 0;
            foreach (string raw in System.IO.Directory.GetFiles(System.IO.Path.GetFullPath("../out/unity"), "spike*.raw").OrderBy(f => f))
            {
                if (!ProfilerDriver.LoadProfile(raw, false)) { sb.AppendLine($"load failed {raw}"); continue; }
                sb.AppendLine($"== {System.IO.Path.GetFileName(raw)}");
                counted += Dump(sb, gc, seen);
                Stacks(stacks);
            }
            sb.AppendLine($"=== GC by caller over {counted} frames, capture work left out (KB total)");
            foreach (var kv in gc.OrderByDescending(kv => kv.Value).Take(60)) sb.AppendLine($"{kv.Value / 1024} {kv.Key}");
            sb.AppendLine($"=== GC by caller in over a tenth of frames (bytes per frame, frames seen)");
            foreach (var kv in gc.Where(kv => seen[kv.Key] * 10 > counted).OrderByDescending(kv => kv.Value).Take(80)) sb.AppendLine($"{kv.Value / System.Math.Max(1, counted)} {seen[kv.Key]} {kv.Key}");
            sb.AppendLine($"=== GC by call stack over {counted} frames (bytes per frame)");
            foreach (var kv in stacks.OrderByDescending(kv => kv.Value).Take(60)) sb.AppendLine($"{kv.Value / System.Math.Max(1, counted)} {kv.Key}");
            System.IO.File.WriteAllText(outPath, sb.ToString());
            EditorApplication.Exit(0);
        }

        public static void StacksHere()
        {
            var stacks = new Dictionary<string, long>();
            int counted = 0;
            foreach (string raw in System.IO.Directory.GetFiles(System.IO.Path.GetFullPath("../out/unity"), "spike*.raw").OrderBy(f => f))
            {
                if (!ProfilerDriver.LoadProfile(raw, false)) continue;
                counted += ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex + 1;
                Stacks(stacks);
            }
            var sb = new StringBuilder($"GC by call stack over {counted} frames (bytes per frame)\n");
            foreach (var kv in stacks.OrderByDescending(kv => kv.Value).Take(120)) sb.AppendLine($"{kv.Value / System.Math.Max(1, counted)} {kv.Key}");
            System.IO.File.WriteAllText(System.IO.Path.GetFullPath("../out/unity/spike-stacks.txt"), sb.ToString());
        }

        static int Dump(StringBuilder sb, Dictionary<string, long> gc, Dictionary<string, int> seen)
        {
            var frames = new List<(int f, float ms)>();
            for (int f = ProfilerDriver.firstFrameIndex; f <= ProfilerDriver.lastFrameIndex; f++)
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    if (v.valid) frames.Add((f, v.frameTimeMs - Named(v, v.GetRootItemID(), "GameCapture.Poll")));
            sb.AppendLine($"{frames.Count} frames, first {ProfilerDriver.firstFrameIndex}");
            var asked = (System.Environment.GetEnvironmentVariable("STABBY_DUMP_FRAMES") ?? "").Split(',').Where(s => int.TryParse(s, out _)).Select(int.Parse).ToHashSet();
            foreach (var (f, ms) in frames.OrderByDescending(x => x.ms).Take(2).Concat(frames.Where(x => asked.Contains(x.f)).OrderBy(x => x.f)))
            {
                sb.AppendLine($"=== frame {f} {ms:F0} ms");
                for (int t = 0; ; t++)
                {
                    using var v = ProfilerDriver.GetHierarchyFrameDataView(f, t, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
                    if (!v.valid) break;
                    float min = asked.Contains(f) ? 0.5f : t == 0 ? 2f : 10f;
                    var lines = new StringBuilder();
                    Walk(v, v.GetRootItemID(), 0, min, lines);
                    if (lines.Length > 0) sb.Append($"-- thread {v.threadName}\n").Append(lines);
                }
            }
            int counted = 0;
            foreach (var (f, _) in frames)
            {
                var one = new Dictionary<string, long>();
                counted++;
                for (int t = 0; ; t++)
                    using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, t, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnGcMemory, false))
                    {
                        if (!v.valid) break;
                        var path = new List<string>();
                        if (t > 0) path.Add(v.threadName);
                        Allocs(v, v.GetRootItemID(), path, one);
                    }
                foreach (var kv in one) { gc[kv.Key] = (gc.TryGetValue(kv.Key, out var o) ? o : 0) + kv.Value; seen[kv.Key] = (seen.TryGetValue(kv.Key, out var n) ? n : 0) + 1; }
            }
            return counted;
        }

        static void Stacks(Dictionary<string, long> stacks)
        {
            var addrs = new List<ulong>();
            var names = new Dictionary<ulong, string>();
            for (int f = ProfilerDriver.firstFrameIndex; f <= ProfilerDriver.lastFrameIndex; f++)
                for (int t = 0; ; t++)
                    using (var raw = ProfilerDriver.GetRawFrameDataView(f, t))
                    {
                        if (!raw.valid) break;
                        int alloc = raw.GetMarkerId("GC.Alloc");
                        if (alloc == FrameDataView.invalidMarkerId) continue;
                        string thread = raw.threadName.StartsWith("Thread Pool") ? "pool" : raw.threadName;
                        for (int i = 0; i < raw.sampleCount; i++)
                        {
                            if (raw.GetSampleMarkerId(i) != alloc) continue;
                            long size = raw.GetSampleMetadataCount(i) > 0 ? raw.GetSampleMetadataAsLong(i, 0) : 0;
                            raw.GetSampleCallstack(i, addrs);
                            var key = new StringBuilder(thread).Append(" #").Append(addrs.Count);
                            int used = 0;
                            foreach (ulong a in addrs)
                            {
                                if (!names.TryGetValue(a, out var n)) { var m = raw.ResolveMethodInfo(a); names[a] = n = m.methodName ?? "?" + a.ToString("x"); }
                                if (string.IsNullOrEmpty(n) || n[0] == '?' || n.StartsWith("System.") || n.StartsWith("UnityEngine.Profiling")) continue;
                                key.Append(" < ").Append(n);
                                if (++used == 7) break;
                            }
                            string k = key.ToString();
                            stacks[k] = (stacks.TryGetValue(k, out var o) ? o : 0) + size;
                        }
                    }
        }

        static bool Allocs(HierarchyFrameDataView v, int root, List<string> path, Dictionary<string, long> gc)
        {
            void Visit(int id)
            {
                var kids = new List<int>();
                v.GetItemChildren(id, kids);
                foreach (int k in kids)
                {
                    string name = v.GetItemName(k);
                    if (name == "GC.Alloc")
                    {
                        string at = path.Count == 0 ? "?" : string.Join(" > ", path.Skip(System.Math.Max(0, path.Count - 3)));
                        gc[at] = (gc.TryGetValue(at, out var o) ? o : 0) + (long)v.GetItemColumnDataAsDouble(k, HierarchyFrameDataView.columnGcMemory);
                        continue;
                    }
                    path.Add(name);
                    Visit(k);
                    path.RemoveAt(path.Count - 1);
                }
            }
            Visit(root);
            return true;
        }

        static float Named(HierarchyFrameDataView v, int id, string part)
        {
            var kids = new List<int>();
            v.GetItemChildren(id, kids);
            float sum = 0;
            foreach (int k in kids)
                sum += v.GetItemName(k).Contains(part) ? v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime) : Named(v, k, part);
            return sum;
        }

        static void Walk(HierarchyFrameDataView v, int id, int depth, float min, StringBuilder sb)
        {
            var kids = new List<int>();
            v.GetItemChildren(id, kids);
            foreach (int k in kids)
            {
                float total = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
                if (total < min) continue;
                float self = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnSelfTime);
                sb.Append(' ', depth * 2).Append($"{v.GetItemName(k)} {total:F1} (self {self:F1})\n");
                if (depth < 30) Walk(v, k, depth + 1, min, sb);
            }
        }
    }
}
