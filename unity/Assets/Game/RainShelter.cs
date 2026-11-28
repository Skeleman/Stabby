using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ShelterSystem : SystemBase
    {
        public const int Across = 128;
        public const float TexelM = 1f, MoveM = 8f, RoofReachM = 16f;
        static readonly int GridId = Shader.PropertyToID("_StabbyShelter"), MinId = Shader.PropertyToID("_StabbyShelterMin"), InvId = Shader.PropertyToID("_StabbyShelterInv");

        Texture2D texture;
        NativeArray<float> floors;
        NativeArray<RoofTemplate> templates;
        NativeList<Roof> roofs;
        NativeList<UnsafeList<ObjectInstance>> near;
        ObjectColumnsSystem objects;
        JobHandle job;
        bool running;
        int builds;
        readonly Stopwatch clock = new Stopwatch();
        float3 pending, center = float.MaxValue;

        protected override void OnCreate()
        {
            RequireForUpdate<Ground>();
            if (World.Name != GameSettings.ViewClient) Enabled = false;
        }

        protected override void OnDestroy()
        {
            job.Complete();
            Shader.SetGlobalVector(MinId, Vector4.zero);
            if (floors.IsCreated) floors.Dispose();
            if (templates.IsCreated) templates.Dispose();
            if (roofs.IsCreated) roofs.Dispose();
            if (near.IsCreated) near.Dispose();
            if (texture != null) Object.Destroy(texture);
        }

        void Near(float2 min)
        {
            near.Clear();
            objects ??= World.GetExistingSystemManaged<ObjectColumnsSystem>();
            var field = objects?.Field;
            if (field == null) return;
            if (!templates.IsCreated)
            {
                var defs = objects.Library.Objects;
                templates = new NativeArray<RoofTemplate>(defs.Count, Allocator.Persistent);
                for (int d = 0; d < defs.Count; d++) templates[d] = Shelter.Template(defs[d]);
            }
            int across = field.ChunksAcross;
            float size = field.ChunkMeters;
            int2 lo = (int2)math.floor((min - RoofReachM) / size), hi = (int2)math.floor((min + Across * TexelM + RoofReachM) / size);
            for (int cz = math.max(0, lo.y); cz <= math.min(across - 1, hi.y); cz++)
                for (int cx = math.max(0, lo.x); cx <= math.min(across - 1, hi.x); cx++)
                    if (objects.Objects(cx + cz * across, out var list)) near.Add(list);
        }

        protected override void OnUpdate()
        {
            if (running)
            {
                if (!job.IsCompleted) return;
                job.Complete();
                running = false;
                if (texture == null)
                {
                    texture = new Texture2D(Across, Across, TextureFormat.RFloat, false, true) { name = "Shelter", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    Shader.SetGlobalTexture(GridId, texture);
                }
                texture.SetPixelData(floors, 0);
                texture.Apply(false, false);
                Shader.SetGlobalVector(MinId, new Vector4(pending.x, 0, pending.z, 1));
                Shader.SetGlobalVector(InvId, new Vector4(1f / (Across * TexelM), 0, 1f / (Across * TexelM), 0));
                GameStatus.Lines[World.Name + "~shelter"] = $"{World.Name}: shelter build {++builds}: {Across}x{Across} m around {center.x:F0},{center.y:F0},{center.z:F0}, {roofs.Length} roofs, in {clock.Elapsed.TotalMilliseconds:F1} ms";
                return;
            }
            var cam = Camera.main;
            if (cam == null || PrecipSystem.Precip <= 0) return;
            float3 p = cam.transform.position, d = math.abs(p - center);
            if (d.x < MoveM && d.z < MoveM && d.y < MoveM) return;
            center = p;
            if (!floors.IsCreated)
            {
                floors = new NativeArray<float>(Across * Across, Allocator.Persistent);
                roofs = new NativeList<Roof>(256, Allocator.Persistent);
                near = new NativeList<UnsafeList<ObjectInstance>>(4, Allocator.Persistent);
            }
            pending = math.floor((p - 0.5f * TexelM * Across) / TexelM) * TexelM;
            Near(pending.xz);
            var gather = default(JobHandle);
            if (templates.IsCreated) gather = new RoofGatherJob { Lists = near, Templates = templates, Min = pending.xz - RoofReachM, Max = pending.xz + Across * TexelM + RoofReachM, Roofs = roofs }.Schedule();
            else roofs.Clear();
            job = new ShelterJob { Ground = SystemAPI.GetSingleton<Ground>(), Min = pending.xz, Eye = p.y, Roofs = roofs.AsDeferredJobArray(), Floors = floors }.Schedule(Across * Across, 32, gather);
            clock.Restart();
            running = true;
        }
    }

    [BurstCompile]
    struct RoofGatherJob : IJob
    {
        [ReadOnly] public NativeList<UnsafeList<ObjectInstance>> Lists;
        [ReadOnly] public NativeArray<RoofTemplate> Templates;
        public float2 Min, Max;
        public NativeList<Roof> Roofs;

        public void Execute()
        {
            Roofs.Clear();
            for (int l = 0; l < Lists.Length; l++)
            {
                var list = Lists[l];
                for (int k = 0; k < list.Length; k++)
                {
                    var o = list[k];
                    if (o.Def < 0 || o.Def >= Templates.Length || o.X < Min.x || o.X > Max.x || o.Z < Min.y || o.Z > Max.y) continue;
                    var t = Templates[o.Def];
                    if (t.Valid) Roofs.Add(Shelter.Place(t, o));
                }
            }
        }
    }

    struct RoofSpan : IRoofList
    {
        public NativeArray<Roof> Items;
        public int Count => Items.Length;
        public Roof this[int k] => Items[k];
    }

    [BurstCompile]
    struct ShelterJob : IJobParallelFor
    {
        public Ground Ground;
        public float2 Min;
        public float Eye;
        [ReadOnly] public NativeArray<Roof> Roofs;
        public NativeArray<float> Floors;

        public void Execute(int c)
        {
            const int n = ShelterSystem.Across;
            const float texel = ShelterSystem.TexelM;
            float top = Ground.Top;
            float x = math.clamp(Min.x + (c % n + 0.5f) * texel, 0, top), z = math.clamp(Min.y + (c / n + 0.5f) * texel, 0, top);
            Floors[c] = Shelter.Floor(Ground, Ground.EditsNear(x, z, 0), new RoofSpan { Items = Roofs }, Ground.WalkStep, x, z, Eye + Shelter.ReachM, Eye - Shelter.ReachM);
        }
    }
}
