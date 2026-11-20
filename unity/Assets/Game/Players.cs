using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial class PlayerPrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, LocalTransform.Identity);
            EntityManager.AddComponentData(e, new GhostOwner());
            EntityManager.AddComponentData(e, default(GhostOwnerIsLocal));
            EntityManager.AddComponentData(e, new AutoCommandTarget { Enabled = true });
            EntityManager.AddComponentData(e, new Player());
            EntityManager.AddComponentData(e, new PlayerInput());
            EntityManager.AddBuffer<InputBufferData<PlayerInput>>(e);
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, e, new GhostPrefabCreation.Config
            {
                Name = "Player",
                Importance = 1,
                SupportedGhostModes = GhostModeMask.All,
                DefaultGhostMode = GhostMode.OwnerPredicted,
                OptimizationMode = GhostOptimizationMode.Dynamic,
            });
            EntityManager.CreateSingleton(new PlayerPrefab { Value = e });
            Enabled = false;
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct ClientGoInGameSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(conn);
                Rpc.Send(ecb, conn, new GoInGameRpc());
            }
            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ServerGoInGameSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rx, e) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRpc>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(rx.ValueRO.SourceConnection);
                ecb.DestroyEntity(e);
            }
            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ServerPlayerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Ground>();
            state.RequireForUpdate<SpawnPoint>();
            state.RequireForUpdate<PlayerPrefab>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ground = SystemAPI.GetSingleton<Ground>();
            var spawn = SystemAPI.GetSingleton<SpawnPoint>().XZ;
            var prefab = SystemAPI.GetSingleton<PlayerPrefab>().Value;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (id, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithNone<PlayerSpawned>().WithEntityAccess())
            {
                int nid = id.ValueRO.Value;
                var p = ecb.Instantiate(prefab);
                float x = spawn.x + nid * 8f, z = spawn.y;
                ecb.SetComponent(p, LocalTransform.FromPosition(new float3(x, ground.HeightAt(x, z), z)));
                ecb.SetComponent(p, new GhostOwner { NetworkId = nid });
                ecb.AppendToBuffer(conn, new LinkedEntityGroup { Value = p });
                ecb.AddComponent<PlayerSpawned>(conn);
            }
            ecb.Playback(state.EntityManager);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial struct PlayerMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<Ground>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ground = SystemAPI.GetSingleton<Ground>();
            float dt = SystemAPI.Time.DeltaTime, top = ground.Top;
            foreach (var (t, input) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerInput>>().WithAll<Player, Simulate>())
            {
                var d = input.ValueRO.Dir;
                if (math.lengthsq(d) < 1e-4f) continue;
                d = math.normalizesafe(d);
                var pos = t.ValueRO.Position;
                pos.x = math.clamp(pos.x + d.x * GameSettings.WalkSpeed * dt, 0, top);
                pos.z = math.clamp(pos.z + d.y * GameSettings.WalkSpeed * dt, 0, top);
                pos.y = ground.HeightAt(pos.x, pos.z);
                t.ValueRW.Position = pos;
                t.ValueRW.Rotation = quaternion.LookRotationSafe(new float3(d.x, 0, d.y), math.up());
            }
        }
    }

    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class ClientInputSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float2 dir;
            if (World.Name == GameSettings.ViewClient && !Application.isBatchMode)
                dir = new float2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            else
            {
                float t = (float)SystemAPI.Time.ElapsedTime * 0.4f * (World.Name == GameSettings.ViewClient ? 1 : -1);
                dir = new float2(math.cos(t), math.sin(t));
            }
            foreach (var input in SystemAPI.Query<RefRW<PlayerInput>>().WithAll<GhostOwnerIsLocal>())
                input.ValueRW.Dir = dir;
        }
    }
}
