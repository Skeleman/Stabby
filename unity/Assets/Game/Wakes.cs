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
        public float4 At, Motion;
        public float Pixels;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CreatureAnimSystem))]
    public partial struct WakeEmitterSystem : ISystem
    {
        public const int Cap = 16;
        public const float FadeFrom = 4f, FadeTo = 12f, PlayerRadius = 0.6f, PlayerHeight = 3f, TeleportSpeed = 20f;

        public NativeList<float4> Shown;
        public int Published;

        NativeList<float4> back;
        NativeList<WakeCandidate> candidates;
        NativeHashMap<Entity, float3> last;
        JobHandle running;
        bool scheduled;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            Shown = new NativeList<float4>(2 * Cap, Allocator.Persistent);
            back = new NativeList<float4>(2 * Cap, Allocator.Persistent);
            candidates = new NativeList<WakeCandidate>(64, Allocator.Persistent);
            last = new NativeHashMap<Entity, float3>(8, Allocator.Persistent);
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
            var view = DetailView.Current;
            var ground = SystemAPI.GetSingleton<Ground>();
            float dt = SystemAPI.Time.DeltaTime;
            candidates.Clear();
            var h = new CreatureWakeJob { Ground = ground, View = view, Out = candidates }.Schedule(state.Dependency);
            h = new PlayerWakeJob { View = view, Dt = dt, Last = last, Out = candidates }.Schedule(h);
            running = new PickWakesJob { In = candidates, Out = back }.Schedule(h);
            state.Dependency = running;
            scheduled = true;
        }

        static float Weight(DetailView view, float sizeM, float3 at, out float px)
        {
            px = view.ScreenSize(sizeM, at);
            float q = math.max(view.Quality, 0.1f);
            return Detail.Crossfade(px, FadeFrom / q, FadeTo / q);
        }

        [BurstCompile]
        partial struct CreatureWakeJob : IJobEntity
        {
            public Ground Ground;
            public DetailView View;
            public NativeList<WakeCandidate> Out;

            void Execute(in CreatureRoot c)
            {
                if (!c.Visible) return;
                var sc = c.Scale;
                float bed = Ground.HeightAt(c.Position.x, c.Position.z);
                var at = new float3(c.Position.x, bed + sc.Stand, c.Position.z);
                float w = Weight(View, math.max(sc.Length, sc.Stand), at, out float px);
                if (w <= 0f) return;
                math.sincos(c.Heading, out float s, out float co);
                float speed = c.Move * c.WalkSpeed;
                Out.Add(new WakeCandidate
                {
                    At = new float4(c.Position.x, bed, c.Position.z, math.max(0.15f, 0.3f * sc.Length)),
                    Motion = new float4(s * speed, co * speed, math.max(0.3f, sc.Stand), w),
                    Pixels = px,
                });
            }
        }

        [BurstCompile]
        [WithAll(typeof(Player))]
        partial struct PlayerWakeJob : IJobEntity
        {
            public DetailView View;
            public float Dt;
            public NativeHashMap<Entity, float3> Last;
            public NativeList<WakeCandidate> Out;

            void Execute(Entity e, in LocalTransform t)
            {
                var pos = t.Position;
                float2 v = 0f;
                if (Last.TryGetValue(e, out var was) && Dt > 0f)
                {
                    v = (pos.xz - was.xz) / Dt;
                    if (math.length(v) > TeleportSpeed) v = 0f;
                }
                Last[e] = pos;
                float w = Weight(View, PlayerHeight, pos + new float3(0f, 0.5f * PlayerHeight, 0f), out float px);
                if (w <= 0f) return;
                Out.Add(new WakeCandidate
                {
                    At = new float4(pos, PlayerRadius),
                    Motion = new float4(v.x, v.y, PlayerHeight, w),
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
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * WakeEmitterSystem.Cap, 16);
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
            Count = e.Shown.Length / 2;
            if (Count > 0) buffer.SetData(e.Shown.AsArray(), 0, 0, e.Shown.Length);
            Shader.SetGlobalBuffer(WakesId, buffer);
            Shader.SetGlobalInt(CountId, Count);
        }
    }
}
