using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly int[] DumpBodies = { 0, 0, 0, 0, 1, 2 };

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        static void DumpPose(Unity.Entities.World w, NativeArray<Entity> bench, string label, List<string> lines)
        {
            var em = w.EntityManager;
            em.CompleteAllTrackedJobs();
            using var q = em.CreateEntityQuery(typeof(CreatureRoot), typeof(CreatureLink));
            using var roots = q.ToComponentDataArray<CreatureRoot>(Allocator.Temp);
            using var rootEntities = q.ToEntityArray(Allocator.Temp);
            var anim = w.GetExistingSystem<CreatureAnimSystem>();
            bool gpu = false;
            NativeList<CreatureGpu> instances = default;
            if (anim != SystemHandle.Null)
            {
                ref var sys = ref w.Unmanaged.GetUnsafeSystemRef<CreatureAnimSystem>(anim);
                sys.Gpu.Complete();
                instances = sys.Instances;
                using var tq = em.CreateEntityQuery(typeof(CreatureTally));
                gpu = tq.CalculateEntityCount() == 1 && tq.GetSingleton<CreatureTally>().Instanced;
            }
            for (int b = 0; b < bench.Length; b++)
            {
                int id = em.GetComponentData<CreatureState>(bench[b]).Id;
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i].Ghost != bench[b]) continue;
                    var links = em.GetBuffer<CreatureLink>(rootEntities[i], true);
                    var c = roots[i];
                    lines.Add($"{label} id {id} root pos {F(c.Position.x)} {F(c.Position.y)} {F(c.Position.z)} pitch {F(c.Pitch)} sink {F(c.Sink)} high {c.HighOn} low {c.LowOn} links {links.Length}");
                    if (gpu) break;
                    for (int l = 0; l < links.Length; l++)
                    {
                        if (links[l].Value == Entity.Null || !em.HasComponent<LocalToWorld>(links[l].Value)) continue;
                        var m = em.GetComponentData<LocalToWorld>(links[l].Value).Value;
                        lines.Add($"{label} id {id} link {l} {F(m.c0.x)} {F(m.c0.y)} {F(m.c0.z)} {F(m.c1.x)} {F(m.c1.y)} {F(m.c1.z)} {F(m.c2.x)} {F(m.c2.y)} {F(m.c2.z)} {F(m.c3.x)} {F(m.c3.y)} {F(m.c3.z)}");
                    }
                    break;
                }
            }
            if (!gpu || !instances.IsCreated) return;
            var rows = new List<string>();
            for (int i = 0; i < instances.Length; i++)
            {
                var x = instances[i];
                rows.Add($"{F(x.R0.x)} {F(x.R0.y)} {F(x.R0.z)} {F(x.R0.w)} {F(x.R1.x)} {F(x.R1.y)} {F(x.R1.z)} {F(x.R1.w)} {F(x.R2.x)} {F(x.R2.y)} {F(x.R2.z)} {F(x.R2.w)} group {x.Group}");
            }
            rows.Sort(System.StringComparer.Ordinal);
            foreach (var r in rows) lines.Add($"{label} gpu {r}");
        }

        static System.Collections.IEnumerator RigDump(Camera cam, string dir)
        {
            string path = System.Environment.GetEnvironmentVariable(BenchFlag) ?? "entity";
            var view = ViewWorld();
            if (view == null) { GameStatus.Lines["~rig dump"] = "rig dump: no view world"; yield break; }
            var vem = view.EntityManager;
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            if (!hasGround) { GameStatus.Lines["~rig dump"] = "rig dump: no ground"; yield break; }

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

            int baseCount = CreatureBuildSystem.Count;
            var bench = SpawnBench(view, DumpBodies.Length, 90);
            for (int i = 0; i < bench.Length; i++)
            {
                var s = vem.GetComponentData<CreatureState>(bench[i]);
                s.Body = DumpBodies[i];
                vem.SetComponentData(bench[i], s);
            }
            PlaceBench(view, bench, cam, 1.6f, 0.9f, 300f, ground);
            int frames = 0;
            while (CreatureBuildSystem.Count < baseCount + bench.Length && frames < 1200) { yield return null; frames++; }
            var (rigRef, length, stand, _, _, _) = BenchRig(vem);
            float q = math.max(DetailView.Current.Quality, 0.1f);
            var lines = new List<string> { $"rig dump path {path}, {CreatureBuildSystem.Count - baseCount} bench creatures built in {frames} frames, bench length {F(length)} stand {F(stand)}" };
            var poses = new (string Label, int Tier, CreatureView Pose)[]
            {
                ("walk-ik", 0, new CreatureView { Override = 1, Move = 1, Phase = 0.3f }),
                ("run-alert-ik", 0, new CreatureView { Override = 1, Move = 1, Run = 1, Phase = 0.15f, Alert = 1 }),
                ("graze-anim", 2, new CreatureView { Override = 1, Move = 0, Phase = 0, Down = 1 }),
                ("walk-rest", 3, new CreatureView { Override = 1, Move = 1, Phase = 0.3f }),
            };
            foreach (var p in poses)
            {
                CreatureView.Current = p.Pose;
                PlaceBench(view, bench, cam, length, stand, rigRef.IsCreated ? BenchPixels(rigRef, p.Tier, q) : 300f, ground);
                for (int k = 0; k < 60; k++) yield return null;
                DumpPose(view, bench, p.Label, lines);
            }

            vem.DestroyEntity(bench);
            bench.Dispose();
            CreatureView.Current = default;
            FreezeCreatures(false);
            GameStatus.Lines["~rig dump"] = $"rig dump: {lines.Count} lines";
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, $"rig-dump-{path}.txt"), lines);
        }
    }
}
