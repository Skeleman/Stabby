using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
        public int N, Bands, EditAcross;
        public uint Seed, GullySeed, RockSeed, CoastSeed;
        public float CellMeters, Top, Period;
        public BlobArray<float> Height, Amp, Offset, Band;
        public BlobArray<int> PieceStart, PieceItems;
        public BlobArray<RiverPiece> Pieces;
        public BlobArray<FeatureEdit> Edits;
        public BlobArray<int> EditStart, EditItems;
    }

    public struct Ground : IComponentData, IGroundHeight
    {
        public BlobAssetReference<GroundData> Blob;

        public float Top => Blob.Value.Top;

        public float WalkStep => Features.LatticeStepM;

        public GroundEdits Edits => new GroundEdits { Blob = Blob };

        public GroundEdits EditsNear(float x, float z, float reach)
        {
            ref var g = ref Blob.Value;
            if (reach > EditGrid.PadM) return Edits;
            int c = EditGrid.CellOf(z, g.EditAcross) * g.EditAcross + EditGrid.CellOf(x, g.EditAcross);
            return new GroundEdits { Blob = Blob, Cell = true, Start = g.EditStart[c], N = g.EditStart[c + 1] - g.EditStart[c] };
        }

        public NearCells Cells => new NearCells { Blob = Blob };

        public float HeightAt(float x, float z) => math.max(0, NearField.Sample(Cells, x, z));

        public float Surface(float x, float z) => NearField.Sample(Cells, x, z);

        public void Range(float x0, float z0, float x1, float z1, out float lo, out float hi) => NearField.Range(Cells, x0, z0, x1, z1, out lo, out hi);

        public struct NearCells : INearCells
        {
            public BlobAssetReference<GroundData> Blob;
            public int N => Blob.Value.N;
            public float CellMeters => Blob.Value.CellMeters;
            public uint Seed => Blob.Value.Seed;
            public uint GullySeed => Blob.Value.GullySeed;
            public uint RockSeed => Blob.Value.RockSeed;
            public uint CoastSeed => Blob.Value.CoastSeed;
            public float Period => Blob.Value.Period;
            public int Bands => Blob.Value.Bands;
            public float Height(int i) => Blob.Value.Height[i];
            public float Amp(int i) => Blob.Value.Amp[i];
            public float Offset(int i) => Blob.Value.Offset[i];
            public float Band(int k) => Blob.Value.Band[k];
            public int PieceStart(int cell) => Blob.Value.PieceStart[cell];
            public int PieceAt(int k) => Blob.Value.PieceItems[k];
            public RiverPiece Piece(int p) => Blob.Value.Pieces[p];
        }

        public static GroundParts Fill(GeneratedWorld w)
        {
            var detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail);
            var c = detail.Cells;
            int n = c.N, nn = n * n;
            var b = new BlobBuilder(Allocator.Persistent);
            ref var root = ref b.ConstructRoot<GroundData>();
            root.N = n;
            root.Seed = c.Seed;
            root.GullySeed = c.GullySeed;
            root.RockSeed = c.RockSeed;
            root.CoastSeed = c.CoastSeed;
            root.CellMeters = c.CellMeters;
            root.Period = c.Period;
            root.Bands = c.Bands;
            root.Top = (n - 1) * c.CellMeters;
            var h = b.Allocate(ref root.Height, nn);
            var a = b.Allocate(ref root.Amp, nn);
            var o = b.Allocate(ref root.Offset, nn);
            for (int i = 0; i < nn; i++) { h[i] = c.Height(i); a[i] = c.Amp(i); o[i] = c.Offset(i); }
            var band = b.Allocate(ref root.Band, c.Bands);
            for (int k = 0; k < c.Bands; k++) band[k] = c.Band(k);
            var start = b.Allocate(ref root.PieceStart, nn + 1);
            for (int i = 0; i <= nn; i++) start[i] = c.PieceStart(i);
            var items = b.Allocate(ref root.PieceItems, c.IndexLength);
            for (int k = 0; k < c.IndexLength; k++) items[k] = c.PieceAt(k);
            var pieces = b.Allocate(ref root.Pieces, c.PieceCount);
            for (int p = 0; p < c.PieceCount; p++) pieces[p] = c.Piece(p);
            var e = b.Allocate(ref root.Edits, edits.Length);
            for (int i = 0; i < edits.Length; i++) e[i] = edits[i];
            EditGrid.Build(edits, n * c.CellMeters, out root.EditAcross, out var cellStart, out var cellItems);
            var es = b.Allocate(ref root.EditStart, cellStart.Length);
            for (int i = 0; i < cellStart.Length; i++) es[i] = cellStart[i];
            var ei = b.Allocate(ref root.EditItems, cellItems.Length);
            for (int i = 0; i < cellItems.Length; i++) ei[i] = cellItems[i];
            return new GroundParts { Detail = detail, Edits = edits, Blob = b };
        }

        public static BlobAssetReference<GroundData> Create(EntityManager em, BlobAssetReference<GroundData> blob)
        {
            em.CreateSingleton(new Ground { Blob = blob });
            return blob;
        }
    }

    public struct BlobBake<T> where T : unmanaged
    {
        struct Job : IJob
        {
            public BlobBuilder Builder;
            public NativeReference<BlobAssetReference<T>> Out;

            public void Execute()
            {
                Out.Value = Builder.CreateBlobAssetReference<T>(Allocator.Persistent);
            }
        }

        NativeReference<BlobAssetReference<T>> result;
        BlobBuilder builder;
        JobHandle handle;

        public bool Started => result.IsCreated;
        public bool IsCompleted => handle.IsCompleted;

        public static BlobBake<T> Start(BlobBuilder builder)
        {
            var r = new NativeReference<BlobAssetReference<T>>(Allocator.Persistent);
            return new BlobBake<T> { result = r, builder = builder, handle = new Job { Builder = builder, Out = r }.Schedule() };
        }

        public BlobAssetReference<T> Take()
        {
            handle.Complete();
            builder.Dispose();
            var blob = result.Value;
            result.Dispose();
            return blob;
        }

        public void Release()
        {
            if (!result.IsCreated) return;
            var blob = Take();
            if (blob.IsCreated) blob.Dispose();
        }
    }

    public sealed class GroundParts
    {
        public TerrainDetail Detail;
        public FeatureEdit[] Edits;
        public BlobBuilder Blob;
    }

    public static class Owned
    {
        public static void Release<T>(Task<T> task, Action<T> dispose)
        {
            if (task == null) return;
            task.ContinueWith(t => { if (t.IsCompletedSuccessfully) dispose(t.Result); }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    public struct GroundEdits : IEditList
    {
        public BlobAssetReference<GroundData> Blob;
        public bool Cell;
        public int Start, N;
        public int Count => Cell ? N : Blob.Value.Edits.Length;
        public FeatureEdit At(int k) => Cell ? Blob.Value.Edits[Blob.Value.EditItems[Start + k]] : Blob.Value.Edits[k];
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
