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

    public struct GroundData
    {
        public int N;
        public uint Seed;
        public float CellMeters, Top;
        public BlobArray<float> Height, Amp;
    }

    public struct Ground : IComponentData
    {
        public BlobAssetReference<GroundData> Blob;

        public float Top => Blob.Value.Top;

        public float HeightAt(float x, float z)
        {
            ref var g = ref Blob.Value;
            return math.max(0, DetailSampler.HeightAt(new Cells { Blob = Blob }, g.N, g.Seed, x / g.CellMeters, z / g.CellMeters));
        }

        public float GridHeight(float gx, float gy)
        {
            ref var g = ref Blob.Value;
            return DetailSampler.HeightAt(new Cells { Blob = Blob }, g.N, g.Seed, gx, gy);
        }

        struct Cells : IDetailCells
        {
            public BlobAssetReference<GroundData> Blob;
            public float Height(int i) => Blob.Value.Height[i];
            public float Amp(int i) => Blob.Value.Amp[i];
        }

        public static BlobBuilder Fill(GeneratedWorld w)
        {
            var detail = new TerrainDetail(w);
            int n = detail.Size;
            var b = new BlobBuilder(Allocator.Persistent);
            ref var root = ref b.ConstructRoot<GroundData>();
            root.N = n;
            root.Seed = detail.Seed;
            root.CellMeters = w.Config.CellMeters;
            root.Top = (n - 1) * w.Config.CellMeters;
            var h = b.Allocate(ref root.Height, n * n);
            var a = b.Allocate(ref root.Amp, n * n);
            for (int i = 0; i < n * n; i++) { h[i] = detail.Height(i); a[i] = detail.Amplitude(i); }
            return b;
        }

        public static BlobAssetReference<GroundData> Create(EntityManager em, BlobBuilder filled)
        {
            var blob = filled.CreateBlobAssetReference<GroundData>(Allocator.Persistent);
            filled.Dispose();
            em.CreateSingleton(new Ground { Blob = blob });
            return blob;
        }
    }

    public struct SpawnPoint : IComponentData
    {
        public float2 XZ;
    }

    public struct PlayerSpawned : IComponentData { }

    public struct PlayerPrefab : IComponentData
    {
        public Entity Value;
    }
}
