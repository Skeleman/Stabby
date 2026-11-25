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

        static void AimClear(Camera cam, in Ground ground, float reach, float stand)
        {
            var eye = cam.transform.position;
            eye.y = ground.HeightAt(eye.x, eye.z) + 4f;
            float baseYaw = math.atan2(cam.transform.forward.x, cam.transform.forward.z);
            float best = baseYaw, bestClear = float.MinValue;
            for (int a = 0; a < 24; a++)
            {
                float yaw = baseYaw + math.radians(15f * a);
                float clear = float.MaxValue;
                for (int side = -1; side <= 1; side++)
                {
                    float y2 = yaw + math.radians(25f) * side;
                    float2 dir = new float2(math.sin(y2), math.cos(y2));
                    float2 end = new float2(eye.x, eye.z) + dir * reach;
                    float endY = ground.HeightAt(end.x, end.y) + stand;
                    for (int k = 1; k < 24; k++)
                    {
                        float t = k / 24f;
                        float2 at = new float2(eye.x, eye.z) + dir * reach * t;
                        clear = math.min(clear, math.lerp(eye.y, endY, t) - ground.HeightAt(at.x, at.y));
                    }
                }
                if (clear > bestClear) { bestClear = clear; best = yaw; }
                if (clear > 0.6f) break;
            }
            float2 mid = new float2(eye.x, eye.z) + new float2(math.sin(best), math.cos(best)) * reach;
            var target = new Vector3(mid.x, ground.HeightAt(mid.x, mid.y) + stand, mid.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        static System.Collections.IEnumerator Approach(Unity.Entities.World view, Camera cam, string dir, Ground ground, int baseCount, List<string> lines, bool tiered)
        {
            var vem = view.EntityManager;
            var walk = SpawnBench(view, 5, 9);
            PlaceBench(view, walk, cam, 1.6f, 0.9f, 40f, ground);
            int frames = 0;
            while (CreatureBuildSystem.Count < baseCount + walk.Length && frames < 1200) { yield return null; frames++; }
            var (rigRef, length, stand, _, _, _) = BenchRig(vem);
            float q = math.max(DetailView.Current.Quality, 0.1f);
            float low = rigRef.IsCreated ? rigRef.Value.LowPx / q : 40f;
            AimClear(cam, ground, Detail.Reach(length, 1.25f * low, DetailView.Current.PixelsPerMetre), stand);
            PlaceBench(view, walk, cam, length, stand, 0.5f * low, ground);
            for (int k = 0; k < 30; k++) yield return null;
            CreatureBuildSystem.PeakSpend = CreatureBuildSystem.TierBuilds = CreatureBuildSystem.TierDrops = 0;
            int gaps = 0, late = 0, seen = 0, steps = 120;
            bool shot = false;
            using var rq = vem.CreateEntityQuery(typeof(CreatureRoot), typeof(CreatureLink));
            for (int k = 0; k <= 2 * steps; k++)
            {
                float t = k <= steps ? k / (float)steps : 2 - k / (float)steps;
                float px = low * math.lerp(0.5f, 2.1f, t);
                PlaceBench(view, walk, cam, length, stand, px, ground);
                yield return null;
                vem.CompleteAllTrackedJobs();
                using (var es = rq.ToEntityArray(Allocator.Temp))
                    foreach (var e in es)
                    {
                        var c = vem.GetComponentData<CreatureRoot>(e);
                        if (!vem.HasComponent<CreatureState>(c.Ghost) || vem.GetComponentData<CreatureState>(c.Ghost).Id > -900000) continue;
                        var links = vem.GetBuffer<CreatureLink>(e, true);
                        seen++;
                        if (c.Visible && !c.HighOn && !c.LowOn) gaps++;
                        float rpx = DetailView.Current.ScreenSize(c.Scale.Length, new float3(c.Position.x, ground.HeightAt(c.Position.x, c.Position.z) + c.Scale.Stand, c.Position.z));
                        if (tiered && rpx > 1.05f * low && links[0].Value == Entity.Null) late++;
                    }
                if (!shot && k < steps && px >= 1.25f * low)
                {
                    Shot(cam, dir, "tier-fade-a");
                    yield return null;
                    Shot(cam, dir, "tier-fade-b");
                    shot = true;
                }
            }
            lines.Add($"approach and retreat 0.5x to 2.1x low px over {2 * steps} frames, {walk.Length} creatures ({seen} creature-frames seen): {gaps} creature-frames with no tier shown, {late} creature-frames past the crossfade start without the high tier, {CreatureBuildSystem.TierBuilds} tier builds, {CreatureBuildSystem.TierDrops} tier entities released, peak {CreatureBuildSystem.PeakSpend} entities in one frame");
            vem.DestroyEntity(walk);
            walk.Dispose();
            for (int k = 0; k < 30; k++) yield return null;
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
                $"rig blob struct sizes: CreatureRig {UnsafeUtility.SizeOf<CreatureRig>()} B, segment {UnsafeUtility.SizeOf<CreatureSegmentRig>()} B, part {UnsafeUtility.SizeOf<CreaturePartRig>()} B, CreatureFit {UnsafeUtility.SizeOf<CreatureFit>()} B, CreatureScale {UnsafeUtility.SizeOf<CreatureScale>()} B; CreatureRoot {UnsafeUtility.SizeOf<CreatureRoot>()} B, CreatureLink {UnsafeUtility.SizeOf<CreatureLink>()} B, CreatureGpu {UnsafeUtility.SizeOf<CreatureGpu>()} B",
            };

            for (int r = 0; r < MemoryCounts.Length; r++)
            {
                int n = MemoryCounts[r];
                for (int k = 0; k < 30; k++) yield return null;
                var s0 = SampleMemory(vem, draw);
                CreatureBuildSystem.PeakSpend = CreatureBuildSystem.TierBuilds = CreatureBuildSystem.TierDrops = 0;
                var bench = SpawnBench(view, n, 50 + r);
                PlaceBench(view, bench, cam, 1.6f, 0.9f, 40f, ground);
                int frames = 0;
                while (CreatureBuildSystem.Count < baseCount + n && frames < 3600) { yield return null; frames++; }
                var (rigRef, length, stand, _, _, _) = BenchRig(vem);
                float q = math.max(DetailView.Current.Quality, 0.1f);
                PlaceBench(view, bench, cam, length, stand, rigRef.IsCreated ? BenchPixels(rigRef, 1, q) : 40f, ground);
                for (int k = 0; k < 30; k++) yield return null;
                int retier = 0;
                using (var asking = vem.CreateEntityQuery(typeof(CreatureRetier)))
                    while (asking.CalculateEntityCount() > 0 && retier < 1200) { yield return null; retier++; }
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

                lines.Add($"N {n}: built in {frames} frames, retiered after the move in {30 + retier} frames, {CreatureBuildSystem.Count - left + n} live after build; {counts}");
                lines.Add($"  N {n} entities: {(s1.Entities - s0.Entities) / (float)n:F2} client entities per creature (bench ghost stand-in included), chunks {(s1.Chunks - s0.Chunks) * 16f / n:F2} KiB per creature");
                lines.Add($"  N {n} {mem}");
                lines.Add($"  N {n} memory per creature: native {Per(s1.Native - s0.Native, n)}, managed GC {Per(s1.Managed - s0.Managed, n)}, mono heap {Per(s1.Mono - s0.Mono, n)}, instance GPU buffers {Per(s1.Gpu - s0.Gpu, n)}, graphics driver {Per(s1.Driver - s0.Driver, n)}");
                lines.Add($"  N {n} after removal ({gone} frames): native {(s2.Native - s0.Native) / 1024.0:F0} KiB, managed {(s2.Managed - s0.Managed) / 1024.0:F0} KiB, entities {s2.Entities - s0.Entities}, chunks {s2.Chunks - s0.Chunks}");
                lines.Add($"  N {n} build budget: peak {CreatureBuildSystem.PeakSpend}/{FrameBudget.CreatureEntities} entities in one frame, {CreatureBuildSystem.TierBuilds} tier builds, {CreatureBuildSystem.TierDrops} tier entities released");
            }

            var walk = Approach(view, cam, dir, ground, baseCount, lines, path == "entity");
            while (walk.MoveNext()) yield return null;

            CreatureView.Current = default;
            FreezeCreatures(false);
            for (int i = 0; i < lines.Count; i++) GameStatus.Lines[$"~creature memory {i:D2}"] = lines[i];
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"creature-memory-{path}.txt"), lines);
        }
    }
}
