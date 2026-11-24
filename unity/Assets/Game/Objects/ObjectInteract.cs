using System.Collections.Generic;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Stabby.Game
{
    public struct ObjectRemoveRpc : IRpcCommand
    {
        public ulong Id;
    }

    public struct ObjectReplyRpc : IRpcCommand
    {
        public ulong Id;
        public byte Code;
    }

    public struct ObjectDeltaRpc : IRpcCommand
    {
        [GhostFixedListCapacity(Capacity = ObjectDeltaApply.PerRpc)] public FixedList512Bytes<ulong> Ids;
    }

    public struct ObjectsInformed : IComponentData { }

    public static class ObjectDeltaApply
    {
        public const int PerRpc = 60;

        public static void Send(EntityCommandBuffer ecb, Entity conn, IEnumerable<ulong> ids)
        {
            var rpc = new ObjectDeltaRpc();
            foreach (ulong id in ids)
            {
                rpc.Ids.Add(id);
                if (rpc.Ids.Length < PerRpc) continue;
                Rpc.Send(ecb, conn, rpc);
                rpc.Ids.Clear();
            }
            if (rpc.Ids.Length > 0) Rpc.Send(ecb, conn, rpc);
        }

        public static string Name(ObjectField field, ulong id)
        {
            int d = ObjectIds.Def(id);
            return field != null && d < field.Library.Objects.Count ? field.Library.Objects[d].Id : "object";
        }

        public static string Words(RemoveResult r) => r switch
        {
            RemoveResult.Ok => "done",
            RemoveResult.NoChunk => "no such chunk",
            RemoveResult.NoObject => "no such object",
            RemoveResult.NotInteractive => "not interactive",
            RemoveResult.AlreadyRemoved => "already gone",
            _ => "out of reach",
        };
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ObjectColumnsSystem))]
    public partial class ServerObjectDeltaSystem : SystemBase
    {
        struct Ask
        {
            public ulong Id;
            public float3 At;
            public Entity From;
        }

        readonly ObjectDeltas deltas = new ObjectDeltas();
        readonly List<Ask> asks = new List<Ask>(), batch = new List<Ask>();
        readonly List<ulong> fresh = new List<ulong>();
        readonly List<int> need = new List<int>();
        ReachTables tables;
        NativeArray<RemoveAsk> asking;
        NativeArray<byte> results;
        JobSlot check;
        int accepted, rejected, killed;

        public ObjectDeltas Deltas => deltas;
        public ReachTables Tables => tables;
        public int Accepted => accepted;
        public int Rejected => rejected;

        public void Submit(ulong id, float3 at) => asks.Add(new Ask { Id = id, At = at, From = Entity.Null });

        protected override void OnCreate() => tables = new ReachTables();

        protected override void OnDestroy()
        {
            check.Finish();
            if (asking.IsCreated) asking.Dispose();
            if (results.IsCreated) results.Dispose();
            tables.Dispose();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithNone<ObjectsInformed>().WithEntityAccess())
            {
                ecb.AddComponent<ObjectsInformed>(conn);
                foreach (int chunk in deltas.Chunks) ObjectDeltaApply.Send(ecb, conn, deltas.InChunk(chunk));
            }

            var players = new NativeHashMap<int, float3>(8, Allocator.Temp);
            foreach (var (t, owner) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>())
                players[owner.ValueRO.NetworkId] = t.ValueRO.Position;
            foreach (var (req, rx, e) in SystemAPI.Query<RefRO<ObjectRemoveRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                var from = rx.ValueRO.SourceConnection;
                if (!SystemAPI.HasComponent<NetworkId>(from) || !players.TryGetValue(SystemAPI.GetComponent<NetworkId>(from).Value, out var at))
                {
                    Rpc.Send(ecb, from, new ObjectReplyRpc { Id = req.ValueRO.Id, Code = (byte)RemoveResult.OutOfReach });
                    rejected++;
                    continue;
                }
                asks.Add(new Ask { Id = req.ValueRO.Id, At = at, From = from });
            }
            players.Dispose();

            var field = World.GetExistingSystemManaged<ObjectColumnsSystem>().Field;
            if (check.Done)
            {
                check.Finish();
                Apply(ecb);
            }
            if (!check.Busy && field != null && asks.Count > 0)
            {
                need.Clear();
                foreach (var a in asks) need.Add(ObjectIds.Chunk(a.Id));
                if (tables.Ready(field, need)) Schedule();
            }
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void Schedule()
        {
            batch.Clear();
            batch.AddRange(asks);
            asks.Clear();
            asking = new NativeArray<RemoveAsk>(batch.Count, Allocator.Persistent);
            results = new NativeArray<byte>(batch.Count, Allocator.Persistent);
            for (int k = 0; k < batch.Count; k++) asking[k] = new RemoveAsk { Id = batch[k].Id, At = batch[k].At };
            check.Start(new RemoveCheckJob { Chunks = tables.Chunks, Interactive = tables.Interactive, Removed = tables.Removed, Asks = asking, Results = results, Across = tables.Across }.Schedule());
        }

        void Apply(EntityCommandBuffer ecb)
        {
            bool cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc);
            fresh.Clear();
            for (int k = 0; k < batch.Count; k++)
            {
                var a = batch[k];
                var r = (RemoveResult)results[k];
                if (r == RemoveResult.Ok && !deltas.Apply(a.Id)) r = RemoveResult.AlreadyRemoved;
                if (r == RemoveResult.Ok)
                {
                    tables.Remove(a.Id);
                    accepted++;
                    fresh.Add(a.Id);
                    if (cols) killed += oc.Kill(a.Id);
                }
                else rejected++;
                if (a.From != Entity.Null) Rpc.Send(ecb, a.From, new ObjectReplyRpc { Id = a.Id, Code = (byte)r });
            }
            asking.Dispose();
            results.Dispose();
            batch.Clear();
            if (fresh.Count > 0) ObjectDeltaApply.Send(ecb, Entity.Null, fresh);
            GameStatus.Lines[World.Name + "~object deltas"] = $"{World.Name}: {deltas.Count} objects removed in {CountChunks()} chunks, {accepted} accepted, {rejected} rejected, {killed} columns cleared";
        }

        int CountChunks()
        {
            int n = 0;
            foreach (int _ in deltas.Chunks) n++;
            return n;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateAfter(typeof(ObjectColumnsSystem))]
    public partial class ClientObjectDeltaSystem : SystemBase
    {
        public const float PickSeconds = 0.25f;

        readonly ObjectDeltas deltas = new ObjectDeltas();
        readonly List<ulong> pending = new List<ulong>(), resolving = new List<ulong>(), sendQueue = new List<ulong>(), inReach = new List<ulong>();
        readonly List<int> need = new List<int>();
        ReachTables tables;
        NativeArray<ulong> resolveIds;
        NativeArray<ObjectInstance> resolveFound;
        NativeArray<byte> resolveOk;
        NativeList<ulong> pickIds;
        NativeList<float> pickDist;
        JobSlot resolveSlot, pickSlot;
        float3 pickAt;
        bool pickWanted;
        float nextPick;
        int killed, unknown;
        string reply = "", prompt = "";

        public ObjectDeltas Deltas => deltas;
        public ReachTables Tables => tables;
        public int Killed => killed;
        public string LastReply => reply;

        public void Request(ulong id) => sendQueue.Add(id);

        bool Viewing => World.Name == GameSettings.ViewClient && !Application.isBatchMode;

        protected override void OnCreate()
        {
            tables = new ReachTables();
            pickIds = new NativeList<ulong>(16, Allocator.Persistent);
            pickDist = new NativeList<float>(16, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            resolveSlot.Finish();
            pickSlot.Finish();
            ReleaseResolve();
            pickIds.Dispose();
            pickDist.Dispose();
            tables.Dispose();
        }

        protected override void OnUpdate()
        {
            var field = World.GetExistingSystemManaged<ObjectColumnsSystem>().Field;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, e) in SystemAPI.Query<RefRO<ObjectDeltaRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                foreach (ulong id in rpc.ValueRO.Ids) pending.Add(id);
            }
            foreach (var (rpc, e) in SystemAPI.Query<RefRO<ObjectReplyRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                reply = $"{ObjectDeltaApply.Name(field, rpc.ValueRO.Id)} {ObjectDeltaApply.Words((RemoveResult)rpc.ValueRO.Code)}";
                Status();
            }

            if (resolveSlot.Done)
            {
                resolveSlot.Finish();
                Apply();
            }
            if (pickSlot.Done)
            {
                pickSlot.Finish();
                inReach.Clear();
                for (int k = 0; k < pickIds.Length; k++) inReach.Add(pickIds[k]);
            }
            if (Viewing && field != null) Prompt(field);
            if (field != null && !resolveSlot.Busy && !pickSlot.Busy) Schedule(field);

            foreach (ulong id in sendQueue) Rpc.Send(ecb, Entity.Null, new ObjectRemoveRpc { Id = id });
            sendQueue.Clear();
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void Prompt(ObjectField field)
        {
            ulong target = 0;
            bool has = false;
            foreach (ulong id in inReach)
                if (!deltas.Removed(id)) { target = id; has = true; break; }
            string want = has ? $"E: {field.Library.Objects[ObjectIds.Def(target)].Verb} {ObjectDeltaApply.Name(field, target)}" : "nothing to take in reach";
            if (want != prompt) { prompt = want; Status(); }
            if (has && Input.GetKeyDown(KeyCode.E)) Request(target);
            float now = (float)SystemAPI.Time.ElapsedTime;
            if (pickWanted || pickSlot.Busy || now < nextPick) return;
            nextPick = now + PickSeconds;
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player, GhostOwnerIsLocal>())
            {
                pickAt = t.ValueRO.Position;
                pickWanted = true;
                break;
            }
        }

        void Schedule(ObjectField field)
        {
            need.Clear();
            foreach (ulong id in pending) need.Add(ObjectIds.Chunk(id));
            var near = pickWanted ? InReachJob.Around(field, pickAt) : default;
            if (pickWanted) near.Chunks9(need);
            if (need.Count == 0 || !tables.Ready(field, need)) return;
            if (pending.Count > 0)
            {
                resolving.Clear();
                resolving.AddRange(pending);
                pending.Clear();
                resolveIds = new NativeArray<ulong>(resolving.Count, Allocator.Persistent);
                resolveFound = new NativeArray<ObjectInstance>(resolving.Count, Allocator.Persistent);
                resolveOk = new NativeArray<byte>(resolving.Count, Allocator.Persistent);
                for (int k = 0; k < resolving.Count; k++) resolveIds[k] = resolving[k];
                resolveSlot.Start(new ResolveJob { Chunks = tables.Chunks, Ids = resolveIds, Found = resolveFound, Ok = resolveOk, Across = tables.Across }.Schedule());
            }
            if (pickWanted)
            {
                pickWanted = false;
                near.Chunks = tables.Chunks;
                near.Ids = pickIds;
                near.Dist = pickDist;
                pickSlot.Start(near.Schedule());
            }
        }

        void Apply()
        {
            bool cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc);
            var view = World.GetExistingSystemManaged<ObjectViewSystem>();
            for (int k = 0; k < resolving.Count; k++)
            {
                ulong id = resolving[k];
                if (resolveOk[k] == 0) { unknown++; continue; }
                if (!deltas.Apply(id)) continue;
                tables.Remove(id);
                if (cols) killed += oc.Kill(id);
                if (view != null && view.Enabled) view.Hide(resolveFound[k]);
            }
            ReleaseResolve();
            resolving.Clear();
            Status();
        }

        void ReleaseResolve()
        {
            if (resolveIds.IsCreated) resolveIds.Dispose();
            if (resolveFound.IsCreated) resolveFound.Dispose();
            if (resolveOk.IsCreated) resolveOk.Dispose();
        }

        void Status()
        {
            string tail = Viewing ? $"; {prompt}" + (reply.Length > 0 ? $"; last: {reply}" : "") : reply.Length > 0 ? $"; last: {reply}" : "";
            GameStatus.Lines[World.Name + "~object deltas"] = $"{World.Name}: {deltas.Count} objects removed, {killed} columns cleared, {unknown} unknown{tail}";
        }
    }
}
