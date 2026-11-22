using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Stabby.World
{
    public static class WorldChunks
    {
        public const int Size = 64;
        public const int Version = 1;
        public const int CellBytes = 32;
        const byte SeaFlag = 1, RiverFlag = 2;

        public static int Across(int n) => (n + Size - 1) / Size;

        public static ulong[] Checksums(GeneratedWorld w)
        {
            int a = Across(w.Config.Size);
            var sums = new ulong[a * a];
            var pool = new ConcurrentBag<byte[]>();
            Parallel.For(0, a, () => pool.TryTake(out var b) ? b : new byte[Size * Size * CellBytes], (cy, _, buf) =>
            {
                for (int cx = 0; cx < a; cx++) sums[cy * a + cx] = Checksum(buf, Pack(w, cx, cy, buf, 0));
                return buf;
            }, pool.Add);
            return sums;
        }

        public static ulong[] EmptyChecksums(int n, bool biomes)
        {
            var tile = WorldFile.Empty(new WorldConfig { Size = Size, CellMeters = 1 }, biomes ? Array.Empty<string>() : null, null);
            var buf = new byte[Size * Size * CellBytes];
            Pack(tile, 0, 0, buf, 0);
            int a = Across(n);
            var sums = new ulong[a * a];
            int lastLength = -1;
            ulong last = 0;
            for (int k = 0; k < sums.Length; k++)
            {
                int length = Bytes(n, k % a, k / a);
                if (length != lastLength) { last = Checksum(buf, length); lastLength = length; }
                sums[k] = last;
            }
            return sums;
        }

        public static ulong Checksum(byte[] data) => Checksum(data, data.Length);

        public static ulong Checksum(byte[] data, int length)
        {
            ulong h = 14695981039346656037UL ^ Version;
            for (int k = 0; k < length; k++) { h ^= data[k]; h *= 1099511628211UL; }
            return h;
        }

        public static byte[] Pack(GeneratedWorld w, int cx, int cy)
        {
            var data = new byte[Bytes(w.Config.Size, cx, cy)];
            Pack(w, cx, cy, data, 0);
            return data;
        }

        public static int Pack(GeneratedWorld w, int cx, int cy, byte[] data, int at)
        {
            int n = w.Config.Size, o = at;
            var hy = w.Water;
            int x1 = Math.Min(n, (cx + 1) * Size), y1 = Math.Min(n, (cy + 1) * Size);
            for (int y = cy * Size; y < y1; y++)
                for (int i = y * n + cx * Size, e = y * n + x1; i < e; i++)
                {
                    PutFloat(data, ref o, w.Height[i]);
                    PutFloat(data, ref o, hy.WaterSurface[i]);
                    PutFloat(data, ref o, hy.DischargeM3s[i]);
                    PutFloat(data, ref o, w.Climate.TemperatureC[i]);
                    PutFloat(data, ref o, w.Climate.PrecipitationMm[i]);
                    PutInt(data, ref o, hy.LakeId[i]);
                    PutInt(data, ref o, w.Biome != null ? w.Biome[i] : -1);
                    data[o++] = Direction(n, i, hy.Receiver[i]);
                    data[o++] = (byte)((w.Sea[i] ? SeaFlag : 0) | (hy.IsRiver[i] ? RiverFlag : 0));
                    data[o++] = 0; data[o++] = 0;
                }
            return o - at;
        }

        public static void Unpack(GeneratedWorld w, int cx, int cy, byte[] data)
        {
            if (data.Length != Bytes(w.Config.Size, cx, cy)) throw new ArgumentException($"chunk {cx},{cy} has {data.Length} bytes");
            Unpack(w, cx, cy, data, 0);
        }

        public static void Unpack(GeneratedWorld w, int cx, int cy, byte[] data, int at)
        {
            int n = w.Config.Size, o = at;
            var hy = w.Water;
            int x1 = Math.Min(n, (cx + 1) * Size), y1 = Math.Min(n, (cy + 1) * Size);
            for (int y = cy * Size; y < y1; y++)
                for (int i = y * n + cx * Size, e = y * n + x1; i < e; i++)
                {
                    w.Height[i] = GetFloat(data, ref o);
                    hy.WaterSurface[i] = GetFloat(data, ref o);
                    hy.DischargeM3s[i] = GetFloat(data, ref o);
                    w.Climate.TemperatureC[i] = GetFloat(data, ref o);
                    w.Climate.PrecipitationMm[i] = GetFloat(data, ref o);
                    hy.LakeId[i] = GetInt(data, ref o);
                    int biome = GetInt(data, ref o);
                    if (w.Biome != null) w.Biome[i] = biome;
                    hy.Receiver[i] = Neighbour(n, i, data[o++]);
                    byte flags = data[o++];
                    w.Sea[i] = (flags & SeaFlag) != 0;
                    hy.IsRiver[i] = (flags & RiverFlag) != 0;
                    o += 2;
                }
        }

        public static void Relink(GeneratedWorld w) => Hydrology.Relink(w.Config, w.Height, w.Sea, w.Water);

        public static int Bytes(int n, int cx, int cy) => Cells(n, cx, cy) * CellBytes;

        static int Cells(int n, int cx, int cy) => (Math.Min(n, (cx + 1) * Size) - cx * Size) * (Math.Min(n, (cy + 1) * Size) - cy * Size);

        static byte Direction(int n, int i, int r)
        {
            if (r == i) return 8;
            for (int k = 0; k < 8; k++)
                if (r == i + Grid.DY[k] * n + Grid.DX[k] && Grid.Adjacent(n, i, r)) return (byte)k;
            throw new InvalidOperationException($"cell {i} drains to non-neighbour {r}");
        }

        static int Neighbour(int n, int i, byte k) => k == 8 ? i : i + Grid.DY[k] * n + Grid.DX[k];

        static void PutInt(byte[] d, ref int o, int v)
        {
            d[o++] = (byte)v; d[o++] = (byte)(v >> 8); d[o++] = (byte)(v >> 16); d[o++] = (byte)(v >> 24);
        }

        static int GetInt(byte[] d, ref int o)
        {
            int v = d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24;
            o += 4;
            return v;
        }

        static void PutFloat(byte[] d, ref int o, float v) => PutInt(d, ref o, BitConverter.SingleToInt32Bits(v));

        static float GetFloat(byte[] d, ref int o) => BitConverter.Int32BitsToSingle(GetInt(d, ref o));
    }
}
