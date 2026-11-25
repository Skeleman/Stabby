using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;

namespace Stabby.Game.Editor
{
    static class SystemCost
    {
        static readonly string[] Watched = { nameof(PlayerMoveSystem), nameof(ClientChunkReceiveSystem), nameof(ClientMapSystem), nameof(ObjectColumnsSystem), nameof(ServerObjectDeltaSystem), nameof(ClientObjectDeltaSystem) };
        public static readonly bool On = Environment.GetEnvironmentVariable("STABBY_SYSTEM_COST") == "1";
        static readonly Dictionary<string, (ProfilerRecorder rec, List<float> ms)> recs = new Dictionary<string, (ProfilerRecorder, List<float>)>();
        static readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
        static readonly HashSet<ProfilerRecorderHandle> seen = new HashSet<ProfilerRecorderHandle>();
        static ProfilerRecorder main;
        static readonly List<float> mainMs = new List<float>();
        static int frame;

        public static void Tick()
        {
            if (!On) return;
            if (!main.Valid) main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            else mainMs.Add(main.LastValue / 1e6f);
            foreach (var r in recs.Values) r.ms.Add(r.rec.LastValue / 1e6f);
            if (frame++ % 120 != 0) return;
            handles.Clear();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !Watched.Any(w => d.Name.Contains(w)) || !seen.Add(h)) continue;
                string key = $"{d.Name} [{d.Category.Name} {seen.Count}]";
                var rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                rec.Start();
                recs[key] = (rec, new List<float>());
            }
        }

        static string Line(string name, List<float> ms)
        {
            var xs = ms.OrderBy(x => x).ToList();
            float P(float p) => xs.Count == 0 ? 0 : xs[Math.Min(xs.Count - 1, (int)(xs.Count * p))];
            return $"{name}: {xs.Count} frames, total {xs.Sum():F1} ms, mean {(xs.Count == 0 ? 0 : xs.Average()):F3} p50 {P(0.5f):F3} p99 {P(0.99f):F3} max {(xs.Count == 0 ? 0 : xs[xs.Count - 1]):F2} ms";
        }

        public static string Report() => "system cost (main-thread markers):\n  " + string.Join("\n  ", recs.OrderBy(k => k.Key).Select(k => Line(k.Key, k.Value.ms)).Prepend(Line("Main Thread", mainMs)));

        public static void Stop()
        {
            foreach (var r in recs.Values) r.rec.Dispose();
            recs.Clear();
            seen.Clear();
            if (main.Valid) main.Dispose();
        }
    }
}
