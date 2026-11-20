using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
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

    public static class ChunkZip
    {
        public static byte[] Deflate(byte[] raw)
        {
            var m = new MemoryStream();
            using (var z = new DeflateStream(m, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
            return m.ToArray();
        }

        public static byte[] Inflate(byte[] packed)
        {
            var m = new MemoryStream();
            using (var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress)) z.CopyTo(m);
            return m.ToArray();
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerMapSystem : SystemBase
    {
        sealed class Prepared
        {
            public GeneratedWorld Map;
            public int Mound;
            public ulong[] Sums;
            public byte[][] Packed;
            public TerrainDetail Detail;
            public string Source;
        }

        Task<Prepared> gen;
        ulong[] sums;
        byte[][] packed;
        readonly HashSet<Entity> informed = new HashSet<Entity>();
        readonly Dictionary<Entity, Queue<(int, int)>> outbox = new Dictionary<Entity, Queue<(int, int)>>();

        public GeneratedWorld Map { get; private set; }
        public int MoundCell { get; private set; }

        protected override void OnCreate()
        {
            var defs = BiomeFile.Load(BiomeFile.DefaultPath);
            string path = MapFile.PathFor(GameSettings.Seed, GameSettings.Size), content = BiomeFile.DefaultPath;
            gen = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var (w, source) = MapFile.LoadOrGenerate(path, content, MapGen.Config(GameSettings.Seed), defs, true);
                var p = new Prepared { Map = w, Mound = MapGen.RaiseMound(w), Sums = WorldChunks.Checksums(w) };
                long sumsMs = sw.ElapsedMilliseconds;
                int a = WorldChunks.Across(w.Config.Size);
                p.Packed = new byte[p.Sums.Length][];
                for (int k = 0; k < p.Packed.Length; k++) p.Packed[k] = ChunkZip.Deflate(WorldChunks.Pack(w, k % a, k / a));
                long packMs = sw.ElapsedMilliseconds;
                p.Detail = new TerrainDetail(w);
                p.Source = $"{source}, checksums at {sumsMs} ms, deflated at {packMs} ms, detail at {sw.ElapsedMilliseconds} ms";
                return p;
            });
        }

        protected override void OnUpdate()
        {
            if (Map == null)
            {
                if (!gen.IsCompleted) return;
                if (gen.IsFaulted) { GameStatus.Failure = gen.Exception.ToString(); Enabled = false; return; }
                var p = gen.Result;
                (Map, MoundCell, sums, packed) = (p.Map, p.Mound, p.Sums, p.Packed);
                Ground.Create(EntityManager, Map, p.Detail);
                int n = Map.Config.Size;
                GameStatus.Lines[World.Name] = $"{World.Name}: seed {GameSettings.Seed} {p.Source}, {sums.Length} chunks, +5 m mound at cell {MoundCell % n},{MoundCell / n}";
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, conn) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithEntityAccess())
            {
                if (!informed.Add(conn)) continue;
                Rpc.Send(ecb, conn, new WorldInfoRpc { Seed = GameSettings.Seed, Version = WorldConfig.GeneratorVersion, Size = Map.Config.Size, Chunks = sums.Length });
                for (int k = 0; k < sums.Length; k++) Rpc.Send(ecb, conn, new ChunkSumRpc { Index = k, Sum = sums[k] });
                outbox[conn] = new Queue<(int, int)>();
            }

            foreach (var (req, rx, e) in SystemAPI.Query<RefRO<ChunkRequestRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                int k = req.ValueRO.Index;
                if (k < 0 || k >= sums.Length || !outbox.TryGetValue(rx.ValueRO.SourceConnection, out var q)) continue;
                int pieces = Pieces(packed[k].Length);
                for (int p = 0; p < pieces; p++) q.Enqueue((k, p));
            }

            foreach (var kv in outbox)
            {
                var q = kv.Value;
                if (!EntityManager.Exists(kv.Key)) { q.Clear(); continue; }
                for (int s = 0; s < GameSettings.PiecesPerTick && q.Count > 0; s++)
                {
                    var (k, p) = q.Dequeue();
                    var data = packed[k];
                    int from = p * GameSettings.PieceBytes, to = Math.Min(data.Length, from + GameSettings.PieceBytes);
                    var rpc = new ChunkPieceRpc { Index = k, Piece = p, Pieces = Pieces(data.Length) };
                    for (int b = from; b < to; b++) rpc.Bytes.Add(data[b]);
                    Rpc.Send(ecb, kv.Key, rpc);
                }
            }
            ecb.Playback(EntityManager);
        }

        static int Pieces(int bytes) => (bytes + GameSettings.PieceBytes - 1) / GameSettings.PieceBytes;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ClientMapSystem : SystemBase
    {
        Task<(GeneratedWorld, ulong[])> gen;
        Task<(int, TerrainDetail)> finish;
        GeneratedWorld world;
        ulong[] mine;
        ulong hostSeed;
        string source;
        ulong[] hostSums;
        int sumsIn, stale, streamed, piecesIn;
        long bytesIn, genMs;
        bool requested;
        Stopwatch clock;
        readonly Dictionary<int, byte[][]> parts = new Dictionary<int, byte[][]>();
        readonly HashSet<int> pending = new HashSet<int>();
        readonly List<(int, byte[])> arrived = new List<(int, byte[])>();

        public List<BiomeDef> Defs { get; private set; }
        public GeneratedWorld Map { get; private set; }

        protected override void OnCreate()
        {
            Defs = BiomeFile.Load(BiomeFile.DefaultPath);
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (info, e) in SystemAPI.Query<RefRO<WorldInfoRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                var i = info.ValueRO;
                if (i.Version != WorldConfig.GeneratorVersion || i.Size != GameSettings.Size)
                {
                    GameStatus.Failure = $"{World.Name}: host runs generator {i.Version} size {i.Size}, this peer {WorldConfig.GeneratorVersion} size {GameSettings.Size}";
                    continue;
                }
                hostSums = new ulong[i.Chunks];
                hostSeed = i.Seed;
                clock = Stopwatch.StartNew();
                var defs = Defs;
                ulong s = i.Seed;
                bool empty = World.Name == GameSettings.FullStreamClient;
                string path = MapFile.PathFor(s, GameSettings.Size), content = BiomeFile.DefaultPath;
                gen = Task.Run(() =>
                {
                    GeneratedWorld w;
                    if (empty)
                    {
                        var ids = new string[defs.Count];
                        for (int b = 0; b < ids.Length; b++) ids[b] = defs[b].Id;
                        w = WorldFile.Empty(new WorldConfig { Seed = s, Size = GameSettings.Size, CellMeters = WorldConfig.CellMetersFor(GameSettings.Size) }, ids);
                        source = "started empty";
                    }
                    else (w, source) = MapFile.LoadOrGenerate(path, content, MapGen.Config(s), defs, false);
                    return (w, WorldChunks.Checksums(w));
                });
            }
            foreach (var (sum, e) in SystemAPI.Query<RefRO<ChunkSumRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                if (hostSums == null) continue;
                hostSums[sum.ValueRO.Index] = sum.ValueRO.Sum;
                sumsIn++;
            }
            foreach (var (piece, e) in SystemAPI.Query<RefRO<ChunkPieceRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                Receive(piece.ValueRO);
            }

            if (world == null && gen != null && gen.IsCompleted)
            {
                if (gen.IsFaulted) { GameStatus.Failure = gen.Exception.ToString(); Enabled = false; ecb.Playback(EntityManager); return; }
                (world, mine) = gen.Result;
                genMs = clock.ElapsedMilliseconds;
            }
            if (world != null && !requested && hostSums != null && sumsIn == hostSums.Length)
            {
                requested = true;
                for (int k = 0; k < mine.Length; k++)
                    if (mine[k] != hostSums[k]) { pending.Add(k); Rpc.Send(ecb, Entity.Null, new ChunkRequestRpc { Index = k }); }
                stale = pending.Count;
            }
            if (requested && finish == null && pending.Count == 0) StartFinish();
            if (finish != null && Map == null) Finish();
            ecb.Playback(EntityManager);
        }

        void Receive(in ChunkPieceRpc p)
        {
            if (world == null || !pending.Contains(p.Index)) return;
            piecesIn++;
            bytesIn += p.Bytes.Length;
            if (!parts.TryGetValue(p.Index, out var got)) parts[p.Index] = got = new byte[p.Pieces][];
            var b = new byte[p.Bytes.Length];
            for (int j = 0; j < b.Length; j++) b[j] = p.Bytes[j];
            got[p.Piece] = b;
            foreach (var g in got) if (g == null) return;

            int len = 0;
            foreach (var g in got) len += g.Length;
            var data = new byte[len];
            int o = 0;
            foreach (var g in got) { Buffer.BlockCopy(g, 0, data, o, g.Length); o += g.Length; }
            arrived.Add((p.Index, data));
            parts.Remove(p.Index);
            pending.Remove(p.Index);
            streamed++;
        }

        void StartFinish()
        {
            var w = world;
            var got = arrived.ToArray();
            var host = hostSums;
            ulong hs = hostSeed;
            arrived.Clear();
            finish = Task.Run(() =>
            {
                int a = WorldChunks.Across(w.Config.Size);
                foreach (var (k, data) in got) WorldChunks.Unpack(w, k % a, k / a, ChunkZip.Inflate(data));
                if (got.Length > 0) WorldChunks.Relink(w);
                w.Config.Seed = hs;
                var sums = WorldChunks.Checksums(w);
                int bad = 0;
                for (int k = 0; k < sums.Length; k++) if (sums[k] != host[k]) bad++;
                return (bad, bad == 0 ? new TerrainDetail(w) : null);
            });
        }

        void Finish()
        {
            if (!finish.IsCompleted) return;
            if (finish.IsFaulted) { GameStatus.Failure = finish.Exception.ToString(); Enabled = false; return; }
            var (bad, detail) = finish.Result;
            Map = world;
            GameStatus.Lines[World.Name] = $"{World.Name}: {source}, ready after {genMs} ms, {stale}/{mine.Length} chunks stale, " +
                $"streamed {streamed} ({bytesIn / 1024} KB deflated in {piecesIn} pieces), synced after {clock.ElapsedMilliseconds} ms, {bad} differ after sync";
            if (bad == 0) { GameStatus.Synced.Add(World.Name); Ground.Create(EntityManager, world, detail); }
            else GameStatus.Failure = GameStatus.Lines[World.Name];
        }
    }
}
