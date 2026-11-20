using Stabby.World;

namespace Stabby.Cli;

static class Render
{
    const uint SeaColor = 0x2E5E8C, LakeColor = 0x3F7FB5, RiverColor = 0x2F6FD0;

    public static (int w, int h, uint[] px) Map(GeneratedWorld w, int scale) =>
        Draw(w, scale, i => Hypsometric(w.Height[i]), true);

    public static (int w, int h, uint[] px) BiomeMap(GeneratedWorld w, IReadOnlyList<BiomeDef> defs, int scale) =>
        Draw(w, scale, i => w.Biome[i] >= 0 ? defs[w.Biome[i]].Color : 0xFF00FF, true);

    public static (int w, int h, uint[] px) Precipitation(GeneratedWorld w, int scale) =>
        Draw(w, scale, i =>
        {
            if (w.Sea[i]) return 0x2A3B4C;
            float t = Math.Min(1f, w.Climate.PrecipitationMm[i] / 3000f);
            return Lerp(0xC9A66B, 0x1F5FA8, (float)Math.Sqrt(t));
        }, false, waterOverlay: false);

    public static (int w, int h, uint[] px) SketchMap(GeneratedWorld w, int scale)
    {
        var img = Draw(w, scale, i => w.Sketch.Land[i] ? Lerp(0x8FA87A, 0x6B4E3A, w.Sketch.Uplift[i]) : SeaColor, false, waterOverlay: false);
        int n = w.Config.Size;
        foreach (var r in w.Sketch.Ranges)
            for (int k = 0; k + 1 < r.X.Length; k++)
                Line(img, n, scale, r.X[k] + 0.5f, r.Y[k] + 0.5f, r.X[k + 1] + 0.5f, r.Y[k + 1] + 0.5f, 2.5f, 0xC0392B);
        return img;
    }

    public static (int w, int h, uint[] px) Sheet(params (int w, int h, uint[] px)[] tiles)
    {
        int tw = tiles[0].w, th = tiles[0].h;
        int cols = 2, rows = (tiles.Length + 1) / 2;
        var px = new uint[cols * tw * rows * th];
        for (int t = 0; t < tiles.Length; t++)
        {
            int ox = (t % cols) * tw, oy = (t / cols) * th;
            for (int y = 0; y < th; y++)
                Array.Copy(tiles[t].px, y * tw, px, (oy + y) * cols * tw + ox, tw);
        }
        return (cols * tw, rows * th, px);
    }

    static (int w, int h, uint[] px) Draw(GeneratedWorld w, int scale, Func<int, uint> color, bool shade, bool waterOverlay = true)
    {
        int n = w.Config.Size, size = n * scale;
        var px = new uint[size * size];
        var hy = w.Water;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                uint c;
                if (waterOverlay && w.Sea[i]) c = SeaColor;
                else if (waterOverlay && hy.LakeId[i] >= 0) c = LakeColor;
                else
                {
                    c = color(i);
                    if (shade) c = Mul(c, Hillshade(w, x, y));
                }
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                        px[((n - 1 - y) * scale + sy) * size + x * scale + sx] = c;
            }
        var img = (size, size, px);
        if (waterOverlay)
            foreach (var r in hy.Rivers)
                for (int k = 0; k + 1 < r.Cells.Count; k++)
                {
                    int a = r.Cells[k], b = r.Cells[k + 1];
                    float thick = Math.Max(1f, r.Width[k] / w.Config.CellMeters * scale * 2f);
                    Line(img, n, scale, a % n + 0.5f, a / n + 0.5f, b % n + 0.5f, b / n + 0.5f, thick, RiverColor);
                }
        return img;
    }

    static void Line((int w, int h, uint[] px) img, int n, int scale, float x0, float y0, float x1, float y1, float thick, uint c)
    {
        float ax = x0 * scale, ay = (n - y0) * scale, bx = x1 * scale, by = (n - y1) * scale;
        float len = MathF.Max(1, MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)));
        int r = (int)MathF.Ceiling(thick / 2);
        for (float t = 0; t <= 1; t += 0.5f / len)
        {
            int cx = (int)(ax + (bx - ax) * t), cy = (int)(ay + (by - ay) * t);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > thick * thick / 4 + 0.25f) continue;
                    int px = cx + dx, py = cy + dy;
                    if (px >= 0 && py >= 0 && px < img.w && py < img.h) img.px[py * img.w + px] = c;
                }
        }
    }

    static float Hillshade(GeneratedWorld w, int x, int y)
    {
        int n = w.Config.Size;
        float H(int xx, int yy) => w.Height[Math.Clamp(yy, 0, n - 1) * n + Math.Clamp(xx, 0, n - 1)];
        float dzdx = (H(x + 1, y) - H(x - 1, y)) / (2 * w.Config.CellMeters);
        float dzdy = (H(x, y + 1) - H(x, y - 1)) / (2 * w.Config.CellMeters);
        float nx = -dzdx * 2, ny = -dzdy * 2, nz = 1;
        float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        float lx = -0.5f, ly = 0.5f, lz = 0.707f;
        float d = (nx * lx + ny * ly + nz * lz) / len;
        return Math.Clamp(0.45f + 0.75f * d, 0.25f, 1.25f);
    }

    static uint Hypsometric(float h)
    {
        if (h < 150) return Lerp(0x6E9E57, 0x8DAA5E, h / 150);
        if (h < 600) return Lerp(0x8DAA5E, 0xB09A62, (h - 150) / 450);
        if (h < 1300) return Lerp(0xB09A62, 0x8A7466, (h - 600) / 700);
        return Lerp(0x8A7466, 0xF2F2F2, Math.Min(1, (h - 1300) / 700));
    }

    static uint Lerp(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        uint ch(int s) => (uint)Math.Round(((a >> s) & 255) + (((b >> s) & 255) - (float)((a >> s) & 255)) * t);
        return (ch(16) << 16) | (ch(8) << 8) | ch(0);
    }

    static uint Mul(uint c, float f)
    {
        uint ch(int s) => (uint)Math.Clamp((int)(((c >> s) & 255) * f), 0, 255);
        return (ch(16) << 16) | (ch(8) << 8) | ch(0);
    }
}
