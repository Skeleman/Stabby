using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

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

    public struct MoveRpc : IRpcCommand
    {
        public float2 Dir;
    }

    public struct Player : IComponentData
    {
        public float2 Dir;
    }

    public struct PlayerPrefab : IComponentData
    {
        public Entity Value;
    }
}
