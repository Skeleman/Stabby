using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Stabby.Game
{
    public struct PackedChunks
    {
        public ulong Seed;
        public int Size;
        public BlobArray<ulong> Sums;
        public BlobArray<int2> Ranges;
        public BlobArray<byte> Bytes;
    }

    public struct ChunkStore : IComponentData
    {
        public BlobAssetReference<PackedChunks> Blob;
    }

    public struct MapInformed : IComponentData { }

    public struct OutPiece : IBufferElementData
    {
        public int Chunk, Piece;
    }

    public struct ChunkSlot
    {
        public int Start, Pieces, Got, Last;
        public bool Wanted;
    }

    public struct ChunkInbox : IComponentData
    {
        public NativeArray<ulong> HostSums;
        public NativeArray<ChunkSlot> Slots;
        public NativeList<byte> Arena;
        public int SumsIn, Remaining, Streamed, PiecesIn;
        public long BytesIn;

        public int Length(int k) => Slots[k].Pieces == 0 ? 0 : (Slots[k].Pieces - 1) * GameSettings.PieceBytes + Slots[k].Last;

        public void Dispose()
        {
            HostSums.Dispose();
            Slots.Dispose();
            Arena.Dispose();
        }
    }

    public static class ChunkPieces
    {
        public static int Count(int bytes) => (bytes + GameSettings.PieceBytes - 1) / GameSettings.PieceBytes;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ServerChunkStreamSystem : ISystem
    {
        BufferLookup<OutPiece> outboxes;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkStore>();
            outboxes = state.GetBufferLookup<OutPiece>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var store = ref SystemAPI.GetSingleton<ChunkStore>().Blob.Value;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithNone<MapInformed>().WithEntityAccess())
            {
                ecb.AddComponent<MapInformed>(conn);
                ecb.AddBuffer<OutPiece>(conn);
                Rpc.Send(ecb, conn, new WorldInfoRpc { Seed = store.Seed, Version = Stabby.World.WorldConfig.GeneratorVersion, Size = store.Size, Chunks = store.Sums.Length });
                for (int k = 0; k < store.Sums.Length; k++) Rpc.Send(ecb, conn, new ChunkSumRpc { Index = k, Sum = store.Sums[k] });
            }

            outboxes.Update(ref state);
            foreach (var (req, rx, e) in SystemAPI.Query<RefRO<ChunkRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                int k = req.ValueRO.Index;
                if (k < 0 || k >= store.Sums.Length || !outboxes.TryGetBuffer(rx.ValueRO.SourceConnection, out var q)) continue;
                int pieces = ChunkPieces.Count(store.Ranges[k].y);
                for (int p = 0; p < pieces; p++) q.Add(new OutPiece { Chunk = k, Piece = p });
            }

            foreach (var (q, conn) in SystemAPI.Query<DynamicBuffer<OutPiece>>().WithEntityAccess())
            {
                int send = math.min(GameSettings.PiecesPerTick, q.Length);
                if (send == 0) continue;
                for (int s = 0; s < send; s++)
                {
                    var o = q[s];
                    int2 range = store.Ranges[o.Chunk];
                    int from = range.x + o.Piece * GameSettings.PieceBytes, to = math.min(range.x + range.y, from + GameSettings.PieceBytes);
                    var rpc = new ChunkPieceRpc { Index = o.Chunk, Piece = o.Piece, Pieces = ChunkPieces.Count(range.y) };
                    for (int b = from; b < to; b++) rpc.Bytes.Add(store.Bytes[b]);
                    Rpc.Send(ecb, conn, rpc);
                }
                q.RemoveRange(0, send);
            }
            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateAfter(typeof(ClientMapSystem))]
    public partial struct ClientChunkReceiveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<ChunkInbox>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var inbox = SystemAPI.GetSingletonRW<ChunkInbox>();
            ref var box = ref inbox.ValueRW;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (sum, e) in SystemAPI.Query<RefRO<ChunkSumRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                int k = sum.ValueRO.Index;
                if (k < 0 || k >= box.HostSums.Length) continue;
                box.HostSums[k] = sum.ValueRO.Sum;
                box.SumsIn++;
            }
            foreach (var (piece, e) in SystemAPI.Query<RefRO<ChunkPieceRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                Receive(ref box, piece.ValueRO);
            }
            ecb.Playback(state.EntityManager);
        }

        static void Receive(ref ChunkInbox box, in ChunkPieceRpc p)
        {
            int k = p.Index;
            if (k < 0 || k >= box.Slots.Length || p.Piece < 0 || p.Piece >= p.Pieces || p.Bytes.Length > GameSettings.PieceBytes) return;
            var slot = box.Slots[k];
            if (!slot.Wanted || (slot.Pieces != 0 && slot.Pieces != p.Pieces)) return;
            if (slot.Pieces == 0)
            {
                slot.Pieces = p.Pieces;
                slot.Start = box.Arena.Length;
                box.Arena.ResizeUninitialized(slot.Start + p.Pieces * GameSettings.PieceBytes);
            }
            int at = slot.Start + p.Piece * GameSettings.PieceBytes;
            for (int j = 0; j < p.Bytes.Length; j++) box.Arena[at + j] = p.Bytes[j];
            if (p.Piece == p.Pieces - 1) slot.Last = p.Bytes.Length;
            slot.Got++;
            box.PiecesIn++;
            box.BytesIn += p.Bytes.Length;
            if (slot.Got == slot.Pieces)
            {
                slot.Wanted = false;
                box.Remaining--;
                box.Streamed++;
            }
            box.Slots[k] = slot;
        }
    }
}
