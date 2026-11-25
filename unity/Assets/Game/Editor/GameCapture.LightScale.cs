using System.Collections.Generic;
using System.Diagnostics;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly int[] ScaleLights = { 100, 1000, 10000 };
        const int ScaleFrames = 300, ScaleDirect = 30;
        const float ScaleRadius = 250f;

        static double Pct(List<double> xs, double p)
        {
            if (xs.Count == 0) return 0;
            xs.Sort();
            return xs[(int)math.round(p * (xs.Count - 1))];
        }

        static System.Collections.IEnumerator LightScaleTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var eye = new Vector3(home.x, GroundHeight(home.x, home.z) + 2.5f, home.z);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.forward + Vector3.down * 0.12f));
            for (var w = ShowHour(NightHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(240); w.MoveNext();) yield return null;
            var world = ViewWorld();
            if (world == null) { GameStatus.Failure = "lightscale: no client view world"; yield break; }
            var em = world.EntityManager;
            var stateQuery = em.CreateEntityQuery(typeof(LightBudgetState));
            for (int k = 0; k < 600 && stateQuery.IsEmpty; k++) yield return null;
            if (stateQuery.IsEmpty) { GameStatus.Failure = "lightscale: LightPresenterSystem never created the budget"; yield break; }
            var presenter = world.GetExistingSystemManaged<LightPresenterSystem>();
            var budgetHandle = world.GetExistingSystem<LightBudgetSystem>();
            var presenterHandle = world.GetExistingSystem<LightPresenterSystem>();
            var emitterQuery = em.CreateEntityQuery(typeof(LightEmitter));
            var objects = ContentFiles.Objects(ContentFiles.Biomes());
            var kinds = new LightKind[3];
            var ids = new[] { "point", "fire", "candle" };
            for (int i = 0; i < 3; i++)
                foreach (var l in objects.Lights) if (l.Id == ids[i]) kinds[i] = l;
            for (int i = 0; i < 3; i++) if (kinds[i] == null) { GameStatus.Failure = "lightscale: light kind " + ids[i] + " missing from content"; yield break; }
            var caps = stateQuery.GetSingleton<LightBudgetState>().Caps;
            int size = UnsafeUtility.SizeOf<LightEmitter>();
            int preexisting = emitterQuery.CalculateEntityCount();
            var lines = new List<string>
            {
                $"light scale bench at {Screen.width}x{Screen.height}, night hour {DayNightSystem.Shown.Hour:F2}, caps shadowed {caps.Shadowed}, unshadowed {caps.Unshadowed}, glow {caps.Glow}; {preexisting} world emitters already present; emitters scattered over a {ScaleRadius:F0} m disc around the eye; LightEmitter {size} B; neither system schedules jobs",
            };
            for (var w = SetLights(stateQuery, presenter, false, true); w.MoveNext();) yield return null;

            using var budgetRec = new SystemMarker(MarkerName(world, typeof(LightBudgetSystem)), 4);
            using var presenterRec = new SystemMarker(MarkerName(world, typeof(LightPresenterSystem)), 4);
            using var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);

            foreach (int n in ScaleLights)
            {
                using var spawned = em.CreateEntity(em.CreateArchetype(typeof(LightEmitter)), n, Allocator.Temp);
                for (int i = 0; i < n; i++)
                {
                    uint h = math.hash(new uint2((uint)i, 0x11D7u));
                    uint g = math.hash(new uint2(h, 7u));
                    float r = ScaleRadius * math.sqrt((h & 0xFFFF) / 65535f), a = (g & 0xFFFF) / 65535f * 2f * math.PI;
                    float x = eye.x + r * math.sin(a), z = eye.z + r * math.cos(a);
                    int k = i % 3;
                    var pos = new float3(x, GroundHeight(x, z) + (k == 0 ? 1.6f : k == 1 ? 0.4f : 0.9f), z);
                    em.SetComponentData(spawned[i], k == 0 ? LightEmitter.Of(kinds[0], pos, new float3(1f, 0.72f, 0.42f), 9f, 6f)
                        : k == 1 ? LightEmitter.Of(kinds[1], pos, new float3(1f, 0.48f, 0.18f), 12f, 9f)
                        : LightEmitter.Of(kinds[2], pos, new float3(1f, 0.78f, 0.5f), 5f, 2f));
                }
                for (int k = 0; k < 30; k++) yield return null;
                for (int k = 0; k < 900 && presenter.Fading; k++) yield return null;
                for (int k = 0; k < 10; k++) yield return null;

                var bMs = new List<double>(ScaleFrames);
                var pMs = new List<double>(ScaleFrames);
                var main = new List<double>(ScaleFrames);
                var dt = new List<double>(ScaleFrames);
                for (int k = 0; k < ScaleFrames; k++)
                {
                    yield return null;
                    bMs.Add(budgetRec.LastValue / 1e6);
                    pMs.Add(presenterRec.LastValue / 1e6);
                    main.Add(mainRec.LastValue / 1e6);
                    dt.Add(Time.unscaledDeltaTime * 1000.0);
                }

                var bDirect = new List<double>(ScaleDirect);
                var pDirect = new List<double>(ScaleDirect);
                long bGc = 0, pGc = 0;
                for (int k = 0; k < ScaleDirect + 3; k++)
                {
                    yield return null;
                    long g0 = System.GC.GetAllocatedBytesForCurrentThread();
                    long t0 = Stopwatch.GetTimestamp();
                    budgetHandle.Update(world.Unmanaged);
                    long t1 = Stopwatch.GetTimestamp();
                    long g1 = System.GC.GetAllocatedBytesForCurrentThread();
                    presenterHandle.Update(world.Unmanaged);
                    long t2 = Stopwatch.GetTimestamp();
                    long g2 = System.GC.GetAllocatedBytesForCurrentThread();
                    if (k < 3) continue;
                    bDirect.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
                    pDirect.Add((t2 - t1) * 1000.0 / Stopwatch.Frequency);
                    bGc += g1 - g0;
                    pGc += g2 - g1;
                }
                int total = emitterQuery.CalculateEntityCount();
                lines.Add($"N {n} ({total} emitters live): " + TierLine(stateQuery, presenter));
                lines.Add($"  LightBudgetSystem main thread: frame marker median {Pct(bMs, 0.5):F3} ms, p99 {Pct(bMs, 0.99):F3} ms; direct update median {Pct(bDirect, 0.5):F3} ms, p99 {Pct(bDirect, 0.99):F3} ms; jobs 0 ms; GC {bGc / (double)ScaleDirect:F0} B per update; temp {total * size} B per update (1 Temp array, copied back in place)");
                lines.Add($"  LightPresenterSystem main thread: frame marker median {Pct(pMs, 0.5):F3} ms, p99 {Pct(pMs, 0.99):F3} ms; direct update median {Pct(pDirect, 0.5):F3} ms, p99 {Pct(pDirect, 0.99):F3} ms; jobs 0 ms; GC {pGc / (double)ScaleDirect:F0} B per update; temp {total * size} B per update (1 Temp array); glow upload {(presenter.Cores + presenter.Glows) * 32} B per frame");
                lines.Add($"  frame: main thread median {Pct(main, 0.5):F2} ms, p99 {Pct(main, 0.99):F2} ms; dt median {Pct(dt, 0.5):F2} ms, p99 {Pct(dt, 0.99):F2} ms over {ScaleFrames} frames");
                em.DestroyEntity(spawned);
                for (int k = 0; k < 10; k++) yield return null;
            }
            GameStatus.Lines["~lightscale"] = string.Join("\n", lines);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "light-scale.txt"), lines);
        }
    }
}
