using Stabby.World;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Stabby.Game
{
    public struct LightEmitter : IComponentData
    {
        public float3 Position, Color;
        public float RangeM, Intensity, Importance, Flicker, FlickerHz, Phase;
        public Detail.Thresholds Thresholds;
        public LightBudget.Slot Slot;

        public static LightEmitter Of(LightKind kind, float3 position, float3 color, float rangeM, float intensity) => new LightEmitter
        {
            Position = position,
            Color = color,
            RangeM = rangeM,
            Intensity = intensity,
            Importance = kind.Importance,
            Thresholds = kind.Thresholds(),
            Slot = LightBudget.Slot.Fresh,
        };

        public float Gain(float t) => Flicker <= 0f ? 1f : 1f + Flicker * (0.6f * math.sin(2f * math.PI * (t * FlickerHz + Phase)) + 0.4f * math.sin(2f * math.PI * (t * FlickerHz * 2.31f + 1.7f * Phase)));
    }

    public struct LightBudgetState : IComponentData
    {
        public LightBudget.Caps Caps;
        public LightBudget.Counts Counts;
        public float Clock;
        public bool Budgeted, Hidden;
    }

    public struct LightGlow
    {
        public float4 PosSize, Color;
    }

    public struct LightResult
    {
        public LightBudget.Counts Counts;
        public int Real, Shadowed, Cores, Glows;
        public float Clock;
        public bool Fading;
    }

    struct EmitterSlots : LightBudget.ISlots
    {
        public NativeArray<LightEmitter> Items;
        public NativeArray<int> Order;

        public int Count => Items.Length;

        public LightBudget.Slot this[int i]
        {
            get => Items[i].Slot;
            set { var e = Items[i]; e.Slot = value; Items[i] = e; }
        }

        public int GetOrder(int i) => Order[i];
        public void SetOrder(int i, int v) => Order[i] = v;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct LightBudgetSystem : ISystem
    {
        public const float CoreM = 0.09f, CoreGain = 2f, HaloShare = 0.18f, HaloGain = 0.06f;

        public NativeList<LightEmitter> Shown;
        public NativeList<LightGlow> ShownGlows;
        public NativeReference<LightResult> ShownResult;
        public int Published;

        EntityQuery query;
        ComponentTypeHandle<LightEmitter> emitterType;
        EntityTypeHandle entityType;
        NativeList<LightEmitter> cache, scratch, back;
        NativeList<Entity> cacheIds, scratchIds;
        NativeList<int> order;
        NativeList<LightGlow> backGlows;
        NativeReference<LightResult> backResult;
        JobHandle running;
        bool scheduled;
        int orderVersion;
        uint seen;
        double scheduledAt;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            query = SystemAPI.QueryBuilder().WithAll<LightEmitter>().Build();
            emitterType = state.GetComponentTypeHandle<LightEmitter>(true);
            entityType = state.GetEntityTypeHandle();
            cache = new NativeList<LightEmitter>(Allocator.Persistent);
            scratch = new NativeList<LightEmitter>(Allocator.Persistent);
            back = new NativeList<LightEmitter>(Allocator.Persistent);
            Shown = new NativeList<LightEmitter>(Allocator.Persistent);
            cacheIds = new NativeList<Entity>(Allocator.Persistent);
            scratchIds = new NativeList<Entity>(Allocator.Persistent);
            order = new NativeList<int>(Allocator.Persistent);
            backGlows = new NativeList<LightGlow>(Allocator.Persistent);
            ShownGlows = new NativeList<LightGlow>(Allocator.Persistent);
            backResult = new NativeReference<LightResult>(Allocator.Persistent);
            ShownResult = new NativeReference<LightResult>(Allocator.Persistent);
            orderVersion = -1;
            scheduledAt = -1.0;
            state.RequireForUpdate<LightBudgetState>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            running.Complete();
            cache.Dispose();
            scratch.Dispose();
            back.Dispose();
            Shown.Dispose();
            cacheIds.Dispose();
            scratchIds.Dispose();
            order.Dispose();
            backGlows.Dispose();
            ShownGlows.Dispose();
            backResult.Dispose();
            ShownResult.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (scheduled)
            {
                if (!running.IsCompleted) return;
                running.Complete();
                scheduled = false;
                var items = Shown;
                Shown = back;
                back = items;
                var glows = ShownGlows;
                ShownGlows = backGlows;
                backGlows = glows;
                var result = ShownResult;
                ShownResult = backResult;
                backResult = result;
                Published++;
                SystemAPI.GetSingletonRW<LightBudgetState>().ValueRW.Counts = ShownResult.Value.Counts;
            }
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0) return;
            var budget = SystemAPI.GetSingleton<LightBudgetState>();
            double now = SystemAPI.Time.ElapsedTime;
            emitterType.Update(ref state);
            entityType.Update(ref state);
            int version = state.EntityManager.GetComponentOrderVersion<LightEmitter>();
            bool all = version != orderVersion;
            orderVersion = version;
            var bases = query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, state.Dependency, out var basesReady);
            if (all)
            {
                int n = query.CalculateEntityCountWithoutFiltering();
                scratch.ResizeUninitialized(n);
                scratchIds.ResizeUninitialized(n);
            }
            var gather = new GatherJob
            {
                Type = emitterType,
                Ids = entityType,
                Bases = bases,
                Since = seen,
                All = all,
                Items = all ? scratch.AsArray() : cache.AsArray(),
                ItemIds = scratchIds.AsArray(),
            }.Schedule(query, basesReady);
            gather = bases.Dispose(gather);
            state.Dependency = gather;
            seen = state.GlobalSystemVersion;
            var chain = all ? new CarryJob { Fresh = scratch, FreshIds = scratchIds, Cache = cache, CacheIds = cacheIds }.Schedule(gather) : gather;
            running = new RankJob
            {
                Items = cache,
                Order = order,
                Out = back,
                Glows = backGlows,
                Result = backResult,
                View = view,
                Caps = budget.Caps,
                Budgeted = budget.Budgeted,
                Dt = scheduledAt < 0.0 ? SystemAPI.Time.DeltaTime : (float)(now - scheduledAt),
                Clock = budget.Clock,
            }.Schedule(chain);
            scheduled = true;
            scheduledAt = now;
        }

        [BurstCompile]
        struct GatherJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LightEmitter> Type;
            [ReadOnly] public EntityTypeHandle Ids;
            [ReadOnly] public NativeArray<int> Bases;
            public uint Since;
            public bool All;
            public NativeArray<LightEmitter> Items;
            public NativeArray<Entity> ItemIds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!All && !chunk.DidChange(ref Type, Since)) return;
                var src = chunk.GetNativeArray(ref Type);
                int b = Bases[unfilteredChunkIndex];
                if (All)
                {
                    var ids = chunk.GetNativeArray(Ids);
                    for (int i = 0; i < src.Length; i++) { Items[b + i] = src[i]; ItemIds[b + i] = ids[i]; }
                    return;
                }
                for (int i = 0; i < src.Length; i++)
                {
                    var e = src[i];
                    e.Slot = Items[b + i].Slot;
                    Items[b + i] = e;
                }
            }
        }

        [BurstCompile]
        struct CarryJob : IJob
        {
            [ReadOnly] public NativeList<LightEmitter> Fresh;
            [ReadOnly] public NativeList<Entity> FreshIds;
            public NativeList<LightEmitter> Cache;
            public NativeList<Entity> CacheIds;

            public void Execute()
            {
                var at = new NativeHashMap<Entity, int>(CacheIds.Length, Allocator.Temp);
                for (int i = 0; i < CacheIds.Length; i++) at[CacheIds[i]] = i;
                int n = Fresh.Length;
                var kept = new NativeArray<LightBudget.Slot>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < n; i++) kept[i] = at.TryGetValue(FreshIds[i], out int j) ? Cache[j].Slot : Fresh[i].Slot;
                Cache.ResizeUninitialized(n);
                CacheIds.ResizeUninitialized(n);
                for (int i = 0; i < n; i++)
                {
                    var e = Fresh[i];
                    e.Slot = kept[i];
                    Cache[i] = e;
                    CacheIds[i] = FreshIds[i];
                }
            }
        }

        [BurstCompile]
        struct RankJob : IJob
        {
            public NativeList<LightEmitter> Items;
            public NativeList<int> Order;
            public NativeList<LightEmitter> Out;
            public NativeList<LightGlow> Glows;
            public NativeReference<LightResult> Result;
            public DetailView View;
            public LightBudget.Caps Caps;
            public bool Budgeted;
            public float Dt, Clock;

            public void Execute()
            {
                var items = Items.AsArray();
                int n = items.Length;
                for (int i = 0; i < n; i++)
                {
                    var e = items[i];
                    var s = e.Slot;
                    s.Pixels = View.ScreenSize(e.RangeM, e.Position);
                    s.Importance = e.Importance;
                    s.Wanted = (byte)Detail.Tier(s.Pixels, e.Thresholds.Scaled(View.Quality), s.Wanted);
                    if (!Budgeted)
                    {
                        s.Previous = s.Target = s.Tier = Detail.LightShadowed;
                        s.Shadow = s.Real = 1f;
                        s.Glow = 0f;
                    }
                    e.Slot = s;
                    items[i] = e;
                }
                Order.ResizeUninitialized(n);
                var slots = new EmitterSlots { Items = items, Order = Order.AsArray() };
                if (Budgeted) LightBudget.Rank(ref slots, Caps);
                var r = new LightResult { Counts = LightBudget.Step(ref slots, Dt), Clock = Clock };
                Out.Clear();
                Glows.Clear();
                Glows.Capacity = math.max(Glows.Capacity, n * 2);
                for (int i = 0; i < n; i++)
                {
                    var e = items[i];
                    var s = e.Slot;
                    if ((s.Shadow > 0f && s.Shadow < 1f) || (s.Real > 0f && s.Real < 1f) || (s.Glow > 0f && s.Glow < 1f)) r.Fading = true;
                    if (s.Real > 0f)
                    {
                        Out.Add(e);
                        r.Real++;
                        if (s.Shadow > 0f) r.Shadowed++;
                    }
                    float intensity = e.Intensity * e.Gain(Clock);
                    var p = new float4(e.Position, CoreM);
                    Glows.AddNoResize(new LightGlow { PosSize = p, Color = new float4(e.Color, CoreGain * intensity) });
                    r.Cores++;
                    if (s.Glow > 0f)
                    {
                        Glows.AddNoResize(new LightGlow { PosSize = new float4(e.Position, e.RangeM * HaloShare), Color = new float4(HaloTint(e.Color), HaloGain * intensity * s.GlowWeight) });
                        r.Glows++;
                    }
                }
                Result.Value = r;
            }
        }

        static float3 HaloTint(float3 c)
        {
            var weights = new float3(0.2126f, 0.7152f, 0.0722f);
            var deep = c * c;
            float luma = math.dot(c, weights), deepLuma = math.dot(deep, weights);
            return deep * (deepLuma > 1e-5f ? luma / deepLuma : 0f);
        }
    }
}
