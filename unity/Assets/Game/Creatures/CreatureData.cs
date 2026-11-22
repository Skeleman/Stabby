using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;

namespace Stabby.Game
{
    public struct CreaturePartRig
    {
        public int Parent, Role, First, Count;
        public float3 Socket, Foot;
        public quaternion Rest;
        public float Phase, Side, Reach;
    }

    public struct CreatureSegmentRig
    {
        public float Length;
        public float3 Idle, Forward, Back, Lift;
    }

    public struct CreatureRig
    {
        public float Length, Rear, Stand, Stride;
        public float FadePx, ShadowPx, AnimPx, IkPx, LowPx;
        public BlobArray<CreaturePartRig> Parts;
        public BlobArray<CreatureSegmentRig> Segments;
    }

    public struct CreatureRoot : IComponentData
    {
        public BlobAssetReference<CreatureRig> Rig;
        public Entity Ghost;
        public float3 Position;
        public float Heading, Pitch, Move, WalkSpeed, Phase, Clock, Sink;
        public bool Visible, Shadow, High, HighOn, LowOn;
        public float4 Fade;
    }

    public struct CreatureState : IComponentData
    {
        [GhostField] public int Id;
        [GhostField] public int Body;
        [GhostField] public ulong BodySeed;
        [GhostField] public uint Seed;
        [GhostField(Quantization = 1000)] public float WalkSpeed;
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.Interpolate)] public float2 Position;
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] public float Heading;
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] public float Move;
    }

    public struct CreatureBrain : IComponentData
    {
        public float2 Home;
        public float Timer, Turn;
        public Random Random;
        public bool Walking;
    }

    public struct CreaturePrefab : IComponentData
    {
        public Entity Value;
    }

    public struct CreatureSpawnRow
    {
        public int Id, Body;
        public float2 At;
        public float Heading, WalkSpeed;
        public ulong BodySeed;
        public uint Seed;
    }

    public struct CreaturePlanData
    {
        public int Across;
        public float SiteMeters, Spread, MaxReach;
        public BlobArray<int> SiteStart;
        public BlobArray<CreatureSpawnRow> Spawns;
        public BlobArray<float2> Reach;
    }

    public struct CreaturePlanBlob : IComponentData
    {
        public BlobAssetReference<CreaturePlanData> Value;
    }

    public struct CreaturePresenter : IComponentData
    {
        public int Index;
    }

    public struct CreatureLink : IBufferElementData
    {
        public Entity Value;
        public float3 Scale;
    }

    [MaterialProperty("_FurBase")]
    public struct CreatureFurBase : IComponentData { public float4 Value; }

    [MaterialProperty("_FurAccent")]
    public struct CreatureFurAccent : IComponentData { public float4 Value; }

    [MaterialProperty("_FurDark")]
    public struct CreatureFurDark : IComponentData { public float4 Value; }

    public struct CreatureView
    {
        public int Override, Show;
        public float Move, Phase;

        sealed class Key { }
        static readonly SharedStatic<CreatureView> Shared = SharedStatic<CreatureView>.GetOrCreate<CreatureView, Key>();

        public static CreatureView Current { get => Shared.Data; set => Shared.Data = value; }
    }
}
