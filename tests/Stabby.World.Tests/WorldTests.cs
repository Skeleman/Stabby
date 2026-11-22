using System.Reflection;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class WorldTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(ContentLoader.FindContentRoot(AppContext.BaseDirectory), Biomes.Select(b => b.Id).ToList());

    static WorldConfig Cfg(ulong seed, int size = 256) =>
        new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) };

    static GeneratedWorld Gen(ulong seed, int size = 256) => WorldGenerator.Generate(Cfg(seed, size), Biomes, Materials, null, null, Objects);

    [Fact]
    public void HundredSeedsPassEveryCheck()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(1, 101, s =>
        {
            foreach (var v in WorldChecks.Run(Gen((ulong)s))) failures.Add($"seed {s}: {v}");
        });
        Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f).Take(20)));
    }

    [Fact]
    public void GameScaleWorldPlacesFeaturesAndPassesChecks()
    {
        var w = Gen(3, 512);
        Assert.Empty(WorldChecks.Run(w));
        var edits = Features.Place(w, new TerrainDetail(w));
        Assert.Contains(edits, e => e.Kind == FeatureEdit.Add);
        Assert.Contains(edits, e => e.Shape == FeatureEdit.Overhang);
        Assert.Contains(edits, e => e.Shape == FeatureEdit.Cave);
        Assert.Contains(edits, e => e.Shape == FeatureEdit.CaveRoom);
    }

    [Fact]
    public void DrySeedsRaiseMesasAndButtes()
    {
        int mesas = 0, buttes = 0, lips = 0;
        Parallel.For(1, 21, s =>
        {
            var w = Gen((ulong)s);
            foreach (var m in w.Sketch.Mesas)
                if (m.Butte) Interlocked.Increment(ref buttes); else Interlocked.Increment(ref mesas);
            foreach (var e in Features.Place(w, new TerrainDetail(w)))
                if (e.Shape == FeatureEdit.Lip) Interlocked.Increment(ref lips);
        });
        Assert.True(mesas >= 10 && buttes >= 4 && lips >= 6, $"{mesas} mesas, {buttes} buttes, {lips} lips in 20 seeds");
    }

    [Fact]
    public void SameSeedSameWorld()
    {
        Assert.Equal(Gen(42).Fingerprint(), Gen(42).Fingerprint());
        Assert.NotEqual(Gen(42).Fingerprint(), Gen(43).Fingerprint());
    }

    [Fact]
    public void FingerprintCoversSketchAndBiomeIds()
    {
        var w = Gen(42);
        ulong full = w.Fingerprint(), output = w.OutputFingerprint();
        w.Sketch.Regions.Add(new RegionSketch { Biome = "x", X = 1, Y = 1, Radius = 1 });
        Assert.NotEqual(full, w.Fingerprint());
        Assert.Equal(output, w.OutputFingerprint());
        w.BiomeIds[0] += "x";
        Assert.NotEqual(output, w.OutputFingerprint());
    }

    [Fact]
    public void EditedSketchRoundTripsToSameWorld()
    {
        var c = Cfg(9);
        var a = WorldGenerator.Generate(c, Biomes, Materials);
        var again = SketchFile.Read(SketchFile.Write(a.Sketch, c), Cfg(1, 64));
        Assert.Equal(a.Fingerprint(), WorldGenerator.Generate(c, Biomes, Materials, again).Fingerprint());
    }

    [Fact]
    public void SketchMatchesItsSchema()
    {
        var c = Cfg(9);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SketchFile.Write(WorldGenerator.Generate(c, Biomes, Materials).Sketch, c))!.AsObject();
        var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
        var schema = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root, "schemas", "sketch.schema.json")))!.AsObject();
        Assert.Equal(SketchFile.SchemaVersion, (int)schema["properties"]!["schemaVersion"]!["const"]!);
        var props = schema["properties"]!.AsObject().Select(p => p.Key).OrderBy(k => k).ToList();
        var required = schema["required"]!.AsArray().Select(r => (string)r!).OrderBy(k => k).ToList();
        Assert.Equal(props, json.Select(p => p.Key).OrderBy(k => k).ToList());
        Assert.Equal(props, required);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":1,\"size\":64,\"cellMeters\":90}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"bogus\":1}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"ranges\":[{\"x\":[1],\"y\":[1],\"width\":2,\"strength\":1}]}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"shores\":[{\"kind\":\"lava\",\"x\":1,\"y\":1,\"radius\":2}]}")]
    public void MalformedSketchIsRejected(string json)
    {
        var e = Record.Exception(() => SketchFile.Read(json, Cfg(1, 64)));
        Assert.True(e is InvalidDataException || e is System.Text.Json.JsonException, e?.GetType().Name ?? "accepted");
    }

    [Fact]
    public void AuthoredEditsAreHonoured()
    {
        var c = Cfg(11);
        var s = WorldGenerator.Generate(c, Biomes, Materials).Sketch;
        var big = s.Landmasses[0];
        s.Regions.Clear();
        s.Regions.Add(new RegionSketch { Biome = "desert", X = big.X, Y = big.Y, Radius = big.Radius * 0.5f });
        s.Landmarks.Add(new LandmarkSketch { Kind = "town", X = big.X + big.Radius * 0.3f, Y = big.Y });
        s.Build(c);
        var w = WorldGenerator.Generate(c, Biomes, Materials, s);
        Assert.Empty(WorldChecks.Run(w));
        int n = c.Size, centre = (int)big.Y * n + (int)big.X;
        if (!w.Sea[centre] && w.Water.LakeId[centre] < 0) Assert.Equal("desert", w.BiomeIds[w.Biome[centre]]);
    }

    [Fact]
    public void TerrainStageSkipsClimateAndBiomes()
    {
        var c = Cfg(5);
        c.UniformRainMm = 1200;
        var w = WorldGenerator.Generate(c, Biomes, Materials);
        Assert.Null(w.Biome);
        Assert.Empty(WorldChecks.Run(w));
        Assert.True(w.Water.Rivers.Count > 0);
    }

    [Fact]
    public void BiomeContentMatchesSchema()
    {
        var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
        using var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "biomes.schema.json")));
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "biomes.json")));
        var item = schema.RootElement.GetProperty("properties").GetProperty("biomes").GetProperty("items");
        var required = item.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToList();
        var allowed = item.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var b in data.RootElement.GetProperty("biomes").EnumerateArray())
        {
            var names = b.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Empty(required.Except(names));
            Assert.Empty(names.Except(allowed));
        }
    }

    [Fact]
    public void MountainsCastRainShadows()
    {
        double wind = 0, lee = 0; int nw = 0, nl = 0;
        for (ulong s = 1; s <= 20; s++)
        {
            var w = Gen(s);
            int n = w.Config.Size;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (w.Sea[i] || w.Height[i] < 200) continue;
                    float rise = w.Height[i + 1] - w.Height[i - 1];
                    if (rise > 20) { wind += w.Climate.PrecipitationMm[i]; nw++; }
                    else if (rise < -20) { lee += w.Climate.PrecipitationMm[i]; nl++; }
                }
        }
        Assert.True(nw > 100 && nl > 100, $"too few slope cells: {nw} windward, {nl} leeward");
        Assert.True(wind / nw > 1.1 * lee / nl, $"windward {wind / nw:F0} mm vs leeward {lee / nl:F0} mm");
    }

    [Fact]
    public void WorldsHaveRiversLakesAndMountains()
    {
        var w = Gen(3, 256);
        Assert.True(w.Water.Rivers.Count > 10, $"{w.Water.Rivers.Count} river reaches");
        Assert.True(w.Height.Max() > 300, $"peak {w.Height.Max()} m");
    }

    [Fact]
    public void ConditionalBiomesFollowTheirConditions()
    {
        var c = Cfg(3); c.SeaLevelTempC = 26;
        var w = WorldGenerator.Generate(c, Biomes, Materials);
        int n = c.Size, mangrove = World.Biomes.IndexOf(Biomes, "mangrove"), alpine = World.Biomes.IndexOf(Biomes, "alpine");
        var coast = World.Biomes.Distance(n, w.Sea, c.CellMeters);
        int m = 0;
        for (int i = 0; i < n * n; i++)
        {
            if (w.Biome[i] == mangrove) { m++; Assert.True(w.Height[i] <= 8 && coast[i] <= 135); }
            if (w.Biome[i] == alpine) Assert.True(w.Height[i] >= 600);
        }
        Assert.True(m > 0, "no mangrove on a warm coast");
    }

    [Fact]
    public void BiomeContentLoads()
    {
        Assert.Equal(13, Biomes.Count);
        var alpine = Biomes[World.Biomes.IndexOf(Biomes, "alpine")];
        Assert.True(alpine.Conditional && alpine.HeightMinM == 600 && float.IsNaN(alpine.HeightMaxM));
        Assert.False(Biomes[0].Conditional);
        var bad = new BiomeDef { Id = "x", TempMinC = 0, TempMaxC = 1, PrecipMaxMm = 1, HeightMinM = 5, HeightMaxM = 2, WaterWithinM = 0 };
        Assert.Equal(2, World.Biomes.Validate(new[] { bad }).Count);
    }

    [Fact]
    public void MaterialContentMatchesSchema()
    {
        var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
        using var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "materials.schema.json")));
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "materials.json")));
        var top = schema.RootElement;
        var topNames = data.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Empty(top.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Except(topNames));
        Assert.Empty(topNames.Except(top.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
        var surface = top.GetProperty("properties").GetProperty("surface");
        var surfaceNames = data.RootElement.GetProperty("surface").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Empty(surface.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Except(surfaceNames));
        Assert.Empty(surfaceNames.Except(surface.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
        var defs = top.GetProperty("$defs");
        var rock = surface.GetProperty("properties").GetProperty("rock");
        var coat = defs.GetProperty("coat");
        void Same(System.Text.Json.JsonElement s, System.Text.Json.JsonElement d)
        {
            var names = d.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Empty(s.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Except(names));
            Assert.Empty(names.Except(s.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
        }
        var surfaceData = data.RootElement.GetProperty("surface");
        Same(rock, surfaceData.GetProperty("rock"));
        Same(surface.GetProperty("properties").GetProperty("coats"), surfaceData.GetProperty("coats"));
        foreach (var c in surfaceData.GetProperty("coats").EnumerateObject()) Same(coat, c.Value);
        var liquid = top.GetProperty("properties").GetProperty("liquids").GetProperty("items");
        foreach (var l in data.RootElement.GetProperty("liquids").EnumerateArray())
        {
            Same(liquid, l);
            Same(liquid.GetProperty("properties").GetProperty("fall"), l.GetProperty("fall"));
        }
        var lib = ContentLoader.LoadMaterials(root);
        Assert.True(lib.Surface.StrataWobbleM > 0 && lib.Surface.Rock.BedSpacingM > 0);
        Assert.True(lib.Water.DeepColor != 0 && lib.Water.ShoreM > 0 && lib.Water.FoamWidthM > 0);
        Assert.Contains(MaterialLibrary.Validate(new MaterialLibrary { Surface = lib.Surface, Strata = lib.Strata, OffsetWavelengthM = 1, Liquids = { new LiquidDef { Id = "water" } } }), e => e.Contains("water.shoreM"));
        foreach (var b in ContentLoader.LoadBiomes(root)) Assert.True(lib.Surface.CoatOf(b.Id) >= 0, b.Id + " has no coat");
        lib.Surface.Coats[1].GrainAmp = 2;
        lib.Surface.Coats[3].Biomes.Add(lib.Surface.Coats[0].Biomes[0]);
        var errors = MaterialLibrary.Validate(lib);
        Assert.Contains(errors, e => e.Contains("coats.soil.grainAmp"));
        Assert.Contains(errors, e => e.Contains("already has a coat"));
        var item = top.GetProperty("properties").GetProperty("strata").GetProperty("items");
        var required = item.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToList();
        var allowed = item.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var b in data.RootElement.GetProperty("strata").EnumerateArray())
        {
            var names = b.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Empty(required.Except(names));
            Assert.Empty(names.Except(allowed));
        }
    }

    [Fact]
    public void StrataBandsFollowThickness()
    {
        var lib = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
        var bands = lib.Bands(0.5f);
        Assert.Equal((int)Math.Round(lib.PeriodM / 0.5f), bands.Length);
        for (int s = 0; s < lib.Strata.Count; s++)
            Assert.Equal((int)Math.Round(lib.Strata[s].ThicknessM / 0.5f), bands.Count(b => b == s));
        var w = Gen(3);
        var a = lib.Offsets(w);
        Assert.Equal(a, lib.Offsets(w));
        Assert.Contains(a, v => Math.Abs(v) > 1f);
        var coat = lib.Surface.CoatWeights(w);
        Assert.Equal(w.Height.Length * 4, coat.Length);
        for (int i = 0; i < w.Height.Length; i++)
        {
            Assert.Equal(255, coat[i * 4] + coat[i * 4 + 1] + coat[i * 4 + 2] + coat[i * 4 + 3]);
            if (w.Sea[i]) Assert.Equal(255, coat[i * 4 + SurfaceDetail.Sand]);
        }
        var forest = SurfaceDetail.ForestWeights(w, Objects, Biomes);
        Assert.Equal(w.Height.Length * 4, forest.Length);
        int boreal = Array.IndexOf(w.BiomeIds, "boreal"), desert = Array.IndexOf(w.BiomeIds, "desert");
        for (int i = 0; i < w.Height.Length; i++)
        {
            Assert.True(forest[i * 4] + forest[i * 4 + 1] <= 256);
            if (w.Biome[i] == desert && desert >= 0) Assert.True(forest[i * 4] + forest[i * 4 + 1] < 64 && forest[i * 4 + 2] < 32);
        }
        Assert.Contains(Enumerable.Range(0, w.Height.Length), i => w.Biome[i] == boreal && forest[i * 4 + 1] > 128);
        Assert.Contains(Enumerable.Range(0, w.Height.Length), i => w.Biome[i] == Array.IndexOf(w.BiomeIds, "temperate-rain") && forest[i * 4 + 2] > 200);
    }

    [Fact]
    public void WorldLibraryHasNoEngineOrFileDependencies()
    {
        var asm = typeof(WorldGenerator).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(r => r.Name ?? "").ToList();
        Assert.DoesNotContain(refs, r => r.StartsWith("UnityEngine", StringComparison.Ordinal) || r.StartsWith("System.Text.Json", StringComparison.Ordinal));
        var banned = new[] { "System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.Console" };
        foreach (var t in asm.GetTypes())
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var body = m.GetMethodBody();
                if (body == null) continue;
                var il = body.GetILAsByteArray() ?? Array.Empty<byte>();
                for (int k = 0; k + 4 < il.Length; k++)
                {
                    if (il[k] != 0x28 && il[k] != 0x6F && il[k] != 0x73) continue;
                    MethodBase callee;
                    try { callee = m.Module.ResolveMethod(BitConverter.ToInt32(il, k + 1))!; } catch { continue; }
                    var owner = callee.DeclaringType?.FullName ?? "";
                    Assert.False(banned.Contains(owner), $"{t.Name}.{m.Name} calls {owner}.{callee.Name}");
                }
            }
    }
}
