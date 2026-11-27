using System;
using System.IO;
using System.IO.Compression;

namespace Stabby.World
{
    public static class PlacedPack
    {
        const int Head = 36;
        const float Half = 0.70710678f;

        public static byte[] Pack(ObjectInstance[] objs, int chunk, out int rawBytes, out ulong sum)
        {
            int n = objs.Length;
            float x0 = 0, x1 = 0, y0 = 0, y1 = 0, z0 = 0, z1 = 0, s0 = 0, s1 = 0;
            if (n > 0)
            {
                x0 = x1 = objs[0].X; y0 = y1 = objs[0].Y; z0 = z1 = objs[0].Z; s0 = s1 = objs[0].Size;
                foreach (var o in objs)
                {
                    x0 = Math.Min(x0, o.X); x1 = Math.Max(x1, o.X);
                    y0 = Math.Min(y0, o.Y); y1 = Math.Max(y1, o.Y);
                    z0 = Math.Min(z0, o.Z); z1 = Math.Max(z1, o.Z);
                    s0 = Math.Min(s0, o.Size); s1 = Math.Max(s1, o.Size);
                }
            }
            var raw = new MemoryStream(Head + n * 24);
            var b = new byte[8];
            void Int(int v) { BitConverter.TryWriteBytes(b, v); raw.Write(b, 0, 4); }
            void Var(ulong v) { for (; v >= 0x80; v >>= 7) raw.WriteByte((byte)(v | 0x80)); raw.WriteByte((byte)v); }
            Int(n);
            foreach (var f in new[] { x0, x1, y0, y1, z0, z1, s0, s1 }) Int(BitConverter.SingleToInt32Bits(f));
            foreach (var o in objs)
            {
                if (o.Def < 0 || o.Def > 255) throw new InvalidOperationException($"object def {o.Def} does not fit a packed chunk");
                raw.WriteByte((byte)o.Def);
            }
            foreach (var o in objs) Var((ulong)(((long)o.Variant << 1) ^ ((long)o.Variant >> 63)));
            foreach (var o in objs)
            {
                bool home = o.Id >> 32 == ((ulong)(uint)chunk << 8 | (uint)o.Def);
                Var(home ? (ulong)(uint)o.Id << 1 : 1);
            }
            foreach (var o in objs)
                if (o.Id >> 32 != ((ulong)(uint)chunk << 8 | (uint)o.Def)) { BitConverter.TryWriteBytes(b, o.Id); raw.Write(b, 0, 8); }
            var q = new ushort[n];
            void Column(Func<ObjectInstance, float> get, float lo, float hi)
            {
                float step = hi > lo ? 65535f / (hi - lo) : 0;
                for (int i = 0; i < n; i++) q[i] = (ushort)Math.Max(0, Math.Min(65535, (int)Math.Round((get(objs[i]) - lo) * step)));
                for (int i = 0; i < n; i++) raw.WriteByte((byte)q[i]);
                for (int i = 0; i < n; i++) raw.WriteByte((byte)(q[i] >> 8));
            }
            Column(o => o.X, x0, x1);
            Column(o => o.Y, y0, y1);
            Column(o => o.Z, z0, z1);
            Column(o => o.Size, s0, s1);
            var r = new uint[n];
            for (int i = 0; i < n; i++) r[i] = Rotation(objs[i]);
            for (int s = 0; s < 32; s += 8)
                for (int i = 0; i < n; i++) raw.WriteByte((byte)(r[i] >> s));
            rawBytes = (int)raw.Length;
            sum = WorldChunks.Checksum(raw.GetBuffer(), rawBytes);
            var m = new MemoryStream(rawBytes / 2 + 16);
            using (var z = new DeflateStream(m, CompressionLevel.Optimal, true)) z.Write(raw.GetBuffer(), 0, rawBytes);
            return m.ToArray();
        }

        public static ObjectInstance[] TryUnpack(byte[] pack, int at, int count, int rawBytes, ulong sum, int chunk)
        {
            try { return Unpack(pack, at, count, rawBytes, sum, chunk); }
            catch (InvalidDataException) { return null; }
        }

        static ObjectInstance[] Unpack(byte[] pack, int at, int count, int rawBytes, ulong sum, int chunk)
        {
            if (rawBytes < Head) throw new InvalidDataException($"packed chunk {chunk} is {rawBytes} bytes");
            var d = new byte[rawBytes];
            int got = 0;
            using (var z = new DeflateStream(new MemoryStream(pack, at, count, false), CompressionMode.Decompress))
            {
                for (int g; got < rawBytes && (g = z.Read(d, got, rawBytes - got)) > 0;) got += g;
                if (got < rawBytes || z.ReadByte() >= 0) throw new InvalidDataException($"packed chunk {chunk} inflates to the wrong length");
            }
            if (WorldChunks.Checksum(d, rawBytes) != sum) throw new InvalidDataException($"packed chunk {chunk} fails its checksum");
            int p = 0;
            int Int() { if (p + 4 > rawBytes) throw new InvalidDataException($"packed chunk {chunk} ends early"); int v = BitConverter.ToInt32(d, p); p += 4; return v; }
            float Float() => BitConverter.Int32BitsToSingle(Int());
            ulong Var()
            {
                ulong v = 0;
                for (int s = 0; ; s += 7)
                {
                    if (p >= rawBytes || s > 63) throw new InvalidDataException($"packed chunk {chunk} has a bad number");
                    byte c = d[p++];
                    v |= (ulong)(c & 0x7f) << s;
                    if (c < 0x80) return v;
                }
            }
            int n = Int();
            if (n < 0 || n > rawBytes) throw new InvalidDataException($"packed chunk {chunk} claims {n} objects");
            float x0 = Float(), x1 = Float(), y0 = Float(), y1 = Float(), z0 = Float(), z1 = Float(), s0 = Float(), s1 = Float();
            var objs = new ObjectInstance[n];
            if (p + n > rawBytes) throw new InvalidDataException($"packed chunk {chunk} ends early");
            for (int i = 0; i < n; i++) objs[i].Def = d[p++];
            for (int i = 0; i < n; i++) { ulong v = Var(); objs[i].Variant = (int)((long)(v >> 1) ^ -(long)(v & 1)); }
            var ids = new ulong[n];
            for (int i = 0; i < n; i++) ids[i] = Var();
            for (int i = 0; i < n; i++)
            {
                if (ids[i] == 1)
                {
                    if (p + 8 > rawBytes) throw new InvalidDataException($"packed chunk {chunk} ends early");
                    objs[i].Id = BitConverter.ToUInt64(d, p);
                    p += 8;
                }
                else objs[i].Id = ObjectIds.Make(chunk, objs[i].Def, (int)(uint)(ids[i] >> 1));
            }
            if (p + n * 12 != rawBytes) throw new InvalidDataException($"packed chunk {chunk} has {rawBytes - p} bytes for {n} objects");
            float Value(int i, float lo, float hi) => lo + (d[p + i] | d[p + n + i] << 8) * ((hi - lo) / 65535f);
            for (int i = 0; i < n; i++) objs[i].X = Value(i, x0, x1);
            p += 2 * n;
            for (int i = 0; i < n; i++) objs[i].Y = Value(i, y0, y1);
            p += 2 * n;
            for (int i = 0; i < n; i++) objs[i].Z = Value(i, z0, z1);
            p += 2 * n;
            for (int i = 0; i < n; i++) objs[i].Size = Value(i, s0, s1);
            p += 2 * n;
            for (int i = 0; i < n; i++)
                Rotation(ref objs[i], (uint)(d[p + i] | d[p + n + i] << 8 | d[p + 2 * n + i] << 16 | d[p + 3 * n + i] << 24));
            return objs;
        }

        static uint Rotation(ObjectInstance o)
        {
            if (o.Qx == 0 && o.Qy == 0 && o.Qz == 0 && o.Qw == 0) return uint.MaxValue;
            Span<float> c = stackalloc float[4];
            c[0] = o.Qx; c[1] = o.Qy; c[2] = o.Qz; c[3] = o.Qw;
            int big = 0;
            for (int k = 1; k < 4; k++) if (Math.Abs(c[k]) > Math.Abs(c[big])) big = k;
            float sign = c[big] < 0 ? -1 : 1;
            uint r = (uint)big;
            for (int k = 0, s = 2; k < 4; k++)
            {
                if (k == big) continue;
                int v = (int)Math.Round((c[k] * sign / Half + 1) * 511.5f);
                r |= (uint)Math.Max(0, Math.Min(1023, v)) << s;
                s += 10;
            }
            return r;
        }

        static void Rotation(ref ObjectInstance o, uint r)
        {
            if (r == uint.MaxValue) { o.Qx = o.Qy = o.Qz = o.Qw = 0; return; }
            int big = (int)(r & 3);
            Span<float> c = stackalloc float[4];
            float sum = 0;
            for (int k = 0, s = 2; k < 4; k++)
            {
                if (k == big) continue;
                c[k] = (((r >> s) & 1023) / 511.5f - 1) * Half;
                sum += c[k] * c[k];
                s += 10;
            }
            c[big] = (float)Math.Sqrt(Math.Max(0, 1 - sum));
            o.Qx = c[0]; o.Qy = c[1]; o.Qz = c[2]; o.Qw = c[3];
        }
    }
}
