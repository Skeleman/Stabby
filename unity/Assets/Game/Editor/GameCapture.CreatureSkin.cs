using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const int SkinCount = 300;
        const int SkinFrames = 120;

        static double RenderSynced(Camera cam, RenderTexture rt, Texture2D px)
        {
            long t0 = Stopwatch.GetTimestamp();
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        struct SkinTimes
        {
            public double Frame, Main, Render;
        }

        static System.Collections.IEnumerator SkinMeasure(Camera cam, ProfilerRecorder mainRec, SkinTimes[] result)
        {
            var rt = new RenderTexture(1920, 1080, 24);
            var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            List<double> frame = new List<double>(), main = new List<double>(), render = new List<double>();
            for (int k = 0; k < SkinFrames + 10; k++)
            {
                yield return null;
                if (k < 10) continue;
                frame.Add(Time.unscaledDeltaTime * 1000.0);
                main.Add(mainRec.LastValue / 1e6);
                render.Add(RenderSynced(cam, rt, px));
            }
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(px);
            result[0] = new SkinTimes { Frame = Median(frame), Main = Median(main), Render = Median(render) };
        }

        static (Vector3, Vector3) FlatSpot(in Ground ground, Vector3 start, Vector3 fwd)
        {
            float baseYaw = math.atan2(fwd.x, fwd.z);
            float best = float.MaxValue;
            Vector3 bestEye = start, bestFwd = fwd;
            for (int ring = 0; ring < 4; ring++)
                for (int a = 0; a < 12; a++)
                {
                    float yaw = baseYaw + math.radians(30f * a);
                    var dir = new Vector3(math.sin(yaw), 0, math.cos(yaw));
                    var at = start + dir * (40f * ring);
                    float h0 = ground.HeightAt(at.x, at.z);
                    float worst = 0;
                    for (int s = -2; s <= 2; s++)
                    {
                        float sy = yaw + math.radians(12.5f * s);
                        for (int r = 1; r <= 8; r++)
                        {
                            float d = 5f * r;
                            worst = math.max(worst, math.abs(ground.HeightAt(at.x + math.sin(sy) * d, at.z + math.cos(sy) * d) - h0));
                        }
                    }
                    if (worst < best) { best = worst; bestEye = at; bestFwd = dir; }
                }
            return (bestEye, bestFwd);
        }

        static System.Collections.IEnumerator CreatureSkin(Camera cam, string dir)
        {
            string path = System.Environment.GetEnvironmentVariable(BenchFlag) ?? "entity";
            var view = ViewWorld();
            if (view == null) { GameStatus.Lines["~creature skin"] = "creature skin: no view world"; yield break; }
            var vem = view.EntityManager;
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            if (!hasGround) { GameStatus.Lines["~creature skin"] = "creature skin: no ground"; yield break; }

            GameStatus.HoldCamera = true;
            var eye = cam.transform.position;
            var flatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            (eye, flatFwd) = FlatSpot(ground, eye, flatFwd);
            eye.y = GroundHeight(eye.x, eye.z) + 1.7f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(flatFwd) * Quaternion.Euler(6f, 0, 0));
            FreezeCreatures(true);
            CreatureView.Current = new CreatureView { Override = 1, Move = 1, Phase = 0.3f };
            var settle = Settle(240);
            while (settle.MoveNext()) yield return null;

            var draw = view.GetExistingSystemManaged<CreatureDrawSystem>();
            var animTypes = CreaturePath.Instanced ? new[] { typeof(CreatureDrawSystem), typeof(CreatureAnimSystem) } : new[] { typeof(CreatureAnimSystem) };
            var anim = BenchSystems(view, animTypes);
            using var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
            int baseCount = CreatureBuildSystem.Count;

            var times = new SkinTimes[1];
            var measure = SkinMeasure(cam, mainRec, times);
            while (measure.MoveNext()) yield return null;
            var t0 = times[0];
            var animBase = TimeSystems(view, anim, BenchSamples);
            var s0 = SampleMemory(vem, draw);

            var bench = SpawnBench(view, SkinCount, 70);
            PlaceBench(view, bench, cam, 1.6f, 0.9f, 40f, ground);
            int frames = 0;
            while (CreatureBuildSystem.Count < baseCount + SkinCount && frames < 3600) { yield return null; frames++; }
            var (rigRef, length, stand, _, _, _) = BenchRig(vem);
            float q = math.max(DetailView.Current.Quality, 0.1f);
            float nearPx = rigRef.IsCreated ? BenchPixels(rigRef, 0, q) : 120f;
            PlaceBench(view, bench, cam, length, stand, nearPx, ground);
            for (int k = 0; k < 30; k++) yield return null;
            int retier = 0;
            using (var asking = vem.CreateEntityQuery(typeof(CreatureRetier)))
                while (asking.CalculateEntityCount() > 0 && retier < 1200) { yield return null; retier++; }
            var counts = TierCounts(view, ground);
            int drawn = draw != null ? draw.Drawn : 0;

            measure = SkinMeasure(cam, mainRec, times);
            while (measure.MoveNext()) yield return null;
            var t1 = times[0];
            var animTime = TimeSystems(view, anim, BenchSamples);
            var s1 = SampleMemory(vem, draw);
            Shot(cam, null, "warmup");
            Shot(cam, dir, $"skin-crowd-{path}");

            int built = frames;
            vem.DestroyEntity(bench);
            bench.Dispose();
            int gone = 0;
            while (CreatureBuildSystem.Count > baseCount && gone < 900) { yield return null; gone++; }
            var close = SpawnBench(view, 3, 71);
            PlaceBench(view, close, cam, length, stand, nearPx, ground);
            frames = 0;
            while (CreatureBuildSystem.Count < baseCount + 3 && frames < 600) { yield return null; frames++; }
            for (int k = 0; k < 30; k++) yield return null;
            var closeCounts = TierCounts(view, ground);
            Shot(cam, null, "warmup");
            Shot(cam, dir, $"skin-close-{path}");
            PlaceBench(view, close, cam, length, stand, nearPx * 2f, ground);
            for (int k = 0; k < 30; k++) yield return null;
            var closerCounts = TierCounts(view, ground);
            Shot(cam, null, "warmup");
            Shot(cam, dir, $"skin-closer-{path}");
            vem.DestroyEntity(close);
            close.Dispose();

            var lines = new List<string>
            {
                $"creature skin bench path {path}, quality {q:F2}, {SkinCount} creatures at the near tier ({nearPx:F1} px), 1920x1080 render target, {baseCount} plan creatures shown",
                $"built in {built} frames, retiered in {30 + retier} frames; {counts}{(CreaturePath.Instanced && draw != null ? $", instances drawn {drawn}" : "")}",
                $"frame ms (median of {SkinFrames}): {t1.Frame:F2} (baseline {t0.Frame:F2}, +{t1.Frame - t0.Frame:F2}); main thread {t1.Main:F2} (baseline {t0.Main:F2}, +{t1.Main - t0.Main:F2})",
                $"render ms, camera render plus GPU sync: {t1.Render:F2} (baseline {t0.Render:F2}, +{t1.Render - t0.Render:F2})",
                $"anim systems: {animTime:F3} ms (baseline {animBase:F3}, +{animTime - animBase:F3})",
                $"entities: {(s1.Entities - s0.Entities) / (float)SkinCount:F2} client entities per creature, {s1.Entities - s0.Entities} total, chunks {(s1.Chunks - s0.Chunks) * 16f / SkinCount:F2} KiB per creature",
                $"memory per creature: native {Per(s1.Native - s0.Native, SkinCount)}, managed {Per(s1.Managed - s0.Managed, SkinCount)}, instance GPU buffers {Per(s1.Gpu - s0.Gpu, SkinCount)}, graphics driver {Per(s1.Driver - s0.Driver, SkinCount)}",
                $"close shots, 3 creatures at {nearPx:F0} px: {closeCounts}; at {nearPx * 2f:F0} px: {closerCounts}",
            };
            CreatureView.Current = default;
            FreezeCreatures(false);
            for (int i = 0; i < lines.Count; i++) GameStatus.Lines[$"~creature skin {i:D2}"] = lines[i];
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"creature-skin-{path}.txt"), lines);
        }
    }
}
