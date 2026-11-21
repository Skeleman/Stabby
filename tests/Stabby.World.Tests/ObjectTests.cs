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

    static readonly float[] RisingLods = { 0.01f, 0.05f };

    static GeneratedWorld Gen(ulong seed, int size = 256) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) }, Biomes, Materials, null, null, Objects);

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
        var obj = top.GetProperty("properties").GetProperty("objects").GetProperty("items");
        var part = obj.GetProperty("properties").GetProperty("parts").GetProperty("items");
        var place = obj.GetProperty("properties").GetProperty("placement");
        foreach (var o in data.RootElement.GetProperty("objects").EnumerateArray())
        {
            Matches(obj, o);
            Matches(place, o.GetProperty("placement"));
            foreach (var p in o.GetProperty("parts").EnumerateArray()) Matches(part, p);
        }
        var foliage = top.GetProperty("properties").GetProperty("foliage");
        var fd = data.RootElement.GetProperty("foliage");
        Matches(foliage, fd);
        Matches(foliage.GetProperty("properties").GetProperty("wind"), fd.GetProperty("wind"));
        Matches(foliage.GetProperty("properties").GetProperty("grass"), fd.GetProperty("grass"));
        Assert.Equal(fd.GetProperty("wind").GetProperty("gustScaleM").GetSingle(), Objects.Foliage.Wind.GustScaleM);
        Assert.Equal(fd.GetProperty("grass").GetProperty("reachM").GetSingle(), Objects.Foliage.Grass.ReachM);
        Assert.True(Objects.Foliage.Grass.ReachM > Objects.Foliage.Grass.NearM);
        var bad = new FoliageDef();
        bad.Grass.NearM = 50;
        bad.Wind.GustScaleM = 0;
        var errors = new List<string>();
        bad.Validate(errors);
        Assert.Equal(2, errors.Count);
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
        log.Placement.Biomes.Add("moon");
        try { Assert.Contains(ObjectLibrary.Validate(Objects, Biomes.Select(b => b.Id).ToList()), e => e.Contains("unknown biome moon")); }
        finally { log.Placement.Biomes.Remove("moon"); }
        var bad = new ObjectLibrary();
        bad.Kinds.Add(new ObjectKind { Id = "static", Lods = 3, LodScreen = RisingLods });
        bad.Objects.Add(new ObjectDef { Id = "x", Kind = "animated", Variants = 0 });
        Assert.Equal(4, ObjectLibrary.Validate(bad).Count);
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
        Assert.Equal(Objects.Objects.Count, kinds.Count);
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

    [Fact]
    public void WalkingStepsOntoLowRocksAndStopsAtTallOnes()
    {
        var flat = new FlatGround();
        var none = default(NoEdits);
        var cols = ColumnArrays.Build(new List<ObjectColumn>
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
}
