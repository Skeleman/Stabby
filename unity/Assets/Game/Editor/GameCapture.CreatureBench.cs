using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly int[] BenchCounts = { 10, 50, 100, 250, 500 };
        static readonly string[] BenchTiers = { "close", "mid", "far", "still", "hidden" };
        const int BenchSamples = 30;
        const string BenchFlag = "STABBY_CREATURE_PATH";

        static float BenchPixels(BlobAssetReference<CreatureRig> rigRef, int tier, float q)
        {
            ref var rig = ref rigRef.Value;
            float px = tier switch
            {
                0 => math.max(rig.IkPx, rig.LowPx) * 1.5f * 1.25f,
                1 => (rig.AnimPx * 1.5f + rig.LowPx) * 0.5f,
                2 => rig.AnimPx * 1.25f,
                3 => rig.FadePx * 1.2f,
                _ => rig.FadePx * 0.6f,
            };
            return px / q;
        }

        static double Median(List<double> xs)
        {
            if (xs.Count == 0) return 0;
            xs.Sort();
            return xs[xs.Count / 2];
        }

        static double TimeSystems(Unity.Entities.World w, SystemHandle[] systems, int samples)
        {
            var list = new List<double>(samples);
            for (int k = 0; k < samples + 5; k++)
            {
                w.EntityManager.CompleteAllTrackedJobs();
                long t0 = Stopwatch.GetTimestamp();
                foreach (var h in systems) h.Update(w.Unmanaged);
                w.EntityManager.CompleteAllTrackedJobs();
                long t1 = Stopwatch.GetTimestamp();
                if (k >= 5) list.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
            }
            return Median(list);
        }

        static (double Par, double Single) TimeBoth(Unity.Entities.World w, SystemHandle[] systems)
        {
            double par = TimeSystems(w, systems, BenchSamples), single = -1;
            int workers = JobsUtility.JobWorkerCount;
            try
            {
                JobsUtility.JobWorkerCount = 0;
                single = TimeSystems(w, systems, BenchSamples);
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("creature bench: single-thread timing unavailable: " + e.Message); }
            finally { JobsUtility.JobWorkerCount = workers; }
            return (par, single);
        }

        static SystemHandle[] BenchSystems(Unity.Entities.World w, params System.Type[] types) =>
            types.Select(t => w.GetExistingSystem(t)).Where(h => h != SystemHandle.Null).ToArray();

        static (BlobAssetReference<CreatureRig> Rig, float Length, float Stand, int Segs, int Parts, int Bytes) BenchRig(EntityManager em)
        {
            using var rq = em.CreateEntityQuery(typeof(CreatureRoot));
            using var roots = rq.ToComponentDataArray<CreatureRoot>(Allocator.Temp);
            foreach (var c in roots)
            {
                if (!em.HasComponent<CreatureState>(c.Ghost) || em.GetComponentData<CreatureState>(c.Ghost).Id >= 0) continue;
                ref var rig = ref c.Rig.Value;
                int bytes = UnsafeUtility.SizeOf<CreatureRig>() + rig.Segments.Length * UnsafeUtility.SizeOf<CreatureSegmentRig>() + rig.Parts.Length * UnsafeUtility.SizeOf<CreaturePartRig>();
                return (c.Rig, rig.Length, rig.Stand, rig.Segments.Length, rig.Parts.Length, bytes);
            }
            return (default, 1.6f, 0.9f, 0, 0, 0);
        }

        static void AddBrainHerds(Unity.Entities.World server, SystemHandle host, int n, int r, float2 at, float reach)
        {
            var sem = server.EntityManager;
            sem.CompleteAllTrackedJobs();
            using var es = sem.CreateEntity(sem.CreateArchetype(typeof(CreatureState), typeof(CreatureBrain)), n, Allocator.Temp);
            ref var sys = ref server.Unmanaged.GetUnsafeSystemRef<CreatureHostSystem>(host);
            for (int i = 0; i < n; i++)
            {
                uint h = math.hash(new uint2((uint)i, (uint)r + 991u));
                int herd = -1 - i / 10 - 1000 * r;
                float ang = (i / 10) * 2.39996f, rad = math.lerp(20f, reach, ((i / 10) * 0.618034f) % 1f);
                float2 centre = at + new float2(math.sin(ang), math.cos(ang)) * rad;
                float2 p = centre + new float2((h & 0xFF) / 255f - 0.5f, (h >> 8 & 0xFF) / 255f - 0.5f) * 8f;
                sem.SetComponentData(es[i], new CreatureState { Id = -1 - i - 100000 * (r + 1), Body = 0, Seed = h, WalkSpeed = 1.2f, Position = p, Heading = ang });
                sem.SetComponentData(es[i], new CreatureBrain { Home = p, Herd = herd, Random = new Unity.Mathematics.Random(h | 1), Timer = 0.5f + (h % 997) / 997f * 3 });
                if (!sys.Herds.ContainsKey(herd)) sys.Herds.Add(herd, new CreatureHerd { Mind = HerdMind.Start(h), Body = 0 });
            }
        }

        static string MarkerName(Unity.Entities.World w, System.Type t) => $"{w.Name} {TypeManager.GetSystemName(t)}";

        static void PlaceBench(Unity.Entities.World w, NativeArray<Entity> bench, Camera cam, float length, float stand, float px, in Ground ground)
        {
            var em = w.EntityManager;
            float ppm = DetailView.Current.PixelsPerMetre;
            Vector3 eye = cam.transform.position;
            float baseYaw = math.atan2(cam.transform.forward.x, cam.transform.forward.z);
            for (int i = 0; i < bench.Length; i++)
            {
                uint h = math.hash(new uint2((uint)i, 0x51A7u));
                float jitter = 1 + 0.06f * ((h & 0xFFFF) / 65535f - 0.5f);
                float d3 = Detail.Reach(length, px * jitter, ppm);
                float yaw = baseYaw + math.radians(25f) * (bench.Length == 1 ? 0 : 2f * i / (bench.Length - 1) - 1);
                float2 dir = new float2(math.sin(yaw), math.cos(yaw));
                float flat = d3;
                for (int k = 0; k < 3; k++)
                {
                    float2 at = new float2(eye.x, eye.z) + dir * flat;
                    float dy = eye.y - (ground.HeightAt(at.x, at.y) + stand);
                    flat = math.sqrt(math.max(0.25f, d3 * d3 - dy * dy));
                }
                float2 p = new float2(eye.x, eye.z) + dir * flat;
                var s = em.GetComponentData<CreatureState>(bench[i]);
                s.Position = p;
                s.Heading = yaw + math.PI * 0.5f;
                em.SetComponentData(bench[i], s);
            }
        }

        static string TierCounts(Unity.Entities.World w, in Ground ground)
        {
            var em = w.EntityManager;
            using var q = em.CreateEntityQuery(typeof(CreatureRoot));
            using var roots = q.ToComponentDataArray<CreatureRoot>(Allocator.Temp);
            var view = DetailView.Current;
            float qual = math.max(view.Quality, 0.1f);
            int visible = 0, high = 0, low = 0, anim = 0, ik = 0, shadow = 0;
            foreach (var c in roots)
            {
                if (!em.HasComponent<CreatureState>(c.Ghost) || em.GetComponentData<CreatureState>(c.Ghost).Id >= 0) continue;
                ref var rig = ref c.Rig.Value;
                float px = view.ScreenSize(rig.Length, new float3(c.Position.x, ground.HeightAt(c.Position.x, c.Position.z) + rig.Stand, c.Position.z));
                if (c.Visible) visible++;
                if (c.HighOn) high++;
                if (c.LowOn) low++;
                if (c.Visible && c.Shadow) shadow++;
                if (c.Visible && px > rig.AnimPx / qual) anim++;
                if (c.Visible && px > rig.IkPx / qual) ik++;
            }
            return $"visible {visible}, high {high}, low {low}, animated {anim}, ik {ik}, shadow {shadow}";
        }

        static NativeArray<Entity> SpawnBench(Unity.Entities.World w, int n, int round)
        {
            var em = w.EntityManager;
            var bench = em.CreateEntity(em.CreateArchetype(typeof(CreatureState)), n, Allocator.Persistent);
            for (int i = 0; i < n; i++)
            {
                uint h = math.hash(new uint2((uint)i, (uint)round + 77u));
                em.SetComponentData(bench[i], new CreatureState
                {
                    Id = -1 - i - 100000 * round, Body = 0, BodySeed = ((ulong)h << 32) | math.hash(new uint2(h, 3u)), Seed = h, WalkSpeed = 1.2f,
                });
            }
            return bench;
        }

        static System.Collections.IEnumerator CreatureBench(Camera cam, string dir)
        {
            string path = System.Environment.GetEnvironmentVariable(BenchFlag) ?? "entity";
            var view = ViewWorld();
            var server = ServerWorld();
            if (view == null || server == null) { GameStatus.Lines["~creature bench"] = "creature bench: no worlds"; yield break; }
            var vem = view.EntityManager;
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            if (!hasGround) { GameStatus.Lines["~creature bench"] = "creature bench: no ground"; yield break; }

            GameStatus.HoldCamera = true;
            var eye = cam.transform.position;
            var flatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            eye.y = GroundHeight(eye.x, eye.z) + 1.7f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flatFwd) * Quaternion.Euler(6f, 0, 0));
            FreezeCreatures(true);
            CreatureView.Current = new CreatureView { Override = 1, Move = 1, Phase = 0.3f };
            var settle = Settle(120);
            while (settle.MoveNext()) yield return null;

            var animTypes = CreaturePath.Instanced ? new[] { typeof(CreatureDrawSystem), typeof(CreatureAnimSystem) } : new[] { typeof(CreatureAnimSystem) };
            var draw = view.GetExistingSystemManaged<CreatureDrawSystem>();
            var anim = BenchSystems(view, animTypes);
            var build = BenchSystems(view, typeof(CreatureBuildSystem));
            int baseCount = CreatureBuildSystem.Count;
            var baseline = TimeBoth(view, anim);
            var baseBuild = TimeSystems(view, build, BenchSamples);
            var lines = new List<string>
            {
                $"creature bench path {path}, quality {DetailView.Current.Quality:F2}, {JobsUtility.JobWorkerCount} job workers, {baseCount} plan creatures shown",
                $"baseline (plan creatures only): anim {baseline.Par:F3} ms parallel / {baseline.Single:F3} ms one thread, build steady {baseBuild:F3} ms",
            };

            using var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
            using var buildRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName(view, typeof(CreatureBuildSystem)), 4);
            using var animRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName(view, typeof(CreatureAnimSystem)), 4);

            for (int r = 0; r < BenchCounts.Length; r++)
            {
                int n = BenchCounts[r];
                int entities0 = vem.UniversalQuery.CalculateEntityCount(), chunks0 = vem.UniversalQuery.CalculateChunkCount();
                var bench = SpawnBench(view, n, r + 1);
                float placeholderPx = 40f;
                PlaceBench(view, bench, cam, 1.6f, 0.9f, placeholderPx, ground);
                int frames = 0;
                double worstMain = 0, worstBuild = 0, worstDt = 0;
                while (CreatureBuildSystem.Count < baseCount + n && frames < 1800)
                {
                    yield return null;
                    frames++;
                    worstMain = math.max(worstMain, mainRec.LastValue / 1e6);
                    worstBuild = math.max(worstBuild, buildRec.LastValue / 1e6);
                    worstDt = math.max(worstDt, Time.unscaledDeltaTime * 1000.0);
                }
                for (int k = 0; k < 3; k++)
                {
                    yield return null;
                    worstMain = math.max(worstMain, mainRec.LastValue / 1e6);
                    worstBuild = math.max(worstBuild, buildRec.LastValue / 1e6);
                }
                int entities1 = vem.UniversalQuery.CalculateEntityCount(), chunks1 = vem.UniversalQuery.CalculateChunkCount();

                var (rigRef, length, stand, segs, parts, blobBytes) = BenchRig(vem);
                float perEntities = (entities1 - entities0) / (float)n;
                float perKiB = (chunks1 - chunks0) * 16f / n;
                lines.Add($"N {n}: built in {frames} frames, worst main thread {worstMain:F2} ms, worst build system {worstBuild:F2} ms, worst frame dt {worstDt:F1} ms; " +
                          $"{perEntities:F1} client entities per creature (ghost included), chunk memory {perKiB:F2} KiB per creature, rig blob {blobBytes} B; " +
                          $"anim temp {segs * 32 + parts * 28} B per visible creature in 5 allocations");

                float q = math.max(DetailView.Current.Quality, 0.1f);
                for (int t = 0; t < BenchTiers.Length; t++)
                {
                    float px = rigRef.IsCreated ? BenchPixels(rigRef, t, q) : 20f;
                    PlaceBench(view, bench, cam, length, stand, px, ground);
                    for (int k = 0; k < 4; k++) yield return null;
                    var counts = TierCounts(view, ground);
                    var times = TimeBoth(view, anim);
                    double steadyBuild = TimeSystems(view, build, BenchSamples);
                    double per = n > 0 ? (times.Par - baseline.Par) * 1000.0 / n : 0;
                    lines.Add($"  N {n} {BenchTiers[t]} ({px:F1} px): anim {times.Par:F3} ms parallel ({per:F2} us per creature over baseline) / {times.Single:F3} ms one thread; build steady {steadyBuild:F3} ms; frame anim marker {animRec.LastValue / 1e6:F3} ms; {counts}{(CreaturePath.Instanced && draw != null ? $", instances drawn {draw.Drawn}" : "")}");
                }

                int left = CreatureBuildSystem.Count - n;
                vem.DestroyEntity(bench);
                bench.Dispose();
                int gone = 0;
                double worstGone = 0;
                while (CreatureBuildSystem.Count > left && gone < 600)
                {
                    yield return null;
                    gone++;
                    worstGone = math.max(worstGone, buildRec.LastValue / 1e6);
                }
                lines.Add($"  N {n} removal: {gone} frames, worst build system {worstGone:F2} ms");
            }

            var brain = BrainBench(server);
            while (brain.MoveNext()) yield return null;
            lines.AddRange(brainLines);

            CreatureView.Current = default;
            FreezeCreatures(false);
            for (int i = 0; i < lines.Count; i++) GameStatus.Lines[$"~creature bench {i:D2}"] = lines[i];
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"creature-bench-{path}.txt"), lines);
        }

        static readonly List<string> brainLines = new List<string>();

        static bool FirstPlayer(EntityManager em, out float2 at)
        {
            using var pq = em.CreateEntityQuery(typeof(Player), typeof(Unity.Transforms.LocalTransform));
            using var players = pq.ToComponentDataArray<Unity.Transforms.LocalTransform>(Allocator.Temp);
            at = players.Length > 0 ? players[0].Position.xz : default;
            return players.Length > 0;
        }

        static void DestroyBrainBench(EntityManager em)
        {
            using var bq = em.CreateEntityQuery(typeof(CreatureState), typeof(CreatureBrain));
            using var all = bq.ToEntityArray(Allocator.Temp);
            foreach (var e in all) if (em.GetComponentData<CreatureState>(e).Id < 0) em.DestroyEntity(e);
        }

        static System.Collections.IEnumerator BrainBench(Unity.Entities.World server)
        {
            brainLines.Clear();
            var sem = server.EntityManager;
            var host = server.GetExistingSystem<CreatureHostSystem>();
            if (host == SystemHandle.Null) { brainLines.Add("brain bench: no host system"); yield break; }
            if (!FirstPlayer(sem, out float2 at)) { brainLines.Add("brain bench: no players"); yield break; }
            float reach = CreaturePlanSystem.DespawnReach > 0 ? CreaturePlanSystem.DespawnReach * 0.7f : 150f;
            var systems = new[] { host };
            FreezeCreatures(false);
            yield return null;
            double baseMs = TimeSystems(server, systems, BenchSamples);
            brainLines.Add($"brain baseline (plan creatures only): host {baseMs:F3} ms");
            using var brainRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName(server, typeof(CreatureHostSystem)), 4);
            int[] counts = { 10, 50, 100, 250, 500, 2000, 5000 };
            for (int r = 0; r < counts.Length; r++)
            {
                int n = counts[r];
                AddBrainHerds(server, host, n, r, at, reach);
                yield return null;
                yield return null;
                var (ms, single) = TimeBoth(server, systems);
                double frame = 0;
                for (int k = 0; k < 10; k++) { yield return null; frame = math.max(frame, brainRec.LastValue / 1e6); }
                brainLines.Add($"brain N {n}: host {ms:F3} ms ({(ms - baseMs) * 1000.0 / n:F2} us per creature over baseline) / {single:F3} ms one thread, worst in-frame marker {frame:F3} ms");
                DestroyBrainBench(sem);
                yield return null;
            }
            FreezeCreatures(true);
        }
    }
}
