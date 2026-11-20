using System.IO.Compression;

namespace Stabby.Cli;

static class Png
{
    static readonly uint[] Crc = BuildCrc();

    static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static void Write(string path, int width, int height, uint[] rgb)
    {
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)width); Be(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(fs, "IHDR", ihdr);

        var raw = new byte[height * (width * 3 + 1)];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < width; x++)
            {
                uint c = rgb[y * width + x];
                raw[o++] = (byte)(c >> 16); raw[o++] = (byte)(c >> 8); raw[o++] = (byte)c;
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", []);
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, (uint)data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        data.CopyTo(td, 4);
        s.Write(td);
        uint c = 0xFFFFFFFFu;
        foreach (byte b in td) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        var crc = new byte[4]; Be(crc, 0, c ^ 0xFFFFFFFFu); s.Write(crc);
    }

    static void Be(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
}
