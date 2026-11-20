using System.Diagnostics;
using System.Text.Json;
using Stabby.Cli;
using Stabby.Content;
using Stabby.World;

// stabby gen   --seed 7 [--size 256] [--out out/seed7] [--scale 3] [--sketch sketch.json]
// stabby sweep [--from 1] [--count 100] [--size 128]
var opts = ParseArgs(args.Skip(1).ToArray());
var biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

switch (args.FirstOrDefault())
{
    case "gen": return Gen();
    case "sweep": return Sweep();
    default:
        Console.WriteLine("usage: stabby gen --seed N [--size 256] [--out dir] [--scale 3] [--sketch file]\n       stabby sweep [--from 1] [--count 100] [--size 128]");
        return 2;
}

int Gen()
{
    var c = new WorldConfig { Seed = ulong.Parse(Opt("seed", "1")), Size = int.Parse(Opt("size", "256")) };
    var dir = Opt("out", Path.Combine("out", "seed" + c.Seed));
    int scale = int.Parse(Opt("scale", c.Size <= 256 ? "3" : "2"));
    Directory.CreateDirectory(dir);

    WorldSketch? sketch = null;
    if (opts.TryGetValue("sketch", out var sketchPath))
    {
        var file = JsonSerializer.Deserialize<SketchFile>(File.ReadAllText(sketchPath), json);
        sketch = WorldSketch.FromRanges(c, file!.LandFraction, file.Ranges);
    }

    var sw = Stopwatch.StartNew();
    var w = WorldGenerator.Generate(c, biomes, sketch);
    long ms = sw.ElapsedMilliseconds;
    var violations = WorldChecks.Run(w);

    var map = Render.Map(w, scale);
    var bio = Render.BiomeMap(w, biomes, scale);
    var rain = Render.Precipitation(w, scale);
    var sk = Render.SketchMap(w, scale);
    Save(Path.Combine(dir, "map.png"), map);
    Save(Path.Combine(dir, "biomes.png"), bio);
    Save(Path.Combine(dir, "precipitation.png"), rain);
    Save(Path.Combine(dir, "sketch.png"), sk);
    Save(Path.Combine(dir, "sheet.png"), Render.Sheet(sk, map, rain, bio));

    File.WriteAllText(Path.Combine(dir, "sketch.json"),
        JsonSerializer.Serialize(new SketchFile { LandFraction = w.Sketch.LandFraction, Ranges = w.Sketch.Ranges }, json));
    var m = Metrics(w, biomes, ms, violations);
    File.WriteAllText(Path.Combine(dir, "metrics.json"), JsonSerializer.Serialize(m, json));
    Console.WriteLine($"seed {c.Seed}: {ms} ms, {m["lakes"]} lakes, {m["riverSegments"]} river reaches, {m["riverKm"]} km of river, peak {m["peakM"]} m, {violations.Count} violations -> {dir}");
    foreach (var v in violations.Take(10)) Console.WriteLine("  " + v);
    return violations.Count == 0 ? 0 : 1;
}

int Sweep()
{
    ulong from = ulong.Parse(Opt("from", "1"));
    int count = int.Parse(Opt("count", "100")), size = int.Parse(Opt("size", "128"));
    var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
    var times = new System.Collections.Concurrent.ConcurrentBag<long>();
    Parallel.For(0, count, k =>
    {
        var c = new WorldConfig { Seed = from + (ulong)k, Size = size };
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
    for (int i = 0; i < n * n; i++) if (w.Biome[i] >= 0) shares[defs[w.Biome[i]].Id] += 100f / land;
    return new()
    {
        ["seed"] = w.Config.Seed, ["generatorVersion"] = WorldConfig.GeneratorVersion, ["size"] = n,
        ["kmAcross"] = n * w.Config.CellMeters / 1000f, ["ms"] = ms, ["fingerprint"] = w.Fingerprint().ToString("x16"),
        ["landPercent"] = MathF.Round(100f * land / (n * n), 1), ["ranges"] = w.Sketch.Ranges.Count,
        ["peakM"] = MathF.Round(w.Height.Max()), ["lakes"] = w.Water.Lakes.Count,
        ["riverSegments"] = w.Water.Rivers.Count, ["riverKm"] = MathF.Round(riverKm, 1),
        ["maxDischargeM3s"] = MathF.Round(w.Water.DischargeM3s.Where((q, i) => !w.Sea[i]).Max(), 1),
        ["biomePercent"] = shares.ToDictionary(kv => kv.Key, kv => MathF.Round(kv.Value, 1)),
        ["violations"] = violations,
    };
}

string Opt(string key, string fallback) => opts.TryGetValue(key, out var v) ? v : fallback;

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}

static void Save(string path, (int w, int h, uint[] px) img) => Png.Write(path, img.w, img.h, img.px);

sealed class SketchFile
{
    public float LandFraction;
    public List<RangeSketch> Ranges = new();
}
