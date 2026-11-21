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
            System.IO.File.WriteAllText(outPath, sb.ToString());
            EditorApplication.Exit(0);
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
