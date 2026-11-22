using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
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
    public partial class CreaturePrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new CreatureState());
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, e, new GhostPrefabCreation.Config
            {
                Name = "Creature",
                Importance = 1,
                SupportedGhostModes = GhostModeMask.Interpolated,
                DefaultGhostMode = GhostMode.Interpolated,
                OptimizationMode = GhostOptimizationMode.Dynamic,
            });
            EntityManager.CreateSingleton(new CreaturePrefab { Value = e });
            Enabled = false;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class CreaturePlanSystem : SystemBase
    {
        sealed class Built
        {
            public List<CreatureSpawn> Spawns;
            public CreatureLibrary Lib;
            public int Across;
            public float SiteMeters;
            public WaterGround Ground;
        }

        Task<Built> plan;
        BlobAssetReference<CreaturePlanData> blob;
        List<BiomeDef> defs;

        public static int Planned { get; private set; }
        public static float SpawnReach { get; private set; }
        public static float DespawnReach { get; private set; }

        protected override void OnCreate()
        {
            defs = ContentFiles.Biomes();
            Planned = 0;
        }

        protected override void OnDestroy()
        {
            Owned.Release(plan, _ => { });
            if (blob.IsCreated) blob.Dispose();
        }

        protected override void OnUpdate()
        {
            if (plan == null)
            {
                var map = World.GetExistingSystemManaged<ServerMapSystem>()?.Map;
                if (map == null) return;
                var biomes = defs;
                plan = Task.Run(() =>
                {
                    var lib = ContentFiles.Creatures(biomes);
                    return new Built { Spawns = CreaturePlan.Build(map, lib), Lib = lib, Across = CreaturePlan.SitesAcross(map.Config.Size), SiteMeters = CreaturePlan.SiteCells * map.Config.CellMeters, Ground = CreaturePlan.Ground(map) };
                });
                return;
            }
            if (!plan.IsCompleted) return;
            if (plan.IsFaulted) { Debug.LogException(plan.Exception); Enabled = false; return; }
            var p = plan.Result;
            plan = null;
            Enabled = false;

            var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<CreaturePlanData>();
            root.Across = p.Across;
            root.SiteMeters = p.SiteMeters;
            root.Spread = CreaturePlan.HerdSpread;
            var reach = b.Allocate(ref root.Reach, p.Lib.Bodies.Count);
            float max = 0;
            for (int i = 0; i < reach.Length; i++)
            {
                var def = p.Lib.Bodies[i];
                reach[i] = new float2(CreaturePlan.SpawnReach(def), CreaturePlan.DespawnReach(def));
                max = math.max(max, reach[i].y);
            }
            root.MaxReach = max;
            var wary = b.Allocate(ref root.Wary, p.Lib.Bodies.Count);
            for (int i = 0; i < wary.Length; i++) wary[i] = CreaturePlan.Wary(p.Lib.Bodies[i]);
            root.Size = p.Ground.Size;
            root.CellMeters = p.Ground.CellMeters;
            var wet = b.Allocate(ref root.Wet, p.Ground.Wet.Length);
            for (int i = 0; i < wet.Length; i++) wet[i] = p.Ground.Wet[i];
            var start = b.Allocate(ref root.SiteStart, p.Across * p.Across + 1);
            var rows = b.Allocate(ref root.Spawns, p.Spawns.Count);
            int site = 0;
            for (int i = 0; i < p.Spawns.Count; i++)
            {
                var s = p.Spawns[i];
                int of = CreaturePlan.SiteOf(s.Id);
                while (site <= of) start[site++] = i;
                rows[i] = new CreatureSpawnRow { Id = s.Id, Body = s.Body, At = new float2(s.X, s.Z), Heading = s.Heading, WalkSpeed = s.WalkSpeed, BodySeed = s.BodySeed, HerdSeed = s.HerdSeed, Seed = s.Seed };
            }
            while (site < start.Length) start[site++] = p.Spawns.Count;
            blob = b.CreateBlobAssetReference<CreaturePlanData>(Allocator.Persistent);
            b.Dispose();
            EntityManager.CreateSingleton(new CreaturePlanBlob { Value = blob });
            Planned = p.Spawns.Count;
            if (reach.Length > 0) { SpawnReach = CreaturePlan.SpawnReach(p.Lib.Bodies[0]); DespawnReach = CreaturePlan.DespawnReach(p.Lib.Bodies[0]); }
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureHostSystem : ISystem
    {
        public NativeHashMap<int, CreatureHerd> Herds;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreaturePlanBlob>();
            state.RequireForUpdate<CreaturePrefab>();
            state.RequireForUpdate<GhostRelevancy>();
            Herds = new NativeHashMap<int, CreatureHerd>(64, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (Herds.IsCreated) Herds.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var planRef = SystemAPI.GetSingleton<CreaturePlanBlob>().Value;
            ref var plan = ref planRef.Value;
            var ground = new CreatureGround { Plan = planRef };
            var prefab = SystemAPI.GetSingleton<CreaturePrefab>().Value;
            float dt = SystemAPI.Time.DeltaTime;
            bool hold = SystemAPI.HasSingleton<CreatureFreeze>();

            var players = new NativeList<float2>(8, Allocator.Temp);
            var owners = new NativeList<int>(8, Allocator.Temp);
            foreach (var (t, owner) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<GhostOwner>>().WithAll<Player>())
            {
                players.Add(t.ValueRO.Position.xz);
                owners.Add(owner.ValueRO.NetworkId);
            }

            var live = new NativeHashSet<int>(256, Allocator.Temp);
            var threats = new NativeHashMap<int, HerdThreat>(64, Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (s, brain, e) in SystemAPI.Query<RefRO<CreatureState>, RefRO<CreatureBrain>>().WithEntityAccess())
            {
                float despawn = plan.Reach[s.ValueRO.Body].y;
                bool near = false;
                for (int p = 0; p < players.Length && !near; p++) near = math.distancesq(players[p], brain.ValueRO.Home) <= despawn * despawn;
                if (!near) { ecb.DestroyEntity(e); continue; }
                live.Add(s.ValueRO.Id);
                int herd = brain.ValueRO.Herd;
                if (Herds.TryGetValue(herd, out var h) && !h.Seen) { h.Seen = true; Herds[herd] = h; }
                float2 away = Nearest(players, s.ValueRO.Position);
                float d = math.length(away);
                if (!threats.TryGetValue(herd, out var t) || d < t.Distance) threats[herd] = new HerdThreat { Distance = d, Away = away, At = s.ValueRO.Position };
            }

            var keys = Herds.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                var h = Herds[keys[i]];
                if (!h.Seen) { Herds.Remove(keys[i]); continue; }
                h.Seen = false;
                h.Rehome = false;
                if (!hold)
                {
                    if (threats.TryGetValue(keys[i], out var t)) h.Rehome = h.Mind.Step(plan.Wary[h.Body], t.Distance, t.Away.x, t.Away.y, t.At.x, t.At.y, ref ground, dt);
                    else h.Rehome = h.Mind.Step(plan.Wary[h.Body], float.MaxValue, 0, 1, dt);
                }
                Herds[keys[i]] = h;
            }

            if (!hold)
                foreach (var (s, brain) in SystemAPI.Query<RefRW<CreatureState>, RefRW<CreatureBrain>>())
                {
                    if (!Herds.TryGetValue(brain.ValueRO.Herd, out var h)) continue;
                    Behave(ref s.ValueRW, ref brain.ValueRW, h, plan.Wary[s.ValueRO.Body], Nearest(players, s.ValueRO.Position), ref ground, dt);
                }

            float pad = plan.MaxReach + plan.Spread;
            for (int p = 0; p < players.Length; p++)
            {
                float2 at = players[p];
                int x0 = math.max(0, (int)math.floor((at.x - pad) / plan.SiteMeters)), x1 = math.min(plan.Across - 1, (int)math.floor((at.x + pad) / plan.SiteMeters));
                int z0 = math.max(0, (int)math.floor((at.y - pad) / plan.SiteMeters)), z1 = math.min(plan.Across - 1, (int)math.floor((at.y + pad) / plan.SiteMeters));
                for (int sz = z0; sz <= z1; sz++)
                    for (int sx = x0; sx <= x1; sx++)
                    {
                        int site = sz * plan.Across + sx;
                        for (int i = plan.SiteStart[site]; i < plan.SiteStart[site + 1]; i++)
                        {
                            ref var row = ref plan.Spawns[i];
                            float spawn = plan.Reach[row.Body].x;
                            if (math.distancesq(at, row.At) > spawn * spawn || !live.Add(row.Id)) continue;
                            var e = ecb.Instantiate(prefab);
                            ecb.SetComponent(e, new CreatureState { Id = row.Id, Body = row.Body, BodySeed = row.BodySeed, Seed = row.Seed, WalkSpeed = row.WalkSpeed, Position = row.At, Heading = row.Heading });
                            int herd = CreaturePlan.HerdOf(row.Id);
                            ecb.AddComponent(e, new CreatureBrain { Home = row.At, Herd = herd, Random = new Unity.Mathematics.Random(row.Seed), Timer = 0.5f + (row.Seed % 997) / 997f * 3 });
                            if (!Herds.ContainsKey(herd)) Herds.Add(herd, new CreatureHerd { Mind = HerdMind.Start(row.HerdSeed), Body = row.Body });
                        }
                    }
            }
            ecb.Playback(state.EntityManager);

            var relevancy = SystemAPI.GetSingletonRW<GhostRelevancy>();
            relevancy.ValueRW.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;
            var set = relevancy.ValueRO.GhostRelevancySet;
            set.Clear();
            var ghosts = new NativeList<int>(256, Allocator.Temp);
            var homes = new NativeList<float3>(256, Allocator.Temp);
            foreach (var (s, brain, ghost) in SystemAPI.Query<RefRO<CreatureState>, RefRO<CreatureBrain>, RefRO<GhostInstance>>())
            {
                if (ghost.ValueRO.ghostId == 0) continue;
                ghosts.Add(ghost.ValueRO.ghostId);
                homes.Add(new float3(brain.ValueRO.Home, plan.Reach[s.ValueRO.Body].y));
            }
            foreach (var id in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>())
            {
                int nid = id.ValueRO.Value, who = -1;
                for (int p = 0; p < owners.Length; p++) if (owners[p] == nid) who = p;
                for (int g = 0; g < ghosts.Length; g++)
                {
                    float3 h = homes[g];
                    if (who >= 0 && math.distancesq(players[who], h.xy) <= h.z * h.z) continue;
                    set.TryAdd(new RelevantGhostForConnection(nid, ghosts[g]), 1);
                }
            }
        }

        struct HerdThreat
        {
            public float Distance;
            public float2 Away, At;
        }

        static float2 Nearest(NativeList<float2> players, float2 at)
        {
            float2 best = new float2(0, float.MaxValue);
            for (int p = 0; p < players.Length; p++)
            {
                float2 v = at - players[p];
                if (math.lengthsq(v) < math.lengthsq(best)) best = v;
            }
            return best;
        }

        static void Behave(ref CreatureState s, ref CreatureBrain b, in CreatureHerd h, in CreatureWary w, float2 away, ref CreatureGround ground, float dt)
        {
            if (h.Rehome) { b.Home = s.Position; b.Walking = false; b.Timer = 1 + (s.Seed % 383) / 383f * 2; }
            switch (h.Mind.Mode)
            {
                case HerdMode.Calm:
                    s.Alert = math.max(0, s.Alert - 0.8f * dt);
                    Wander(ref s, ref b, dt);
                    return;
                case HerdMode.Alert:
                    s.Alert = math.min(1, s.Alert + 3 * dt);
                    s.Move = math.lerp(s.Move, 0, math.min(1, 3 * dt));
                    if (s.Move < 0.3f && math.lengthsq(away) < float.MaxValue)
                        s.Heading = HerdMind.Toward(s.Heading, math.atan2(-away.x, -away.y), 0.6f * dt);
                    break;
                default:
                    s.Alert = math.lerp(s.Alert, 0.3f, math.min(1, 3 * dt));
                    s.Heading = HerdMind.Toward(s.Heading, h.Mind.Heading + HerdMind.MemberOffset(s.Seed), 3 * dt);
                    s.Move = math.lerp(s.Move, w.RunRatio, math.min(1, 2.5f * dt));
                    float2 next = s.Position + new float2(math.sin(s.Heading), math.cos(s.Heading)) * (s.Move * s.WalkSpeed * dt);
                    if (ground.Blocked(next.x, next.y)) s.Move = 0;
                    break;
            }
            s.Position += new float2(math.sin(s.Heading), math.cos(s.Heading)) * (s.Move * s.WalkSpeed * dt);
        }

        static void Wander(ref CreatureState s, ref CreatureBrain b, float dt)
        {
            b.Timer -= dt;
            if (b.Timer <= 0)
            {
                b.Walking = b.Random.NextFloat() < 0.6f;
                b.Timer = b.Random.NextFloat(3, 9);
                b.Turn = b.Random.NextFloat(-0.6f, 0.6f);
            }
            float2 fwd = new float2(math.sin(s.Heading), math.cos(s.Heading));
            float2 toHome = b.Home - s.Position;
            float steer = math.clamp(0.02f * (fwd.y * toHome.x - fwd.x * toHome.y), -0.8f, 0.8f);
            s.Move = math.lerp(s.Move, b.Walking ? 1 : 0, math.min(1, 1.2f * dt));
            s.Heading += (b.Turn * 0.4f + steer) * dt * s.Move;
            s.Position += fwd * (s.Move * s.WalkSpeed * dt);
        }
    }
}
