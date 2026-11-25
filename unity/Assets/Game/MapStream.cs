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

        public int Length(int k) => Length(Slots[k]);

        public static int Length(ChunkSlot s) => s.Pieces == 0 ? 0 : (s.Pieces - 1) * GameSettings.PieceBytes + s.Last;

        public void Dispose()
        {
            HostSums.Dispose();
            Slots.Dispose();
            Arena.Dispose();
        }
    }

    public struct ChunkCounts : IComponentData
    {
        public int SumsIn, Remaining, Streamed, PiecesIn;
        public long BytesIn;
    }

    public static class ChunkPieces
    {
        public static int Count(int bytes) => (bytes + GameSettings.PieceBytes - 1) / GameSettings.PieceBytes;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ServerChunkStreamSystem : ISystem
    {
        [BurstCompile]
        [WithAll(typeof(NetworkStreamInGame))]
        [WithNone(typeof(MapInformed))]
        partial struct InformJob : IJobEntity
        {
            public BlobAssetReference<PackedChunks> Store;
            public EntityCommandBuffer Ecb;

            void Execute(Entity conn, in NetworkId _)
            {
                ref var store = ref Store.Value;
                Ecb.AddComponent<MapInformed>(conn);
                Ecb.AddBuffer<OutPiece>(conn);
                Rpc.Send(Ecb, conn, new WorldInfoRpc { Seed = store.Seed, Version = Stabby.World.WorldConfig.GeneratorVersion, Size = store.Size, Chunks = store.Sums.Length });
                for (int k = 0; k < store.Sums.Length; k++) Rpc.Send(Ecb, conn, new ChunkSumRpc { Index = k, Sum = store.Sums[k] });
            }
        }

        [BurstCompile]
        partial struct RequestJob : IJobEntity
        {
            public BlobAssetReference<PackedChunks> Store;
            public BufferLookup<OutPiece> Outboxes;
            public EntityCommandBuffer Ecb;

            void Execute(Entity e, in ChunkRequestRpc req, in ReceiveRpcCommandRequest rx)
            {
                ref var store = ref Store.Value;
                Ecb.DestroyEntity(e);
                int k = req.Index;
                if (k < 0 || k >= store.Sums.Length || !Outboxes.TryGetBuffer(rx.SourceConnection, out var q)) return;
                int pieces = ChunkPieces.Count(store.Ranges[k].y);
                for (int p = 0; p < pieces; p++) q.Add(new OutPiece { Chunk = k, Piece = p });
            }
        }

        [BurstCompile]
        partial struct SendJob : IJobEntity
        {
            public BlobAssetReference<PackedChunks> Store;
            [ReadOnly] public BufferLookup<OutgoingRpcDataStreamBuffer> Backlogs;
            public EntityCommandBuffer Ecb;

            void Execute(Entity conn, ref DynamicBuffer<OutPiece> q)
            {
                ref var store = ref Store.Value;
                int queued = Backlogs.TryGetBuffer(conn, out var backlog) ? backlog.Length : 0;
                int send = math.min(math.min(GameSettings.PiecesPerTick, q.Length), math.max(0, (GameSettings.BacklogBytes - queued) / GameSettings.PieceBytes));
                if (send == 0) return;
                for (int s = 0; s < send; s++)
                {
                    var o = q[s];
                    int2 range = store.Ranges[o.Chunk];
                    int from = range.x + o.Piece * GameSettings.PieceBytes, to = math.min(range.x + range.y, from + GameSettings.PieceBytes);
                    var rpc = new ChunkPieceRpc { Index = o.Chunk, Piece = o.Piece, Pieces = ChunkPieces.Count(range.y) };
                    for (int b = from; b < to; b++) rpc.Bytes.Add(store.Bytes[b]);
                    Rpc.Send(Ecb, conn, rpc);
                }
                q.RemoveRange(0, send);
            }
        }

        BufferLookup<OutPiece> outboxes;
        BufferLookup<OutgoingRpcDataStreamBuffer> backlogs;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkStore>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            outboxes = state.GetBufferLookup<OutPiece>();
            backlogs = state.GetBufferLookup<OutgoingRpcDataStreamBuffer>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var store = SystemAPI.GetSingleton<ChunkStore>().Blob;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            outboxes.Update(ref state);
            backlogs.Update(ref state);
            var dep = new InformJob { Store = store, Ecb = ecb }.Schedule(state.Dependency);
            dep = new RequestJob { Store = store, Outboxes = outboxes, Ecb = ecb }.Schedule(dep);
            state.Dependency = new SendJob { Store = store, Backlogs = backlogs, Ecb = ecb }.Schedule(dep);
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateAfter(typeof(ClientMapSystem))]
    public partial struct ClientChunkReceiveSystem : ISystem
    {
        [BurstCompile]
        [WithAll(typeof(ReceiveRpcCommandRequest))]
        partial struct SumJob : IJobEntity
        {
            public ComponentLookup<ChunkCounts> Counts;
            public Entity Box;
            public NativeArray<ulong> HostSums;
            public EntityCommandBuffer Ecb;

            void Execute(Entity e, in ChunkSumRpc sum)
            {
                Ecb.DestroyEntity(e);
                int k = sum.Index;
                if (k < 0 || k >= HostSums.Length) return;
                HostSums[k] = sum.Sum;
                var c = Counts[Box];
                c.SumsIn++;
                Counts[Box] = c;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ReceiveRpcCommandRequest))]
        partial struct PieceJob : IJobEntity
        {
            public ComponentLookup<ChunkCounts> Counts;
            public Entity Box;
            public NativeArray<ChunkSlot> Slots;
            public NativeList<byte> Arena;
            public EntityCommandBuffer Ecb;

            void Execute(Entity e, in ChunkPieceRpc p)
            {
                Ecb.DestroyEntity(e);
                int k = p.Index;
                if (k < 0 || k >= Slots.Length || p.Piece < 0 || p.Piece >= p.Pieces || p.Bytes.Length > GameSettings.PieceBytes) return;
                var slot = Slots[k];
                if (!slot.Wanted || (slot.Pieces != 0 && slot.Pieces != p.Pieces)) return;
                if (slot.Pieces == 0)
                {
                    slot.Pieces = p.Pieces;
                    slot.Start = Arena.Length;
                    Arena.ResizeUninitialized(slot.Start + p.Pieces * GameSettings.PieceBytes);
                }
                int at = slot.Start + p.Piece * GameSettings.PieceBytes;
                for (int j = 0; j < p.Bytes.Length; j++) Arena[at + j] = p.Bytes[j];
                if (p.Piece == p.Pieces - 1) slot.Last = p.Bytes.Length;
                slot.Got++;
                var c = Counts[Box];
                c.PiecesIn++;
                c.BytesIn += p.Bytes.Length;
                if (slot.Got == slot.Pieces)
                {
                    slot.Wanted = false;
                    c.Remaining--;
                    c.Streamed++;
                }
                Counts[Box] = c;
                Slots[k] = slot;
            }
        }

        ComponentLookup<ChunkCounts> counts;
        EntityQuery sums, pieces;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkInbox>();
            state.RequireForUpdate<ChunkCounts>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            counts = state.GetComponentLookup<ChunkCounts>();
            sums = SystemAPI.QueryBuilder().WithAll<ChunkSumRpc, ReceiveRpcCommandRequest>().Build();
            pieces = SystemAPI.QueryBuilder().WithAll<ChunkPieceRpc, ReceiveRpcCommandRequest>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            bool anySums = !sums.IsEmptyIgnoreFilter, anyPieces = !pieces.IsEmptyIgnoreFilter;
            if (!anySums && !anyPieces) return;
            var box = SystemAPI.GetSingleton<ChunkInbox>();
            var e = SystemAPI.GetSingletonEntity<ChunkInbox>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            bool receiving = SystemAPI.GetComponent<ChunkCounts>(e).Remaining > 0;
            if (anyPieces && !receiving) ecb.DestroyEntity(pieces, EntityQueryCaptureMode.AtRecord);
            counts.Update(ref state);
            if (anySums) state.Dependency = new SumJob { Counts = counts, Box = e, HostSums = box.HostSums, Ecb = ecb }.Schedule(sums, state.Dependency);
            if (anyPieces && receiving) state.Dependency = new PieceJob { Counts = counts, Box = e, Slots = box.Slots, Arena = box.Arena, Ecb = ecb }.Schedule(pieces, state.Dependency);
        }
    }
}
