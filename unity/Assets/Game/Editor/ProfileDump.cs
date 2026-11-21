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
            string raw = System.IO.Path.GetFullPath("../out/unity/spike.raw"), outPath = System.IO.Path.GetFullPath("../out/unity/spike-dump.txt");
            var sb = new StringBuilder();
            if (!ProfilerDriver.LoadProfile(raw, false)) { System.IO.File.WriteAllText(outPath, "load failed"); EditorApplication.Exit(1); return; }
            var frames = new List<(int f, float ms)>();
            for (int f = ProfilerDriver.firstFrameIndex; f <= ProfilerDriver.lastFrameIndex; f++)
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    if (v.valid) frames.Add((f, v.frameTimeMs));
            sb.AppendLine($"{frames.Count} frames, first {ProfilerDriver.firstFrameIndex}");
            foreach (var (f, ms) in frames.OrderByDescending(x => x.ms).Take(8))
            {
                sb.AppendLine($"=== frame {f} {ms:F0} ms");
                for (int t = 0; ; t++)
                {
                    using var v = ProfilerDriver.GetHierarchyFrameDataView(f, t, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
                    if (!v.valid) break;
                    float min = t == 0 ? 2f : 10f;
                    var lines = new StringBuilder();
                    Walk(v, v.GetRootItemID(), 0, min, lines);
                    if (lines.Length > 0) sb.Append($"-- thread {v.threadName}\n").Append(lines);
                }
            }
            var gc = new Dictionary<string, long>();
            int counted = 0;
            foreach (var (f, _) in frames)
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnGcMemory, false))
                {
                    if (!v.valid) continue;
                    var path = new List<string>();
                    if (Allocs(v, v.GetRootItemID(), path, gc)) counted++;
                }
            sb.AppendLine($"=== GC by caller over {counted} frames, capture work left out (KB total)");
            foreach (var kv in gc.OrderByDescending(kv => kv.Value).Take(60)) sb.AppendLine($"{kv.Value / 1024} {kv.Key}");
            System.IO.File.WriteAllText(outPath, sb.ToString());
            EditorApplication.Exit(0);
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
                    if (name.Contains("GameCapture.Poll")) continue;
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
