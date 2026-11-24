using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LightBudgetSystem))]
    public partial class LightPresenterSystem : SystemBase
    {
        public const float CoreM = 0.09f, CoreGain = 2f, HaloShare = 0.18f, HaloGain = 0.06f;
        static readonly int GlowsId = Shader.PropertyToID("_Glows");
        Task<LightBudget.Caps> capsTask;
        readonly List<Light> pool = new List<Light>();
        public struct GlowItem { public float4 PosSize, Color; }
        public struct RealLight { public float3 Position, Color; public float Range, Intensity, ShadowWeight; public bool Shadow; }
        readonly UploadRing<GlowItem> glows = new UploadRing<GlowItem>(GraphicsBuffer.Target.Structured, 32, "Light glows");
        NativeList<RealLight> reals;
        NativeArray<int> stats;
        JobHandle job;
        bool running;
        Material material;
        EntityQuery query;
        public int Real, Shadowed, Glows, Cores;
        public bool Fading;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<LightEmitter>());
            reals = new NativeList<RealLight>(64, Allocator.Persistent);
            stats = new NativeArray<int>(3, Allocator.Persistent);
            capsTask = Task.Run(() => ContentFiles.Objects(ContentFiles.Biomes()).LightCaps);
        }

        protected override void OnDestroy()
        {
            job.Complete();
            glows.Release();
            reals.Dispose();
            stats.Dispose();
            if (material != null) Object.Destroy(material);
            foreach (var l in pool) if (l != null) Object.Destroy(l.gameObject);
            pool.Clear();
        }

        protected override void OnUpdate()
        {
            Submit();
            if (capsTask != null && capsTask.IsCompleted)
            {
                if (capsTask.IsFaulted) GameStatus.Failure = capsTask.Exception.ToString();
                else if (!SystemAPI.HasSingleton<LightBudgetState>()) EntityManager.CreateSingleton(new LightBudgetState { Caps = capsTask.Result, Budgeted = true });
                capsTask = null;
            }
            bool hidden = !SystemAPI.TryGetSingleton<LightBudgetState>(out var budget) || budget.Hidden;
            if (hidden) return;
            int need = query.CalculateEntityCount();
            job = new GlowJob
            {
                T = (float)(DayNightSystem.Clock % 3600.0), Glows = glows.Lock(2 * need), Reals = reals, Stats = stats,
            }.Schedule(query, new ResetJob { Reals = reals, Stats = stats }.Schedule(Dependency));
            Dependency = job;
            running = true;
        }

        public void Submit()
        {
            int used = 0;
            Real = Shadowed = Glows = Cores = 0;
            Fading = false;
            if (running)
            {
                running = false;
                job.Complete();
                Cores = stats[0];
                Glows = stats[1];
                Fading = stats[2] != 0;
                glows.Unlock(Cores + Glows);
                for (int i = 0; i < reals.Length; i++)
                {
                    var r = reals[i];
                    var l = Take(used++);
                    l.transform.position = r.Position;
                    l.color = new Color(r.Color.x, r.Color.y, r.Color.z);
                    l.range = r.Range;
                    l.intensity = r.Intensity;
                    l.shadows = r.Shadow ? LightShadows.Soft : LightShadows.None;
                    l.shadowStrength = r.ShadowWeight;
                    Real++;
                    if (r.Shadow) Shadowed++;
                }
            }
            for (int i = used; i < pool.Count; i++) if (pool[i].enabled) pool[i].enabled = false;
            Draw(Cores + Glows);
        }

        Light Take(int i)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject("Stabby light " + pool.Count) { hideFlags = HideFlags.DontSave };
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadowBias = 0.05f;
                l.shadowNormalBias = 0.4f;
                pool.Add(l);
            }
            var light = pool[i];
            if (!light.enabled) light.enabled = true;
            return light;
        }

        void Draw(int count)
        {
            if (count == 0) return;
            if (material == null)
            {
                var shader = Shader.Find("Stabby/Glow");
                if (shader == null) return;
                material = new Material(shader) { name = "Stabby light glow" };
            }
            material.SetBuffer(GlowsId, glows.Current);
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, count);
        }

        [BurstCompile]
        struct ResetJob : IJob
        {
            public NativeList<RealLight> Reals;
            public NativeArray<int> Stats;

            public void Execute()
            {
                Reals.Clear();
                Stats[0] = Stats[1] = Stats[2] = 0;
            }
        }

        [BurstCompile]
        partial struct GlowJob : IJobEntity
        {
            public float T;
            public NativeArray<GlowItem> Glows;
            public NativeList<RealLight> Reals;
            public NativeArray<int> Stats;

            static float4 HaloTint(float3 c)
            {
                var luma3 = new float3(0.2126f, 0.7152f, 0.0722f);
                var deep = c * c;
                float luma = math.dot(c, luma3), deepLuma = math.dot(deep, luma3);
                deep *= deepLuma > 1e-5f ? luma / deepLuma : 0f;
                return new float4(deep, 0f);
            }

            void Execute(in LightEmitter e)
            {
                var s = e.Slot;
                float intensity = e.Intensity * e.Gain(T);
                if ((s.Shadow > 0f && s.Shadow < 1f) || (s.Real > 0f && s.Real < 1f) || (s.Glow > 0f && s.Glow < 1f)) Stats[2] = 1;
                if (s.Real > 0f)
                    Reals.Add(new RealLight { Position = e.Position, Color = e.Color, Range = e.RangeM, Intensity = intensity * s.RealWeight, Shadow = s.Shadow > 0f, ShadowWeight = s.ShadowWeight });
                int at = Stats[0] + Stats[1];
                var p = new float4(e.Position, CoreM);
                Glows[at++] = new GlowItem { PosSize = p, Color = new float4(e.Color, CoreGain * intensity) };
                Stats[0]++;
                if (s.Glow > 0f)
                {
                    Glows[at] = new GlowItem { PosSize = new float4(e.Position, e.RangeM * HaloShare), Color = HaloTint(e.Color) + new float4(0, 0, 0, HaloGain * intensity * s.GlowWeight) };
                    Stats[1]++;
                }
            }
        }
    }
}
