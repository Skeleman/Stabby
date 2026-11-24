using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly string[] WalkParts = { "terrain", "objects", "collision", "grass", "map", "creatures" };

        static readonly (System.Type type, int part)[] WalkSystems =
        {
            (typeof(TerrainViewSystem), 0),
            (typeof(ObjectViewSystem), 1), (typeof(ObjectLodSystem), 1), (typeof(ContactFieldSystem), 1), (typeof(ClientObjectDeltaSystem), 1), (typeof(ServerObjectDeltaSystem), 1),
            (typeof(ObjectColumnsSystem), 2), (typeof(PlayerMoveSystem), 2),
            (typeof(GrassViewSystem), 3),
            (typeof(ClientMapSystem), 4), (typeof(ServerMapSystem), 4), (typeof(ClientChunkReceiveSystem), 4), (typeof(ServerChunkStreamSystem), 4),
            (typeof(CreatureBuildSystem), 5), (typeof(CreatureAnimSystem), 5), (typeof(CreatureDrawSystem), 5), (typeof(CreaturePlanSystem), 5), (typeof(CreatureHostSystem), 5),
        };

        const float FastWalkMps = 30f, FastWalkM = 2000f, NormalWalkM = 360f;

        static System.Collections.IEnumerator FastWalkTour(Camera cam)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            if (server == null || view == null || !hasGround) { GameStatus.Lines["~walk"] = "walk: no worlds or ground"; yield break; }
            GameStatus.HoldCamera = true;
            var args = System.Environment.GetCommandLineArgs();
            int fovAt = System.Array.IndexOf(args, "-stabbyWalkFov");
            cam.fieldOfView = fovAt >= 0 && fovAt + 1 < args.Length && float.TryParse(args[fovAt + 1], out float fov) ? fov : 60f;
            int resAt = System.Array.IndexOf(args, "-stabbyWalkRes");
            int rows = resAt >= 0 && resAt + 1 < args.Length && int.TryParse(args[resAt + 1], out int r) ? r : 1080;
            var target = new RenderTexture(rows * 16 / 9, rows, 24);
            cam.targetTexture = target;
            yield return null;
            var start = new Vector2(home.x, home.z);
            Vector2 d = WalkLine(ground, start, NormalWalkM + FastWalkM, out float land, out float relief);
            int to = System.Array.IndexOf(args, "-stabbyWalkTo");
            if (to >= 0 && to + 2 < args.Length && float.TryParse(args[to + 1], out float tx) && float.TryParse(args[to + 2], out float tz))
            {
                d = (new Vector2(tx, tz) - start).normalized;
                land = relief = -1;
            }

            var recs = new List<(int part, ProfilerRecorder rec)>();
            foreach (var w in Unity.Entities.World.All)
                foreach (var (type, part) in WalkSystems)
                    if (w.GetExistingSystem(type) != SystemHandle.Null)
                        recs.Add((part, ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName(w, type), 1)));
            var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var allocRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var lines = new List<string>
            {
                $"walk line from {start.x:F0},{start.y:F0} along {d.x:F2},{d.y:F2}: {land * 100:F0}% land, relief {relief:F0} m, FOV {cam.fieldOfView:F0}, {target.width}x{target.height}, quality {DetailView.Current.Quality:F2}, {recs.Count} system markers over {Unity.Entities.World.All.Count} worlds",
            };
            try
            {
                foreach (var (label, from, length, speed) in new[] { ("normal", 0f, NormalWalkM, GameSettings.WalkSpeed), ("fast", NormalWalkM, FastWalkM, FastWalkMps) })
                {
                    var frames = new List<float[]>();
                    float along = from;
                    bool first = true;
                    int gcRuns = System.GC.CollectionCount(0);
                    bool profile = label == "fast" && System.Environment.GetEnvironmentVariable("STABBY_WALK_PROFILE") == "1";
                    var churn0 = Churn();
                    float churnT0 = Time.realtimeSinceStartup;
                    while (along < from + length)
                    {
                        if (profile && frames.Count % 1000 == 0) WalkProfile(frames.Count / 1000);
                        if (!first)
                        {
                            var row = new float[WalkParts.Length + 5];
                            int runs = System.GC.CollectionCount(0);
                            row[WalkParts.Length + 3] = runs - gcRuns;
                            row[WalkParts.Length + 4] = allocRec.LastValue / 1024f;
                            gcRuns = runs;
                            row[0] = Time.unscaledDeltaTime * 1000f;
                            row[1] = mainRec.LastValue / 1e6f;
                            float sum = 0;
                            foreach (var (part, rec) in recs) { float ms = rec.LastValue / 1e6f; row[3 + part] += ms; sum += ms; }
                            row[2] = Mathf.Max(0, row[1] - sum);
                            frames.Add(row);
                        }
                        first = false;
                        along = Mathf.Min(from + length, along + speed * Mathf.Min(Time.deltaTime, 0.1f));
                        var at = start + d * along;
                        PlaceServerPlayers(server, at);
                        var foot = new Vector3(at.x, GroundHeight(at.x, at.y), at.y);
                        var fwd = new Vector3(d.x, 0, d.y);
                        var eye = foot - fwd * 6f + Vector3.up * 3f;
                        eye.y = Mathf.Max(eye.y, GroundHeight(eye.x, eye.z) + 1.5f);
                        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(foot + fwd * 20f + Vector3.up * 1.2f - eye));
                        yield return null;
                    }
                    if (profile) UnityEngine.Profiling.Profiler.enabled = false;
                    lines.Add(WalkReport(label, speed, length, frames));
                    lines.Add(ChurnReport(label, churn0, Churn(), Time.realtimeSinceStartup - churnT0));
                    for (var s = Settle(120); s.MoveNext();) yield return null;
                    lines.Add(Resident(view, label));
                }
                var end = start + d * (NormalWalkM + FastWalkM);
                var jump = TeleportSpot(ground, end, d);
                PlaceServerPlayers(server, jump);
                var jfoot = new Vector3(jump.x, GroundHeight(jump.x, jump.y), jump.y);
                var jfwd = new Vector3(d.x, 0, d.y);
                var jeye = jfoot - jfwd * 6f + Vector3.up * 3f;
                jeye.y = Mathf.Max(jeye.y, GroundHeight(jeye.x, jeye.z) + 1.5f);
                cam.transform.SetPositionAndRotation(jeye, Quaternion.LookRotation(jfoot + jfwd * 20f + Vector3.up * 1.2f - jeye));
                float t0 = Time.realtimeSinceStartup;
                var fill = new List<float[]>();
                int settled = 0;
                while (settled < 3 && Time.realtimeSinceStartup - t0 < 120f)
                {
                    yield return null;
                    var row = new float[WalkParts.Length + 5];
                    row[0] = Time.unscaledDeltaTime * 1000f;
                    row[1] = mainRec.LastValue / 1e6f;
                    float sum = 0;
                    foreach (var (part, rec) in recs) { float ms = rec.LastValue / 1e6f; row[3 + part] += ms; sum += ms; }
                    row[2] = Mathf.Max(0, row[1] - sum);
                    fill.Add(row);
                    settled = Loading() ? 0 : settled + 1;
                }
                float filled = Time.realtimeSinceStartup - t0;
                lines.Add($"teleport {Vector2.Distance(end, jump):F0} m to {jump.x:F0},{jump.y:F0}: view filled in {filled:F2} s ({fill.Count} frames{(settled < 3 ? ", timed out" : "")})");
                lines.Add(WalkReport("teleport fill", 0, 0, fill));
                for (var s = Settle(240); s.MoveNext();) yield return null;
                lines.Add(Resident(view, "after teleport"));
                var mem = new MemoryCounters { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryCounters>() };
                GetProcessMemoryInfo(GetCurrentProcess(), ref mem, mem.Size);
                lines.Add($"memory: process peak working set {(ulong)mem.PeakWorkingSetSize / 1e9:F2} GB, now {(ulong)mem.WorkingSetSize / 1e9:F2} GB, peak commit {(ulong)mem.PeakPagefileUsage / 1e9:F2} GB; Unity reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1e9:F2} GB, allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1e9:F2} GB, graphics driver {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1e9:F2} GB");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(target);
                foreach (var (_, rec) in recs) rec.Dispose();
                mainRec.Dispose();
                allocRec.Dispose();
            }
            GameStatus.Lines["~walk"] = string.Join("\n", lines);
        }

        static (long bytes, long native, int chunks, int allocs, int made, int destroyed) Churn() =>
            (TerrainViewSystem.ChunkBytes, TerrainViewSystem.NativeBytes, TerrainViewSystem.ChunksApplied, TerrainViewSystem.NativeAllocs, TerrainViewSystem.MeshesMade, TerrainViewSystem.MeshesDestroyed);

        static string ChurnReport(string label, (long bytes, long native, int chunks, int allocs, int made, int destroyed) a, (long bytes, long native, int chunks, int allocs, int made, int destroyed) b, float seconds)
        {
            int chunks = Mathf.Max(1, b.chunks - a.chunks);
            float minutes = Mathf.Max(seconds, 1e-3f) / 60f;
            return $"terrain churn {label}: {b.chunks - a.chunks} chunks applied in {seconds:F1} s, managed {(b.bytes - a.bytes) / (double)chunks:F0} B/chunk, native {(b.allocs - a.allocs) / (double)chunks:F2} allocs and {(b.native - a.native) / 1024.0 / chunks:F0} KB/chunk, meshes made {b.made - a.made} ({(b.made - a.made) / minutes:F1}/min), destroyed {b.destroyed - a.destroyed}";
        }

        static void WalkProfile(int segment)
        {
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.logFile = System.IO.Path.GetFullPath($"../out/unity/spike-walk-{segment:D3}.raw");
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enableAllocationCallstacks = true;
            UnityEngine.Profiling.Profiler.enabled = true;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MemoryCounters
        {
            public uint Size, PageFaultCount;
            public System.UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern System.IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        static extern bool GetProcessMemoryInfo(System.IntPtr process, ref MemoryCounters counters, uint size);

        static string Resident(Unity.Entities.World view, string label)
        {
            GameStatus.Line("terrain", out var terrain);
            return $"resident {label}: {ObjectViewSystem.LiveEntities} object entities, {ObjectViewSystem.ChunksHeld} object chunks, {ObjectViewSystem.PartsHeld} parts, {view.EntityManager.UniversalQuery.CalculateEntityCount()} view-world entities; {terrain}; {GrassViewSystem.Counts()}";
        }

        static Vector2 TeleportSpot(Ground ground, Vector2 from, Vector2 d)
        {
            float top = ground.Top, margin = 300f;
            var side = new Vector2(-d.y, d.x);
            foreach (float m in new[] { 1f, -1f })
                for (float dist = 1600f; dist >= 800f; dist -= 200f)
                {
                    var p = from + side * m * dist;
                    if (p.x < margin || p.y < margin || p.x > top - margin || p.y > top - margin) continue;
                    if (ground.Surface(p.x, p.y) > 2f) return p;
                }
            return new Vector2(top - from.x, top - from.y);
        }

        static Vector2 WalkLine(Ground ground, Vector2 start, float length, out float land, out float relief)
        {
            Vector2 best = new Vector2(0, 1);
            float bestScore = float.MinValue, top = ground.Top;
            land = relief = 0;
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8;
                var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                int on = 0, n = 0;
                float lo = float.MaxValue, hi = float.MinValue, steps = 0, prev = 0;
                for (float s = 0; s <= length; s += 20f, n++)
                {
                    var p = start + d * s;
                    if (p.x < 0 || p.y < 0 || p.x > top || p.y > top) continue;
                    float h = ground.Surface(p.x, p.y);
                    if (h <= 1f) continue;
                    on++;
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                    if (s > 0) steps += Mathf.Abs(h - prev);
                    prev = h;
                }
                float fraction = on / (float)n, range = on > 0 ? hi - lo : 0;
                float score = fraction * 1000f + Mathf.Min(range, 300f) + Mathf.Min(steps, 2000f) * 0.05f;
                if (score > bestScore) { bestScore = score; best = d; land = fraction; relief = range; }
            }
            return best;
        }

        static float Pct(List<float> sorted, float p) => sorted.Count == 0 ? 0 : sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * p))];

        static string WalkReport(string label, float speed, float length, List<float[]> frames)
        {
            var names = new[] { "frame dt", "main thread", "other" }.Concat(WalkParts).ToArray();
            int gcAt = names.Length;
            var parts = new List<string>();
            for (int c = 0; c < names.Length; c++)
            {
                var xs = frames.Select(f => f[c]).OrderBy(x => x).ToList();
                parts.Add($"{names[c]} p50 {Pct(xs, 0.5f):F1} p99 {Pct(xs, 0.99f):F1} max {(xs.Count > 0 ? xs[xs.Count - 1] : 0):F1}");
            }
            int over = frames.Count(f => f[1] > 33f), overDt = frames.Count(f => f[0] > 33f);
            int streaming = frames.Count(f => f[3] + f[4] + f[5] + f[6] + f[7] > 33f);
            int collections = frames.Count(f => f[gcAt] > 0), slowCollections = frames.Count(f => f[gcAt] > 0 && f[1] > 15f), slowOthers = frames.Count(f => f[gcAt] == 0 && f[1] > 15f);
            var allocs = frames.Select(f => f[gcAt + 1]).OrderBy(x => x).ToList();
            var worst = frames.Select((f, i) => (f, i)).OrderByDescending(x => x.f[1]).Take(8)
                .Select(x => $"#{x.i} {x.f[1]:F0} ms (" + string.Join(", ", Enumerable.Range(2, names.Length - 2).Where(c => x.f[c] >= 0.5f).Select(c => $"{names[c]} {x.f[c]:F1}")) + (x.f[gcAt] > 0 ? ", GC" : "") + $", alloc {x.f[gcAt + 1]:F0} KB)");
            return $"walk {label} {speed:F0} m/s over {length:F0} m, {frames.Count} frames: {over} main-thread frames over 33 ms ({overDt} by dt), {streaming} with streaming systems over 33 ms; {collections} GC frames ({slowCollections} over 15 ms, {slowOthers} non-GC over 15 ms), alloc KB/frame p50 {Pct(allocs, 0.5f):F0} p99 {Pct(allocs, 0.99f):F0}\n  " +
                   string.Join("\n  ", parts) + "\n  worst: " + string.Join("; ", worst);
        }
    }
}
