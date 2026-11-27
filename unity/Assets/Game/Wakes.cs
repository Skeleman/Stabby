using Stabby.Viewer;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    public struct WakeCandidate
    {
        public float4 At, Motion, Gain;
        public float Pixels;
    }

    public struct WakeRule
    {
        public float FadePixels, FullPixels, Ring, V, Normal, RefSpeed, RefSize, SpeedExp, SizeExp, MaxGain, SmoothS;

        public static WakeRule From(WakeDef d) => new WakeRule
        {
            FadePixels = d.FadePixels, FullPixels = d.FullPixels, Ring = d.RingAmplitude, V = d.VAmplitude, Normal = d.NormalStrength, RefSpeed = d.RefSpeedMps, RefSize = d.RefSizeM,
            SpeedExp = d.SpeedExponent, SizeExp = d.SizeExponent, MaxGain = d.MaxGain, SmoothS = d.SpeedSmoothS,
        };

        public float Weight(DetailView view, float sizeM, float3 at, out float px)
        {
            px = view.ScreenSize(sizeM, at);
            float q = math.max(view.Quality, 0.1f);
            return Detail.Crossfade(px, FadePixels / q, FullPixels / q);
        }

        public float4 Gain(float speed, float sizeM)
        {
            float size = math.pow(math.max(sizeM, 0.01f) / RefSize, SizeExp);
            float pace = math.pow(math.max(speed, 0f) / RefSpeed, SpeedExp);
            float v = math.clamp(size * pace, 1f, MaxGain);
            return new float4(Ring * math.clamp(size, 1f, MaxGain), V * v, 1f + (Normal - 1f) * math.saturate(v - 1f), 0f);
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CreatureAnimSystem))]
    public partial struct WakeEmitterSystem : ISystem
    {
        public const int Cap = 16, Stride = 3;
        public const float PlayerRadius = 0.6f, PlayerHeight = 3f, TeleportSpeed = 20f;

        public NativeList<float4> Shown;
        public int Published;
        public WakeRule Rule;

        NativeList<float4> back;
        NativeList<WakeCandidate> candidates;
        NativeHashMap<Entity, float4> last;
        JobHandle running;
        bool scheduled;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Shown = new NativeList<float4>(Stride * Cap, Allocator.Persistent);
            back = new NativeList<float4>(Stride * Cap, Allocator.Persistent);
            candidates = new NativeList<WakeCandidate>(64, Allocator.Persistent);
            last = new NativeHashMap<Entity, float4>(8, Allocator.Persistent);
            state.RequireForUpdate<Ground>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            JobPoll.Teardown(running);
            Shown.Dispose();
            back.Dispose();
            candidates.Dispose();
            last.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (scheduled)
            {
                if (!JobPoll.Done(running)) return;
                scheduled = false;
                (Shown, back) = (back, Shown);
                Published++;
            }
            if (Rule.FullPixels <= 0f) return;
            var view = DetailView.Current;
            var ground = SystemAPI.GetSingleton<Ground>();
            float dt = SystemAPI.Time.DeltaTime;
            candidates.Clear();
            var h = new CreatureWakeJob { Ground = ground, View = view, Rule = Rule, Out = candidates }.Schedule(state.Dependency);
            h = new PlayerWakeJob { View = view, Rule = Rule, Dt = dt, Last = last, Out = candidates }.Schedule(h);
            running = new PickWakesJob { In = candidates, Out = back }.Schedule(h);
            state.Dependency = running;
            scheduled = true;
        }

        [BurstCompile]
        partial struct CreatureWakeJob : IJobEntity
        {
            public Ground Ground;
            public DetailView View;
            public WakeRule Rule;
            public NativeList<WakeCandidate> Out;

            void Execute(in CreatureRoot c)
            {
                if (!c.Visible) return;
                var sc = c.Scale;
                float bed = Ground.HeightAt(c.Position.x, c.Position.z);
                var at = new float3(c.Position.x, bed + sc.Stand, c.Position.z);
                float size = math.max(sc.Length, sc.Stand);
                float w = Rule.Weight(View, size, at, out float px);
                if (w <= 0f) return;
                math.sincos(c.Heading, out float s, out float co);
                float speed = c.Move * c.WalkSpeed;
                Out.Add(new WakeCandidate
                {
                    At = new float4(c.Position.x, bed, c.Position.z, math.max(0.15f, 0.3f * sc.Length)),
                    Motion = new float4(s * speed, co * speed, math.max(0.3f, sc.Stand), w),
                    Gain = Rule.Gain(speed, size),
                    Pixels = px,
                });
            }
        }

        [BurstCompile]
        [WithAll(typeof(Player))]
        partial struct PlayerWakeJob : IJobEntity
        {
            public DetailView View;
            public WakeRule Rule;
            public float Dt;
            public NativeHashMap<Entity, float4> Last;
            public NativeList<WakeCandidate> Out;

            void Execute(Entity e, in LocalTransform t)
            {
                var pos = t.Position;
                float2 v = 0f;
                if (Last.TryGetValue(e, out var was) && Dt > 0f)
                {
                    var raw = (pos.xz - was.xy) / Dt;
                    if (math.length(raw) > TeleportSpeed) raw = 0f;
                    float k = Rule.SmoothS > 0f ? 1f - math.exp(-Dt / Rule.SmoothS) : 1f;
                    v = math.lerp(was.zw, raw, k);
                }
                Last[e] = new float4(pos.xz, v);
                float w = Rule.Weight(View, PlayerHeight, pos + new float3(0f, 0.5f * PlayerHeight, 0f), out float px);
                if (w <= 0f) return;
                Out.Add(new WakeCandidate
                {
                    At = new float4(pos, PlayerRadius),
                    Motion = new float4(v.x, v.y, PlayerHeight, w),
                    Gain = Rule.Gain(math.length(v), PlayerHeight),
                    Pixels = px,
                });
            }
        }

        [BurstCompile]
        struct PickWakesJob : IJob
        {
            public NativeList<WakeCandidate> In;
            public NativeList<float4> Out;

            public void Execute()
            {
                Out.Clear();
                int n = math.min(In.Length, Cap);
                for (int k = 0; k < n; k++)
                {
                    int best = k;
                    for (int j = k + 1; j < In.Length; j++)
                        if (In[j].Pixels > In[best].Pixels) best = j;
                    var c = In[best];
                    In[best] = In[k];
                    In[k] = c;
                    Out.Add(c.At);
                    Out.Add(c.Motion);
                    Out.Add(c.Gain);
                }
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(WakeEmitterSystem))]
    public partial class WakePresenterSystem : SystemBase
    {
        static readonly int WakesId = Shader.PropertyToID("_StabbyWakes"), CountId = Shader.PropertyToID("_StabbyWakeCount");
        SystemHandle emitter;
        GraphicsBuffer buffer;
        int uploaded = -1;
        public int Count;

        protected override void OnCreate()
        {
            emitter = World.GetOrCreateSystem<WakeEmitterSystem>();
            if (World.Name != GameSettings.ViewClient)
            {
                World.Unmanaged.ResolveSystemStateRef(emitter).Enabled = false;
                Enabled = false;
                return;
            }
            World.Unmanaged.GetUnsafeSystemRef<WakeEmitterSystem>(emitter).Rule = WakeRule.From(ContentFiles.Materials(ContentFiles.MaterialsPath).Wakes);
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, WakeEmitterSystem.Stride * WakeEmitterSystem.Cap, 16);
            Shader.SetGlobalBuffer(WakesId, buffer);
            Shader.SetGlobalInt(CountId, 0);
        }

        protected override void OnDestroy()
        {
            if (buffer == null) return;
            Shader.SetGlobalInt(CountId, 0);
            buffer.Release();
        }

        protected override void OnUpdate()
        {
            ref var e = ref World.Unmanaged.GetUnsafeSystemRef<WakeEmitterSystem>(emitter);
            if (e.Published == uploaded) return;
            uploaded = e.Published;
            Count = e.Shown.Length / WakeEmitterSystem.Stride;
            if (Count > 0) buffer.SetData(e.Shown.AsArray(), 0, 0, e.Shown.Length);
            Shader.SetGlobalBuffer(WakesId, buffer);
            Shader.SetGlobalInt(CountId, Count);
        }
    }
}
