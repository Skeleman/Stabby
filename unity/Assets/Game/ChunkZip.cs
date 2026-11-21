using System.IO;
using System.IO.Compression;
using Stabby.World;

namespace Stabby.Game
{
    public static class ChunkZip
    {
        const int Stride = WorldChunks.CellBytes;

        public static byte[] Deflate(byte[] raw) => Deflate(raw, raw.Length, new byte[raw.Length]);

        public static byte[] Deflate(byte[] raw, int length, byte[] scratch)
        {
            var m = new MemoryStream(length / 2);
            Deflate(raw, length, scratch, m);
            return m.ToArray();
        }

        public static int Deflate(byte[] raw, int length, byte[] scratch, MemoryStream into)
        {
            if (length % Stride != 0) throw new InvalidDataException($"chunk of {length} bytes is not whole cells");
            int cells = length / Stride;
            for (int c = 0; c < cells; c++)
                for (int b = 0, s = c * Stride; b < Stride; b++) scratch[b * cells + c] = raw[s + b];
            long start = into.Length;
            into.Position = start;
            using (var z = new DeflateStream(into, CompressionLevel.Optimal, true)) z.Write(scratch, 0, length);
            return (int)(into.Length - start);
        }

        public static byte[] Inflate(byte[] packed, int bytes)
        {
            var raw = new byte[bytes];
            Inflate(packed, 0, packed.Length, bytes, new byte[bytes], raw);
            return raw;
        }

        public static void Inflate(byte[] packed, int at, int count, int bytes, byte[] scratch, byte[] raw)
        {
            if (bytes % Stride != 0) throw new InvalidDataException($"chunk of {bytes} bytes is not whole cells");
            int got = 0;
            using (var z = new DeflateStream(new MemoryStream(packed, at, count, false), CompressionMode.Decompress))
            {
                for (int r; got < bytes && (r = z.Read(scratch, got, bytes - got)) > 0;) got += r;
                if (got < bytes) throw new InvalidDataException($"chunk inflates to {got} of {bytes} bytes");
                if (z.ReadByte() >= 0) throw new InvalidDataException($"chunk inflates past {bytes} bytes");
            }
            int cells = bytes / Stride;
            for (int c = 0; c < cells; c++)
                for (int b = 0, s = c * Stride; b < Stride; b++) raw[s + b] = scratch[b * cells + c];
        }
    }
}
