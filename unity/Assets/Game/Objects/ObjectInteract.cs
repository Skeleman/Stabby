using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
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

        sealed class Verdict
        {
            public Ask Ask;
            public RemoveResult Outcome;
            public ObjectInstance Found;
        }

        readonly ObjectDeltas deltas = new ObjectDeltas();
        readonly List<Ask> asks = new List<Ask>();
        readonly List<ulong> fresh = new List<ulong>();
        Task<List<Verdict>> check;
        int accepted, rejected, killed;

        public ObjectDeltas Deltas => deltas;
        public int Accepted => accepted;
        public int Rejected => rejected;

        public void Submit(ulong id, float3 at) => asks.Add(new Ask { Id = id, At = at, From = Entity.Null });

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
            if (check != null && check.IsCompleted)
            {
                if (check.IsFaulted) GameStatus.Failure = check.Exception.ToString();
                else Apply(ecb, field, check.Result);
                check = null;
            }
            if (check == null && field != null && asks.Count > 0)
            {
                var batch = asks.ToArray();
                asks.Clear();
                var known = deltas;
                check = Task.Run(() =>
                {
                    var list = new List<Verdict>(batch.Length);
                    foreach (var a in batch)
                    {
                        var r = known.Check(field, a.Id, a.At.x, a.At.y, a.At.z, out var found);
                        list.Add(new Verdict { Ask = a, Outcome = r, Found = found });
                    }
                    return list;
                });
            }
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void Apply(EntityCommandBuffer ecb, ObjectField field, List<Verdict> verdicts)
        {
            bool cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc);
            fresh.Clear();
            foreach (var v in verdicts)
            {
                var r = v.Outcome;
                if (r == RemoveResult.Ok && !deltas.Apply(v.Ask.Id)) r = RemoveResult.AlreadyRemoved;
                if (r == RemoveResult.Ok)
                {
                    accepted++;
                    fresh.Add(v.Ask.Id);
                    if (cols) killed += oc.Kill(v.Ask.Id);
                }
                else rejected++;
                if (v.Ask.From != Entity.Null) Rpc.Send(ecb, v.Ask.From, new ObjectReplyRpc { Id = v.Ask.Id, Code = (byte)r });
            }
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

        sealed class Found
        {
            public ulong Id;
            public bool Ok;
            public ObjectInstance Instance;
        }

        readonly ObjectDeltas deltas = new ObjectDeltas();
        readonly List<ulong> pending = new List<ulong>();
        readonly List<ulong> sendQueue = new List<ulong>();
        Task<List<Found>> resolve;
        Task<List<ulong>> pick;
        List<ulong> inReach = new List<ulong>();
        float nextPick;
        int killed, unknown;
        string reply = "", prompt = "";

        public ObjectDeltas Deltas => deltas;
        public int Killed => killed;
        public string LastReply => reply;

        public void Request(ulong id) => sendQueue.Add(id);

        bool Viewing => World.Name == GameSettings.ViewClient && !Application.isBatchMode;

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

            if (resolve != null && resolve.IsCompleted)
            {
                if (resolve.IsFaulted) GameStatus.Failure = resolve.Exception.ToString();
                else Apply(field, resolve.Result);
                resolve = null;
            }
            if (resolve == null && field != null && pending.Count > 0)
            {
                var batch = pending.ToArray();
                pending.Clear();
                resolve = Task.Run(() =>
                {
                    var list = new List<Found>(batch.Length);
                    foreach (ulong id in batch)
                    {
                        bool ok = ObjectDeltas.TryFind(field, id, out var o);
                        list.Add(new Found { Id = id, Ok = ok, Instance = o });
                    }
                    return list;
                });
            }

            if (Viewing && field != null) Pick(field);
            foreach (ulong id in sendQueue) Rpc.Send(ecb, Entity.Null, new ObjectRemoveRpc { Id = id });
            sendQueue.Clear();
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void Pick(ObjectField field)
        {
            if (pick != null && pick.IsCompleted)
            {
                if (pick.IsFaulted) GameStatus.Failure = pick.Exception.ToString();
                else inReach = pick.Result;
                pick = null;
            }
            ulong target = 0;
            bool has = false;
            foreach (ulong id in inReach)
                if (!deltas.Removed(id)) { target = id; has = true; break; }
            string want = has ? $"E: {field.Library.Objects[ObjectIds.Def(target)].Verb} {ObjectDeltaApply.Name(field, target)}" : "nothing to take in reach";
            if (want != prompt) { prompt = want; Status(); }
            if (has && Input.GetKeyDown(KeyCode.E)) Request(target);
            float now = (float)SystemAPI.Time.ElapsedTime;
            if (pick != null || now < nextPick) return;
            nextPick = now + PickSeconds;
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player, GhostOwnerIsLocal>())
            {
                var p = t.ValueRO.Position;
                pick = Task.Run(() =>
                {
                    var ids = new List<ulong>();
                    ObjectDeltas.InReach(field, p.x, p.y, p.z, ids);
                    return ids;
                });
                break;
            }
        }

        void Apply(ObjectField field, List<Found> found)
        {
            bool cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc);
            var view = World.GetExistingSystemManaged<ObjectViewSystem>();
            foreach (var f in found)
            {
                if (!f.Ok) { unknown++; continue; }
                if (!deltas.Apply(f.Id)) continue;
                if (cols) killed += oc.Kill(f.Id);
                if (view != null && view.Enabled) view.Hide(f.Instance);
            }
            Status();
        }

        void Status()
        {
            string tail = Viewing ? $"; {prompt}" + (reply.Length > 0 ? $"; last: {reply}" : "") : reply.Length > 0 ? $"; last: {reply}" : "";
            GameStatus.Lines[World.Name + "~object deltas"] = $"{World.Name}: {deltas.Count} objects removed, {killed} columns cleared, {unknown} unknown{tail}";
        }
    }
}
