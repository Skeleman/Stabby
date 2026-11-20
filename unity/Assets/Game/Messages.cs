using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Stabby.World;

namespace Stabby.Game
{
    public struct GoInGameRpc : IRpcCommand { }

    public struct WorldInfoRpc : IRpcCommand
    {
        public ulong Seed;
        public int Version, Size, Chunks;
    }

    public struct ChunkSumRpc : IRpcCommand
    {
        public int Index;
        public ulong Sum;
    }

    public struct ChunkRequestRpc : IRpcCommand
    {
        public int Index;
    }

    public struct ChunkPieceRpc : IRpcCommand
    {
        public int Index, Piece, Pieces;
        [GhostFixedListCapacity(Capacity = 1024)] public FixedList4096Bytes<byte> Bytes;
    }

    public struct PlayerInput : IInputComponentData
    {
        public float2 Dir;
    }

    public struct Player : IComponentData { }

    public sealed class Ground : IComponentData
    {
        public TerrainDetail Detail;
        public float CellMeters, Top;

        public float HeightAt(float x, float z) => math.max(0, Detail.HeightAt(x / CellMeters, z / CellMeters));

        public static Entity Create(EntityManager em, GeneratedWorld w, TerrainDetail detail)
        {
            var e = em.CreateEntity();
            em.AddComponentObject(e, new Ground { Detail = detail, CellMeters = w.Config.CellMeters, Top = (w.Config.Size - 1) * w.Config.CellMeters });
            return e;
        }
    }

    public struct PlayerPrefab : IComponentData
    {
        public Entity Value;
    }
}
