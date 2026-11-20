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

    public static (int w, int h, uint[] px) SketchMap(GeneratedWorld w, IReadOnlyList<BiomeDef> defs, int scale)
    {
        var s = w.Sketch;
        var img = Draw(w, scale, i => s.Land[i] ? Lerp(0x8FA87A, 0x6B4E3A, s.Uplift[i]) : SeaColor, false, waterOverlay: false);
        int n = w.Config.Size;
        float px = Math.Max(1.5f, scale * n / 400f);
        foreach (var r in s.Regions)
        {
            var d = defs.FirstOrDefault(b => b.Id == r.Biome);
            Ring(img, n, scale, r.X + 0.5f, r.Y + 0.5f, r.Radius, px, d?.Color ?? 0xFF00FF);
        }
        foreach (var m in s.Landmasses.Concat(s.Islets))
        {
            if (m.Lobes.Count == 0) Ring(img, n, scale, m.X + 0.5f, m.Y + 0.5f, m.Radius, px * 0.6f, 0xF0F0F0);
            foreach (var l in m.Lobes) Ring(img, n, scale, l.X + 0.5f, l.Y + 0.5f, l.Radius, px * 0.6f, 0xF0F0F0);
        }
        foreach (var m in s.Lakes)
            foreach (var l in m.Lobes) Ring(img, n, scale, l.X + 0.5f, l.Y + 0.5f, l.Radius, px, LakeColor);
        foreach (var h in s.Shores) Ring(img, n, scale, h.X + 0.5f, h.Y + 0.5f, h.Radius, px, h.Cliff ? 0x7A3B2Eu : 0xE8D49Au);
        foreach (var h in s.Highlands) Ring(img, n, scale, h.X + 0.5f, h.Y + 0.5f, h.Radius, px, h.Plateau ? 0xD9C27Au : 0xFFFFFFu);
        foreach (var r in s.Ranges) Polyline(img, n, scale, r.X, r.Y, px * 1.6f, 0xC0392B);
        foreach (var r in s.Corridors) Polyline(img, n, scale, r.X, r.Y, Math.Max(px * 1.6f, r.Width * 2 * scale), RiverColor);
        foreach (var l in s.Landmarks) Line(img, n, scale, l.X + 0.5f, l.Y + 0.5f, l.X + 0.5f, l.Y + 0.5f, px * 4, 0x111111);
        return img;
    }

    static void Polyline((int w, int h, uint[] px) img, int n, int scale, float[] xs, float[] ys, float thick, uint c)
    {
        for (int k = 0; k + 1 < xs.Length; k++)
            Line(img, n, scale, xs[k] + 0.5f, ys[k] + 0.5f, xs[k + 1] + 0.5f, ys[k + 1] + 0.5f, thick, c);
    }

    static void Ring((int w, int h, uint[] px) img, int n, int scale, float cx, float cy, float r, float thick, uint c)
    {
        const int Steps = 96;
        float px = cx + r, py = cy;
        for (int k = 1; k <= Steps; k++)
        {
            double a = 2 * Math.PI * k / Steps;
            float qx = cx + r * (float)Math.Cos(a), qy = cy + r * (float)Math.Sin(a);
            Line(img, n, scale, px, py, qx, qy, thick, c);
            px = qx; py = qy;
        }
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
                if (waterOverlay && w.Sea[i]) c = Mul(SeaDepth(w, i), 0.5f + 0.5f * Hillshade(w, x, y, seabed: true));
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
                    float thick = Math.Max(1f, r.Width[k] / w.Config.CellMeters * scale);
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

    static uint SeaDepth(GeneratedWorld w, int i) =>
        Lerp(0x4A8FB8, 0x1B3A5C, (float)Math.Sqrt(Math.Min(1f, -w.Height[i] / w.Config.DeepDepth)));

    // The seabed treats land as sea level so cliffs cast no rays offshore, and
    // its gentler relief is exaggerated to read at all.
    static float Hillshade(GeneratedWorld w, int x, int y, bool seabed = false)
    {
        int n = w.Config.Size;
        float H(int xx, int yy)
        {
            float h = w.Height[Math.Clamp(yy, 0, n - 1) * n + Math.Clamp(xx, 0, n - 1)];
            return seabed ? 2 * Math.Min(h, 0) : h;
        }
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
