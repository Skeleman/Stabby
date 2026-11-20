using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Stabby.Game.Editor
{
    static class SpikeProbe
    {
        static readonly float Limit = Arg();
        public static bool On => Limit > 0;
        static readonly List<(string name, ProfilerRecorder rec)> recs = new List<(string, ProfilerRecorder)>();
        static readonly HashSet<string> names = new HashSet<string>();
        static readonly List<string> report = new List<string>();
        static string prev = "";
        static int frame;

        public static void Tick()
        {
            if (!On) return;
            if (frame++ % 120 == 0) Scan();
            var top = recs.Select(r => (r.name, ms: r.rec.LastValue / 1e6)).Where(r => r.ms >= 3).OrderByDescending(r => r.ms).Take(14);
            string now = string.Join(", ", top.Select(r => $"{r.name} {r.ms:F0}"));
            float dt = Time.unscaledDeltaTime * 1000f;
            if (frame > 10 && dt > Limit && report.Count < 40) report.Add($"frame {frame} {dt:F0} ms\n  this: {now}\n  prev: {prev}");
            prev = now;
        }

        public static string Report() => On ? $"spikes over {Limit:F0} ms ({recs.Count} markers watched):\n" + string.Join("\n", report) : "";

        static float Arg()
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-stabbySpikes");
            if (i < 0) return 0;
            return i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms) ? ms : 33f;
        }

        static void Scan()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !names.Add(d.Name)) continue;
                recs.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1)));
            }
        }

        public static void Stop()
        {
            foreach (var r in recs) r.rec.Dispose();
            recs.Clear();
        }
    }
}
