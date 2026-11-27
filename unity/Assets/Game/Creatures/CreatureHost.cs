using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
            public DrinkSpotList Drinks;
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
                var server = World.GetExistingSystemManaged<ServerMapSystem>();
                var map = server?.Map;
                var parts = server?.Parts;
                if (map == null || parts == null) return;
                var biomes = defs;
                var entry = server.Shared;
                plan = Task.Run(async () =>
                {
                    var lib = ContentFiles.Creatures(biomes);
                    var objects = ContentFiles.Objects(biomes);
                    System.Func<ObjectField> make = () => parts.Objects(map, objects);
                    var field = await (entry == null ? Task.Run(make) : WorldStore.Objects(entry, make));
                    var spawns = CreaturePlan.Build(map, parts.Detail.Cells, lib, CreaturePlan.Pools(field, parts.Brooks), out var drinks);
                    return new Built { Spawns = spawns, Drinks = drinks, Lib = lib, Across = CreaturePlan.SitesAcross(map.Config.Size), SiteMeters = CreaturePlan.SiteCells * map.Config.CellMeters, Ground = CreaturePlan.Ground(map) };
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
            float route = 0;
            foreach (var sp in p.Spawns) route = math.max(route, sp.Route.Extent);
            root.RouteReach = route;
            var think = b.Allocate(ref root.Think, p.Lib.Bodies.Count);
            var length = b.Allocate(ref root.Length, p.Lib.Bodies.Count);
            var grade = b.Allocate(ref root.Grade, p.Lib.Bodies.Count);
            for (int i = 0; i < think.Length; i++)
            {
                grade[i] = Footing.Grade(p.Lib.Bodies[i].MaxSlope);
                think[i] = CreaturePlan.ThinkReach(p.Lib.Bodies[i]);
                length[i] = p.Lib.Bodies[i].Length;
            }
            var wary = b.Allocate(ref root.Wary, p.Lib.Bodies.Count);
            for (int i = 0; i < wary.Length; i++) wary[i] = CreaturePlan.Wary(p.Lib.Bodies[i]);
            var drives = b.Allocate(ref root.Drives, p.Lib.Bodies.Count);
            for (int i = 0; i < drives.Length; i++) drives[i] = p.Lib.Bodies[i].Drives;
            root.Size = p.Ground.Size;
            root.CellMeters = p.Ground.CellMeters;
            var wet = b.Allocate(ref root.Wet, p.Ground.Wet.Length);
            for (int i = 0; i < wet.Length; i++) wet[i] = p.Ground.Wet[i];
            root.DrinkReach = CreaturePlan.DrinkReach(p.Ground.CellMeters);
            var drinkStart = b.Allocate(ref root.DrinkStart, p.Drinks.Start.Length);
            for (int i = 0; i < drinkStart.Length; i++) drinkStart[i] = p.Drinks.Start[i];
            var drinkSpots = b.Allocate(ref root.Drinks, p.Drinks.Count);
            for (int i = 0; i < drinkSpots.Length; i++) drinkSpots[i] = p.Drinks.Spots[i];
            var start = b.Allocate(ref root.SiteStart, p.Across * p.Across + 1);
            var rows = b.Allocate(ref root.Spawns, p.Spawns.Count);
            int site = 0;
            for (int i = 0; i < p.Spawns.Count; i++)
            {
                var s = p.Spawns[i];
                int of = CreaturePlan.SiteOf(s.Id);
                while (site <= of) start[site++] = i;
                rows[i] = new CreatureSpawnRow { Id = s.Id, Body = s.Body, At = new float2(s.X, s.Z), Heading = s.Heading, WalkSpeed = s.WalkSpeed, BodySeed = s.BodySeed, HerdSeed = s.HerdSeed, Seed = s.Seed, Route = s.Route, Needs = s.Needs, Age = s.Age, Lead = s.Lead };
            }
            while (site < start.Length) start[site++] = p.Spawns.Count;
            blob = b.CreateBlobAssetReference<CreaturePlanData>(Allocator.Persistent);
            b.Dispose();
            EntityManager.CreateSingleton(new CreaturePlanBlob { Value = blob });
            Planned = p.Spawns.Count;
            SpawnReach = 0;
            DespawnReach = 0;
            for (int i = 0; i < reach.Length; i++) { SpawnReach = math.max(SpawnReach, reach[i].x); DespawnReach = math.max(DespawnReach, reach[i].y); }
        }
    }

    public struct HerdThreat
    {
        public float Distance;
        public float2 Away, At;
    }

    public struct CreatureNeighbour
    {
        public float2 At;
        public float Radius;
        public int Id;
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureHostSystem : ISystem
    {
        public const float GridMeters = 16f, SearchPeriod = 0.25f, MaxThinkStep = 0.3f, MaxPush = 1f, ShiftSeconds = 300f, Follow = 2 * CreaturePlan.HerdSpread;
        public const float YoungFollow = 3.5f, YoungHurry = 1.6f, YoungAhead = 1.5f, YoungRun = 1.15f;
        public const int LodFrames = 8, SpawnBudget = 64;

        public NativeHashMap<int, CreatureHerd> Herds;
        NativeParallelMultiHashMap<int, HerdThreat> threats;
        NativeParallelMultiHashMap<int, CreatureNeighbour> grid;
        NativeParallelHashSet<int> live;
        uint frame;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreaturePlanBlob>();
            state.RequireForUpdate<CreaturePrefab>();
            state.RequireForUpdate<Ground>();
            state.RequireForUpdate<GhostRelevancy>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            Herds = new NativeHashMap<int, CreatureHerd>(64, Allocator.Persistent);
            threats = new NativeParallelMultiHashMap<int, HerdThreat>(256, Allocator.Persistent);
            grid = new NativeParallelMultiHashMap<int, CreatureNeighbour>(256, Allocator.Persistent);
            live = new NativeParallelHashSet<int>(256, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (Herds.IsCreated) Herds.Dispose(state.Dependency);
            if (threats.IsCreated) threats.Dispose(state.Dependency);
            if (grid.IsCreated) grid.Dispose(state.Dependency);
            if (live.IsCreated) live.Dispose(state.Dependency);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var planRef = SystemAPI.GetSingleton<CreaturePlanBlob>().Value;
            var prefab = SystemAPI.GetSingleton<CreaturePrefab>().Value;
            var cells = SystemAPI.GetSingleton<Ground>().Cells;
            var ecbs = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            float dt = SystemAPI.Time.DeltaTime;
            double now = SystemAPI.Time.ElapsedTime;
            bool hold = SystemAPI.HasSingleton<CreatureFreeze>();
            frame++;

            var players = new NativeList<float2>(8, Allocator.TempJob);
            var owners = new NativeList<int>(8, Allocator.TempJob);
            var viewers = new NativeList<int2>(8, Allocator.TempJob);
            var gathered = new GatherPlayers { Players = players, Owners = owners }.Schedule(state.Dependency);
            gathered = new GatherViewers { Owners = owners, Viewers = viewers }.Schedule(gathered);
            var near = players.AsDeferredJobArray();

            int count = SystemAPI.QueryBuilder().WithAll<CreatureState, CreatureBrain>().Build().CalculateEntityCountWithoutFiltering();
            var dep = new Reset { Threats = threats, Grid = grid, Live = live, Count = count + SpawnBudget }.Schedule(gathered);
            dep = new Sense
            {
                Players = near, Plan = planRef, Ecb = ecbs.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                Live = live.AsParallelWriter(), Threats = threats.AsParallelWriter(), Grid = grid.AsParallelWriter(),
            }.ScheduleParallel(dep);
            dep = new StepHerds { Herds = Herds, Threats = threats, Plan = planRef, Cells = cells, Hold = hold, Dt = dt, Now = now }.Schedule(dep);
            if (!hold)
                dep = new Think { Players = near, Herds = Herds, Grid = grid, Plan = planRef, Cells = cells, Step = dt, Now = now, Frame = frame }.ScheduleParallel(dep);
            dep = new Spawn
            {
                Players = near, Plan = planRef, Live = live, Herds = Herds, Prefab = prefab, Now = now,
                Ecb = ecbs.CreateCommandBuffer(state.WorldUnmanaged),
            }.Schedule(dep);

            var relevancy = SystemAPI.GetSingletonRW<GhostRelevancy>();
            relevancy.ValueRW.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;
            var set = relevancy.ValueRO.GhostRelevancySet;
            dep = new ClearRelevancy { Set = set }.Schedule(dep);
            dep = new Relevancy { Set = set, Players = near, Viewers = viewers.AsDeferredJobArray(), Plan = planRef }.Schedule(dep);
            players.Dispose(dep);
            viewers.Dispose(dep);
            owners.Dispose(dep);
            state.Dependency = dep;
        }

        public static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            return h;
        }

        public static bool SearchDue(double now, float step, float period, uint mix)
        {
            double phase = (mix & 0xFFFF) / 65536.0 * period;
            return math.floor((now + phase) / period) != math.floor((now - step + phase) / period);
        }

        public static int Cell(float2 at) => (int)math.floor(at.x / GridMeters) * 73856093 ^ (int)math.floor(at.y / GridMeters) * 19349663;

        public static float2 Nearest(NativeArray<float2> players, float2 at)
        {
            float2 best = new float2(0, float.MaxValue);
            for (int p = 0; p < players.Length; p++)
            {
                float2 v = at - players[p];
                if (math.lengthsq(v) < math.lengthsq(best)) best = v;
            }
            return best;
        }

        public static void Behave(ref CreatureState s, ref CreatureBrain b, in CreatureHerd h, in CreatureWary w, in CreatureDrives drives, float2 away, ref CreatureGround ground, float dt, bool led = false, float2 lead = default)
        {
            if (h.Rehome) { b.Home = s.Position; b.Walking = false; b.Timer = 1 + (s.Seed % 383) / 383f * 2; b.Placed = false; }
            if (h.Mind.Mode != HerdMode.Calm) s.Act = Drives.None;
            switch (h.Mind.Mode)
            {
                case HerdMode.Calm:
                    s.Alert = math.max(0, s.Alert - 0.8f * dt);
                    if (!Desire(ref s, ref b, h.Needs, drives, ref ground, dt)) Wander(ref s, ref b, ref ground, dt, led ? YoungFollow : Follow, led ? YoungHurry : 1);
                    return;
                case HerdMode.Alert:
                    s.Alert = math.min(1, s.Alert + 3 * dt);
                    s.Move = math.lerp(s.Move, 0, math.min(1, 3 * dt));
                    if (s.Move < 0.3f && math.lengthsq(away) < float.MaxValue)
                        s.Heading = HerdMind.Toward(s.Heading, math.atan2(-away.x, -away.y), 0.6f * dt);
                    break;
                default:
                    s.Alert = math.lerp(s.Alert, 0.3f, math.min(1, 3 * dt));
                    float dart = 0;
                    if (w.Dart > 0)
                    {
                        b.Timer -= dt;
                        if (b.Timer <= 0)
                        {
                            b.Turn = b.Turn > 0 ? -w.Dart : w.Dart;
                            b.Timer = b.Random.NextFloat(0.4f, 0.9f);
                        }
                        dart = b.Turn;
                    }
                    float aim = h.Mind.Heading + HerdMind.MemberOffset(s.Seed) + dart, run = w.RunRatio;
                    if (led)
                    {
                        float2 ahead = lead + new float2(math.sin(h.Mind.Heading), math.cos(h.Mind.Heading)) * YoungAhead - s.Position;
                        float gap = math.length(ahead);
                        if (gap > 0.3f) aim = math.atan2(ahead.x, ahead.y);
                        run *= gap > YoungFollow ? YoungRun : gap < 1 ? 0.9f : 1;
                    }
                    s.Heading = HerdMind.Toward(s.Heading, aim, 3 * dt);
                    s.Move = math.lerp(s.Move, run, math.min(1, 2.5f * dt));
                    float2 next = s.Position + new float2(math.sin(s.Heading), math.cos(s.Heading)) * (s.Move * s.WalkSpeed * dt);
                    float2 feel = s.Position + new float2(math.sin(s.Heading), math.cos(s.Heading)) * Footing.FeelM;
                    if (ground.Blocked(next.x, next.y) || ground.Crosses(s.Position.x, s.Position.y, feel.x, feel.y)) s.Move = 0;
                    break;
            }
            s.Position += new float2(math.sin(s.Heading), math.cos(s.Heading)) * (s.Move * s.WalkSpeed * dt);
        }

        public static bool Desire(ref CreatureState s, ref CreatureBrain b, in HerdNeeds needs, in CreatureDrives drives, ref CreatureGround ground, float dt)
        {
            byte want = Drives.Choose(b.Levels, drives, needs, b.Want);
            if (want != b.Want) { b.Want = want; b.Placed = false; s.Act = Drives.None; }
            if (want == Drives.None) { s.Act = Drives.None; return false; }
            if (!b.Placed) { b.Spot = Spot(s.Seed, s.Position, b.Offset, want, needs, ref ground, out b.Water); b.Placed = true; b.Seek = Drives.GiveUp + 2 * math.distance(b.Spot, s.Position) / math.max(0.1f, s.WalkSpeed); }
            float2 to = b.Spot - s.Position;
            float d = math.length(to);
            if (s.Act == want || d <= Drives.Arrive || d <= Drives.Reach && math.lengthsq(b.Push) > 0)
            {
                s.Act = want;
                s.Move = math.lerp(s.Move, 0, math.min(1, 3 * dt));
                if (want == Drives.Drink) s.Heading = HerdMind.Toward(s.Heading, math.atan2(b.Water.x, b.Water.y), 1.5f * dt);
                return true;
            }
            b.Seek -= dt;
            if (b.Seek <= 0)
            {
                b.Levels[want - 1] = 0;
                b.Want = Drives.None;
                b.Placed = false;
                return false;
            }
            s.Heading = HerdMind.Toward(s.Heading, math.atan2(to.x, to.y), 3 * dt);
            s.Move = math.lerp(s.Move, math.min(1, d / 1.5f), math.min(1, 1.5f * dt));
            float2 fwd = new float2(math.sin(s.Heading), math.cos(s.Heading));
            float feel = math.min(Footing.FeelM, d);
            if (s.Move > 0.05f && ground.Crosses(s.Position.x, s.Position.y, s.Position.x + fwd.x * feel, s.Position.y + fwd.y * feel))
            {
                if (d <= Drives.Reach || want == Drives.Drink && NearWater(s.Position, b.Water, ref ground)) { s.Act = want; return true; }
                s.Move *= 0.5f;
                s.Heading += (s.Seed & 1) == 0 ? 1.2f : -1.2f;
                return true;
            }
            s.Position += fwd * (s.Move * s.WalkSpeed * dt);
            return true;
        }

        static bool NearWater(float2 at, float2 water, ref CreatureGround ground)
        {
            for (float k = 1; k <= Drives.Shore; k += 1)
                if (ground.Blocked(at.x + water.x * k, at.y + water.y * k)) return true;
            return false;
        }

        static float2 Spot(uint seed, float2 at, float2 offset, byte want, in HerdNeeds needs, ref CreatureGround ground, out float2 water)
        {
            needs.Target(want, out float tx, out float tz);
            float2 target = new float2(tx, tz);
            water = new float2(needs.WaterX, needs.WaterZ);
            if (want == Drives.Drink)
            {
                ref var p = ref ground.Plan.Value;
                int k = DrinkSpotList.Nearest(ref ground, ref ground, p.Size, p.CellMeters, at.x, at.y, ground.Grade, p.DrinkReach);
                if (k >= 0)
                {
                    var d = ground.Spot(k);
                    target = new float2(d.X, d.Z);
                    water = new float2(d.WaterX, d.WaterZ);
                }
            }
            float2 side = want == Drives.Drink ? new float2(-water.y, water.x) * (((seed >> 8) & 0xFFFF) / 65535f * 2 - 1) * 1.5f * CreaturePlan.HerdSpread : offset;
            float2 spot = target + side;
            return ground.Blocked(spot.x, spot.y) || ground.Crosses(target.x, target.y, spot.x, spot.y) ? target : spot;
        }

        static void Wander(ref CreatureState s, ref CreatureBrain b, ref CreatureGround ground, float dt, float follow, float hurry)
        {
            b.Timer -= dt;
            if (b.Timer <= 0)
            {
                b.Walking = b.Random.NextFloat() < 0.6f;
                b.Timer = b.Random.NextFloat(3, 9);
                b.Turn = b.Random.NextFloat(-0.6f, 0.6f);
            }
            float2 toHome = b.Home - s.Position;
            bool lag = math.lengthsq(toHome) > follow * follow;
            if (lag)
            {
                b.Walking = true;
                s.Heading = HerdMind.Toward(s.Heading, math.atan2(toHome.x, toHome.y), 1.2f * dt);
            }
            float2 fwd = new float2(math.sin(s.Heading), math.cos(s.Heading));
            float steer = math.clamp(0.02f * (fwd.y * toHome.x - fwd.x * toHome.y), -0.8f, 0.8f);
            s.Move = math.lerp(s.Move, b.Walking ? (lag ? hurry : 1) : 0, math.min(1, 1.2f * dt));
            if (s.Move > 0.05f && ground.Crosses(s.Position.x, s.Position.y, s.Position.x + fwd.x * Footing.FeelM, s.Position.y + fwd.y * Footing.FeelM))
            {
                s.Move *= 0.5f;
                s.Heading += b.Turn >= 0 ? 1.2f : -1.2f;
                return;
            }
            s.Heading += (b.Turn * 0.4f + steer) * dt * s.Move;
            s.Position += fwd * (s.Move * s.WalkSpeed * dt);
        }
    }

    [BurstCompile]
    [WithAll(typeof(Player))]
    partial struct GatherPlayers : IJobEntity
    {
        public NativeList<float2> Players;
        public NativeList<int> Owners;

        void Execute(in LocalTransform t, in GhostOwner owner)
        {
            Players.Add(t.Position.xz);
            Owners.Add(owner.NetworkId);
        }
    }

    [BurstCompile]
    [WithAll(typeof(NetworkStreamInGame))]
    partial struct GatherViewers : IJobEntity
    {
        [ReadOnly] public NativeList<int> Owners;
        public NativeList<int2> Viewers;

        void Execute(in NetworkId id)
        {
            int nid = id.Value, who = -1;
            for (int p = 0; p < Owners.Length; p++) if (Owners[p] == nid) who = p;
            Viewers.Add(new int2(nid, who));
        }
    }

    [BurstCompile]
    struct Reset : IJob
    {
        public NativeParallelMultiHashMap<int, HerdThreat> Threats;
        public NativeParallelMultiHashMap<int, CreatureNeighbour> Grid;
        public NativeParallelHashSet<int> Live;
        public int Count;

        public void Execute()
        {
            Threats.Clear();
            Grid.Clear();
            Live.Clear();
            if (Threats.Capacity < Count) Threats.Capacity = Count;
            if (Grid.Capacity < Count) Grid.Capacity = Count;
            if (Live.Capacity < Count) Live.Capacity = Count;
        }
    }

    [BurstCompile]
    partial struct Sense : IJobEntity
    {
        [ReadOnly] public NativeArray<float2> Players;
        public BlobAssetReference<CreaturePlanData> Plan;
        public EntityCommandBuffer.ParallelWriter Ecb;
        public NativeParallelHashSet<int>.ParallelWriter Live;
        public NativeParallelMultiHashMap<int, HerdThreat>.ParallelWriter Threats;
        public NativeParallelMultiHashMap<int, CreatureNeighbour>.ParallelWriter Grid;

        void Execute(Entity e, [ChunkIndexInQuery] int chunk, in CreatureState s, in CreatureBrain brain)
        {
            ref var plan = ref Plan.Value;
            float despawn = plan.Reach[s.Body].y;
            bool near = false;
            for (int p = 0; p < Players.Length && !near; p++) near = math.distancesq(Players[p], brain.Home) <= despawn * despawn;
            if (!near) { Ecb.DestroyEntity(chunk, e); return; }
            Live.Add(s.Id);
            float2 away = CreatureHostSystem.Nearest(Players, s.Position);
            Threats.Add(brain.Herd, new HerdThreat { Distance = math.length(away), Away = away, At = s.Position });
            Grid.Add(CreatureHostSystem.Cell(s.Position), new CreatureNeighbour { At = s.Position, Radius = 0.5f * plan.Length[s.Body], Id = s.Id });
        }
    }

    [BurstCompile]
    struct StepHerds : IJob
    {
        public NativeHashMap<int, CreatureHerd> Herds;
        [ReadOnly] public NativeParallelMultiHashMap<int, HerdThreat> Threats;
        public BlobAssetReference<CreaturePlanData> Plan;
        public Ground.NearCells Cells;
        public bool Hold;
        public float Dt;
        public double Now;

        public void Execute()
        {
            ref var plan = ref Plan.Value;
            var ground = new CreatureGround { Plan = Plan, Cells = Cells };
            var keys = Herds.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                if (!Threats.TryGetFirstValue(keys[i], out var t, out var it)) { Herds.Remove(keys[i]); continue; }
                var best = t;
                float2 sum = t.At;
                int members = 1;
                while (Threats.TryGetNextValue(out t, ref it))
                {
                    if (t.Distance < best.Distance || t.Distance == best.Distance && (t.At.x < best.At.x || t.At.x == best.At.x && t.At.y < best.At.y)) best = t;
                    sum += t.At;
                    members++;
                }
                var h = Herds[keys[i]];
                h.Rehome = false;
                ground.Grade = plan.Grade[h.Body];
                if (!Hold) h.Rehome = h.Mind.Step(plan.Wary[h.Body], best.Distance, best.Away.x, best.Away.y, best.At.x, best.At.y, ref ground, Dt);
                h.Route.At(Now, out float rx, out float rz, out _);
                float2 route = new float2(rx, rz);
                if (h.Rehome) h.Shift = sum / members - route;
                else if (!Hold && h.Mind.Mode == HerdMode.Calm) h.Shift *= math.exp(-Dt / CreatureHostSystem.ShiftSeconds);
                h.Centre = route + h.Shift;
                Herds[keys[i]] = h;
            }
            keys.Dispose();
        }
    }

    [BurstCompile]
    partial struct Think : IJobEntity
    {
        [ReadOnly] public NativeArray<float2> Players;
        [ReadOnly] public NativeHashMap<int, CreatureHerd> Herds;
        [ReadOnly] public NativeParallelMultiHashMap<int, CreatureNeighbour> Grid;
        public BlobAssetReference<CreaturePlanData> Plan;
        public Ground.NearCells Cells;
        public float Step;
        public double Now;
        public uint Frame;

        void Execute(ref CreatureState s, ref CreatureBrain b)
        {
            if (!Herds.TryGetValue(b.Herd, out var h)) return;
            ref var plan = ref Plan.Value;
            var ground = new CreatureGround { Plan = Plan, Cells = Cells, Grade = plan.Grade[s.Body] };
            float2 away = CreatureHostSystem.Nearest(Players, s.Position);
            uint mix = CreatureHostSystem.Mix((uint)s.Id);
            float think = plan.Think[s.Body];
            bool far = h.Mind.Mode == HerdMode.Calm && !h.Rehome && math.lengthsq(away) > think * think;
            if (far && (Frame + mix) % CreatureHostSystem.LodFrames != 0)
            {
                b.Debt += Step;
                s.Position += new float2(math.sin(s.Heading), math.cos(s.Heading)) * (s.Move * s.WalkSpeed * Step);
                return;
            }
            float dt = Step;
            if (b.Debt > 0)
            {
                s.Position -= new float2(math.sin(s.Heading), math.cos(s.Heading)) * (s.Move * s.WalkSpeed * b.Debt);
                dt = math.min(Step + b.Debt, CreatureHostSystem.MaxThinkStep);
                b.Debt = 0;
            }
            float2 lead = s.Position;
            bool led = b.Lead >= 0 && Find(b.Lead, s.Position, out lead);
            if (h.Mind.Mode == HerdMode.Calm && !h.Rehome) b.Home = led ? lead : h.Centre + b.Offset;
            var drives = plan.Drives[s.Body];
            Drives.Advance(ref b.Levels, drives, s.Act, dt);
            if (far) b.Push = 0;
            else if (CreatureHostSystem.SearchDue(Now, Step, CreatureHostSystem.SearchPeriod, mix)) b.Push = Separate(s.Id, s.Position, 0.5f * plan.Length[s.Body]);
            CreatureHostSystem.Behave(ref s, ref b, h, plan.Wary[s.Body], drives, away, ref ground, dt, led, lead);
            if (math.lengthsq(b.Push) > 0)
            {
                float2 next = s.Position + b.Push * dt;
                if (!ground.Blocked(next.x, next.y)) s.Position = next;
            }
        }

        bool Find(int id, float2 at, out float2 found)
        {
            found = at;
            int cx = (int)math.floor(at.x / CreatureHostSystem.GridMeters), cz = (int)math.floor(at.y / CreatureHostSystem.GridMeters);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = (cx + dx) * 73856093 ^ (cz + dz) * 19349663;
                    if (!Grid.TryGetFirstValue(key, out var n, out var it)) continue;
                    do
                    {
                        if (n.Id != id) continue;
                        found = n.At;
                        return true;
                    } while (Grid.TryGetNextValue(out n, ref it));
                }
            return false;
        }

        float2 Separate(int id, float2 at, float radius)
        {
            float2 push = 0;
            int cx = (int)math.floor(at.x / CreatureHostSystem.GridMeters), cz = (int)math.floor(at.y / CreatureHostSystem.GridMeters);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = (cx + dx) * 73856093 ^ (cz + dz) * 19349663;
                    if (!Grid.TryGetFirstValue(key, out var n, out var it)) continue;
                    do
                    {
                        if (n.Id == id) continue;
                        float2 v = at - n.At;
                        float d = math.length(v), reach = radius + n.Radius;
                        if (d >= reach || d < 1e-4f) continue;
                        push += v / d * (reach - d);
                    } while (Grid.TryGetNextValue(out n, ref it));
                }
            float len = math.length(push);
            return len > CreatureHostSystem.MaxPush ? push * (CreatureHostSystem.MaxPush / len) : push;
        }
    }

    [BurstCompile]
    struct Spawn : IJob
    {
        [ReadOnly] public NativeArray<float2> Players;
        public BlobAssetReference<CreaturePlanData> Plan;
        public NativeParallelHashSet<int> Live;
        public NativeHashMap<int, CreatureHerd> Herds;
        public Entity Prefab;
        public EntityCommandBuffer Ecb;
        public double Now;

        public void Execute()
        {
            ref var plan = ref Plan.Value;
            var wanted = new NativeList<float2>(64, Allocator.Temp);
            float pad = plan.MaxReach + plan.Spread + plan.RouteReach;
            for (int p = 0; p < Players.Length; p++)
            {
                float2 at = Players[p];
                int x0 = math.max(0, (int)math.floor((at.x - pad) / plan.SiteMeters)), x1 = math.min(plan.Across - 1, (int)math.floor((at.x + pad) / plan.SiteMeters));
                int z0 = math.max(0, (int)math.floor((at.y - pad) / plan.SiteMeters)), z1 = math.min(plan.Across - 1, (int)math.floor((at.y + pad) / plan.SiteMeters));
                for (int sz = z0; sz <= z1; sz++)
                    for (int sx = x0; sx <= x1; sx++)
                    {
                        int site = sz * plan.Across + sx;
                        for (int i = plan.SiteStart[site]; i < plan.SiteStart[site + 1]; i++)
                        {
                            ref var row = ref plan.Spawns[i];
                            float spawn = plan.Reach[row.Body].x, d = math.distancesq(at, Wake(ref row, out _));
                            if (d > spawn * spawn || Live.Contains(row.Id)) continue;
                            wanted.Add(new float2(d, i));
                        }
                    }
            }
            if (wanted.Length == 0) return;
            wanted.Sort(new Closer());
            var budget = new FrameBudget(CreatureHostSystem.SpawnBudget);
            for (int w = 0; w < wanted.Length; w++)
            {
                ref var row = ref plan.Spawns[(int)wanted[w].y];
                if (Live.Contains(row.Id)) continue;
                if (!budget.Take(1)) break;
                Live.Add(row.Id);
                var e = Ecb.Instantiate(Prefab);
                int herd = CreaturePlan.HerdOf(row.Id);
                float2 offset = row.At - new float2(row.Route.X0, row.Route.Z0);
                float2 at = Wake(ref row, out float heading);
                if (Herds.TryGetValue(herd, out var known)) at += known.Shift;
                else Herds.Add(herd, new CreatureHerd { Mind = HerdMind.Start(row.HerdSeed), Route = row.Route, Body = row.Body, Centre = at - offset, Needs = row.Needs });
                byte want = Drives.Wake(plan.Drives[row.Body], row.Needs, Now, row.HerdSeed, row.Seed, out var levels);
                Ecb.SetComponent(e, new CreatureState { Id = row.Id, Body = row.Body, BodySeed = row.BodySeed, Seed = row.Seed, WalkSpeed = row.WalkSpeed, Position = at, Heading = heading + 2 * HerdMind.MemberOffset(row.Seed), Age = row.Age });
                Ecb.AddComponent(e, new CreatureBrain { Home = at, Offset = offset, Herd = herd, Random = new Unity.Mathematics.Random(row.Seed), Timer = 0.5f + (row.Seed % 997) / 997f * 3, Levels = levels, Want = want, Lead = row.Lead });
            }
        }

        float2 Wake(ref CreatureSpawnRow row, out float heading)
        {
            row.Route.At(Now, out float x, out float z, out heading);
            return new float2(x, z) + row.At - new float2(row.Route.X0, row.Route.Z0);
        }

        struct Closer : IComparer<float2>
        {
            public int Compare(float2 a, float2 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
        }
    }

    [BurstCompile]
    struct ClearRelevancy : IJob
    {
        public NativeParallelHashMap<RelevantGhostForConnection, int> Set;

        public void Execute() => Set.Clear();
    }

    [BurstCompile]
    partial struct Relevancy : IJobEntity
    {
        public NativeParallelHashMap<RelevantGhostForConnection, int> Set;
        [ReadOnly] public NativeArray<float2> Players;
        [ReadOnly] public NativeArray<int2> Viewers;
        public BlobAssetReference<CreaturePlanData> Plan;

        void Execute(in CreatureState s, in CreatureBrain brain, in GhostInstance ghost)
        {
            if (ghost.ghostId == 0) return;
            float reach = Plan.Value.Reach[s.Body].y;
            for (int v = 0; v < Viewers.Length; v++)
            {
                int who = Viewers[v].y;
                if (who >= 0 && math.distancesq(Players[who], brain.Home) <= reach * reach) continue;
                Set.TryAdd(new RelevantGhostForConnection(Viewers[v].x, ghost.ghostId), 1);
            }
        }
    }
}
