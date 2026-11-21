using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Stabby.Game
{
    public static class MapGen
    {
        public static WorldConfig Config(ulong seed) => new WorldConfig { Seed = seed, Size = GameSettings.Size, CellMeters = WorldConfig.CellMetersFor(GameSettings.Size) };

        public static int RaiseMound(GeneratedWorld w, float rise = 5f, int radius = 10)
        {
            int n = w.Config.Size, s = WorldChunks.Size, best = -1;
            long bestD = long.MaxValue;
            for (int y = s; y < n; y += s)
                for (int x = s; x < n; x += s)
                {
                    long d = (long)(x - n / 2) * (x - n / 2) + (long)(y - n / 2) * (y - n / 2);
                    if (d < bestD && Dry(w, x, y, radius)) { bestD = d; best = y * n + x; }
                }
            if (best < 0) return -1;
            int cx = best % n, cy = best / n;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    float f = 1f - (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    if (f <= 0) continue;
                    w.Height[y * n + x] += rise * f;
                    w.Water.WaterSurface[y * n + x] += rise * f;
                }
            return best;
        }

        static bool Dry(GeneratedWorld w, int cx, int cy, int r)
        {
            int n = w.Config.Size;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= n || y >= n) return false;
                    int i = y * n + x;
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || w.Water.IsRiver[i] || w.Height[i] < 2f) return false;
                }
            return true;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerMapSystem : SystemBase
    {
        sealed class Prepared
        {
            public GeneratedWorld Map;
            public int Mound;
            public BlobBuilder Packed, Ground;
            public string Source;
        }

        Task<Prepared> gen;
        BlobAssetReference<GroundData> ground;
        BlobAssetReference<PackedChunks> packed;

        public GeneratedWorld Map { get; private set; }

        protected override void OnCreate()
        {
            var defs = BiomeFile.Load(BiomeFile.DefaultPath);
            var lib = MaterialFile.Load(MaterialFile.DefaultPath);
            var objects = ObjectFile.ForBiomes(defs);
            string path = MapFile.PathFor(GameSettings.Seed, GameSettings.Size);
            gen = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var (w, source) = MapFile.LoadOrGenerate(path, MapGen.Config(GameSettings.Seed), defs, lib, true);
                w.Objects = objects;
                var p = new Prepared { Map = w, Mound = MapGen.RaiseMound(w) };
                var sums = WorldChunks.Checksums(w);
                long sumsMs = sw.ElapsedMilliseconds;
                p.Packed = Pack(w, sums);
                long packMs = sw.ElapsedMilliseconds;
                p.Ground = Ground.Fill(w);
                p.Source = $"{source}, checksums at {sumsMs} ms, deflated at {packMs} ms, detail at {sw.ElapsedMilliseconds} ms, {sums.Length} chunks";
                return p;
            });
        }

        static BlobBuilder Pack(GeneratedWorld w, ulong[] sums)
        {
            int a = WorldChunks.Across(w.Config.Size), total = 0;
            var zipped = new byte[sums.Length][];
            for (int k = 0; k < zipped.Length; k++) { zipped[k] = ChunkZip.Deflate(WorldChunks.Pack(w, k % a, k / a)); total += zipped[k].Length; }
            var b = new BlobBuilder(Allocator.Persistent);
            ref var root = ref b.ConstructRoot<PackedChunks>();
            root.Seed = w.Config.Seed;
            root.Size = w.Config.Size;
            var s = b.Allocate(ref root.Sums, sums.Length);
            var r = b.Allocate(ref root.Ranges, sums.Length);
            var bytes = b.Allocate(ref root.Bytes, total);
            int at = 0;
            for (int k = 0; k < zipped.Length; k++)
            {
                s[k] = sums[k];
                r[k] = new int2(at, zipped[k].Length);
                for (int j = 0; j < zipped[k].Length; j++) bytes[at + j] = zipped[k][j];
                at += zipped[k].Length;
            }
            return b;
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            if (ground.IsCreated) { ground.Dispose(); packed.Dispose(); }
            Owned.Release(gen, p => { p.Ground.Dispose(); p.Packed.Dispose(); });
        }

        protected override void OnUpdate()
        {
            if (!gen.IsCompleted) return;
            Enabled = false;
            if (gen.IsFaulted) { GameStatus.Failure = gen.Exception.ToString(); gen = null; return; }
            var p = gen.Result;
            gen = null;
            Map = p.Map;
            int mound = p.Mound, n = Map.Config.Size;
            ground = Ground.Create(EntityManager, p.Ground);
            packed = p.Packed.CreateBlobAssetReference<PackedChunks>(Allocator.Persistent);
            p.Packed.Dispose();
            if (mound >= 0) EntityManager.CreateSingleton(new SpawnPoint { XZ = new float2(mound % n, mound / n) * Map.Config.CellMeters });
            EntityManager.CreateSingleton(new ChunkStore { Blob = packed });
            GameStatus.Lines[World.Name] = $"{World.Name}: seed {GameSettings.Seed} {p.Source}, +5 m mound at cell {mound % n},{mound / n}";
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ClientMapSystem : SystemBase
    {
        Task<(GeneratedWorld, ulong[])> gen;
        Task<(int, BlobBuilder)> finish;
        BlobAssetReference<GroundData> ground;
        GeneratedWorld world;
        ulong[] mine;
        ulong hostSeed;
        string source;
        int stale;
        long genMs;
        bool requested;
        Stopwatch clock;
        Entity inbox;

        public List<BiomeDef> Defs { get; private set; }
        public MaterialLibrary Materials { get; private set; }
        public ObjectLibrary Objects { get; private set; }
        public GeneratedWorld Map { get; private set; }

        protected override void OnCreate()
        {
            Defs = BiomeFile.Load(BiomeFile.DefaultPath);
            Materials = MaterialFile.Load(MaterialFile.DefaultPath);
            Objects = ObjectFile.ForBiomes(Defs);
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            if (ground.IsCreated) ground.Dispose();
            if (Map == null) Owned.Release(finish, r => { if (r.Item1 == 0) r.Item2.Dispose(); });
            if (inbox != Entity.Null && EntityManager.Exists(inbox)) EntityManager.GetComponentData<ChunkInbox>(inbox).Dispose();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            int chunks = -1;
            foreach (var (info, e) in SystemAPI.Query<RefRO<WorldInfoRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                var i = info.ValueRO;
                if (gen != null) continue;
                if (i.Version != WorldConfig.GeneratorVersion || i.Size != GameSettings.Size)
                {
                    GameStatus.Failure = $"{World.Name}: host runs generator {i.Version} size {i.Size}, this peer {WorldConfig.GeneratorVersion} size {GameSettings.Size}";
                    continue;
                }
                chunks = i.Chunks;
                hostSeed = i.Seed;
                clock = Stopwatch.StartNew();
                var defs = Defs;
                var lib = Materials;
                var objects = Objects;
                ulong s = i.Seed;
                bool empty = World.Name == GameSettings.FullStreamClient;
                string path = MapFile.PathFor(s, GameSettings.Size);
                gen = Task.Run(() =>
                {
                    GeneratedWorld w;
                    if (empty)
                    {
                        var ids = new string[defs.Count];
                        for (int b = 0; b < ids.Length; b++) ids[b] = defs[b].Id;
                        w = WorldFile.Empty(new WorldConfig { Seed = s, Size = GameSettings.Size, CellMeters = WorldConfig.CellMetersFor(GameSettings.Size) }, ids, lib);
                        source = "started empty";
                    }
                    else (w, source) = MapFile.LoadOrGenerate(path, MapGen.Config(s), defs, lib, false);
                    w.Objects = objects;
                    return (w, WorldChunks.Checksums(w));
                });
            }
            ecb.Playback(EntityManager);
            if (chunks >= 0)
                inbox = EntityManager.CreateSingleton(new ChunkInbox
                {
                    HostSums = new NativeArray<ulong>(chunks, Allocator.Persistent),
                    Slots = new NativeArray<ChunkSlot>(chunks, Allocator.Persistent),
                    Arena = new NativeList<byte>(Allocator.Persistent),
                });

            if (world == null && gen != null && gen.IsCompleted)
            {
                if (gen.IsFaulted) { GameStatus.Failure = gen.Exception.ToString(); Enabled = false; return; }
                (world, mine) = gen.Result;
                genMs = clock.ElapsedMilliseconds;
            }
            if (inbox == Entity.Null || world == null) return;
            var box = EntityManager.GetComponentData<ChunkInbox>(inbox);
            if (!requested && box.SumsIn == box.HostSums.Length)
            {
                requested = true;
                var send = new EntityCommandBuffer(Allocator.Temp);
                for (int k = 0; k < mine.Length; k++)
                {
                    if (mine[k] == box.HostSums[k]) continue;
                    box.Slots[k] = new ChunkSlot { Wanted = true };
                    stale++;
                    Rpc.Send(send, Entity.Null, new ChunkRequestRpc { Index = k });
                }
                box.Remaining = stale;
                EntityManager.SetComponentData(inbox, box);
                send.Playback(EntityManager);
            }
            if (requested && finish == null && box.Remaining == 0) StartFinish(box);
            if (finish != null && Map == null) Finish(box);
        }

        void StartFinish(ChunkInbox box)
        {
            var w = world;
            var host = box.HostSums.ToArray();
            var got = new List<(int, byte[])>();
            for (int k = 0; k < box.Slots.Length; k++)
            {
                int len = box.Length(k);
                if (len > 0) got.Add((k, box.Arena.AsArray().GetSubArray(box.Slots[k].Start, len).ToArray()));
            }
            ulong hs = hostSeed;
            finish = Task.Run(() =>
            {
                int a = WorldChunks.Across(w.Config.Size);
                foreach (var (k, data) in got) WorldChunks.Unpack(w, k % a, k / a, ChunkZip.Inflate(data, WorldChunks.Bytes(w.Config.Size, k % a, k / a)));
                w.Config.Seed = hs;
                if (got.Count > 0) WorldChunks.Relink(w);
                var sums = WorldChunks.Checksums(w);
                int bad = 0;
                for (int k = 0; k < sums.Length; k++) if (sums[k] != host[k]) bad++;
                return (bad, bad == 0 ? Ground.Fill(w) : default);
            });
        }

        void Finish(ChunkInbox box)
        {
            if (!finish.IsCompleted) return;
            if (finish.IsFaulted) { GameStatus.Failure = finish.Exception.ToString(); Enabled = false; return; }
            var (bad, blob) = finish.Result;
            Map = world;
            GameStatus.Lines[World.Name] = $"{World.Name}: {source}, ready after {genMs} ms, {stale}/{mine.Length} chunks stale, " +
                $"streamed {box.Streamed} ({box.BytesIn / 1024} KB deflated in {box.PiecesIn} pieces), synced after {clock.ElapsedMilliseconds} ms, {bad} differ after sync";
            if (bad == 0) { GameStatus.Synced.Add(World.Name); ground = Ground.Create(EntityManager, blob); }
            else GameStatus.Failure = GameStatus.Lines[World.Name];
        }
    }
}
