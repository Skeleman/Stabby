using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Stabby.Game.Editor
{
    static class SpikeProbe
    {
        static readonly float Limit = Arg();
        public static bool On => Limit > 0;
        static readonly List<(string name, ProfilerRecorder rec)> recs = new List<(string, ProfilerRecorder)>();
        static readonly HashSet<string> names = new HashSet<string>();
        static readonly List<string> report = new List<string>();
        static int frame, gcRuns, builtSpikes, loadSpikes;
        static long gcBuilt, gcBefore, gcPlay;
        static int playFrames;
        static ProfilerRecorder gcFrame;
        const int Segment = 2400;
        static int recorded, resumes;
        static int gcPlayRuns, gcTourRuns, tourFrames;
        static long gcTour;
        static bool counting, touringNow;
        public static bool Touring;

        public static void Exit()
        {
            int runs = GC.CollectionCount(0);
            if (counting && runs != gcRuns) gcPlayRuns += runs - gcRuns;
            if (counting && touringNow && runs != gcRuns) gcTourRuns += runs - gcRuns;
            gcRuns = runs;
            counting = GameStatus.TerrainBuilt && !GameStatus.AfterTool;
            touringNow = Touring;
        }

        static readonly bool Record = Array.IndexOf(Environment.GetCommandLineArgs(), "-stabbyProfile") >= 0;

        public static void Tick()
        {
            if (Record)
            {
                bool play = GameStatus.TerrainBuilt && !GameStatus.AfterTool && recorded < Segment;
                if (play && !Profiler.enabled)
                {
                    Profiler.logFile = System.IO.Path.GetFullPath($"../out/unity/spike-{resumes++:D3}.raw");
                    Profiler.enableBinaryLog = true;
                    Profiler.enableAllocationCallstacks = true;
                    Profiler.enabled = true;
                }
                else if (!play && Profiler.enabled) { Profiler.enabled = false; Profiler.logFile = ""; }
                if (play) recorded++;
            }
            if (!On) return;
            if (!gcFrame.Valid) gcFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            long gc = gcFrame.LastValue;
            if (GameStatus.TerrainBuilt) gcBuilt += gc; else gcBefore += gc;
            if (GameStatus.TerrainBuilt && !GameStatus.AfterTool) { gcPlay += gc; playFrames++; if (Touring) { gcTour += gc; tourFrames++; } }
            bool collected = GC.CollectionCount(0) != gcRuns;
            if (frame++ % (frame < 300 ? 5 : 120) == 0) Scan();
            if (cur.Length < recs.Count) { Array.Resize(ref cur, recs.Count); Array.Resize(ref last, recs.Count); }
            for (int r = 0; r < recs.Count; r++) cur[r] = recs[r].rec.LastValue;
            float dt = Time.unscaledDeltaTime * 1000f;
            if (frame > 10 && dt > Limit && !GameStatus.AfterTool && (GameStatus.TerrainBuilt ? builtSpikes++ : loadSpikes++) < 40) report.Add($"frame {frame}{(GameStatus.TerrainBuilt ? " built" : "")} {dt:F0} ms, gc {gc / 1024} KB{(collected ? ", collection" : "")}\n  this: {Top(cur)}\n  prev: {Top(last)}");
            (cur, last) = (last, cur);
        }

        static long[] cur = new long[0], last = new long[0];

        static string Top(long[] ns) =>
            string.Join(", ", Enumerable.Range(0, recs.Count).Select(r => (recs[r].name, ms: ns[r] / 1e6)).Where(r => r.ms >= 1).OrderByDescending(r => r.ms).Take(80).Select(r => $"{r.name} {r.ms:F0}"));

        public static string Report() => On ? $"GC allocated in frames: {gcBefore / 1048576} MB before terrain built, {gcBuilt / 1048576} MB after, {gcPlay / 1024 / System.Math.Max(1, playFrames)} KB per play frame over {playFrames} frames, {gcRuns} collections, {gcPlayRuns} in play frames\ntour: {gcTour / 1024 / System.Math.Max(1, tourFrames)} KB per frame over {tourFrames} frames, {gcTourRuns} collections\nspikes over {Limit:F0} ms ({recs.Count} markers watched):\n" + string.Join("\n", report) : "";

        static float Arg()
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-stabbySpikes");
            if (i < 0) return 0;
            return i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms) ? ms : 33f;
        }

        static readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
        static int scanned;

        static void Scan()
        {
            ProfilerRecorderHandle.GetAvailable(handles);
            if (handles.Count == scanned) return;
            scanned = handles.Count;
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !names.Add(d.Name)) continue;
                recs.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1)));
            }
        }

        public static void Stop()
        {
            if (Record) { Profiler.enabled = false; Profiler.logFile = ""; ProfileDump.StacksHere(); }
            foreach (var r in recs) r.rec.Dispose();
            if (gcFrame.Valid) gcFrame.Dispose();
            recs.Clear();
        }
    }
}
