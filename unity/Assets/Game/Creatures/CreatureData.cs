using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
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
        public float FadePx, ShadowPx, AnimPx, IkPx;
        public BlobArray<CreaturePartRig> Parts;
        public BlobArray<CreatureSegmentRig> Segments;
    }

    public struct CreatureRoot : IComponentData
    {
        public BlobAssetReference<CreatureRig> Rig;
        public float3 Position;
        public float2 Home;
        public float Heading, Pitch, Move, Speed, WalkSpeed, Phase, Clock, Timer, Turn, Sink, Ik;
        public Random Random;
        public bool Walking, Visible, Shadow;
        public float4 Fade;
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
        public int Override;
        public float Move, Phase;

        sealed class Key { }
        static readonly SharedStatic<CreatureView> Shared = SharedStatic<CreatureView>.GetOrCreate<CreatureView, Key>();

        public static CreatureView Current { get => Shared.Data; set => Shared.Data = value; }
    }
}
