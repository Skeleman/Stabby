using System.IO;
using System.IO.Compression;

namespace Stabby.World
{
    public static class ChunkZip
    {
        public static byte[] Deflate(byte[] raw) => Deflate(raw, raw.Length);

        public static byte[] Deflate(byte[] raw, int length)
        {
            var m = new MemoryStream(length / 2);
            Deflate(raw, length, m);
            return m.ToArray();
        }

        public static int Deflate(byte[] raw, int length, MemoryStream into)
        {
            long start = into.Length;
            into.Position = start;
            using (var z = new DeflateStream(into, CompressionLevel.Optimal, true)) z.Write(raw, 0, length);
            return (int)(into.Length - start);
        }

        public static byte[] Inflate(byte[] packed, int bytes)
        {
            var raw = new byte[bytes];
            Inflate(packed, 0, packed.Length, bytes, raw);
            return raw;
        }

        public static void Inflate(byte[] packed, int at, int count, int bytes, byte[] raw)
        {
            int got = 0;
            using (var z = new DeflateStream(new MemoryStream(packed, at, count, false), CompressionMode.Decompress))
            {
                for (int r; got < bytes && (r = z.Read(raw, got, bytes - got)) > 0;) got += r;
                if (got < bytes) throw new InvalidDataException($"chunk inflates to {got} of {bytes} bytes");
                if (z.ReadByte() >= 0) throw new InvalidDataException($"chunk inflates past {bytes} bytes");
            }
        }
    }
}
