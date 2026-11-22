using Stabby.World;
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
        public float Phase, RunPhase, Side, Reach;
    }

    public struct CreatureSegmentRig
    {
        public float Length;
        public float3 Idle, Forward, Back, Lift, RunForward, RunBack, RunLift, Alert;
    }

    public struct CreatureRig
    {
        public float Length, Rear, Stand, Stride, RunStride, RunRatio;
        public float FadePx, ShadowPx, AnimPx, IkPx, LowPx;
        public BlobArray<CreaturePartRig> Parts;
        public BlobArray<CreatureSegmentRig> Segments;
        public BlobArray<float3> RestPos;
        public BlobArray<quaternion> RestRot;
    }

    public struct CreatureRoot : IComponentData
    {
        public BlobAssetReference<CreatureRig> Rig;
        public Entity Ghost;
        public float3 Position;
        public float Heading, Pitch, Move, Alert, WalkSpeed, Phase, Clock, Sink, Wake;
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
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.Interpolate)] public float Alert;
    }

    public struct CreatureBrain : IComponentData
    {
        public float2 Home, Offset;
        public float2 Push;
        public float Timer, Turn, Debt;
        public int Herd;
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
        public ulong BodySeed, HerdSeed;
        public uint Seed;
        public HerdRoute Route;
    }

    public struct CreaturePlanData
    {
        public int Across;
        public float SiteMeters, Spread, MaxReach, RouteReach;
        public BlobArray<int> SiteStart;
        public BlobArray<CreatureSpawnRow> Spawns;
        public BlobArray<float2> Reach;
        public BlobArray<float> Think, Length;
        public BlobArray<CreatureWary> Wary;
        public int Size;
        public float CellMeters;
        public BlobArray<byte> Wet;
    }

    public struct CreatureGround : IHerdGround, IWetCells
    {
        public BlobAssetReference<CreaturePlanData> Plan;

        public bool WetCell(int cell) => Plan.Value.Wet[cell] != 0;

        public bool Blocked(float x, float z)
        {
            ref var p = ref Plan.Value;
            return HerdMind.OffMap(p.Size, p.CellMeters, x, z, out int c) || p.Wet[c] != 0;
        }

        public bool Crosses(float x0, float z0, float x1, float z1)
        {
            ref var p = ref Plan.Value;
            return HerdMind.CrossesWet(ref this, p.Size, p.CellMeters, x0, z0, x1, z1);
        }
    }

    public struct CreatureHerd
    {
        public HerdMind Mind;
        public HerdRoute Route;
        public float2 Shift, Centre;
        public int Body;
        public bool Rehome;
    }

    public struct CreatureFreeze : IComponentData { }

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
        public int Mesh;
    }

    public struct CreatureFur : IComponentData
    {
        public float4 Base, Accent, Dark;
    }

    public struct CreatureTally : IComponentData
    {
        public int Links, Groups;
        public bool Instanced;
    }

    public struct CreatureGpu
    {
        public float4 R0, R1, R2, Fade;
        public int Fur, Group, Pad0, Pad1;
    }

    public struct CreatureFrustum
    {
        public float4 P0, P1, P2, P3, P4, P5;
        public bool On;

        public bool Outside(float3 p, float r) =>
            On && (math.dot(P0.xyz, p) + P0.w < -r || math.dot(P1.xyz, p) + P1.w < -r || math.dot(P2.xyz, p) + P2.w < -r ||
                   math.dot(P3.xyz, p) + P3.w < -r || math.dot(P4.xyz, p) + P4.w < -r || math.dot(P5.xyz, p) + P5.w < -r);
    }

    public static class CreaturePath
    {
        public static readonly bool Instanced = System.Environment.GetEnvironmentVariable("STABBY_CREATURE_PATH") == "instanced";
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
        public float Move, Phase, Run, Alert;

        sealed class Key { }
        static readonly SharedStatic<CreatureView> Shared = SharedStatic<CreatureView>.GetOrCreate<CreatureView, Key>();

        public static CreatureView Current { get => Shared.Data; set => Shared.Data = value; }
    }
}
