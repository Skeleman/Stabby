using System.Text.Json;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class ObjectTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    static readonly float[] RisingLods = { 10f, 50f };

    static GeneratedWorld Gen(ulong seed, int size = 256) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) }, Biomes, Materials, null, null, Objects, Caves);

    [Fact]
    public void FirePitsCarryOneLightThatCampsAndHermitagesInherit()
    {
        var pit = Objects.Objects.First(o => o.Id == "fire-pit");
        Assert.NotNull(pit.Light);
        Assert.Equal("fire", pit.Light.Category);
        Assert.NotNull(Objects.LightNamed("fire"));
        Assert.True(pit.Light.Intensity > 0 && pit.Light.RangeM > 0 && pit.Light.Flicker is > 0 and <= 1 && pit.Light.FlickerHz > 0);
        Assert.Equal(0xff9a4cu, pit.Light.Color);
        Assert.Same(pit, Objects.EmitterFor(ObjectPart.FirePit));
        Assert.Null(Objects.EmitterFor(ObjectPart.Rock));
        foreach (int shape in new[] { ObjectPart.FirePit, ObjectPart.Campsite })
            for (int v = 0; v < 4; v++)
            {
                var l = Structures.Layout(shape, ObjectMeshes.Key("structure", 0, v));
                Assert.Single(l.Lights);
                Assert.Equal(ObjectPart.FirePit, l.Lights[0].Shape);
            }
        var hermit = Enumerable.Range(0, 12).Select(v => Structures.Layout(ObjectPart.Hermitage, ObjectMeshes.Key("structure", 0, v)).Lights.Count).ToList();
        Assert.All(hermit, n => Assert.InRange(n, 0, 1));
        Assert.Contains(1, hermit);
        var shapes = PartShapes.Of(Objects);
        int camp = Objects.Objects.FindIndex(o => o.Parts.Count > 0 && o.Parts[0].Shape == ObjectPart.Campsite);
        var inst = new ObjectInstance { Def = camp, X = 100, Y = 20, Z = 50, Qw = 1, Size = 1 };
        var found = new List<StructureLight>();
        ObjectField.AddLights(Objects, shapes, inst, found);
        Assert.Single(found);
        Assert.InRange(found[0].X, 90, 110);
        Assert.InRange(found[0].Y, 20, 22);
        var bad = new ObjectLibrary();
        bad.Lights.Add(new LightKind { Id = "fire", Importance = 1 });
        bad.Kinds.Add(new ObjectKind { Id = "static", Lods = 3, LodPixels = RisingLods });
        bad.Objects.Add(new ObjectDef { Id = "pit", Kind = "static", Variants = 1, Parts = { new ObjectPart() }, Light = new ObjectLight { Category = "torch", Flicker = 2 } });
        var errors = ObjectLibrary.Validate(bad);
        Assert.Contains(errors, e => e == "pit: light category torch is not defined");
        Assert.Contains(errors, e => e == "pit: light needs intensity and radiusM above 0");
        Assert.Contains(errors, e => e.StartsWith("pit: light flicker", StringComparison.Ordinal));
        Assert.Contains(errors, e => e == "pit: lights belong only to structure objects");
    }

    static void Matches(JsonElement schema, JsonElement data)
    {
        var names = data.EnumerateObject().Select(p => p.Name).ToList();
        if (schema.TryGetProperty("required", out var req)) Assert.Empty(req.EnumerateArray().Select(e => e.GetString()!).Except(names));
        Assert.Empty(names.Except(schema.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public void ObjectContentMatchesSchema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "schemas", "objects.schema.json")));
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "objects.json")));
        var top = schema.RootElement;
        Assert.Equal(ContentLoader.ObjectSchemaVersion, top.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        Matches(top, data.RootElement);
        var kind = top.GetProperty("properties").GetProperty("kinds").GetProperty("items");
        foreach (var k in data.RootElement.GetProperty("kinds").EnumerateArray()) Matches(kind, k);
        var light = top.GetProperty("properties").GetProperty("lights").GetProperty("items");
        foreach (var l in data.RootElement.GetProperty("lights").EnumerateArray()) Matches(light, l);
        var budget = data.RootElement.GetProperty("lightBudget");
        Matches(top.GetProperty("properties").GetProperty("lightBudget"), budget);
        Assert.Equal(budget.GetProperty("shadowed").GetInt32(), Objects.LightCaps.Shadowed);
        Assert.Equal(budget.GetProperty("unshadowed").GetInt32(), Objects.LightCaps.Unshadowed);
        Assert.Equal(budget.GetProperty("glow").GetInt32(), Objects.LightCaps.Glow);
        Assert.Equal(data.RootElement.GetProperty("lights").GetArrayLength(), Objects.Lights.Count);
        Assert.All(Objects.Lights, l => Assert.True(l.Importance > 0));
        var obj = top.GetProperty("properties").GetProperty("objects").GetProperty("items");
        var part = obj.GetProperty("properties").GetProperty("parts").GetProperty("items");
        var place = obj.GetProperty("properties").GetProperty("placement");
        foreach (var o in data.RootElement.GetProperty("objects").EnumerateArray())
        {
            Matches(obj, o);
            Matches(place, o.GetProperty("placement"));
            if (o.TryGetProperty("interact", out var ia)) Matches(obj.GetProperty("properties").GetProperty("interact"), ia);
            foreach (var p in o.GetProperty("parts").EnumerateArray()) Matches(part, p);
        }
        var foliage = top.GetProperty("properties").GetProperty("foliage");
        var fd = data.RootElement.GetProperty("foliage");
        Matches(foliage, fd);
        Matches(foliage.GetProperty("properties").GetProperty("wind"), fd.GetProperty("wind"));
        Matches(foliage.GetProperty("properties").GetProperty("grass"), fd.GetProperty("grass"));
        Assert.Equal(fd.GetProperty("wind").GetProperty("gustScaleM").GetSingle(), Objects.Foliage.Wind.GustScaleM);
        Assert.Equal(fd.GetProperty("grass").GetProperty("reachPixels").GetSingle(), Objects.Foliage.Grass.ReachPixels);
        Assert.Equal(80f, Objects.Foliage.Grass.ReachAt(Objects.Foliage.Grass.ReachPixels, Detail.ReferencePixelsPerMetre), 1);
        Assert.Equal(data.RootElement.GetProperty("terrain").GetProperty("quadPixels").GetSingle(), Objects.Terrain.QuadPixels);
        Assert.True(Objects.Foliage.Grass.NearPixels > Objects.Foliage.Grass.ReachPixels);
        var bad = new FoliageDef();
        bad.Grass.NearPixels = 5;
        bad.Wind.GustScaleM = 0;
        var errors = new List<string>();
        bad.Validate(errors);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ObjectColorsComeFromTheMaterialLibrary()
    {
        var path = Path.Combine(Root, "objects.json");
        var json = File.ReadAllText(path);
        var ids = Biomes.Select(b => b.Id).ToList();
        Assert.DoesNotContain("\"color\"", json);
        Assert.DoesNotContain("leafColor", json);
        var oak = Objects.Objects.First(o => o.Id == "oak");
        Assert.Equal(Materials.Find("oak-bark")!.Color, oak.Color);
        Assert.Equal(Materials.Find("oak-leaf")!.Color, oak.Parts[0].LeafColor);
        var inline = Assert.Throws<InvalidDataException>(() => ContentLoader.ParseObjects(json.Replace("\"material\": \"oak-bark\"", "\"color\": \"#5f5446\""), ids, path, Materials));
        Assert.Contains("oak: inline \"color\"", inline.Message);
        var unknown = Assert.Throws<InvalidDataException>(() => ContentLoader.ParseObjects(json.Replace("\"leafMaterial\": \"oak-leaf\"", "\"leafMaterial\": \"no-such\""), ids, path, Materials));
        Assert.Contains("unknown material \"no-such\"", unknown.Message);
    }

    [Fact]
    public void ObjectContentLoadsWithKindPolicyAndPartOverrides()
    {
        Assert.Equal(4, Objects.Kinds.Count);
        var cluster = Objects.Objects.First(o => o.Id == "boulder-cluster");
        Assert.Equal("static", cluster.Kind);
        Assert.True(Objects.PartSolid(cluster, cluster.Parts[0]) && Objects.PartShadows(cluster, cluster.Parts[0]));
        Assert.False(Objects.PartSolid(cluster, cluster.Parts[2]) || Objects.PartShadows(cluster, cluster.Parts[2]));
        Assert.Equal(0.75f, cluster.Parts[1].OffsetX);
        var log = Objects.Objects.First(o => o.Id == "fallen-log");
        Assert.Equal(ObjectPart.Log, log.Parts[0].Shape);
        Assert.Contains("wetland", log.Placement.Biomes);
        Assert.Empty(ObjectLibrary.Validate(Objects, Biomes.Select(b => b.Id).ToList()));
        Assert.All(Objects.Kinds, k => Assert.True(Objects.RuleOf(k).Light.Near == 432f && Objects.RuleOf(k).Light.Mid == 108f && Objects.RuleOf(k).Light.Far == 10.8f, k.Id));
        var stone = Objects.Objects.First(o => o.Id == "loose-stone");
        Assert.Equal("take", stone.Verb);
        Assert.True(Objects.IsInteractive(Objects.Objects.IndexOf(stone)) && stone.ReachM > 0);
        Assert.Contains(Objects.Objects, o => o.Kind == "interactive" && o.Verb == "cut");
        Assert.All(Objects.Objects.Where(o => o.Kind != "interactive"), o => Assert.Null(o.Verb));
        log.Placement.Biomes.Add("moon");
        try { Assert.Contains(ObjectLibrary.Validate(Objects, Biomes.Select(b => b.Id).ToList()), e => e.Contains("unknown biome moon")); }
        finally { log.Placement.Biomes.Remove("moon"); }
        var bad = new ObjectLibrary();
        bad.Lights.Add(new LightKind { Id = "point" });
        bad.Kinds.Add(new ObjectKind { Id = "static", Lods = 3, LodPixels = RisingLods });
        bad.Objects.Add(new ObjectDef { Id = "x", Kind = "animated", Variants = 0 });
        Assert.Equal(4, ObjectLibrary.Validate(bad).Count);
        bad.Kinds[0].Light = "torch";
        bad.Lights.Add(new LightKind { Id = "point", GlowPixels = 200f });
        var lights = ObjectLibrary.Validate(bad);
        Assert.Contains(lights, e => e == "static: light torch is not defined");
        Assert.Contains(lights, e => e == "duplicate light point");
        Assert.Contains(lights, e => e.StartsWith("light point: shadowedPixels", StringComparison.Ordinal));
        bad.Kinds[0].Light = "point";
        bad.Lights.RemoveAt(1);
        bad.Lights[0].Importance = 0;
        bad.LightCaps.Glow = -1;
        var budget = ObjectLibrary.Validate(bad);
        Assert.Contains(budget, e => e == "light point: importance > 0");
        Assert.Contains(budget, e => e == "lightBudget: caps >= 0");
        bad.Lights[0].Importance = 1;
        bad.LightCaps.Glow = 4;
        bad.Kinds.Add(new ObjectKind { Id = "interactive", Lods = 3, LodPixels = RisingLods });
        bad.Objects.Clear();
        bad.Objects.Add(new ObjectDef { Id = "y", Kind = "interactive", Verb = "eat", ReachM = 2, Parts = { new ObjectPart() } });
        bad.Objects.Add(new ObjectDef { Id = "z", Kind = "static", Verb = "take", ReachM = 2, Parts = { new ObjectPart() } });
        var verbs = ObjectLibrary.Validate(bad);
        Assert.Contains(verbs, e => e.StartsWith("y: interactive objects need", StringComparison.Ordinal));
        Assert.Contains(verbs, e => e.StartsWith("z: only interactive", StringComparison.Ordinal));
    }

    [Fact]
    public void PlaceIntoMatchesCachedAndFreshPlacement()
    {
        var w = Gen(4);
        var detail = new TerrainDetail(w);
        var edits = Features.Place(w, detail);
        var cached = new ObjectField(w, detail, edits, Objects);
        var fresh = new ObjectField(w, detail, edits, Objects);
        var list = new List<ObjectInstance>();
        int n = fresh.ChunksAcross;
        foreach (var (cx, cz) in new[] { (0, 0), (n / 2, n / 2), (n / 3, 2 * n / 3), (n - 1, n - 1), (n / 2, n / 2) })
        {
            fresh.PlaceInto(cx, cz, list);
            var expect = cached.Chunk(cx, cz);
            Assert.Equal(expect, list.ToArray());
            Assert.Equal(expect, fresh.Place(cx, cz));
            cached.PlaceInto(cx, cz, list);
            Assert.Equal(expect, list.ToArray());
        }
    }

    [Fact]
    public void ConcurrentChunksComputeOnceAndAgree()
    {
        var w = Gen(4);
        var detail = new TerrainDetail(w);
        var edits = Features.Place(w, detail);
        var serial = new ObjectField(w, detail, edits, Objects);
        int n = serial.ChunksAcross, c = Enumerable.Range(0, n * n).First(i => serial.Chunk(i % n, i / n).Length > 0);
        var expect = serial.Chunk(c % n, c / n);
        var hammered = new ObjectField(w, detail, edits, Objects);
        const int threads = 8;
        var results = new ObjectInstance[threads][];
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() => { start.SignalAndWait(); results[t] = hammered.Chunk(c % n, c / n); })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        Assert.Equal(1, hammered.ChunkComputes);
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(expect, results[0]);
    }

    [Fact]
    public void ConcurrentSitesComputeOnceAndAgree()
    {
        var w = Gen(4);
        var detail = new TerrainDetail(w);
        var edits = Features.Place(w, detail);
        var serial = new ObjectField(w, detail, edits, Objects);
        int n = serial.ChunksAcross, c = Enumerable.Range(0, n * n).First(i => serial.Sites(i % n, i / n).Length > 0);
        var expect = serial.Sites(c % n, c / n);
        var hammered = new ObjectField(w, detail, edits, Objects);
        const int threads = 8;
        var results = new ObjectInstance[threads][];
        using var start = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() => { start.SignalAndWait(); results[t] = hammered.Sites(c % n, c / n); })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        Assert.Equal(1, hammered.SiteComputes);
        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(expect, results[0]);
    }

    [Fact]
    public void ObjectPlacementIsDeterministicAndVaried()
    {
        var w = Gen(4);
        var detail = new TerrainDetail(w);
        var edits = Features.Place(w, detail);
        var a = new ObjectField(w, detail, edits, Objects);
        var b = new ObjectField(w, detail, edits, Objects);
        var kinds = new HashSet<int>();
        int total = 0;
        for (int cz = 0; cz < a.ChunksAcross; cz++)
            for (int cx = 0; cx < a.ChunksAcross; cx++)
            {
                var p = a.Chunk(cx, cz);
                Assert.Equal(p.Length, b.Chunk(cx, cz).Length);
                Assert.Equal(p, b.Chunk(cx, cz));
                total += p.Length;
                foreach (var o in p) kinds.Add(o.Def);
            }
        var present = new HashSet<string>(w.Biome.Where(bb => bb >= 0).Select(bb => w.BiomeIds[bb]));
        var missing = Objects.Objects.Where((d, i) => !kinds.Contains(i) && d.Placement.PerHectare > 0 && (d.Placement.Biomes.Count == 0 || d.Placement.Biomes.Any(present.Contains))).Select(d => d.Id).ToList();
        Assert.True(missing.Count == 0, "missing: " + string.Join(", ", missing));
        Assert.True(total > 10000, $"{total} objects");
    }

    [Fact]
    public void ObjectMeshesHaveFallingLodsAndAreSeeded()
    {
        for (int shape = 0; shape < ObjectPart.Shapes; shape++)
        {
            ulong k = ObjectMeshes.Key("boulder", 0, 1);
            int prev = int.MaxValue;
            for (int lod = 0; lod < 3; lod++)
            {
                var m = ObjectMeshes.Build(shape, k, lod);
                Assert.True(m.TriangleCount < prev && m.TriangleCount > 0);
                Assert.Equal(m.Positions.Length, m.Normals.Length);
                Assert.All(m.Indices, i => Assert.InRange(i, 0, m.VertexCount - 1));
                prev = m.TriangleCount;
            }
            Assert.Equal(ObjectMeshes.Build(shape, k, 0).Positions, ObjectMeshes.Build(shape, k, 0).Positions);
        }
        Assert.NotEqual(ObjectMeshes.Build(0, ObjectMeshes.Key("boulder", 0, 1), 0).Positions, ObjectMeshes.Build(0, ObjectMeshes.Key("boulder", 0, 2), 0).Positions);
    }

    [Theory]
    [InlineData(ObjectPart.Log, ObjectPart.EndsAuto, 0.11f)]
    [InlineData(ObjectPart.Log, ObjectPart.EndsTorn, 0.3f)]
    [InlineData(ObjectPart.Log, ObjectPart.EndsSawn, 0.11f)]
    [InlineData(ObjectPart.Log, ObjectPart.EndsRooted, 0.12f)]
    [InlineData(ObjectPart.Log, ObjectPart.EndsWorn, 0.09f)]
    [InlineData(ObjectPart.Stump, ObjectPart.EndsAuto, 1.11f)]
    [InlineData(ObjectPart.Stump, ObjectPart.EndsSawn, 1.11f)]
    [InlineData(ObjectPart.Stump, ObjectPart.EndsSnapped, 0.5f)]
    public void WoodMeshesFallByLodFaceOutwardAndCarryTones(int shape, int ends, float aspect)
    {
        for (int v = 0; v < 6; v++)
        {
            ulong k = ObjectMeshes.Key("fallen-log", 0, v);
            int prev = int.MaxValue;
            for (int lod = 0; lod < 3; lod++)
            {
                var m = ObjectMeshes.Build(shape, k, lod, aspect, ends);
                Assert.True(m.TriangleCount < prev && m.TriangleCount > 0);
                Assert.Equal(4 * m.VertexCount, m.Tones.Length);
                Assert.All(m.Indices, i => Assert.InRange(i, 0, m.VertexCount - 1));
                double mx = 0, my = 0, mz = 0, outward = 0;
                for (int i = 0; i < m.VertexCount; i++) { mx += m.Positions[3 * i]; my += m.Positions[3 * i + 1]; mz += m.Positions[3 * i + 2]; }
                mx /= m.VertexCount; my /= m.VertexCount; mz /= m.VertexCount;
                for (int i = 0; i < m.VertexCount; i++)
                    outward += m.Normals[3 * i] * (m.Positions[3 * i] - mx) + m.Normals[3 * i + 1] * (m.Positions[3 * i + 1] - my) + m.Normals[3 * i + 2] * (m.Positions[3 * i + 2] - mz);
                Assert.True(outward > 0, $"lod {lod} variant {v} faces inward");
                for (int i = 0; i < m.VertexCount; i++) Assert.InRange(m.Tones[4 * i], 0f, 5f);
                prev = m.TriangleCount;
            }
            Assert.Equal(ObjectMeshes.Build(shape, k, 0, aspect, ends).Positions, ObjectMeshes.Build(shape, k, 0, aspect, ends).Positions);
        }
    }

    [Fact]
    public void RootedLogsCarryPlateAndPitAndSawnEndsStayFlat()
    {
        ulong k = ObjectMeshes.Key("windthrow", 0, 2);
        var rooted = ObjectMeshes.Build(ObjectPart.Log, k, 0, 0.12f, ObjectPart.EndsRooted);
        var torn = ObjectMeshes.Build(ObjectPart.Log, k, 0, 0.12f, ObjectPart.EndsTorn);
        Assert.Contains(Enumerable.Range(0, rooted.VertexCount), i => rooted.Tones[4 * i] == 3f);
        Assert.Contains(Enumerable.Range(0, rooted.VertexCount), i => rooted.Tones[4 * i] == 4f);
        Assert.DoesNotContain(Enumerable.Range(0, torn.VertexCount), i => torn.Tones[4 * i] >= 3f);
        ObjectMeshes.RootPlate(k, out float rp, out float yc, out float thick);
        float top = 0;
        for (int i = 0; i < rooted.VertexCount; i++) top = Math.Max(top, rooted.Positions[3 * i + 1]);
        Assert.InRange(top, 0.5f + 0.5f * (yc + 0.6f * rp), 0.5f + 0.5f * (yc + 1.3f * rp));
        Assert.InRange(thick, 0.8f, 1.4f);
        var sawn = ObjectMeshes.Build(ObjectPart.Stump, k, 0, 1.11f, ObjectPart.EndsSawn);
        Assert.All(Enumerable.Range(0, sawn.VertexCount).Where(i => sawn.Tones[4 * i] == 2f), i => Assert.Equal(0f, sawn.Tones[4 * i + 2]));
    }

    [Fact]
    public void PartEndsAreValidatedPerShape()
    {
        var lib = new ObjectLibrary();
        lib.Kinds.Add(new ObjectKind { Id = "static", Lods = 3, LodPixels = RisingLods });
        lib.Objects.Add(new ObjectDef { Id = "a", Kind = "static", Parts = { new ObjectPart { Id = "p", Shape = ObjectPart.Rock, Ends = ObjectPart.EndsTorn } } });
        lib.Objects.Add(new ObjectDef { Id = "b", Kind = "static", Parts = { new ObjectPart { Id = "p", Shape = ObjectPart.Stump, Ends = ObjectPart.EndsRooted } } });
        lib.Objects.Add(new ObjectDef { Id = "c", Kind = "static", Parts = { new ObjectPart { Id = "p", Shape = ObjectPart.Log, Ends = ObjectPart.EndsSnapped } } });
        lib.Objects.Add(new ObjectDef { Id = "d", Kind = "static", Parts = { new ObjectPart { Id = "p", Shape = ObjectPart.Log, Ends = 9 } } });
        lib.Objects.Add(new ObjectDef { Id = "e", Kind = "static", Parts = { new ObjectPart { Id = "p", Shape = ObjectPart.Log, Ends = ObjectPart.EndsRooted } } });
        var errors = ObjectLibrary.Validate(lib);
        Assert.Contains(errors, e => e.StartsWith("a: part p ends belong only", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("b: part p rooted", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("c: part p snapped", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("d: part p has unknown ends", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.StartsWith("e:", StringComparison.Ordinal));
        var wind = Objects.Objects.First(o => o.Id == "windthrow");
        Assert.Equal(ObjectPart.EndsRooted, wind.Parts[0].Ends);
        var snapped = Objects.Objects.First(o => o.Id == "snapped-tree");
        Assert.Equal(ObjectPart.EndsSnapped, snapped.Parts[0].Ends);
        Assert.Equal(ObjectPart.EndsAuto, snapped.Parts[1].Ends);
    }

    [Fact]
    public void FoliageMeshesFitUnitBoundsAndCarryLeafAndWindData()
    {
        for (int shape = ObjectPart.Shrub; shape <= ObjectPart.Conifer; shape++)
            for (int lod = 0; lod < 3; lod++)
            {
                var m = ObjectMeshes.Build(shape, ObjectMeshes.Key("bush", 0, 3), lod);
                Assert.Equal(4 * m.VertexCount, m.Uvs.Length);
                Assert.Equal(4 * m.VertexCount, m.Winds.Length);
                float maxY = 0, maxR = 0;
                int leaves = 0;
                for (int v = 0; v < m.VertexCount; v++)
                {
                    float x = m.Positions[3 * v], y = m.Positions[3 * v + 1], z = m.Positions[3 * v + 2];
                    maxY = Math.Max(maxY, y);
                    maxR = Math.Max(maxR, (float)Math.Sqrt(x * x + z * z));
                    if (m.Uvs[4 * v + 3] > 0.5f) leaves++;
                    Assert.InRange(m.Winds[4 * v], 0f, 1.5f);
                }
                Assert.InRange(maxY, 0.45f, lod == 0 ? 1.05f : 1.3f);
                Assert.InRange(maxR, 0.1f, lod == 0 ? 0.55f : 0.8f);
                Assert.True(leaves > 0);
            }
        Assert.Null(ObjectMeshes.Build(ObjectPart.Rock, ObjectMeshes.Key("boulder", 0, 1), 0).Uvs);
    }

    [Fact]
    public void InteractiveRemovalIsValidatedRecordedPerChunkAndClearsColumns()
    {
        var w = Gen(4);
        var detail = new TerrainDetail(w);
        var edits = Features.Place(w, detail);
        var host = new ObjectField(w, detail, edits, Objects);
        int stoneDef = Objects.Objects.FindIndex(d => d.Id == "loose-stone"), woodDef = Objects.Objects.FindIndex(d => d.Id == "brushwood");
        ObjectInstance stone = default, wood = default, plain = default;
        bool hasStone = false, hasWood = false, hasPlain = false;
        for (int cz = 0; cz < host.ChunksAcross && !(hasStone && hasWood && hasPlain); cz++)
            for (int cx = 0; cx < host.ChunksAcross; cx++)
                foreach (var o in host.Chunk(cx, cz))
                {
                    if (!hasStone && o.Def == stoneDef) { stone = o; hasStone = true; }
                    if (!hasWood && o.Def == woodDef) { wood = o; hasWood = true; }
                    if (!hasPlain && !Objects.IsInteractive(o.Def)) { plain = o; hasPlain = true; }
                }
        Assert.True(hasStone && hasWood && hasPlain);

        var deltas = new ObjectDeltas();
        Assert.Equal(RemoveResult.OutOfReach, deltas.TryRemove(host, stone.Id, stone.X + 20, stone.Y, stone.Z, out _));
        Assert.Equal(RemoveResult.NotInteractive, deltas.TryRemove(host, plain.Id, plain.X, plain.Y, plain.Z, out _));
        int across = host.ChunksAcross;
        Assert.Equal(RemoveResult.NoChunk, deltas.TryRemove(host, ObjectIds.Make(across * across, stoneDef, 0), 0, 0, 0, out _));
        Assert.Equal(RemoveResult.NoObject, deltas.TryRemove(host, ObjectIds.Make(ObjectIds.Chunk(stone.Id), stoneDef, 1 << 20), stone.X, stone.Y, stone.Z, out _));
        Assert.Equal(RemoveResult.NoObject, deltas.TryRemove(host, ObjectIds.Make(0, 255, 0), 0, 0, 0, out _));
        Assert.Equal(0, deltas.Count);

        Assert.True(deltas.Nearest(host, stone.X + 0.5f, stone.Y, stone.Z, out ulong near));
        Assert.True(Objects.IsInteractive(ObjectIds.Def(near)));
        Assert.Equal(RemoveResult.Ok, deltas.TryRemove(host, stone.Id, stone.X + 1, stone.Y, stone.Z, out var got));
        Assert.Equal(stone, got);
        Assert.Equal(RemoveResult.AlreadyRemoved, deltas.TryRemove(host, stone.Id, stone.X, stone.Y, stone.Z, out _));
        Assert.False(deltas.Nearest(host, stone.X, stone.Y, stone.Z, out ulong again) && again == stone.Id);

        var regionField = new ObjectField(w, detail, edits, Objects);
        var table = ColumnRegions.For(regionField);
        table.Put(ColumnRegion.Build(regionField, ObjectIds.Chunk(wood.Id)));
        var cols = new List<ObjectColumn>();
        host.ColumnsOf(wood, cols);
        Assert.NotEmpty(cols);
        Assert.Equal(cols.Count, table.Slots[ObjectIds.Chunk(wood.Id)].Alive(wood.Id));
        float before = Obstacles.Top(table, cols[0].X, cols[0].Z, 0.05f, cols[0].Bottom, cols[0].Top);
        Assert.True(before >= cols[0].Top);
        Assert.Equal(RemoveResult.Ok, deltas.TryRemove(host, wood.Id, wood.X, wood.Y, wood.Z, out var woodGot));
        Assert.Equal(cols.Count, table.Kill(woodGot.Id));
        Assert.Equal(0, table.Slots[ObjectIds.Chunk(wood.Id)].Alive(wood.Id));
        Assert.Equal(0, table.Kill(woodGot.Id));

        var client = new ObjectDeltas();
        var joiner = new ObjectField(w, detail, edits, Objects);
        foreach (int chunk in deltas.Chunks)
            foreach (ulong id in deltas.InChunk(chunk))
            {
                Assert.Equal(chunk, ObjectIds.Chunk(id));
                Assert.True(client.Apply(id));
                Assert.True(ObjectDeltas.TryFind(joiner, id, out var mirror));
                Assert.True(id == stone.Id ? mirror.Equals(stone) : mirror.Equals(wood));
            }
        Assert.False(client.Apply(stone.Id));
        Assert.Equal(2, client.Count);
        Assert.True(client.Removed(stone.Id) && client.Removed(wood.Id));
    }

    [Fact]
    public void WalkingStepsOntoLowRocksAndStopsAtTallOnes()
    {
        var flat = new FlatGround();
        var none = default(NoEdits);
        var cols = ColumnRegions.Whole(new List<ObjectColumn>
        {
            new ObjectColumn { X = 5, Z = 0, R = 0.6f, Bottom = 0, Top = 0.5f },
            new ObjectColumn { X = 5, Z = 10, R = 0.8f, Bottom = 0, Top = 2.5f },
        });
        float x = 3, y = 0, z = 0;
        for (int k = 0; k < 20; k++) Walk.Step(flat, none, cols, 1, ref x, ref y, ref z, 0.1f, 0, 1);
        Assert.Equal(0.5f, y, 3);
        x = 3; y = 0; z = 10;
        for (int k = 0; k < 40; k++) Walk.Step(flat, none, cols, 1, ref x, ref y, ref z, 0.1f, 0, 1);
        Assert.True(x < 5 - 0.8f - Walk.Radius + 0.15f && y == 0, $"walked to {x:F2},{y:F2}");
        x = 3; y = 0; z = 10;
        for (int k = 0; k < 40; k++) Walk.Step(flat, none, 1, ref x, ref y, ref z, 0.1f, 0, 1);
        Assert.True(x > 6.5f);
    }

    struct FlatGround : IGroundHeight
    {
        public float HeightAt(float x, float z) => 0;
    }

    [Fact]
    public void TorsAndHoodoosStandAsSolidAdds()
    {
        int tors = 0, hoodoos = 0;
        foreach (ulong seed in new ulong[] { 4, 19 })
        {
            var w = Gen(seed);
            foreach (var e in Features.Place(w, new TerrainDetail(w)))
            {
                if (e.Shape == FeatureEdit.Tor) { Assert.Equal(FeatureEdit.Add, e.Kind); tors++; }
                if (e.Shape == FeatureEdit.Hoodoo) { Assert.Equal(FeatureEdit.Add, e.Kind); hoodoos++; }
            }
        }
        Assert.True(tors > 0 && hoodoos > 0, $"{tors} tor edits, {hoodoos} hoodoo edits");
    }

    [Fact]
    public void LargeBouldersBecomeSolidEditsAndSmallOnesStayObjects()
    {
        var w = Gen(4);
        var detail = new TerrainDetail(w);
        var edits = Features.Place(w, detail);
        Assert.Contains(edits, e => e.Shape == FeatureEdit.Boulder && e.Kind == FeatureEdit.Add);
        var field = new ObjectField(w, detail, edits, Objects);
        int big = Objects.Objects.FindIndex(o => o.Id == "large-boulder");
        float min = Objects.Objects[big].Placement.EditMinSizeM;
        int small = 0;
        for (int cz = 0; cz < field.ChunksAcross; cz++)
            for (int cx = 0; cx < field.ChunksAcross; cx++)
                foreach (var o in field.Chunk(cx, cz))
                    if (o.Def == big) { Assert.True(o.Size < min); small++; }
        Assert.True(small > 0);
    }

    [Fact]
    public void ObjectsStayInTheirBiomes()
    {
        var w = Gen(5);
        Assert.Empty(WorldChecks.Run(w));
        var c = Objects.Objects.First(o => o.Id == "small-rock");
        var keep = c.Placement.Biomes;
        c.Placement.Biomes = new List<string> { "boreal" };
        try
        {
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            int n = w.Config.Size, d = Objects.Objects.IndexOf(c);
            for (int cz = 0; cz < field.ChunksAcross; cz++)
                for (int cx = 0; cx < field.ChunksAcross; cx++)
                    foreach (var o in field.Chunk(cx, cz))
                        if (o.Def == d)
                        {
                            int gx = Math.Min(n - 1, (int)(o.X / w.Config.CellMeters + 0.5f)), gz = Math.Min(n - 1, (int)(o.Z / w.Config.CellMeters + 0.5f));
                            Assert.Equal("boreal", w.BiomeIds[w.Biome[gz * n + gx]]);
                        }
        }
        finally { c.Placement.Biomes = keep; }
    }
    [Fact]
    public void TalusGathersAtCliffFeetAndDryOrWetLandStaysSparse()
    {
        var w = Gen(5, 512);
        var detail = new TerrainDetail(w);
        var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
        int n = w.Config.Size, talus = Objects.Objects.FindIndex(o => o.Id == "talus"), rock = Objects.Objects.FindIndex(o => o.Id == "small-rock");
        var pl = Objects.Objects[talus].Placement;
        var perDef = new int[Objects.Objects.Count];
        int talusFoot = 0, rockFoot = 0, rockSparse = 0, rockDense = 0;
        foreach (var o in Enumerable.Range(0, field.ChunksAcross * field.ChunksAcross).SelectMany(c => field.Chunk(c % field.ChunksAcross, c / field.ChunksAcross)))
        {
            perDef[o.Def]++;
            if (o.Def == talus && field.Foot(pl, o.X, o.Z)) talusFoot++;
            if (o.Def != rock) continue;
            if (field.Foot(pl, o.X, o.Z)) rockFoot++;
            int gx = Math.Min(n - 1, (int)(o.X / w.Config.CellMeters + 0.5f)), gz = Math.Min(n - 1, (int)(o.Z / w.Config.CellMeters + 0.5f));
            string b = w.BiomeIds[w.Biome[gz * n + gx]];
            if (b == "desert" || b == "wetland" || b == "mangrove") rockSparse++; else rockDense++;
        }
        int sparseCells = 0, denseCells = 0;
        for (int i = 0; i < n * n; i++)
        {
            if (w.Biome[i] < 0) continue;
            string b = w.BiomeIds[w.Biome[i]];
            if (b == "desert" || b == "wetland" || b == "mangrove") sparseCells++; else denseCells++;
        }
        var report = string.Join(", ", Objects.Objects.Select((o, d) => o.Id + " " + perDef[d])) + $"; talus at feet {talusFoot}, small rocks at feet {rockFoot}, sparse {rockSparse}/{sparseCells} dense {rockDense}/{denseCells}";
        Assert.True(perDef[talus] > 1000, report);
        Assert.True(talusFoot * 2 > perDef[talus] * 0.4f * 2 && talusFoot * (double)perDef[rock] > 5.0 * rockFoot * perDef[talus], report);
        Assert.True(sparseCells == 0 || rockSparse / (double)sparseCells < 0.4 * rockDense / denseCells, report);
    }

    [Fact]
    public void RiverBanksGrowReedsBarsDriftAndWetStones()
    {
        var w = Gen(5, 512);
        var detail = new TerrainDetail(w);
        var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
        var ids = new[] { "reeds", "driftwood", "drift-root", "river-gravel", "river-cobble", "wet-stone" };
        var perDef = new int[Objects.Objects.Count];
        foreach (var o in Enumerable.Range(0, field.ChunksAcross * field.ChunksAcross).SelectMany(c => field.Chunk(c % field.ChunksAcross, c / field.ChunksAcross)))
            perDef[o.Def]++;
        string report = string.Join(", ", ids.Select(id => id + " " + perDef[Objects.Objects.FindIndex(o => o.Id == id)]));
        foreach (var id in ids) Assert.True(perDef[Objects.Objects.FindIndex(o => o.Id == id)] > 0, report);
        Assert.Empty(WorldChecks.Run(w));
    }
}
