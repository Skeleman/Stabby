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
    public static class CaveDark
    {
        public const float SkyReach = 1.4f, SkyFall = 0.7f;

        public static void Add(in FeatureEdit e, float2 light, float3 p, ref float inside, ref float lit)
        {
            float3 a = new float3(e.Ax, e.Ay, e.Az), ab = new float3(e.Bx, e.By, e.Bz) - a, ap = p - a;
            float ll = math.dot(ab, ab), t = ll > 0 ? math.saturate(math.dot(ap, ab) / ll) : 0;
            float d = math.length(ap - t * ab) / e.R;
            if (d >= SkyReach) return;
            inside = math.max(inside, 1 - math.smoothstep(1f, SkyReach, d));
            lit = math.max(lit, math.exp(-math.lerp(light.x, light.y, t) / (SkyFall * e.R)));
        }

        public static float Of(float inside, float lit, float cover) => inside * math.smoothstep(0.5f, 3f, cover) * (1 - lit);
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CaveDarkSystem : SystemBase
    {
        public const int Across = 128, Tall = 64;
        public const float TexelM = 1f, MoveM = 16f;
        static readonly int VolumeId = Shader.PropertyToID("_StabbyCaveDark"), MinId = Shader.PropertyToID("_StabbyCaveDarkMin"), InvId = Shader.PropertyToID("_StabbyCaveDarkInv");

        Texture3D texture;
        NativeArray<byte> voxels;
        JobHandle job;
        bool running;
        int builds;
        readonly Stopwatch clock = new Stopwatch();
        float3 pending, center = float.MaxValue;

        protected override void OnCreate() => RequireForUpdate<Ground>();

        protected override void OnDestroy()
        {
            job.Complete();
            Shader.SetGlobalVector(MinId, Vector4.zero);
            if (voxels.IsCreated) voxels.Dispose();
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
                    texture = new Texture3D(Across, Tall, Across, TextureFormat.R8, false) { name = "CaveDark", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    Shader.SetGlobalTexture(VolumeId, texture);
                }
                texture.SetPixelData(voxels, 0);
                texture.Apply(false, false);
                Shader.SetGlobalVector(MinId, new Vector4(pending.x, pending.y, pending.z, 1));
                Shader.SetGlobalVector(InvId, new Vector4(1f / (Across * TexelM), 1f / (Tall * TexelM), 1f / (Across * TexelM), 0));
                GameStatus.Lines[World.Name + "~cavedark"] = $"{World.Name}: cave dark build {++builds}: {Across}x{Tall}x{Across} m around {center.x:F0},{center.y:F0},{center.z:F0} in {clock.Elapsed.TotalMilliseconds:F1} ms";
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            float3 p = cam.transform.position, d = math.abs(p - center);
            if (d.x < MoveM && d.z < MoveM && d.y < 0.5f * MoveM) return;
            center = p;
            if (!voxels.IsCreated) voxels = new NativeArray<byte>(Across * Tall * Across, Allocator.Persistent);
            pending = math.floor((p - 0.5f * TexelM * new float3(Across, Tall, Across)) / TexelM) * TexelM;
            job = new CaveDarkJob { Ground = SystemAPI.GetSingleton<Ground>(), Min = pending, Voxels = voxels }.Schedule(Across * Across, 16);
            clock.Restart();
            running = true;
        }
    }

    [BurstCompile]
    struct CaveDarkJob : IJobParallelFor
    {
        public Ground Ground;
        public float3 Min;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Voxels;

        public void Execute(int c)
        {
            const int n = CaveDarkSystem.Across, tall = CaveDarkSystem.Tall;
            const float texel = CaveDarkSystem.TexelM;
            int i = c % n, k = c / n, at = i + n * tall * k;
            for (int j = 0; j < tall; j++) Voxels[at + n * j] = 0;
            ref var g = ref Ground.Blob.Value;
            float x = Min.x + (i + 0.5f) * texel, z = Min.z + (k + 0.5f) * texel;
            int cell = EditGrid.CellOf(z, g.EditAcross) * g.EditAcross + EditGrid.CellOf(x, g.EditAcross);
            int s0 = g.EditStart[cell], s1 = g.EditStart[cell + 1];
            bool covered = false, lit = false;
            for (int s = s0; s < s1; s++)
            {
                var e = g.Edits[g.EditItems[s]];
                lit |= CaveLight.Lit(e);
                covered |= x >= e.MinX && x <= e.MaxX && z >= e.MinZ && z <= e.MaxZ;
            }
            if (!covered || !lit) return;
            float h = Ground.Surface(math.clamp(x, 0, g.Top), math.clamp(z, 0, g.Top));
            for (int j = 0; j < tall; j++)
            {
                float y = Min.y + (j + 0.5f) * texel, cover = h - y;
                if (cover <= 0.5f) break;
                float inside = 0, sky = 0;
                var p = new float3(x, y, z);
                for (int s = s0; s < s1; s++)
                {
                    int f = g.EditItems[s];
                    var e = g.Edits[f];
                    if (CaveLight.Lit(e)) CaveDark.Add(e, g.CaveLight[f], p, ref inside, ref sky);
                }
                Voxels[at + n * j] = (byte)math.round(255f * CaveDark.Of(inside, sky, cover));
            }
        }
    }
}
