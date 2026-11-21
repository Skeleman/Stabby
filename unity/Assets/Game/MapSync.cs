using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
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

    public static class WorldStore
    {
        public sealed class Entry
        {
            public ulong[] Sums;
            public GeneratedWorld Map;
            public GroundParts Parts;
            public BlobAssetReference<GroundData> Ground;
            public Task<SharedObjects> Objects;
            public string Path;
            public int Refs;
            public bool Failed;
        }

        public sealed class SharedObjects
        {
            public ObjectField Field;
            public NativeArray<int> Starts;
            public NativeArray<ObjectColumn> Items;
            public float Cell, Max;
            public long Ms;
            bool disposed;

            public int Count => Items.Length;

            public bool Build(out BlobBuilder b)
            {
                lock (this)
                {
                    b = default;
                    if (disposed) return false;
                    b = new BlobBuilder(Allocator.Persistent);
                    ref var root = ref b.ConstructRoot<ObjectColumnData>();
                    root.Cell = Cell;
                    root.Max = Max;
                    var starts = b.Allocate(ref root.Starts, Starts.Length);
                    for (int k = 0; k < Starts.Length; k++) starts[k] = Starts[k];
                    var items = b.Allocate(ref root.Items, Items.Length);
                    for (int k = 0; k < Items.Length; k++) items[k] = Items[k];
                    return true;
                }
            }

            public void Dispose()
            {
                lock (this)
                {
                    if (disposed) return;
                    disposed = true;
                    Starts.Dispose();
                    Items.Dispose();
                }
            }
        }

        static readonly List<Entry> entries = new List<Entry>();

        public static Entry Offer(GeneratedWorld map, ulong[] sums, string path)
        {
            var e = new Entry { Map = map, Sums = sums, Path = path, Refs = 1 };
            lock (entries) entries.Add(e);
            return e;
        }

        public static void Publish(Entry e, GroundParts parts, BlobAssetReference<GroundData> ground)
        {
            lock (entries) { e.Parts = parts; e.Ground = ground; }
        }

        public static void Fail(Entry e)
        {
            if (e == null) return;
            lock (entries) e.Failed = true;
        }

        public static bool Ready(Entry e, out bool failed)
        {
            lock (entries) { failed = e.Failed; return e.Ground.IsCreated; }
        }

        public static Task<SharedObjects> Objects(Entry e, Func<SharedObjects> build)
        {
            lock (entries) return e.Objects ??= Task.Run(build);
        }

        public static Entry Find(ulong[] sums)
        {
            lock (entries)
                foreach (var e in entries)
                    if (!e.Failed && e.Refs > 0 && Same(e.Sums, sums)) { e.Refs++; return e; }
            return null;
        }

        public static Entry Local(string path)
        {
            lock (entries)
                foreach (var e in entries)
                    if (!e.Failed && e.Refs > 0 && e.Path == path) { e.Refs++; return e; }
            return null;
        }

        public static void Release(Entry e)
        {
            if (e == null) return;
            lock (entries)
            {
                if (--e.Refs > 0) return;
                entries.Remove(e);
                if (e.Ground.IsCreated) e.Ground.Dispose();
                e.Ground = default;
                e.Map = null;
                e.Parts = null;
                Owned.Release(e.Objects, o => o.Dispose());
                e.Objects = null;
            }
        }

        static bool Same(ulong[] a, ulong[] b)
        {
            if (a.Length != b.Length) return false;
            for (int k = 0; k < a.Length; k++) if (a[k] != b[k]) return false;
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
            public BlobBuilder Packed;
            public Task<GroundParts> Fill;
            public long FillMs;
            public string Source;
            public WorldStore.Entry Entry;
        }

        Task<Prepared> gen;
        Prepared prepared;
        BlobAssetReference<GroundData> ground;
        BlobAssetReference<PackedChunks> packed;
        BlobBake<PackedChunks> packBake;
        BlobBake<GroundData> groundBake;
        GroundParts filled;
        WorldStore.Entry entry;

        public WorldStore.Entry Shared => entry;
        public GeneratedWorld Map { get; private set; }
        public GroundParts Parts { get; private set; }

        protected override void OnCreate()
        {
            var defs = ContentFiles.Biomes();
            var lib = ContentFiles.Materials(ContentFiles.MaterialsPath);
            var objects = ContentFiles.Objects(defs);
            string path = MapFile.PathFor(GameSettings.Seed, GameSettings.Size);
            gen = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var (w, source) = MapFile.LoadOrGenerate(path, MapGen.Config(GameSettings.Seed), defs, lib, true);
                w.Objects = objects;
                var p = new Prepared { Map = w, Mound = MapGen.RaiseMound(w) };
                p.Fill = Task.Run(() =>
                {
                    var g = Ground.Fill(w);
                    p.FillMs = sw.ElapsedMilliseconds;
                    return g;
                });
                var sums = WorldChunks.Checksums(w);
                p.Entry = WorldStore.Offer(w, sums, path);
                long sumsMs = sw.ElapsedMilliseconds;
                p.Packed = Pack(w, sums);
                Shed(w);
                p.Source = $"{source}, checksums at {sumsMs} ms, deflated at {sw.ElapsedMilliseconds} ms, {sums.Length} chunks";
                return p;
            });
        }

        sealed class Buffers
        {
            public readonly byte[] Raw = new byte[WorldChunks.Size * WorldChunks.Size * WorldChunks.CellBytes];
            public readonly byte[] Scratch = new byte[WorldChunks.Size * WorldChunks.Size * WorldChunks.CellBytes];
            public readonly MemoryStream Row = new MemoryStream();
        }

        public static void Shed(GeneratedWorld w)
        {
            w.Climate = null;
            w.Water.Receiver = null;
            w.Water.DischargeM3s = null;
        }

        static BlobBuilder Pack(GeneratedWorld w, ulong[] sums)
        {
            int a = WorldChunks.Across(w.Config.Size), total = 0;
            var rows = new byte[a][];
            var lengths = new int[sums.Length];
            var pool = new ConcurrentBag<Buffers>();
            Parallel.For(0, a, () => pool.TryTake(out var spare) ? spare : new Buffers(), (cy, _, u) =>
            {
                u.Row.SetLength(0);
                for (int cx = 0; cx < a; cx++) lengths[cy * a + cx] = ChunkZip.Deflate(u.Raw, WorldChunks.Pack(w, cx, cy, u.Raw, 0), u.Scratch, u.Row);
                rows[cy] = u.Row.ToArray();
                return u;
            }, pool.Add);
            for (int cy = 0; cy < a; cy++) total += rows[cy].Length;
            var b = new BlobBuilder(Allocator.Persistent);
            ref var root = ref b.ConstructRoot<PackedChunks>();
            root.Seed = w.Config.Seed;
            root.Size = w.Config.Size;
            var s = b.Allocate(ref root.Sums, sums.Length);
            var r = b.Allocate(ref root.Ranges, sums.Length);
            var bytes = b.Allocate(ref root.Bytes, total);
            int at = 0;
            for (int k = 0; k < sums.Length; k++)
            {
                s[k] = sums[k];
                r[k] = new int2(at, lengths[k]);
                at += lengths[k];
            }
            at = 0;
            for (int cy = 0; cy < a; cy++)
            {
                var z = rows[cy];
                for (int j = 0; j < z.Length; j++) bytes[at + j] = z[j];
                at += z.Length;
            }
            return b;
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            packBake.Release();
            groundBake.Release();
            if (packed.IsCreated) packed.Dispose();
            if (!ground.IsCreated && prepared != null && filled == null) Owned.Release(prepared.Fill, g => g.Blob.Dispose());
            if (!ground.IsCreated) WorldStore.Fail(entry);
            WorldStore.Release(entry);
            Owned.Release(gen, p => { p.Packed.Dispose(); WorldStore.Fail(p.Entry); WorldStore.Release(p.Entry); Owned.Release(p.Fill, g => g.Blob.Dispose()); });
        }

        protected override void OnUpdate()
        {
            if (gen != null)
            {
                if (!gen.IsCompleted) return;
                if (gen.IsFaulted) { GameStatus.Failure = gen.Exception.ToString(); gen = null; Enabled = false; return; }
                prepared = gen.Result;
                entry = prepared.Entry;
                gen = null;
                packBake = BlobBake<PackedChunks>.Start(prepared.Packed);
            }
            if (packBake.Started)
            {
                if (!packBake.IsCompleted) return;
                packed = packBake.Take();
                int n = prepared.Map.Config.Size, mound = prepared.Mound;
                if (mound >= 0) EntityManager.CreateSingleton(new SpawnPoint { XZ = new float2(mound % n, mound / n) * prepared.Map.Config.CellMeters });
                EntityManager.CreateSingleton(new ChunkStore { Blob = packed });
            }
            var p = prepared;
            if (filled == null)
            {
                if (!p.Fill.IsCompleted) return;
                if (p.Fill.IsFaulted) { Enabled = false; prepared = null; WorldStore.Fail(entry); GameStatus.Failure = p.Fill.Exception.ToString(); return; }
                filled = p.Fill.Result;
                groundBake = BlobBake<GroundData>.Start(filled.Blob);
            }
            if (!groundBake.IsCompleted) return;
            Enabled = false;
            prepared = null;
            Parts = filled;
            ground = Ground.Create(EntityManager, groundBake.Take());
            WorldStore.Publish(entry, filled, ground);
            Map = p.Map;
            int size = Map.Config.Size;
            GameStatus.Lines[World.Name] = $"{World.Name}: seed {GameSettings.Seed} {p.Source}, detail at {p.FillMs} ms, +5 m mound at cell {p.Mound % size},{p.Mound / size}";
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ClientMapSystem : SystemBase
    {
        sealed class Buffers
        {
            public byte[] Packed = new byte[0];
            public readonly byte[] Raw = new byte[WorldChunks.Size * WorldChunks.Size * WorldChunks.CellBytes];
            public readonly byte[] Scratch = new byte[WorldChunks.Size * WorldChunks.Size * WorldChunks.CellBytes];
        }

        Task<(GeneratedWorld, ulong[], WorldStore.Entry)> gen;
        Task<(int, GroundParts, WorldStore.Entry, GeneratedWorld)> finish;
        WorldStore.Entry shared, borrowed;
        BlobAssetReference<GroundData> ground;
        BlobBake<GroundData> groundBake;
        GroundParts synced;
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
        public GroundParts Parts { get; private set; }
        public WorldStore.Entry Shared => shared;

        protected override void OnCreate()
        {
            Defs = ContentFiles.Biomes();
            Materials = ContentFiles.Materials(ContentFiles.MaterialsPath);
            Objects = ContentFiles.Objects(Defs);
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            groundBake.Release();
            if (shared != null) WorldStore.Release(shared);
            else if (ground.IsCreated) ground.Dispose();
            if (borrowed != null) WorldStore.Release(borrowed);
            if (mine == null) Owned.Release(gen, r => { if (r.Item3 != null) WorldStore.Release(r.Item3); });
            if (Map == null && synced == null && shared == null) Owned.Release(finish, r => { if (r.Item3 != null) WorldStore.Release(r.Item3); else if (r.Item1 == 0) r.Item2.Blob.Dispose(); });
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
                if (gen != null || mine != null) continue;
                if (i.Version != WorldConfig.GeneratorVersion || i.Size != GameSettings.Size)
                {
                    GameStatus.Failure = $"{World.Name}: host runs generator {i.Version} size {i.Size}, this peer {WorldConfig.GeneratorVersion} size {GameSettings.Size}";
                    continue;
                }
                chunks = i.Chunks;
                hostSeed = i.Seed;
                clock = Stopwatch.StartNew();
                Load(true);
            }
            ecb.Playback(EntityManager);
            if (chunks >= 0)
                inbox = EntityManager.CreateSingleton(new ChunkInbox
                {
                    HostSums = new NativeArray<ulong>(chunks, Allocator.Persistent),
                    Slots = new NativeArray<ChunkSlot>(chunks, Allocator.Persistent),
                    Arena = new NativeList<byte>(Allocator.Persistent),
                });

            if (mine == null && gen != null && gen.IsCompleted)
            {
                if (gen.IsFaulted) { GameStatus.Failure = gen.Exception.ToString(); Enabled = false; return; }
                (world, mine, borrowed) = gen.Result;
                gen = null;
                genMs = clock.ElapsedMilliseconds;
            }
            if (inbox == Entity.Null || mine == null) return;
            var box = EntityManager.GetComponentData<ChunkInbox>(inbox);
            if (!requested && box.SumsIn == box.HostSums.Length && borrowed != null)
            {
                bool same = true;
                for (int k = 0; k < mine.Length; k++) same &= mine[k] == box.HostSums[k];
                if (!same) { WorldStore.Release(borrowed); borrowed = null; mine = null; Load(false); return; }
                shared = borrowed;
                borrowed = null;
                requested = true;
                GameStatus.Lines[World.Name] = $"{World.Name}: {source}, ready after {genMs} ms, 0/{mine.Length} chunks stale, streamed 0 (0 KB deflated in 0 pieces), synced after {clock.ElapsedMilliseconds} ms, 0 differ after sync";
            }
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
            if (requested && finish == null && shared == null && box.Remaining == 0) StartFinish(box);
            if ((finish != null || shared != null) && Map == null) Finish(box);
        }

        void Load(bool borrow)
        {
            var defs = Defs;
            var lib = Materials;
            var objects = Objects;
            ulong s = hostSeed;
            bool empty = World.Name == GameSettings.FullStreamClient;
            string path = MapFile.PathFor(s, GameSettings.Size);
            gen = Task.Run<(GeneratedWorld, ulong[], WorldStore.Entry)>(() =>
            {
                var local = borrow && !empty ? WorldStore.Local(path) : null;
                if (local != null)
                {
                    source = "borrowed the host world";
                    return (null, local.Sums, local);
                }
                if (empty)
                {
                    source = "started empty";
                    return (null, WorldChunks.EmptyChecksums(GameSettings.Size, true), null);
                }
                var (w, note) = MapFile.LoadOrGenerate(path, MapGen.Config(s), defs, lib, false);
                source = note;
                w.Objects = objects;
                return (w, WorldChunks.Checksums(w), null);
            });
        }

        void StartFinish(ChunkInbox box)
        {
            var w = world;
            var host = box.HostSums.ToArray();
            var slots = box.Slots.ToArray();
            var arena = box.Arena.AsArray();
            ulong hs = hostSeed;
            var defs = Defs;
            var lib = Materials;
            var objects = Objects;
            finish = Task.Run<(int, GroundParts, WorldStore.Entry, GeneratedWorld)>(() =>
            {
                int n = GameSettings.Size, a = WorldChunks.Across(n);
                var got = new List<int>();
                for (int k = 0; k < slots.Length; k++) if (slots[k].Pieces > 0) got.Add(k);
                var pool = new ConcurrentBag<Buffers>();
                if (w == null)
                {
                    int differ = 0;
                    Parallel.For(0, got.Count, () => pool.TryTake(out var spare) ? spare : new Buffers(), (j, _, u) =>
                    {
                        int k = got[j], cx = k % a, cy = k / a, len = ChunkInbox.Length(slots[k]), raw = WorldChunks.Bytes(n, cx, cy);
                        if (u.Packed.Length < len) u.Packed = new byte[len];
                        NativeArray<byte>.Copy(arena, slots[k].Start, u.Packed, 0, len);
                        ChunkZip.Inflate(u.Packed, 0, len, raw, u.Scratch, u.Raw);
                        if (WorldChunks.Checksum(u.Raw, raw) != host[k]) Interlocked.Increment(ref differ);
                        return u;
                    }, pool.Add);
                    var hostWorld = differ == 0 ? WorldStore.Find(host) : null;
                    if (hostWorld != null) return (0, null, hostWorld, null);
                    var ids = new string[defs.Count];
                    for (int b = 0; b < ids.Length; b++) ids[b] = defs[b].Id;
                    w = WorldFile.Empty(new WorldConfig { Seed = hs, Size = n, CellMeters = WorldConfig.CellMetersFor(n) }, ids, lib);
                    w.Objects = objects;
                }
                Parallel.For(0, got.Count, () => pool.TryTake(out var spare) ? spare : new Buffers(), (j, _, u) =>
                {
                    int k = got[j], cx = k % a, cy = k / a, len = ChunkInbox.Length(slots[k]);
                    if (u.Packed.Length < len) u.Packed = new byte[len];
                    NativeArray<byte>.Copy(arena, slots[k].Start, u.Packed, 0, len);
                    ChunkZip.Inflate(u.Packed, 0, len, WorldChunks.Bytes(n, cx, cy), u.Scratch, u.Raw);
                    WorldChunks.Unpack(w, cx, cy, u.Raw, 0);
                    return u;
                }, pool.Add);
                w.Config.Seed = hs;
                if (got.Count > 0) WorldChunks.Relink(w);
                var sums = WorldChunks.Checksums(w);
                int bad = 0;
                for (int k = 0; k < sums.Length; k++) if (sums[k] != host[k]) bad++;
                if (bad != 0) return (bad, null, null, w);
                var found = WorldStore.Find(sums);
                if (found != null) return (0, null, found, null);
                ServerMapSystem.Shed(w);
                return (0, Ground.Fill(w), null, w);
            });
        }

        void Finish(ChunkInbox box)
        {
            if (synced == null && shared == null)
            {
                if (!finish.IsCompleted) return;
                if (finish.IsFaulted) { GameStatus.Failure = finish.Exception.ToString(); Enabled = false; return; }
                var (bad, parts, found, made) = finish.Result;
                if (made != null) world = made;
                GameStatus.Lines[World.Name] = $"{World.Name}: {source}, ready after {genMs} ms, {stale}/{mine.Length} chunks stale, " +
                    $"streamed {box.Streamed} ({box.BytesIn / 1024} KB deflated in {box.PiecesIn} pieces), synced after {clock.ElapsedMilliseconds} ms, {bad} differ after sync";
                if (bad != 0) { Map = world; GameStatus.Failure = GameStatus.Lines[World.Name]; return; }
                shared = found;
                if (found == null)
                {
                    synced = parts;
                    groundBake = BlobBake<GroundData>.Start(parts.Blob);
                }
            }
            if (shared != null)
            {
                if (!WorldStore.Ready(shared, out bool failed))
                {
                    if (failed) { GameStatus.Failure = $"{World.Name}: shared host world failed"; Enabled = false; }
                    return;
                }
                ground = Ground.Create(EntityManager, shared.Ground);
                GameStatus.Lines[World.Name] += ", shares host world";
                GameStatus.Synced.Add(World.Name);
                Parts = shared.Parts;
                Map = shared.Map;
                world = null;
                return;
            }
            if (!groundBake.IsCompleted) return;
            ground = Ground.Create(EntityManager, groundBake.Take());
            GameStatus.Synced.Add(World.Name);
            Parts = synced;
            Map = world;
        }
    }
}
