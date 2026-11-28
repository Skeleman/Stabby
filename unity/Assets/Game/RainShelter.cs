using Stabby.World;
using Unity.Burst;
using Unity.Collections;
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
        public const float TexelM = 1f, MoveM = 8f;
        static readonly int GridId = Shader.PropertyToID("_StabbyShelter"), MinId = Shader.PropertyToID("_StabbyShelterMin"), InvId = Shader.PropertyToID("_StabbyShelterInv");

        Texture2D texture;
        NativeArray<float> floors;
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
            if (texture != null) Object.Destroy(texture);
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
                GameStatus.Lines[World.Name + "~shelter"] = $"{World.Name}: shelter build {++builds}: {Across}x{Across} m around {center.x:F0},{center.y:F0},{center.z:F0} in {clock.Elapsed.TotalMilliseconds:F1} ms";
                return;
            }
            var cam = Camera.main;
            if (cam == null || PrecipSystem.Precip <= 0) return;
            float3 p = cam.transform.position, d = math.abs(p - center);
            if (d.x < MoveM && d.z < MoveM && d.y < MoveM) return;
            center = p;
            if (!floors.IsCreated) floors = new NativeArray<float>(Across * Across, Allocator.Persistent);
            pending = math.floor((p - 0.5f * TexelM * Across) / TexelM) * TexelM;
            job = new ShelterJob { Ground = SystemAPI.GetSingleton<Ground>(), Min = pending.xz, Eye = p.y, Floors = floors }.Schedule(Across * Across, 32);
            clock.Restart();
            running = true;
        }
    }

    [BurstCompile]
    struct ShelterJob : IJobParallelFor
    {
        public Ground Ground;
        public float2 Min;
        public float Eye;
        public NativeArray<float> Floors;

        public void Execute(int c)
        {
            const int n = ShelterSystem.Across;
            const float texel = ShelterSystem.TexelM;
            float top = Ground.Top;
            float x = math.clamp(Min.x + (c % n + 0.5f) * texel, 0, top), z = math.clamp(Min.y + (c / n + 0.5f) * texel, 0, top);
            Floors[c] = Shelter.Floor(Ground, Ground.EditsNear(x, z, 0), Ground.WalkStep, x, z, Eye + Shelter.ReachM, Eye - Shelter.ReachM);
        }
    }
}
