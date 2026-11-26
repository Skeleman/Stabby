using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Stabby.Game.Editor
{
    static class ObjectScale
    {
        const int Frames = 120;
        static readonly string[] Wanted = { "LodJob", "ObjectLodSystem", "ObjectViewSystem", "EntitiesGraphicsSystem" };
        static readonly List<(string name, ProfilerRecorder rec, double[] ms)> recs = new List<(string, ProfilerRecorder, double[])>();
        static readonly List<string> sets = new List<string>();
        static int frame = -1;
        static double[] view = new double[Frames];

        public static string Density => System.Environment.GetEnvironmentVariable("STABBY_OBJECT_DENSITY") ?? "1";

        public static bool Sample(string label)
        {
            if (frame < 0)
            {
                if (recs.Count == 0) Start();
                frame = 0;
                return false;
            }
            for (int r = 0; r < recs.Count; r++) recs[r].ms[frame] = recs[r].rec.LastValue / 1e6;
            view[frame] = ObjectViewSystem.LastMs;
            if (++frame < Frames) return false;
            var parts = new List<string> { $"view system {Avg(view):F3} avg / {view.Max():F2} max" };
            foreach (var (name, _, ms) in recs) if (ms.Max() > 0) parts.Add($"{name} {Avg(ms):F3} avg / {ms.Max():F2} max");
            sets.Add($"scale {label} over {Frames} frames (ms): {string.Join(", ", parts)}; {ObjectViewSystem.LiveEntities} object entities, {ObjectViewSystem.PartsHeld} parts held in {ObjectViewSystem.ChunksHeld} chunks, {ObjectViewSystem.Recollects} re-collects, slowest first collect {ObjectViewSystem.FirstCollectMaxMs:F0} ms, slowest re-collect {ObjectViewSystem.RecollectMaxMs:F0} ms, worst wait for parts in view {ObjectViewSystem.PartsWaitMaxMs:F0} ms");
            frame = -1;
            return true;
        }

        static double Avg(double[] a) => a.Sum() / a.Length;

        static void Start()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var seen = new HashSet<string>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (!Wanted.Any(w => d.Name.Contains(w)) || !seen.Add(d.Name)) continue;
                recs.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.Default), new double[Frames]));
            }
        }

        public static string Forced()
        {
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var lod = w.GetExistingSystem<ObjectLodSystem>();
                w.EntityManager.CompleteAllTrackedJobs();
                var ms = new double[30];
                for (int k = 0; k < ms.Length; k++)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    lod.Update(w.Unmanaged);
                    w.EntityManager.CompleteAllTrackedJobs();
                    ms[k] = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }
                System.Array.Sort(ms);
                int all = w.EntityManager.UniversalQuery.CalculateEntityCount();
                return $"scale lod forced (update + complete, 30 runs, ms): median {ms[15]:F3}, best {ms[0]:F3}; {all} entities in the view world";
            }
            return "scale lod forced: no view world";
        }

        public static string Memory()
        {
            long mb = 1024 * 1024;
            var mem = new Counters { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Counters>() };
            GetProcessMemoryInfo(GetCurrentProcess(), ref mem, mem.Size);
            long meshBytes = 0, texBytes = 0, chunks = 0;
            var meshList = Resources.FindObjectsOfTypeAll<Mesh>();
            var groups = new Dictionary<string, long>();
            foreach (var m in meshList)
            {
                long b = Profiler.GetRuntimeMemorySizeLong(m);
                meshBytes += b;
                var key = new string(m.name.TakeWhile(c => !char.IsDigit(c) && c != ' ' && c != '(').ToArray());
                groups[key] = (groups.TryGetValue(key, out long had) ? had : 0) + b;
            }
            var top = string.Join(", ", groups.OrderByDescending(g => g.Value).Take(6).Select(g => $"'{g.Key}' {g.Value / mb}"));
            var lakes = meshList.Where(m => m.name.StartsWith("Water ")).ToArray();
            long lakeVerts = lakes.Sum(m => (long)m.vertexCount), lakeMax = lakes.Length > 0 ? lakes.Max(m => m.vertexCount) : 0;
            string counters = string.Join(", ", new[] { "System Used Memory", "Total Used Memory", "Gfx Used Memory", "Mesh Memory" }.Select(c =>
            {
                using var r = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, c);
                return $"{c} {r.CurrentValue / mb}";
            }));
            int stride = lakes.Length > 0 ? lakes[0].GetVertexBufferStride(0) : 0, readable = lakes.Count(m => m.isReadable);
            var texList = Resources.FindObjectsOfTypeAll<Texture>();
            foreach (var t in texList) texBytes += Profiler.GetRuntimeMemorySizeLong(t);
            foreach (var w in Unity.Entities.World.All) chunks += w.EntityManager.UniversalQuery.CalculateChunkCountWithoutFiltering();
            var archetypes = new List<(string world, int chunks, int capacity, int entities)>();
            foreach (var w in Unity.Entities.World.All)
            {
                using var all = new Unity.Collections.NativeList<EntityArchetype>(Unity.Collections.Allocator.Temp);
                w.EntityManager.GetAllArchetypes(all);
                foreach (var a in all) if (a.ChunkCount > 0) archetypes.Add((w.Name, a.ChunkCount, a.ChunkCapacity, a.ChunkCount * a.ChunkCapacity));
            }
            int partBytes = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<Stabby.Game.ObjectSpawn>();
            string owners = $"object parts {ObjectViewSystem.PartsHeld} x {partBytes} B = {(long)ObjectViewSystem.PartsHeld * partBytes / mb}, entity chunks {chunks * 16 / 1024}, largest archetypes " + string.Join(", ", archetypes.OrderByDescending(a => a.chunks).Take(4).Select(a => $"{a.world} {a.chunks} chunks of {a.capacity} = {a.chunks * 16 / 1024}"));
            GameStatus.Lines["~scale native"] = $"scale native owners (MB): {owners}";
            return $"scale memory (MB):unity allocated {Profiler.GetTotalAllocatedMemoryLong() / mb}, reserved {Profiler.GetTotalReservedMemoryLong() / mb}, graphics driver {Profiler.GetAllocatedMemoryForGraphicsDriver() / mb}, mono used {Profiler.GetMonoUsedSizeLong() / mb}, working set {(ulong)mem.WorkingSetSize / (ulong)mb}, peak working set {(ulong)mem.PeakWorkingSetSize / (ulong)mb}, peak commit {(ulong)mem.PeakPagefileUsage / (ulong)mb}; meshes {meshList.Length} {meshBytes / mb}, textures {texList.Length} {texBytes / mb}, entity chunks {chunks} {chunks * 16 / 1024}; top mesh groups {top}; lake meshes {lakes.Length}, vertices {lakeVerts} (max {lakeMax}), stride {stride} B, readable {readable}; recorders {counters}";
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Counters
        {
            public uint Size, PageFaultCount;
            public System.UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern System.IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        static extern bool GetProcessMemoryInfo(System.IntPtr process, ref Counters counters, uint size);

        public static string Report() => $"scale density {Density}\n" + string.Join("\n", sets);

        public static void Stop()
        {
            foreach (var r in recs) r.rec.Dispose();
            recs.Clear();
        }
    }
}
