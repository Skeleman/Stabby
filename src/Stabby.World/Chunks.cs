using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace Stabby.World
{
    public static class WorldChunks
    {
        public const int Size = 64;
        public const int Version = 2;
        public const int HeadBytes = 8, CellBytes = 10, NodeBytes = 2;
        public const int MaxBytes = HeadBytes + Size * Size * (CellBytes + NodeBytes);
        public const float ClimateMeters = 45f;
        const int SeaFlag = 16, RiverFlag = 32, DirectionMask = 15;

        [ThreadStatic] static float[] nodeT, nodeP;

        public static int Across(int n) => (n + Size - 1) / Size;

        public static int ClimateStride(float cellMeters) => Math.Max(1, (int)Math.Floor(ClimateMeters / cellMeters + 0.5f));

        public static int FitHeights(GeneratedWorld w)
        {
            int n = w.Config.Size, a = Across(n);
            var lo = new float[a * a];
            var hi = new float[a * a];
            Parallel.For(0, a * a, k =>
            {
                float l = float.MaxValue, h = float.MinValue;
                int x0 = k % a * Size, y0 = k / a * Size, x1 = Math.Min(n, x0 + Size), y1 = Math.Min(n, y0 + Size);
                for (int y = y0; y < y1; y++)
                    for (int i = y * n + x0, e = y * n + x1; i < e; i++)
                    {
                        float g = w.Height[i], s = w.Water.WaterSurface[i];
                        if (g < l) l = g;
                        if (g > h) h = g;
                        if (s < l) l = s;
                        if (s > h) h = s;
                    }
                lo[k] = l;
                hi[k] = h;
            });
            for (int scale = 0; scale <= ChunkCodes.MaxHeightScale; scale++)
            {
                double step = ChunkCodes.Step(scale);
                bool fits = true;
                for (int k = 0; k < lo.Length && fits; k++) fits = ChunkCodes.Height(hi[k], step) - ChunkCodes.Height(lo[k], step) <= 0xFFFF;
                if (fits) { w.HeightScale = scale; return scale; }
            }
            throw new InvalidOperationException("world heights span too far to store in 16 bits per chunk");
        }

        public static ulong[] Checksums(GeneratedWorld w)
        {
            FitHeights(w);
            int a = Across(w.Config.Size);
            var sums = new ulong[a * a];
            var pool = new ConcurrentBag<byte[]>();
            Parallel.For(0, a, () => pool.TryTake(out var b) ? b : new byte[MaxBytes], (cy, _, buf) =>
            {
                for (int cx = 0; cx < a; cx++) sums[cy * a + cx] = Checksum(buf, Pack(w, cx, cy, buf, 0));
                return buf;
            }, pool.Add);
            return sums;
        }

        public static ulong[] EmptyChecksums(int n, float cellMeters, bool biomes)
        {
            int a = Across(n), f = ClimateStride(cellMeters);
            var sums = new ulong[a * a];
            var buf = new byte[MaxBytes];
            int lastLength = -1;
            ulong last = 0;
            for (int k = 0; k < sums.Length; k++)
            {
                int cells = Cells(n, k % a, k / a), nodes = Nodes(n, f, k % a, k / a), length = HeadBytes + cells * CellBytes + nodes * NodeBytes;
                if (length != lastLength) { last = Checksum(buf, EmptyChunk(buf, cells, nodes, biomes)); lastLength = length; }
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
            var data = new byte[Bytes(w.Config, cx, cy)];
            Pack(w, cx, cy, data, 0);
            return data;
        }

        public static int Pack(GeneratedWorld w, int cx, int cy, byte[] data, int at)
        {
            var c = w.Config;
            int n = c.Size, scale = w.HeightScale >= 0 ? w.HeightScale : FitHeights(w);
            double step = ChunkCodes.Step(scale);
            var hy = w.Water;
            int x0 = cx * Size, y0 = cy * Size, x1 = Math.Min(n, x0 + Size), y1 = Math.Min(n, y0 + Size), cells = (x1 - x0) * (y1 - y0);
            long low = long.MaxValue;
            for (int y = y0; y < y1; y++)
                for (int i = y * n + x0, e = y * n + x1; i < e; i++)
                    low = Math.Min(low, Math.Min(ChunkCodes.Height(w.Height[i], step), ChunkCodes.Height(hy.WaterSurface[i], step)));
            if (low < int.MinValue || low > int.MaxValue) throw new InvalidOperationException($"chunk {cx},{cy} heights are out of range");
            PutInt(data, at, (int)low);
            PutInt(data, at + 4, scale);
            int hp = at + HeadBytes, wp = hp + 2 * cells, dp = wp + 2 * cells, lp = dp + 2 * cells, bp = lp + 2 * cells, fp = bp + cells, np = fp + cells;
            int ph = 0, pw = 0, j = 0;
            for (int y = y0; y < y1; y++)
                for (int i = y * n + x0, e = y * n + x1; i < e; i++, j++)
                {
                    float g = w.Height[i], s = hy.WaterSurface[i];
                    int hc = Code16(ChunkCodes.Height(g, step) - low, cx, cy), wc = Code16(ChunkCodes.Height(s, step) - low, cx, cy);
                    if (wc < hc && s >= g - WorldChecks.SurfaceTolerance) wc = hc;
                    Put16(data, hp, cells, j, hc - ph);
                    Put16(data, wp, cells, j, wc - pw);
                    ph = hc;
                    pw = wc;
                    Put16(data, dp, cells, j, ChunkCodes.Nearest(ChunkCodes.Discharge, hy.DischargeM3s[i]));
                    int lake = hy.LakeId[i];
                    if (lake >= ChunkCodes.NoLake) throw new InvalidOperationException($"lake id {lake} does not fit 16 bits");
                    Put16(data, lp, cells, j, lake < 0 ? ChunkCodes.NoLake : lake);
                    int biome = w.Biome != null ? w.Biome[i] : -1;
                    if (biome >= ChunkCodes.NoBiome) throw new InvalidOperationException($"biome {biome} does not fit a byte");
                    data[bp + j] = (byte)(biome < 0 ? ChunkCodes.NoBiome : biome);
                    data[fp + j] = (byte)(Direction(n, i, hy.Receiver[i]) | (w.Sea[i] ? SeaFlag : 0) | (hy.IsRiver[i] ? RiverFlag : 0));
                }
            int f = ClimateStride(c.CellMeters), jx0 = x0 / f, jy0 = y0 / f, jx1 = (x1 - 1 + f - 1) / f, jy1 = (y1 - 1 + f - 1) / f;
            int nodes = (jx1 - jx0 + 1) * (jy1 - jy0 + 1), k = 0;
            for (int jy = jy0; jy <= jy1; jy++)
                for (int jx = jx0; jx <= jx1; jx++, k++)
                {
                    int p = Math.Min(f * jy, n - 1) * n + Math.Min(f * jx, n - 1);
                    data[np + k] = (byte)ChunkCodes.Nearest(ChunkCodes.Temperature, w.Climate.TemperatureC[p]);
                    data[np + nodes + k] = (byte)ChunkCodes.Nearest(ChunkCodes.Precipitation, w.Climate.PrecipitationMm[p]);
                }
            return np + 2 * nodes - at;
        }

        public static void Unpack(GeneratedWorld w, int cx, int cy, byte[] data)
        {
            if (data.Length != Bytes(w.Config, cx, cy)) throw new ArgumentException($"chunk {cx},{cy} has {data.Length} bytes");
            Unpack(w, cx, cy, data, 0);
        }

        public static void Unpack(GeneratedWorld w, int cx, int cy, byte[] data, int at)
        {
            var c = w.Config;
            int n = c.Size;
            long low = GetInt(data, at);
            int scale = GetInt(data, at + 4);
            if (scale < 0 || scale > ChunkCodes.MaxHeightScale) throw new InvalidDataException($"chunk {cx},{cy} has height scale {scale}");
            w.HeightScale = scale;
            double step = ChunkCodes.Step(scale);
            var hy = w.Water;
            int x0 = cx * Size, y0 = cy * Size, x1 = Math.Min(n, x0 + Size), y1 = Math.Min(n, y0 + Size), cells = (x1 - x0) * (y1 - y0);
            int hp = at + HeadBytes, wp = hp + 2 * cells, dp = wp + 2 * cells, lp = dp + 2 * cells, bp = lp + 2 * cells, fp = bp + cells, np = fp + cells;
            int ph = 0, pw = 0, j = 0;
            var discharge = ChunkCodes.Discharge;
            for (int y = y0; y < y1; y++)
                for (int i = y * n + x0, e = y * n + x1; i < e; i++, j++)
                {
                    ph = (ph + Get16(data, hp, cells, j)) & 0xFFFF;
                    pw = (pw + Get16(data, wp, cells, j)) & 0xFFFF;
                    w.Height[i] = (float)((low + ph) * step);
                    hy.WaterSurface[i] = (float)((low + pw) * step);
                    hy.DischargeM3s[i] = discharge[Get16(data, dp, cells, j)];
                    int lake = Get16(data, lp, cells, j);
                    hy.LakeId[i] = lake == ChunkCodes.NoLake ? -1 : lake;
                    if (w.Biome != null) { int b = data[bp + j]; w.Biome[i] = b == ChunkCodes.NoBiome ? -1 : b; }
                    int flags = data[fp + j], dir = flags & DirectionMask;
                    if (dir > 8) throw new InvalidDataException($"chunk {cx},{cy} has flow direction {dir}");
                    hy.Receiver[i] = Neighbour(n, i, dir);
                    w.Sea[i] = (flags & SeaFlag) != 0;
                    hy.IsRiver[i] = (flags & RiverFlag) != 0;
                }
            int f = ClimateStride(c.CellMeters), jx0 = x0 / f, jy0 = y0 / f, jx1 = (x1 - 1 + f - 1) / f, jy1 = (y1 - 1 + f - 1) / f;
            int across = jx1 - jx0 + 1, nodes = across * (jy1 - jy0 + 1);
            if (nodeT == null) { nodeT = new float[Size * Size]; nodeP = new float[Size * Size]; }
            var t = nodeT;
            var p = nodeP;
            for (int k = 0; k < nodes; k++) { t[k] = ChunkCodes.Temperature[data[np + k]]; p[k] = ChunkCodes.Precipitation[data[np + nodes + k]]; }
            var temperature = w.Climate.TemperatureC;
            var rain = w.Climate.PrecipitationMm;
            for (int y = y0; y < y1; y++)
            {
                int ky = y / f, py = f * ky, ry = (ky - jy0) * across, ry1 = y == py ? ry : ry + across;
                float ty = y == py ? 0 : (float)(y - py) / (Math.Min(py + f, n - 1) - py);
                for (int x = x0; x < x1; x++)
                {
                    int kx = x / f, px = f * kx, a0 = kx - jx0, a1 = x == px ? a0 : a0 + 1;
                    float tx = x == px ? 0 : (float)(x - px) / (Math.Min(px + f, n - 1) - px);
                    int i = y * n + x;
                    temperature[i] = Bilinear(t, ry + a0, ry + a1, ry1 + a0, ry1 + a1, tx, ty);
                    rain[i] = Bilinear(p, ry + a0, ry + a1, ry1 + a0, ry1 + a1, tx, ty);
                }
            }
        }

        public static void Relink(GeneratedWorld w, LoadTimes times = null)
        {
            Hydrology.Relink(w.Config, w.Height, w.Sea, w.Water, times);
            WaterTint.Apply(w);
            times?.Mark("tint");
        }

        public static int Bytes(WorldConfig c, int cx, int cy) => Bytes(c.Size, c.CellMeters, cx, cy);

        public static int Bytes(int n, float cellMeters, int cx, int cy) => HeadBytes + Cells(n, cx, cy) * CellBytes + Nodes(n, ClimateStride(cellMeters), cx, cy) * NodeBytes;

        static int Cells(int n, int cx, int cy) => (Math.Min(n, (cx + 1) * Size) - cx * Size) * (Math.Min(n, (cy + 1) * Size) - cy * Size);

        static int Nodes(int n, int f, int cx, int cy)
        {
            int x0 = cx * Size, y0 = cy * Size, x1 = Math.Min(n, x0 + Size), y1 = Math.Min(n, y0 + Size);
            return ((x1 - 1 + f - 1) / f - x0 / f + 1) * ((y1 - 1 + f - 1) / f - y0 / f + 1);
        }

        static int EmptyChunk(byte[] d, int cells, int nodes, bool biomes)
        {
            int length = HeadBytes + cells * CellBytes + nodes * NodeBytes;
            Array.Clear(d, 0, length);
            int lp = HeadBytes + 6 * cells, bp = lp + 2 * cells, fp = bp + cells, np = fp + cells;
            for (int j = 0; j < cells; j++)
            {
                Put16(d, lp, cells, j, ChunkCodes.NoLake);
                d[bp + j] = (byte)(biomes ? 0 : ChunkCodes.NoBiome);
                d[fp + j] = 8;
            }
            byte t = (byte)ChunkCodes.Nearest(ChunkCodes.Temperature, 0), r = (byte)ChunkCodes.Nearest(ChunkCodes.Precipitation, 0);
            for (int k = 0; k < nodes; k++) { d[np + k] = t; d[np + nodes + k] = r; }
            return length;
        }

        static float Bilinear(float[] v, int a, int b, int c, int d, float tx, float ty)
        {
            float top = v[a] + (v[b] - v[a]) * tx, bottom = v[c] + (v[d] - v[c]) * tx;
            return top + (bottom - top) * ty;
        }

        static int Code16(long v, int cx, int cy)
        {
            if (v < 0 || v > 0xFFFF) throw new InvalidOperationException($"chunk {cx},{cy} height code {v} outside 16 bits");
            return (int)v;
        }

        static void Put16(byte[] d, int plane, int cells, int j, int v) { d[plane + j] = (byte)v; d[plane + cells + j] = (byte)(v >> 8); }

        static int Get16(byte[] d, int plane, int cells, int j) => d[plane + j] | d[plane + cells + j] << 8;

        static int Direction(int n, int i, int r)
        {
            if (r == i) return 8;
            for (int k = 0; k < 8; k++)
                if (r == i + Grid.DY[k] * n + Grid.DX[k] && Grid.Adjacent(n, i, r)) return k;
            throw new InvalidOperationException($"cell {i} drains to non-neighbour {r}");
        }

        static int Neighbour(int n, int i, int k) => k == 8 ? i : i + Grid.DY[k] * n + Grid.DX[k];

        static void PutInt(byte[] d, int o, int v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24); }

        static int GetInt(byte[] d, int o) => d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24;
    }
}
