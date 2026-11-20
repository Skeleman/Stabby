using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Stabby.Game
{
    // Built at runtime, identically in every world, so the spike needs no baked subscene.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial class PlayerPrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, LocalTransform.Identity);
            EntityManager.AddComponentData(e, new GhostOwner());
            EntityManager.AddComponentData(e, new Player());
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, e, new GhostPrefabCreation.Config
            {
                Name = "Player",
                Importance = 1,
                SupportedGhostModes = GhostModeMask.All,
                DefaultGhostMode = GhostMode.Interpolated,
                OptimizationMode = GhostOptimizationMode.Dynamic,
            });
            EntityManager.CreateSingleton(new PlayerPrefab { Value = e });
            Enabled = false;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ClientGoInGameSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(conn);
                Rpc.Send(ecb, conn, new GoInGameRpc());
            }
            ecb.Playback(EntityManager);
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerPlayerSystem : SystemBase
    {
        readonly Dictionary<int, Entity> players = new Dictionary<int, Entity>();
        readonly Dictionary<int, float2> dirs = new Dictionary<int, float2>();

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rx, e) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRpc>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(rx.ValueRO.SourceConnection);
                ecb.DestroyEntity(e);
            }
            foreach (var (move, rx, e) in SystemAPI.Query<RefRO<MoveRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<NetworkId>(rx.ValueRO.SourceConnection))
                    dirs[SystemAPI.GetComponent<NetworkId>(rx.ValueRO.SourceConnection).Value] = move.ValueRO.Dir;
                ecb.DestroyEntity(e);
            }

            var maps = World.GetExistingSystemManaged<ServerMapSystem>();
            if (maps.Map != null && maps.MoundCell >= 0 && SystemAPI.TryGetSingleton<PlayerPrefab>(out var prefab))
            {
                int n = maps.Map.Config.Size;
                float cm = maps.Map.Config.CellMeters;
                foreach (var id in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>())
                {
                    int nid = id.ValueRO.Value;
                    if (players.ContainsKey(nid)) continue;
                    var p = ecb.Instantiate(prefab.Value);
                    float x = (maps.MoundCell % n) * cm + nid * 8f, z = (maps.MoundCell / n) * cm;
                    ecb.SetComponent(p, LocalTransform.FromPosition(new float3(x, Ground(maps, x, z), z)));
                    ecb.SetComponent(p, new GhostOwner { NetworkId = nid });
                    players[nid] = Entity.Null;
                }
            }
            ecb.Playback(EntityManager);

            if (maps.Map == null) return;
            float dt = SystemAPI.Time.DeltaTime, top = (maps.Map.Config.Size - 1) * maps.Map.Config.CellMeters;
            foreach (var (t, owner) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>())
            {
                if (!dirs.TryGetValue(owner.ValueRO.NetworkId, out var d) || math.lengthsq(d) < 1e-4f) continue;
                d = math.normalizesafe(d);
                var pos = t.ValueRO.Position;
                pos.x = math.clamp(pos.x + d.x * GameSettings.WalkSpeed * dt, 0, top);
                pos.z = math.clamp(pos.z + d.y * GameSettings.WalkSpeed * dt, 0, top);
                pos.y = Ground(maps, pos.x, pos.z);
                t.ValueRW.Position = pos;
                t.ValueRW.Rotation = quaternion.LookRotationSafe(new float3(d.x, 0, d.y), math.up());
            }
        }

        static float Ground(ServerMapSystem maps, float x, float z)
        {
            float cm = maps.Map.Config.CellMeters;
            return math.max(0, maps.Detail.HeightAt(x / cm, z / cm));
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ClientMoveSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<NetworkStreamInGame>();

        protected override void OnUpdate()
        {
            float2 dir;
            if (World.Name == GameSettings.ViewClient && !Application.isBatchMode)
                dir = new float2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            else
            {
                // Batch runs and the second client walk a circle so the capture shows motion.
                float t = (float)SystemAPI.Time.ElapsedTime * 0.4f * (World.Name == GameSettings.ViewClient ? 1 : -1);
                dir = new float2(math.cos(t), math.sin(t));
            }
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Rpc.Send(ecb, Entity.Null, new MoveRpc { Dir = dir });
            ecb.Playback(EntityManager);
        }
    }
}
