using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly int[] MemoryCounts = { 50, 500, 2000 };

        struct MemSample
        {
            public long Native, Managed, Mono, Driver, Gpu;
            public int Entities, Chunks;
        }

        static long BufferBytes(CreatureDrawSystem draw, string field)
        {
            if (draw == null) return 0;
            var f = typeof(CreatureDrawSystem).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(draw) is GraphicsBuffer b && b.IsValid() ? (long)b.count * b.stride : 0;
        }

        static MemSample SampleMemory(EntityManager em, CreatureDrawSystem draw)
        {
            em.CompleteAllTrackedJobs();
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            long managed = System.GC.GetTotalMemory(true);
            return new MemSample
            {
                Native = Profiler.GetTotalAllocatedMemoryLong(), Managed = managed, Mono = Profiler.GetMonoUsedSizeLong(),
                Driver = Profiler.GetAllocatedMemoryForGraphicsDriver(), Gpu = BufferBytes(draw, "instances") + BufferBytes(draw, "fur"),
                Entities = em.UniversalQuery.CalculateEntityCount(), Chunks = em.UniversalQuery.CalculateChunkCount(),
            };
        }

        static string GhostBytes(Unity.Entities.World w, bool brain)
        {
            if (w == null) return "none";
            var em = w.EntityManager;
            using var q = brain ? em.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain)) : em.CreateEntityQuery(typeof(CreatureState));
            using var es = q.ToEntityArray(Allocator.Temp);
            foreach (var e in es)
            {
                if (em.GetComponentData<CreatureState>(e).Id < 0) continue;
                var chunk = em.GetChunk(e);
                return $"{16384 / chunk.Capacity} B ({chunk.Archetype.GetComponentTypes(Allocator.Temp).Length} components, {chunk.Capacity} per chunk)";
            }
            return "no live plan creature";
        }

        static string Per(long bytes, int n) => $"{bytes / (double)n / 1024.0:F2} KiB";

        static System.Collections.IEnumerator CreatureMemory(Camera cam, string dir)
        {
            string path = System.Environment.GetEnvironmentVariable(BenchFlag) ?? "entity";
            var view = ViewWorld();
            var server = ServerWorld();
            if (view == null) { GameStatus.Lines["~creature memory"] = "creature memory: no view world"; yield break; }
            var vem = view.EntityManager;
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            if (!hasGround) { GameStatus.Lines["~creature memory"] = "creature memory: no ground"; yield break; }

            GameStatus.HoldCamera = true;
            var eye = cam.transform.position;
            var flatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            eye.y = GroundHeight(eye.x, eye.z) + 1.7f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flatFwd) * Quaternion.Euler(6f, 0, 0));
            FreezeCreatures(true);
            CreatureView.Current = new CreatureView { Override = 1, Move = 1, Phase = 0.3f };
            var settle = Settle(240);
            while (settle.MoveNext()) yield return null;

            var draw = view.GetExistingSystemManaged<CreatureDrawSystem>();
            int baseCount = CreatureBuildSystem.Count;
            var lines = new List<string>
            {
                $"creature memory path {path}, quality {DetailView.Current.Quality:F2}, {baseCount} plan creatures shown, {CreaturePlanSystem.Planned} planned",
                $"live ghost entity: client {GhostBytes(view, false)}, server {GhostBytes(server, true)}; plan-only creature {UnsafeUtility.SizeOf<CreatureSpawnRow>()} B spawn row, no entities",
                $"rig blob struct sizes: CreatureRig {UnsafeUtility.SizeOf<CreatureRig>()} B, segment {UnsafeUtility.SizeOf<CreatureSegmentRig>()} B, part {UnsafeUtility.SizeOf<CreaturePartRig>()} B; CreatureRoot {UnsafeUtility.SizeOf<CreatureRoot>()} B, CreatureLink {UnsafeUtility.SizeOf<CreatureLink>()} B, CreatureGpu {UnsafeUtility.SizeOf<CreatureGpu>()} B",
            };

            for (int r = 0; r < MemoryCounts.Length; r++)
            {
                int n = MemoryCounts[r];
                for (int k = 0; k < 30; k++) yield return null;
                var s0 = SampleMemory(vem, draw);
                var bench = SpawnBench(view, n, 50 + r);
                PlaceBench(view, bench, cam, 1.6f, 0.9f, 40f, ground);
                int frames = 0;
                while (CreatureBuildSystem.Count < baseCount + n && frames < 3600) { yield return null; frames++; }
                var (rigRef, length, stand, _, _, _) = BenchRig(vem);
                float q = math.max(DetailView.Current.Quality, 0.1f);
                PlaceBench(view, bench, cam, length, stand, rigRef.IsCreated ? BenchPixels(rigRef, 1, q) : 40f, ground);
                for (int k = 0; k < 30; k++) yield return null;
                var counts = TierCounts(view, ground);
                var s1 = SampleMemory(vem, draw);
                string mem = BenchMemory(vem, n);

                int left = CreatureBuildSystem.Count - n;
                vem.DestroyEntity(bench);
                bench.Dispose();
                int gone = 0;
                while (CreatureBuildSystem.Count > left && gone < 1200) { yield return null; gone++; }
                for (int k = 0; k < 30; k++) yield return null;
                var s2 = SampleMemory(vem, draw);

                lines.Add($"N {n}: built in {frames} frames, {CreatureBuildSystem.Count - left + n} live after build; {counts}");
                lines.Add($"  N {n} entities: {(s1.Entities - s0.Entities) / (float)n:F2} client entities per creature (bench ghost stand-in included), chunks {(s1.Chunks - s0.Chunks) * 16f / n:F2} KiB per creature");
                lines.Add($"  N {n} {mem}");
                lines.Add($"  N {n} memory per creature: native {Per(s1.Native - s0.Native, n)}, managed GC {Per(s1.Managed - s0.Managed, n)}, mono heap {Per(s1.Mono - s0.Mono, n)}, instance GPU buffers {Per(s1.Gpu - s0.Gpu, n)}, graphics driver {Per(s1.Driver - s0.Driver, n)}");
                lines.Add($"  N {n} after removal ({gone} frames): native {(s2.Native - s0.Native) / 1024.0:F0} KiB, managed {(s2.Managed - s0.Managed) / 1024.0:F0} KiB, entities {s2.Entities - s0.Entities}, chunks {s2.Chunks - s0.Chunks}");
            }

            CreatureView.Current = default;
            FreezeCreatures(false);
            for (int i = 0; i < lines.Count; i++) GameStatus.Lines[$"~creature memory {i:D2}"] = lines[i];
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"creature-memory-{path}.txt"), lines);
        }
    }
}
