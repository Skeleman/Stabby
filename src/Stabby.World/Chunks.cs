using System;

namespace Stabby.World
{
    // A shared world is seed + generator version + sketch. Every peer generates it,
    // compares per-chunk checksums with the host and receives only chunks that differ,
    // so peers need not be bitwise deterministic. Chunks hold raw float bits: a chunk
    // either matches exactly or is replaced exactly, so values never mix within a lake.
    // Lakes and rivers span chunks; Relink rebuilds them from the patched cells.
    public static class WorldChunks
    {
        public const int Size = 64;
        public const int Version = 1;
        const int CellBytes = 32;
        const byte SeaFlag = 1, RiverFlag = 2;

        public static int Across(int n) => (n + Size - 1) / Size;

        public static ulong[] Checksums(GeneratedWorld w)
        {
            int a = Across(w.Config.Size);
            var sums = new ulong[a * a];
            for (int cy = 0; cy < a; cy++)
                for (int cx = 0; cx < a; cx++)
                    sums[cy * a + cx] = Checksum(Pack(w, cx, cy));
            return sums;
        }

        public static ulong Checksum(byte[] data)
        {
            ulong h = 14695981039346656037UL ^ Version;
            foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
            return h;
        }

        public static byte[] Pack(GeneratedWorld w, int cx, int cy)
        {
            int n = w.Config.Size;
            var hy = w.Water;
            var data = new byte[Cells(n, cx, cy) * CellBytes];
            int o = 0;
            ForCells(n, cx, cy, i =>
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
            });
            return data;
        }

        // Call Relink once after the last chunk of a batch.
        public static void Unpack(GeneratedWorld w, int cx, int cy, byte[] data)
        {
            int n = w.Config.Size;
            if (data.Length != Cells(n, cx, cy) * CellBytes) throw new ArgumentException($"chunk {cx},{cy} has {data.Length} bytes");
            var hy = w.Water;
            int o = 0;
            ForCells(n, cx, cy, i =>
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
            });
        }

        public static void Relink(GeneratedWorld w) => Hydrology.Relink(w.Config, w.Sea, w.Water);

        static int Cells(int n, int cx, int cy) => (Math.Min(n, (cx + 1) * Size) - cx * Size) * (Math.Min(n, (cy + 1) * Size) - cy * Size);

        static void ForCells(int n, int cx, int cy, Action<int> f)
        {
            for (int y = cy * Size; y < Math.Min(n, (cy + 1) * Size); y++)
                for (int x = cx * Size; x < Math.Min(n, (cx + 1) * Size); x++)
                    f(y * n + x);
        }

        // Receivers are always a D8 neighbour or the cell itself (8).
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
