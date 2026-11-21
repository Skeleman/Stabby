using System.Text.Json;
using System.Text.Json.Serialization;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class ContentParityTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);

    static readonly JsonSerializerOptions Dump = new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    static string Of<T>(T v) => JsonSerializer.Serialize(v, Dump);

    [Fact]
    public void BiomesMatchLegacyReader() =>
        Assert.Equal(Of(Legacy.ContentLoader.LoadBiomes(Root)), Of(ContentLoader.LoadBiomes(Root)));

    [Fact]
    public void MaterialsMatchLegacyReader() =>
        Assert.Equal(Of(Legacy.ContentLoader.LoadMaterials(Root)), Of(ContentLoader.LoadMaterials(Root)));

    [Fact]
    public void ObjectsMatchLegacyReader()
    {
        var ids = ContentLoader.LoadBiomes(Root).Select(b => b.Id).ToList();
        Assert.Equal(Of(Legacy.ContentLoader.LoadObjects(Root, ids)), Of(ContentLoader.LoadObjects(Root, ids)));
        Assert.Equal(Of(Legacy.ContentLoader.LoadObjects(Root)), Of(ContentLoader.LoadObjects(Root)));
    }

    public static TheoryData<string> Sketches()
    {
        var d = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "scenarios"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
            d.Add(Path.GetRelativePath(Root, f));
        return d;
    }

    [Theory]
    [MemberData(nameof(Sketches))]
    public void SketchFilesMatchLegacyReader(string rel)
    {
        var json = File.ReadAllText(Path.Combine(Root, rel));
        var a = new WorldConfig();
        var b = new WorldConfig();
        Assert.Equal(Of(Legacy.SketchFile.Read(json, a)), Of(SketchFile.Read(json, b)));
        Assert.Equal(Of(a), Of(b));
    }

    [Theory]
    [InlineData(3UL, 64)]
    [InlineData(9UL, 64)]
    [InlineData(ulong.MaxValue, 64)]
    public void GeneratedSketchesMatchLegacyWriterAndReader(ulong seed, int size)
    {
        var biomes = ContentLoader.LoadBiomes(Root);
        var c = new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) };
        var s = WorldGenerator.Generate(c, biomes, ContentLoader.LoadMaterials(Root)).Sketch;
        var text = SketchFile.Write(s, c);
        Assert.Equal(Legacy.SketchFile.Write(s, c), text);
        var a = new WorldConfig();
        var b = new WorldConfig();
        Assert.Equal(Of(Legacy.SketchFile.Read(text, a)), Of(SketchFile.Read(text, b)));
        Assert.Equal(Of(a), Of(b));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"bogus\":1}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"ranges\":[{\"x\":[1],\"y\":[1],\"width\":2,\"strength\":1}]}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"corridors\":[{\"x\":[1,2],\"y\":[1,2],\"width\":2,\"strength\":1}]}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"highlands\":[{\"kind\":\"hill\",\"x\":1,\"y\":1,\"radius\":2}]}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,\"landmasses\":[{\"x\":1,\"y\":1,\"radius\":2,\"lobes\":[{\"x\":1,\"y\":1,\"radius\":1,\"z\":0}]}]}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64.5,\"cellMeters\":90}")]
    [InlineData("{\"schemaVersion\":5,\"seed\":-1,\"size\":64,\"cellMeters\":90}")]
    [InlineData("{\"schemaVersion\":5,\"size\":64,\"cellMeters\":90,}")]
    public void MalformedSketchFailsInBothReaders(string json)
    {
        Assert.NotNull(Record.Exception(() => Legacy.SketchFile.Read(json, new WorldConfig())));
        Assert.IsType<InvalidDataException>(Record.Exception(() => SketchFile.Read(json, new WorldConfig())));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"biomes\":[],\"extra\":0}")]
    [InlineData("{\"schemaVersion\":2,\"biomes\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"biomes\":[{\"id\":\"a\",\"color\":\"#00ff00\",\"tempC\":1}]}")]
    public void MalformedBiomesFailInBothReaders(string json)
    {
        var dir = Directory.CreateTempSubdirectory("stabby-parity").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "biomes.json"), json);
            Assert.NotNull(Record.Exception(() => Legacy.ContentLoader.LoadBiomes(dir)));
            Assert.IsType<InvalidDataException>(Record.Exception(() => ContentLoader.LoadBiomes(dir)));
        }
        finally { Directory.Delete(dir, true); }
    }
}
