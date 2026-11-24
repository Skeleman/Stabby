using System.Collections.Generic;

namespace Stabby.Game
{
    public static class GameStatus
    {
        public static readonly Dictionary<string, string> Lines = new Dictionary<string, string>();
        public static readonly Dictionary<string, System.Func<string>> Live = new Dictionary<string, System.Func<string>>();
        public static readonly HashSet<string> Synced = new HashSet<string>();
        public static int Capsules, TerrainBehind, ToolFrame = -100, ToolFrames, TerrainVertices;
        public static float TerrainMs;
        public static bool TerrainBuilt, HoldCamera;
        public static string Failure, TerrainStage;
        public static int Frames, SlowFrames;
        public static float WorstMs, WorstPlayMs;
        public static string WorstWhy;
        public const int ToolSettleFrames = 10;
        public static bool AfterTool => UnityEngine.Time.frameCount - ToolFrame <= ToolSettleFrames;

        public static bool Line(string key, out string line)
        {
            if (Live.TryGetValue(key, out var f)) { line = f(); return true; }
            return Lines.TryGetValue(key, out line);
        }

        public static void PutFrames()
        {
            foreach (var kv in Live) Lines[kv.Key] = kv.Value();
            if (Frames > 0) Lines["~frames"] = $"frames {Frames}, worst {WorstMs:F0} ms ({WorstWhy}), {SlowFrames} over 33 ms, worst after terrain built {WorstPlayMs:F0} ms, {ToolFrames} capture frames left out";
        }
    }

    public sealed class MainCost
    {
        long t0, b0, ticks, bytes, peakTicks, peakBytes;
        int frames, arrivals;

        public void Begin()
        {
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            b0 = System.GC.GetAllocatedBytesForCurrentThread();
        }

        public void End(bool worked, int arrived)
        {
            if (!worked) return;
            long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0, db = System.GC.GetAllocatedBytesForCurrentThread() - b0;
            ticks += dt;
            bytes += db;
            frames++;
            arrivals += arrived;
            if (dt > peakTicks) peakTicks = dt;
            if (db > peakBytes) peakBytes = db;
        }

        static double Ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public string Text(string what)
        {
            if (arrivals == 0) return $"no {what} yet";
            return $"{arrivals} {what}: main thread {Ms(ticks) / arrivals:F3} ms and {bytes / 1024.0 / arrivals:F1} KB GC per arrival over {frames} frames, worst frame {Ms(peakTicks):F2} ms, {peakBytes / 1024.0:F1} KB";
        }
    }

    public sealed class CostSplit
    {
        readonly string[] names;
        readonly long[] sums, worst;
        long worstTotal;
        int frames;

        public CostSplit(params string[] names)
        {
            this.names = names;
            sums = new long[names.Length];
            worst = new long[names.Length];
        }

        public void Add(params long[] parts)
        {
            long total = 0;
            for (int i = 0; i < parts.Length; i++) { sums[i] += parts[i]; total += parts[i]; }
            frames++;
            if (total <= worstTotal) return;
            worstTotal = total;
            System.Array.Copy(parts, worst, parts.Length);
        }

        static double Ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public string Text()
        {
            if (frames == 0) return "split none";
            var sb = new System.Text.StringBuilder("split per working frame");
            for (int i = 0; i < names.Length; i++) sb.Append($" {names[i]} {Ms(sums[i]) / frames:F3}");
            sb.Append($" ms; worst frame {Ms(worstTotal):F2} ms =");
            for (int i = 0; i < names.Length; i++) sb.Append($" {names[i]} {Ms(worst[i]):F2}");
            return sb.ToString();
        }
    }
}
