using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
        public bool Budgeted, Hidden;
    }

    struct EmitterSlots : LightBudget.ISlots
    {
        public NativeArray<LightEmitter> Items;

        public int Count => Items.Length;

        public LightBudget.Slot this[int i]
        {
            get => Items[i].Slot;
            set { var e = Items[i]; e.Slot = value; Items[i] = e; }
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct LightBudgetSystem : ISystem
    {
        EntityQuery query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            query = SystemAPI.QueryBuilder().WithAllRW<LightEmitter>().Build();
            state.RequireForUpdate<LightBudgetState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var view = DetailView.Current;
            if (view.PixelsPerMetre <= 0 || query.IsEmpty) return;
            var budget = SystemAPI.GetSingletonRW<LightBudgetState>();
            var items = query.ToComponentDataArray<LightEmitter>(Allocator.Temp);
            for (int i = 0; i < items.Length; i++)
            {
                var e = items[i];
                var s = e.Slot;
                s.Pixels = view.ScreenSize(e.RangeM, e.Position);
                s.Importance = e.Importance;
                s.Wanted = (byte)Detail.Tier(s.Pixels, e.Thresholds.Scaled(view.Quality), s.Wanted);
                if (!budget.ValueRO.Budgeted)
                {
                    s.Previous = s.Target = s.Tier = Detail.LightShadowed;
                    s.Shadow = s.Real = 1f;
                    s.Glow = 0f;
                }
                e.Slot = s;
                items[i] = e;
            }
            var slots = new EmitterSlots { Items = items };
            if (budget.ValueRO.Budgeted) LightBudget.Rank(ref slots, budget.ValueRO.Caps);
            budget.ValueRW.Counts = LightBudget.Step(ref slots, SystemAPI.Time.DeltaTime);
            query.CopyFromComponentDataArray(items);
        }
    }
}
