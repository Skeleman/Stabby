using System.IO;
using System.IO.Compression;

namespace Stabby.Game
{
    public static class ChunkZip
    {
        public static byte[] Deflate(byte[] raw)
        {
            var m = new MemoryStream();
            using (var z = new DeflateStream(m, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
            return m.ToArray();
        }

        public static byte[] Inflate(byte[] packed, int maxBytes)
        {
            var m = new MemoryStream();
            var buf = new byte[16384];
            using (var z = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
                for (int r; (r = z.Read(buf, 0, buf.Length)) > 0;)
                {
                    if (m.Length + r > maxBytes) throw new InvalidDataException($"chunk inflates past {maxBytes} bytes");
                    m.Write(buf, 0, r);
                }
            return m.ToArray();
        }
    }
}
