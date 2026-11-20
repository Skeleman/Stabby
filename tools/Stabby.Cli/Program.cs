using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using Stabby.Cli;
using Stabby.Content;
using Stabby.World;

var opts = ParseArgs(args.Skip(1).ToArray());
var biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

switch (args.FirstOrDefault())
{
    case "gen": return Gen();
    case "sweep": return Sweep();
    default:
        Console.WriteLine("usage: stabby gen --seed N [--size 512] [--out dir] [--scale 3/2/1 by size] [--sketch file] [--rain mm]\n       stabby sweep [--from 1] [--count 100] [--size 256] [--rain mm]\n--rain stops after terrain and hydrology with uniform rainfall (no climate or biomes)");
        return 2;
}

int Gen()
{
    var c = Config(ulong.Parse(Opt("seed", "1"), CultureInfo.InvariantCulture), int.Parse(Opt("size", "512"), CultureInfo.InvariantCulture));
    WorldSketch? sketch = null;
    if (opts.TryGetValue("sketch", out var sketchPath))
        sketch = SketchFile.Read(File.ReadAllText(sketchPath), c);
    var dir = Opt("out", Path.Combine("out", "seed" + c.Seed));
    int scale = int.Parse(Opt("scale", c.Size <= 256 ? "3" : c.Size <= 512 ? "2" : "1"), CultureInfo.InvariantCulture);
    Directory.CreateDirectory(dir);

    var sw = Stopwatch.StartNew();
    var w = WorldGenerator.Generate(c, biomes, sketch);
    long ms = sw.ElapsedMilliseconds;
    var violations = WorldChecks.Run(w);

    var map = Render.Map(w, scale);
    var sk = Render.SketchMap(w, biomes, scale);
    Save(Path.Combine(dir, "map.png"), map);
    Save(Path.Combine(dir, "sketch.png"), sk);
    if (w.Biome != null)
    {
        var bio = Render.BiomeMap(w, biomes, scale);
        var rain = Render.Precipitation(w, scale);
        Save(Path.Combine(dir, "biomes.png"), bio);
        Save(Path.Combine(dir, "precipitation.png"), rain);
        Save(Path.Combine(dir, "sheet.png"), Render.Sheet(sk, map, rain, bio));
    }
    else Save(Path.Combine(dir, "sheet.png"), Render.Sheet(sk, map));

    File.WriteAllText(Path.Combine(dir, "sketch.json"), SketchFile.Write(w.Sketch, c));
    WriteFloats(Path.Combine(dir, "height.f32"), w.Height);
    WriteFloats(Path.Combine(dir, "cliff.f32"), w.Sketch.Cliff);
    WriteFloats(Path.Combine(dir, "plateau.f32"), w.Sketch.Plateau);
    WriteFloats(Path.Combine(dir, "uplift.f32"), w.Sketch.Uplift);
    var m = Metrics(w, biomes, ms, violations);
    File.WriteAllText(Path.Combine(dir, "metrics.json"), JsonSerializer.Serialize(m, json));
    Console.WriteLine($"seed {c.Seed}: {ms} ms, {m["lakes"]} lakes, {m["riverSegments"]} river reaches, {m["riverKm"]} km of river, peak {m["peakM"]} m, {violations.Count} violations -> {dir}");
    foreach (var v in violations.Take(10)) Console.WriteLine("  " + v);
    return violations.Count == 0 ? 0 : 1;
}

static void WriteFloats(string path, float[] v)
{
    var b = new byte[v.Length * 4];
    Buffer.BlockCopy(v, 0, b, 0, b.Length);
    File.WriteAllBytes(path, b);
}

int Sweep()
{
    ulong from = ulong.Parse(Opt("from", "1"), CultureInfo.InvariantCulture);
    int count = int.Parse(Opt("count", "100"), CultureInfo.InvariantCulture), size = int.Parse(Opt("size", "256"), CultureInfo.InvariantCulture);
    var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
    var times = new System.Collections.Concurrent.ConcurrentBag<long>();
    Parallel.For(0, count, k =>
    {
        var c = Config(from + (ulong)k, size);
        var sw = Stopwatch.StartNew();
        var w = WorldGenerator.Generate(c, biomes);
        times.Add(sw.ElapsedMilliseconds);
        foreach (var v in WorldChecks.Run(w)) failures.Add($"seed {c.Seed}: {v}");
    });
    var t = times.OrderBy(x => x).ToArray();
    Console.WriteLine($"{count} seeds at {size}x{size}: median {t[t.Length / 2]} ms, max {t[^1]} ms, {failures.Count} violations");
    foreach (var f in failures.OrderBy(x => x).Take(30)) Console.WriteLine("  " + f);
    return failures.IsEmpty ? 0 : 1;
}

Dictionary<string, object> Metrics(GeneratedWorld w, List<BiomeDef> defs, long ms, List<string> violations)
{
    int n = w.Config.Size, land = w.Sea.Count(s => !s);
    float riverKm = w.Water.Rivers.Sum(r => (r.Cells.Count - 1) * w.Config.CellMeters) / 1000f;
    var shares = new Dictionary<string, float>();
    foreach (var d in defs) shares[d.Id] = 0;
    if (w.Biome != null) for (int i = 0; i < n * n; i++) if (w.Biome[i] >= 0) shares[defs[w.Biome[i]].Id] += 100f / land;
    return new()
    {
        ["seed"] = w.Config.Seed, ["generatorVersion"] = WorldConfig.GeneratorVersion, ["size"] = n,
        ["kmAcross"] = n * w.Config.CellMeters / 1000f, ["ms"] = ms, ["fingerprint"] = w.Fingerprint().ToString("x16", CultureInfo.InvariantCulture),
        ["landPercent"] = MathF.Round(100f * land / (n * n), 1), ["islands"] = w.Sketch.Landmasses.Count,
        ["ranges"] = w.Sketch.Ranges.Count, ["corridors"] = w.Sketch.Corridors.Count, ["landmarks"] = w.Sketch.Landmarks.Count,
        ["sketchLakes"] = w.Sketch.Lakes.Count,
        ["peakM"] = MathF.Round(w.Height.Max()), ["lakes"] = w.Water.Lakes.Count,
        ["largestLakeKm2"] = MathF.Round(w.Water.Lakes.Select(l => l.Cells.Count).DefaultIfEmpty(0).Max() * w.Config.CellMeters * w.Config.CellMeters / 1e6f, 2),
        ["riverSegments"] = w.Water.Rivers.Count, ["riverKm"] = MathF.Round(riverKm, 1),
        ["maxDischargeM3s"] = MathF.Round(w.Water.DischargeM3s.Where((q, i) => !w.Sea[i]).Max(), 1),
        ["biomePercent"] = shares.ToDictionary(kv => kv.Key, kv => MathF.Round(kv.Value, 1)),
        ["chunkSize"] = WorldChunks.Size, ["chunkChecksums"] = WorldChunks.Checksums(w).Select(s => s.ToString("x16", CultureInfo.InvariantCulture)).ToArray(),
        ["violations"] = violations,
    };
}

WorldConfig Config(ulong seed, int size) => new WorldConfig
{
    Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size),
    UniformRainMm = float.Parse(Opt("rain", "0"), CultureInfo.InvariantCulture),
};

string Opt(string key, string fallback) => opts.TryGetValue(key, out var v) ? v : fallback;

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}

static void Save(string path, (int w, int h, uint[] px) img) => Png.Write(path, img.w, img.h, img.px);
