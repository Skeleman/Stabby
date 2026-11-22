using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

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
            EntityManager.AddComponentData(e, new PlayerMotion());
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
            var cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc) ? oc : default;
            var prefab = SystemAPI.GetSingleton<PlayerPrefab>().Value;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (id, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithNone<PlayerSpawned>().WithEntityAccess())
            {
                int nid = id.ValueRO.Value;
                var p = ecb.Instantiate(prefab);
                float x = spawn.x + nid * 8f, y = ground.HeightAt(spawn.x + nid * 8f, spawn.y) + Walk.Height, z = spawn.y;
                Walk.Step(ground, ground.EditsNear(x, z, Walk.Radius), cols, ground.WalkStep, ref x, ref y, ref z, 0, 0, Walk.Reach);
                ecb.SetComponent(p, LocalTransform.FromPosition(new float3(x, y, z)));
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
            var cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc) ? oc : default;
            float dt = SystemAPI.Time.DeltaTime, top = ground.Top, s = ground.WalkStep;
            foreach (var (t, input, motion) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerInput>, RefRW<PlayerMotion>>().WithAll<Player, Simulate>())
            {
                var d = input.ValueRO.Dir;
                float l = math.length(d);
                bool moving = l >= 1e-2f;
                d = moving ? d * (math.min(l, 1f) / l * (input.ValueRO.Sprint ? GameSettings.SprintSpeed : GameSettings.WalkSpeed) * dt) : float2.zero;
                var pos = t.ValueRO.Position;
                d.x = math.clamp(pos.x + d.x, 0, top) - pos.x;
                d.y = math.clamp(pos.z + d.y, 0, top) - pos.z;
                var edits = ground.EditsNear(pos.x, pos.z, math.length(d) + Walk.Radius);
                var m = motion.ValueRO;
                if (!m.Airborne && input.ValueRO.Jump.IsSet) { m.Airborne = true; m.VelY = GameSettings.JumpSpeed; }
                float fall = Walk.FallSpeed * dt, air = pos.y;
                if (m.Airborne)
                {
                    air = pos.y + (m.VelY - 0.5f * GameSettings.Gravity * dt) * dt;
                    m.VelY -= GameSettings.Gravity * dt;
                    if (air >= pos.y && Walk.Solid(ground, edits, s, pos.x, air + Walk.Height, pos.z) >= 0) { air = pos.y; m.VelY = math.min(m.VelY, 0); }
                    if (air >= pos.y) { pos.y = air; fall = 0; }
                    else fall = pos.y - air;
                }
                Walk.Step(ground, edits, cols, s, ref pos.x, ref pos.y, ref pos.z, d.x, d.y, fall);
                if (m.Airborne && m.VelY <= 0 && pos.y > air + 1e-4f) { m.Airborne = false; m.VelY = 0; }
                motion.ValueRW = m;
                t.ValueRW.Position = pos;
                if (moving) t.ValueRW.Rotation = quaternion.LookRotationSafe(new float3(d.x, 0, d.y), math.up());
            }
        }
    }

    public static class PlayerControls
    {
        public const float MouseDegPerCount = 0.12f, StickDegPerSecond = 170f, MinPitch = -15f, MaxPitch = 70f;
        public const float Boom = 24f, BoomMin = 1.5f, BoomMargin = 0.6f, BoomEaseOut = 3f, Lift = 3.5f;
        const int BoomSamples = 48;
        public static float Yaw, Pitch = 20f, BoomNow = Boom;
        public static bool SprintOn, Released, Scripted, ScriptSprint, ScriptJump, ScriptSprintPress;
        public static float2 ScriptMove, ScriptLook;

        public static float3 Back
        {
            get
            {
                math.sincos(math.radians(Yaw), out float sy, out float cy);
                math.sincos(math.radians(Pitch), out float sp, out float cp);
                return new float3(-cp * sy, sp, -cp * cy);
            }
        }

        public static float BoomClear(Ground ground, float3 pivot, float3 back)
        {
            float prevT = 0, prevC = pivot.y - ground.HeightAt(pivot.x, pivot.z) - BoomMargin;
            if (prevC <= 0) return BoomMin;
            for (int k = 1; k <= BoomSamples; k++)
            {
                float t = Boom * k / BoomSamples;
                var p = pivot + back * t;
                float c = p.y - ground.HeightAt(p.x, p.z) - BoomMargin;
                if (c <= 0) return math.max(BoomMin, prevT + (t - prevT) * prevC / (prevC - c));
                prevT = t;
                prevC = c;
            }
            return Boom;
        }

        public static float EaseBoom(float want, float dt)
        {
            BoomNow = want < BoomNow ? want : BoomNow + (want - BoomNow) * (1f - math.exp(-BoomEaseOut * dt));
            return BoomNow;
        }

        public static void Read(float dt, out float2 dir, out bool sprint, out bool jump)
        {
            float2 move = float2.zero, look = float2.zero;
            bool held = false, press = false;
            jump = false;
            if (Scripted)
            {
                move = ScriptMove;
                look = ScriptLook * dt;
                held = ScriptSprint;
                press = ScriptSprintPress;
                jump = ScriptJump;
                ScriptJump = ScriptSprintPress = false;
            }
            else
            {
                var kb = Keyboard.current;
                var mouse = Mouse.current;
                var pad = Gamepad.current;
                if (kb != null)
                {
                    move += new float2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
                    held = kb.leftShiftKey.isPressed;
                    jump |= kb.spaceKey.wasPressedThisFrame;
                    if (kb.escapeKey.wasPressedThisFrame) Released = true;
                }
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && Application.isFocused) Released = false;
                bool lockIt = Application.isFocused && !Released;
                if (lockIt != (Cursor.lockState == CursorLockMode.Locked))
                {
                    Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
                    Cursor.visible = !lockIt;
                }
                if (mouse != null && lockIt) look += (float2)mouse.delta.ReadValue() * MouseDegPerCount;
                if (pad != null)
                {
                    move += (float2)pad.leftStick.ReadValue();
                    look += (float2)pad.rightStick.ReadValue() * (StickDegPerSecond * dt);
                    press = pad.leftStickButton.wasPressedThisFrame;
                    jump |= pad.buttonNorth.wasPressedThisFrame;
                }
            }
            Yaw = (Yaw + look.x) % 360f;
            Pitch = math.clamp(Pitch - look.y, MinPitch, MaxPitch);
            if (math.lengthsq(move) > 1) move = math.normalize(move);
            bool moving = math.lengthsq(move) > 1e-2f;
            if (press) SprintOn = !SprintOn;
            if (!moving) SprintOn = false;
            sprint = moving && (SprintOn || held);
            math.sincos(math.radians(Yaw), out float s, out float c);
            dir = new float2(move.x * c + move.y * s, move.y * c - move.x * s);
        }
    }

    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class ClientInputSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float2 dir;
            bool sprint = false, jump = false;
            if (World.Name == GameSettings.ViewClient && (PlayerControls.Scripted || !Application.isBatchMode))
                PlayerControls.Read(UnityEngine.Time.unscaledDeltaTime, out dir, out sprint, out jump);
            else
            {
                float t = (float)SystemAPI.Time.ElapsedTime * 0.4f * (World.Name == GameSettings.ViewClient ? 1 : -1);
                dir = new float2(math.cos(t), math.sin(t));
            }
            foreach (var input in SystemAPI.Query<RefRW<PlayerInput>>().WithAll<GhostOwnerIsLocal>())
            {
                input.ValueRW = default;
                input.ValueRW.Dir = dir;
                input.ValueRW.Sprint = sprint;
                if (jump) input.ValueRW.Jump.Set();
            }
        }
    }
}
