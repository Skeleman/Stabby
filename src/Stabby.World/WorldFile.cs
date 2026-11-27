using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class WorldBake
    {
        public const int NotRaised = -2;
        public int Mound = NotRaised;
        public ulong[] Sums;
        public byte[] Packed;
        public int[] At, Len;
        public GroundBake Ground;
        public bool Raised => Mound != NotRaised;
    }

    public static class WorldFile
    {
        public const int Version = 2, HeaderBytes = 44, MaxSize = 8192, MaxBiomes = 1024, MaxIdBytes = 256;
        static readonly byte[] Magic = { (byte)'S', (byte)'T', (byte)'B', (byte)'W' };
        static readonly int Full = WorldChunks.Size * WorldChunks.Size * WorldChunks.CellBytes;

        public static byte[] Write(GeneratedWorld w, ulong contentHash = 0) => Write(w, contentHash, WorldBake.NotRaised, null);

        public static byte[] Write(GeneratedWorld w, ulong contentHash, int mound, GroundBake ground)
        {
            var c = w.Config;
            int a = WorldChunks.Across(c.Size), count = a * a;
            var head = new List<byte>(Magic);
            Int(head, Version); Int(head, WorldConfig.GeneratorVersion); Int(head, WorldChunks.Version); Long(head, contentHash);
            Long(head, c.Seed); Int(head, c.Size); Int(head, BitConverter.SingleToInt32Bits(c.CellMeters));
            Int(head, w.BiomeIds == null ? -1 : w.BiomeIds.Length);
            if (w.BiomeIds != null)
                foreach (var id in w.BiomeIds) { var b = Encoding.UTF8.GetBytes(id); Int(head, b.Length); head.AddRange(b); }
            Int(head, mound);
            var sums = new ulong[count];
            var packed = new byte[count][];
            var pool = new ConcurrentBag<byte[][]>();
            Parallel.For(0, count, () => pool.TryTake(out var u) ? u : new[] { new byte[Full], new byte[Full] }, (k, _, u) =>
            {
                int len = WorldChunks.Pack(w, k % a, k / a, u[0], 0);
                sums[k] = WorldChunks.Checksum(u[0], len);
                packed[k] = ChunkZip.Deflate(u[0], len, u[1]);
                return u;
            }, pool.Add);
            var g = ground?.Write();
            var m = new MemoryStream();
            m.Write(head.ToArray(), 0, head.Count);
            var cell = new byte[12];
            for (int k = 0; k < count; k++)
            {
                BitConverter.TryWriteBytes(cell.AsSpan(0, 8), sums[k]);
                BitConverter.TryWriteBytes(cell.AsSpan(8, 4), packed[k].Length);
                m.Write(cell, 0, 12);
            }
            foreach (var p in packed) m.Write(p, 0, p.Length);
            BitConverter.TryWriteBytes(cell.AsSpan(0, 4), g == null ? 0 : g.Length);
            m.Write(cell, 0, 4);
            if (g != null) m.Write(g, 0, g.Length);
            return m.ToArray();
        }

        public static int GeneratorVersionOf(byte[] data) => data.Length >= 12 && HasMagic(data) ? GetInt(data, 8) : -1;

        public static ulong ContentHashOf(byte[] data) =>
            data.Length >= 24 && HasMagic(data) && GetInt(data, 4) == Version ? (uint)GetInt(data, 16) | (ulong)(uint)GetInt(data, 20) << 32 : 0;

        public static ulong ContentHash(params byte[][] files)
        {
            ulong h = 14695981039346656037UL;
            foreach (var f in files)
            {
                long n = 0;
                foreach (var b in f)
                {
                    if (b == (byte)'\r') continue;
                    h = (h ^ b) * 1099511628211UL;
                    n++;
                }
                for (int i = 0; i < 8; i++) h = (h ^ (byte)(n >> (8 * i))) * 1099511628211UL;
            }
            return h;
        }

        public static GeneratedWorld Read(byte[] data, MaterialLibrary materials, LoadTimes times = null)
        {
            if (data.Length < HeaderBytes || !HasMagic(data)) throw new FormatException("not a Stabby world file");
            int o = 4;
            int version = Next(data, ref o), gen = Next(data, ref o), chunkVersion = Next(data, ref o);
            if (version != Version || gen != WorldConfig.GeneratorVersion || chunkVersion != WorldChunks.Version)
                throw new FormatException($"world file v{version} generator {gen} chunks v{chunkVersion}, expected v{Version} generator {WorldConfig.GeneratorVersion} chunks v{WorldChunks.Version}");
            o += 8;
            ulong seed = (uint)Next(data, ref o) | (ulong)(uint)Next(data, ref o) << 32;
            var c = new WorldConfig { Seed = seed, Size = Next(data, ref o), CellMeters = BitConverter.Int32BitsToSingle(Next(data, ref o)) };
            if (c.Size < 1 || c.Size > MaxSize) throw new FormatException($"world file size {c.Size} outside 1..{MaxSize}");
            if (!(c.CellMeters > 0) || float.IsInfinity(c.CellMeters)) throw new FormatException($"world file cell size {c.CellMeters} is not a positive number");
            int ids = Next(data, ref o);
            if (ids < -1 || ids > MaxBiomes) throw new FormatException($"world file biome count {ids} outside -1..{MaxBiomes}");
            string[] biomeIds = null;
            if (ids >= 0)
            {
                biomeIds = new string[ids];
                for (int b = 0; b < ids; b++)
                {
                    if (o + 4 > data.Length) throw new FormatException($"world file ends inside biome id {b}");
                    int len = Next(data, ref o);
                    if (len < 0 || len > MaxIdBytes || o + len > data.Length) throw new FormatException($"world file biome id {b} has bad length {len}");
                    biomeIds[b] = Encoding.UTF8.GetString(data, o, len);
                    o += len;
                }
            }
            int a = WorldChunks.Across(c.Size), count = a * a;
            if (o + 4 + 12L * count > data.Length) throw new FormatException("world file ends inside its chunk table");
            int mound = Next(data, ref o);
            if (mound < WorldBake.NotRaised || mound >= c.Size * c.Size) throw new FormatException($"world file mound {mound} outside the map");
            var bake = new WorldBake { Mound = mound, Sums = new ulong[count], Packed = data, At = new int[count], Len = new int[count] };
            long at = o + 12L * count;
            for (int k = 0; k < count; k++)
            {
                bake.Sums[k] = BitConverter.ToUInt64(data, o);
                int len = BitConverter.ToInt32(data, o + 8);
                o += 12;
                if (len <= 0 || at + len > data.Length) throw new FormatException($"world file chunk {k} has bad length {len}");
                bake.At[k] = (int)at;
                bake.Len[k] = len;
                at += len;
            }
            if (at + 4 > data.Length) throw new FormatException("world file ends before its ground section");
            o = (int)at;
            int groundLen = Next(data, ref o);
            if (groundLen < 0 || (long)o + groundLen != data.Length) throw new FormatException($"world file is {data.Length} bytes, header implies {(long)o + groundLen}");
            times?.Mark("table");
            var w = Empty(c, biomeIds, materials);
            times?.Mark("empty");
            var pool = new ConcurrentBag<byte[][]>();
            try
            {
                Parallel.For(0, count, () => pool.TryTake(out var u) ? u : new[] { new byte[Full], new byte[Full] }, (k, _, u) =>
                {
                    int cx = k % a, cy = k / a, bytes = WorldChunks.Bytes(c.Size, cx, cy);
                    ChunkZip.Inflate(data, bake.At[k], bake.Len[k], bytes, u[1], u[0]);
                    if (WorldChunks.Checksum(u[0], bytes) != bake.Sums[k]) throw new InvalidDataException($"chunk {cx},{cy} fails its checksum");
                    WorldChunks.Unpack(w, cx, cy, u[0], 0);
                    return u;
                }, pool.Add);
            }
            catch (AggregateException e)
            {
                var inner = e.Flatten().InnerException;
                throw new FormatException("world file chunk is corrupt: " + inner.Message, inner);
            }
            times?.Mark("chunks");
            var tint = Task.Run(() => WaterTint.Field(w));
            var groundTimes = times == null ? null : new LoadTimes();
            int groundAt = o;
            var ground = groundLen > 0 ? Task.Run(() => GroundBake.Read(data, groundAt, groundLen, groundTimes)) : null;
            Hydrology.Relink(w.Config, w.Height, w.Sea, w.Water, times);
            WaterTint.Apply(w, tint.GetAwaiter().GetResult());
            times?.Mark("tint");
            if (ground != null) bake.Ground = ground.GetAwaiter().GetResult();
            times?.Add("ground", groundTimes);
            w.Bake = bake;
            return w;
        }

        public static GeneratedWorld Empty(WorldConfig c, string[] biomeIds, MaterialLibrary materials)
        {
            int cells = c.Size * c.Size;
            var hy = new Hydrology
            {
                Receiver = new int[cells], WaterSurface = new float[cells], DischargeM3s = new float[cells],
                LakeId = new int[cells], IsRiver = new bool[cells],
            };
            for (int i = 0; i < cells; i++) { hy.Receiver[i] = i; hy.LakeId[i] = -1; }
            return new GeneratedWorld
            {
                Config = c, Sea = new bool[cells], Height = new float[cells], Water = hy,
                Climate = new ClimateField { TemperatureC = new float[cells], PrecipitationMm = new float[cells] },
                Biome = biomeIds == null ? null : new int[cells], BiomeIds = biomeIds, Materials = materials,
            };
        }

        static bool HasMagic(byte[] d) => d[0] == Magic[0] && d[1] == Magic[1] && d[2] == Magic[2] && d[3] == Magic[3];

        static void Int(List<byte> d, int v) { d.Add((byte)v); d.Add((byte)(v >> 8)); d.Add((byte)(v >> 16)); d.Add((byte)(v >> 24)); }

        static void Long(List<byte> d, ulong v) { Int(d, (int)(uint)v); Int(d, (int)(uint)(v >> 32)); }

        static int GetInt(byte[] d, int o) => d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24;

        static int Next(byte[] d, ref int o) { int v = GetInt(d, o); o += 4; return v; }
    }
}
