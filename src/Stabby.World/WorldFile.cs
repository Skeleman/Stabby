using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Stabby.World
{
    public static class WorldFile
    {
        public const int Version = 1, MaxSize = 8192, MaxBiomes = 1024, MaxIdBytes = 256;
        static readonly byte[] Magic = { (byte)'S', (byte)'T', (byte)'B', (byte)'W' };

        public static byte[] Write(GeneratedWorld w)
        {
            var c = w.Config;
            int a = WorldChunks.Across(c.Size);
            var head = new List<byte>(Magic);
            Int(head, Version); Int(head, WorldConfig.GeneratorVersion); Int(head, WorldChunks.Version);
            Long(head, c.Seed); Int(head, c.Size); Int(head, BitConverter.SingleToInt32Bits(c.CellMeters));
            Int(head, w.BiomeIds == null ? -1 : w.BiomeIds.Length);
            if (w.BiomeIds != null)
                foreach (var id in w.BiomeIds) { var b = Encoding.UTF8.GetBytes(id); Int(head, b.Length); head.AddRange(b); }
            var chunks = new byte[a * a][];
            long total = head.Count;
            for (int k = 0; k < chunks.Length; k++) { chunks[k] = WorldChunks.Pack(w, k % a, k / a); total += chunks[k].Length; }
            var data = new byte[total];
            head.CopyTo(data);
            int o = head.Count;
            foreach (var ch in chunks) { Buffer.BlockCopy(ch, 0, data, o, ch.Length); o += ch.Length; }
            return data;
        }

        public static int GeneratorVersionOf(byte[] data) => data.Length >= 12 && HasMagic(data) ? GetInt(data, 8) : -1;

        public static GeneratedWorld Read(byte[] data, MaterialLibrary materials)
        {
            if (data.Length < 36 || !HasMagic(data)) throw new FormatException("not a Stabby world file");
            int o = 4;
            int version = Next(data, ref o), gen = Next(data, ref o), chunkVersion = Next(data, ref o);
            if (version != Version || gen != WorldConfig.GeneratorVersion || chunkVersion != WorldChunks.Version)
                throw new FormatException($"world file v{version} generator {gen} chunks v{chunkVersion}, expected v{Version} generator {WorldConfig.GeneratorVersion} chunks v{WorldChunks.Version}");
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
            long expected = o + (long)c.Size * c.Size * WorldChunks.CellBytes;
            if (expected != data.Length) throw new FormatException($"world file is {data.Length} bytes, header implies {expected}");
            var w = Empty(c, biomeIds, materials);
            int a = WorldChunks.Across(c.Size);
            var at = new int[a * a];
            for (int k = 0; k < a * a; k++) { at[k] = o; o += WorldChunks.Bytes(c.Size, k % a, k / a); }
            Parallel.For(0, a * a, k => WorldChunks.Unpack(w, k % a, k / a, data, at[k]));
            WorldChunks.Relink(w);
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
