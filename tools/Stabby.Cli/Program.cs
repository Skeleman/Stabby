using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using Stabby.Cli;
using Stabby.Content;
using Stabby.World;

var opts = ParseArgs(args.Skip(1).ToArray());
var biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
var materials = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
var objects = ContentLoader.LoadObjects(ContentLoader.FindContentRoot(AppContext.BaseDirectory), biomes.Select(b => b.Id).ToList());
var caves = ContentLoader.LoadCaves(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
var creatures = ContentLoader.LoadCreatures(ContentLoader.FindContentRoot(AppContext.BaseDirectory), biomes.Select(b => b.Id).ToList());
var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

switch (args.FirstOrDefault())
{
    case "gen": return Gen();
    case "sweep": return Sweep();
    case "bake": return Bake();
    case "scenarios": return opts.ContainsKey("scan") ? ScanScenarios() : RunScenarios();
    default:
        Console.WriteLine("usage: stabby gen --seed N [--size 512] [--out dir] [--scale 3/2/1 by size] [--sketch file] [--rain mm]\n       stabby sweep [--from 1] [--count 100] [--size 256] [--rain mm] [--jobs N] [--checks 0] [--groups a,b] [--low-while file]\n--rain stops after terrain and hydrology with uniform rainfall (no climate or biomes)");
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
    var w = WorldGenerator.Generate(c, biomes, materials, sketch, null, objects, caves);
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

int Bake()
{
    var c = Config(ulong.Parse(Opt("seed", "1"), CultureInfo.InvariantCulture), int.Parse(Opt("size", "512"), CultureInfo.InvariantCulture));
    var path = Opt("out", Path.Combine("out", "maps", $"seed{c.Seed}-{c.Size}.stabbymap"));
    var sw = Stopwatch.StartNew();
    var sketch = opts.TryGetValue("sketch", out var sketchPath) ? SketchFile.Read(File.ReadAllText(sketchPath), c) : null;
    var w = WorldGenerator.Generate(c, biomes, materials, sketch, null, objects, caves);
    long genMs = sw.ElapsedMilliseconds;
    var violations = WorldChecks.Run(w);
    sw.Restart();
    var files = ContentBytes();
    ulong hash = WorldFile.ContentHash(files[0], files[1]);
    var raised = WorldFile.Read(WorldFile.Write(w), materials);
    int mound = Mound.Raise(raised);
    var played = WorldFile.Read(WorldFile.Write(raised, hash, mound, null), materials);
    played.Objects = objects;
    played.Caves = caves;
    var ground = GroundBake.Run(played, GroundBake.KeyOf(files, 1f), true, out _);
    long groundMs = sw.ElapsedMilliseconds;
    var bytes = WorldFile.Write(played, hash, mound, ground);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllBytes(path, bytes);
    sw.Restart();
    var back = WorldFile.Read(File.ReadAllBytes(path), materials);
    long readMs = sw.ElapsedMilliseconds;
    bool same = back.Bake.Sums.SequenceEqual(WorldChunks.Checksums(played)) && back.Bake.Mound == mound && back.Bake.Ground != null
        && back.Bake.Ground.Edits.Length == ground.Edits.Length && back.Bake.Ground.Field != null;
    Console.WriteLine($"seed {c.Seed} size {c.Size}: generated in {genMs} ms, mound and ground in {groundMs} ms, {bytes.Length / 1024} KB, reads back in {readMs} ms{(same ? "" : " WITH DIFFERENCES")}, {violations.Count} violations -> {path}");
    foreach (var v in violations.Take(10)) Console.WriteLine("  " + v);
    return same && violations.Count == 0 ? 0 : 1;
}

List<byte[]> ContentBytes()
{
    var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    var files = new List<byte[]>();
    foreach (var f in "biomes.json materials.json objects.json caves.json".Split(' ')) files.Add(File.ReadAllBytes(Path.Combine(root, f)));
    return files;
}

int RunScenarios()
{
    var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    var dir = Opt("out", Path.Combine("out", "scenarios"));
    int failed = 0;
    foreach (var s in Scenarios.All)
    {
        var c = Config(s.Seed, Scenarios.Size);
        var sketch = s.Sketch == null ? null : ContentLoader.LoadSketch(root, s.Sketch, c);
        var w = WorldGenerator.Generate(c, biomes, materials, sketch, null, objects, caves);
        var violations = WorldChecks.Run(w);
        int at = s.Find(w), n = c.Size;
        bool ok = at >= 0 && violations.Count == 0;
        if (!ok) failed++;
        var map = Render.Map(w, 3);
        if (at >= 0) Mark(map, at % n * 3 + 1, (n - 1 - at / n) * 3 + 1);
        Directory.CreateDirectory(dir);
        Save(Path.Combine(dir, s.Name + ".png"), Render.Sheet(Render.SketchMap(w, biomes, 3), map, Render.BiomeMap(w, biomes, 3)));
        Console.WriteLine($"{(ok ? "pass" : "FAIL")} {s.Name}: seed {s.Seed}{(s.Sketch == null ? "" : " + " + s.Sketch)}, witness {(at < 0 ? "none" : $"{at % n},{at / n}")}, {violations.Count} violations");
    }
    Console.WriteLine($"sheets -> {dir}");
    return failed == 0 ? 0 : 1;
}

int ScanScenarios()
{
    int count = int.Parse(Opt("scan", "100"), CultureInfo.InvariantCulture);
    foreach (var s in Scenarios.All.Where(s => s.Sketch == null))
    {
        var hits = new System.Collections.Concurrent.ConcurrentBag<ulong>();
        Parallel.For(1, count + 1, k =>
        {
            var w = WorldGenerator.Generate(Config((ulong)k, Scenarios.Size), biomes, materials, null, null, objects, caves);
            if (s.Find(w) >= 0 && WorldChecks.Run(w).Count == 0) hits.Add((ulong)k);
        });
        var h = hits.OrderBy(x => x).ToArray();
        Console.WriteLine($"{s.Name}: {h.Length}/{count} seeds pass, first {string.Join(" ", h.Take(8))}");
    }
    return 0;
}

static void Mark((int w, int h, uint[] px) img, int x, int y)
{
    for (int d = -12; d <= 12; d++)
        foreach (var (u, v) in new[] { (x + d, y), (x, y + d), (x + d, y + 1), (x + 1, y + d) })
            if (u >= 0 && v >= 0 && u < img.w && v < img.h && Math.Abs(d) > 3) img.px[v * img.w + u] = 0xFF00FFu;
}

static void WriteFloats(string path, float[] v)
{
    var b = new byte[v.Length * 4];
    Buffer.BlockCopy(v, 0, b, 0, b.Length);
    File.WriteAllBytes(path, b);
}

int Sweep()
{
    string[] order = { "sketch", "erosion", "climate", "hydrology", "biomes", "checks" };
    ulong from = ulong.Parse(Opt("from", "1"), CultureInfo.InvariantCulture);
    int count = int.Parse(Opt("count", "100"), CultureInfo.InvariantCulture), size = int.Parse(Opt("size", "256"), CultureInfo.InvariantCulture);
    var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
    var times = new System.Collections.Concurrent.ConcurrentBag<(long, ulong, string)>();
    var stages = new System.Collections.Concurrent.ConcurrentBag<(string, double, long)>();
    var checkTimes = new System.Collections.Concurrent.ConcurrentBag<long>();
    var perSeed = new System.Collections.Concurrent.ConcurrentDictionary<ulong, List<string>>();
    var par = new ParallelOptions { MaxDegreeOfParallelism = int.Parse(Opt("jobs", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture) };
    bool checks = Opt("checks", "1") != "0";
    var groupOpt = Opt("groups", "all");
    HashSet<string>? groups = groupOpt == "all" ? null : new HashSet<string>(groupOpt.Split(',', StringSplitOptions.RemoveEmptyEntries));
    if (groups != null)
    {
        var unknown = groups.Where(g => !WorldChecks.Groups.Contains(g)).ToArray();
        if (unknown.Length > 0) { Console.WriteLine($"unknown check groups: {string.Join(",", unknown)} (known: {string.Join(",", WorldChecks.Groups)})"); return 2; }
        groups.Add("core");
    }
    var groupTimes = new System.Collections.Concurrent.ConcurrentBag<(string, double, ulong)>();
    var lowWhile = Opt("low-while", "");
    if (lowWhile != "" && File.Exists(lowWhile))
    {
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        _ = Task.Run(async () =>
        {
            while (File.Exists(lowWhile)) await Task.Delay(2000);
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
        });
    }
    Task.Run(() => Parallel.For(0, count, par, k =>
    {
        var c = Config(from + (ulong)k, size);
        var sw = Stopwatch.StartNew();
        double last = 0;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        void Mark(string name)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            long b = GC.GetAllocatedBytesForCurrentThread();
            stages.Add((name, now - last, b - bytes));
            (last, bytes) = (now, b);
        }
        var mine = new List<(string, double)>();
        void Note(string name) { double before = last; Mark(name); mine.Add((name, last - before)); }
        var w = WorldGenerator.Generate(c, biomes, materials, null, Note, objects, caves);
        long genMs = sw.ElapsedMilliseconds;
        times.Add((genMs, c.Seed, string.Join(" ", mine.Select(m => $"{m.Item1} {m.Item2:F0}"))));
        if (!checks) return;
        var spent = new Dictionary<string, double>();
        var found = WorldChecks.Run(w, groups, spent, creatures);
        foreach (var kv in spent) groupTimes.Add((kv.Key, kv.Value, c.Seed));
        foreach (var v in found) failures.Add($"seed {c.Seed}: {v}");
        perSeed[c.Seed] = found;
        Mark("checks");
        checkTimes.Add(sw.ElapsedMilliseconds - genMs);
    })).Wait();
    var t = times.OrderBy(x => x.Item1).ToArray();
    var ct = checkTimes.OrderBy(x => x).ToArray();
    string checkPart = ct.Length > 0 ? $", checks median {ct[ct.Length / 2]} ms, max {ct[^1]} ms" : "";
    Console.WriteLine($"{count} seeds at {size}x{size}: median {t[t.Length / 2].Item1} ms, max {t[^1].Item1} ms (seed {t[^1].Item2}: {t[^1].Item3}){checkPart}, {failures.Count} violations");
    foreach (var g in stages.GroupBy(s => s.Item1).OrderBy(g => Array.IndexOf(order, g.Key)))
    {
        var ms = g.Select(s => s.Item2).OrderBy(x => x).ToArray();
        Console.WriteLine($"  {g.Key,-10} median {ms[ms.Length / 2],7:F1} ms, max {ms[^1],7:F1} ms, {g.Average(s => s.Item3) / 1048576,7:F1} MB allocated");
    }
    if (checks) Console.WriteLine($"  check groups: {(groups == null ? "all" : string.Join(",", WorldChecks.Groups.Where(groups.Contains)))}");
    foreach (var g in groupTimes.GroupBy(s => s.Item1).OrderBy(g => g.Key == "features" ? -1 : Array.IndexOf(WorldChecks.Groups, g.Key)))
    {
        var ms = g.OrderBy(s => s.Item2).ToArray();
        Console.WriteLine($"    {g.Key,-10} median {ms[ms.Length / 2].Item2,8:F1} ms, max {ms[^1].Item2,8:F1} ms (seed {ms[^1].Item3})");
    }
    foreach (var f in failures.OrderBy(x => x).Take(30)) Console.WriteLine("  " + f);
    var list = Opt("violations", "");
    if (list != "") File.WriteAllLines(list, perSeed.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(v => $"seed {p.Key}: {v}")));
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
