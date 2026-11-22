using System.Text.Json;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class CreatureTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<string> BiomeIds = ContentLoader.LoadBiomes(Root).Select(b => b.Id).ToList();
    static readonly CreatureLibrary Lib = ContentLoader.LoadCreatures(Root, BiomeIds);

    static void Matches(JsonElement schema, JsonElement data)
    {
        var names = data.EnumerateObject().Select(p => p.Name).ToList();
        if (schema.TryGetProperty("required", out var req)) Assert.Empty(req.EnumerateArray().Select(e => e.GetString()!).Except(names));
        Assert.Empty(names.Except(schema.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public void CreatureContentMatchesSchema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "schemas", "creatures.schema.json")));
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "creatures.json")));
        var top = schema.RootElement.GetProperty("properties");
        Assert.Equal(ContentLoader.CreatureSchemaVersion, top.GetProperty("schemaVersion").GetProperty("const").GetInt32());
        Matches(schema.RootElement, data.RootElement);
        var part = top.GetProperty("parts").GetProperty("items");
        var seg = part.GetProperty("properties").GetProperty("segments").GetProperty("items");
        foreach (var p in data.RootElement.GetProperty("parts").EnumerateArray())
        {
            Matches(part, p);
            foreach (var s in p.GetProperty("segments").EnumerateArray()) Matches(seg, s);
        }
        var body = top.GetProperty("bodies").GetProperty("items");
        var bp = body.GetProperty("properties");
        foreach (var b in data.RootElement.GetProperty("bodies").EnumerateArray())
        {
            Matches(body, b);
            Matches(bp.GetProperty("colors"), b.GetProperty("colors"));
            Matches(bp.GetProperty("detail"), b.GetProperty("detail"));
            foreach (var s in b.GetProperty("slots").EnumerateArray()) Matches(bp.GetProperty("slots").GetProperty("items"), s);
        }
    }

    [Fact]
    public void DeerLoadsFromSharedParts()
    {
        var deer = Lib.Body("deer");
        Assert.NotNull(deer);
        Assert.Contains("temperate-forest", deer!.Biomes);
        Assert.Equal(0x8c5e38u, deer.Base);
        Assert.Equal(2, deer.HerdMin);
        Assert.Equal(5, deer.HerdMax);
        Assert.Equal(CreaturePartDef.Leg, Lib.Part("hoof-hindleg")!.Role);
        Assert.Equal(CreatureSegmentDef.ToneDark, Lib.Part("eye")!.Segments[0].Tone);
        Assert.Empty(CreatureLibrary.Validate(Lib, BiomeIds));
        Assert.True(deer.FadePixels < deer.AnimPixels && deer.AnimPixels < deer.IkPixels);
    }

    [Fact]
    public void ValidateRejectsBrokenDefinitions()
    {
        const string json = """
        { "schemaVersion": 1,
          "parts": [ { "id": "leg", "role": "leg", "segments": [ { "length": 0.3, "radius": [0.05, 0.04] } ] } ],
          "bodies": [ { "id": "x", "name": "X", "biomes": ["nowhere"], "length": 0.1, "radius": 0.2,
            "colors": { "base": "#000000", "accent": "#ffffff", "dark": "#101010" },
            "size": [1, 0.5], "walkSpeed": 1, "herd": [1, 2],
            "detail": { "fadePixels": 10, "shadowPixels": 20, "animPixels": 5, "ikPixels": 30 },
            "slots": [ { "part": "tail", "socket": [0, 0, 0] }, { "part": "leg", "parent": "head", "socket": [0, 0] } ] } ] }
        """;
        var ex = Assert.Throws<InvalidDataException>(() => ContentLoader.ParseCreatures(json, BiomeIds, "test"));
        foreach (var bit in new[] { "knee", "unknown biome", "shorter than its ends", "size range", "detail pixels", "unknown part", "not an earlier slot", "3 numbers", "hang from the body" })
            Assert.Contains(bit, ex.Message);
        Assert.Throws<InvalidDataException>(() => ContentLoader.ParseCreatures(json.Replace("\"name\"", "\"nmae\""), BiomeIds, "test"));
    }

    [Fact]
    public void AssemblyIsDeterministicAndVariesBySeed()
    {
        var deer = Lib.Body("deer")!;
        var a = CreatureBody.Assemble(deer, Lib, 42);
        var b = CreatureBody.Assemble(deer, Lib, 42);
        var c = CreatureBody.Assemble(deer, Lib, 43);
        Assert.Equal(a.Size, b.Size);
        Assert.Equal(a.Base, b.Base);
        Assert.Equal(a.Segments.Select(s => s.Length), b.Segments.Select(s => s.Length));
        Assert.NotEqual(a.Size, c.Size);
        Assert.NotEqual(a.Base, c.Base);
        Assert.InRange(a.Size, deer.SizeMin, deer.SizeMax);
        Assert.Equal(CreatureBody.Shapes(deer, Lib).Count - 1, a.Segments.Max(s => s.Shape));
    }

    [Fact]
    public void MirroredPartsFlipAndChildrenFollowTheirParent()
    {
        var body = CreatureBody.Assemble(Lib.Body("deer")!, Lib, 7);
        Assert.Equal(11, body.Parts.Count);
        var legs = body.Parts.Where(p => p.Role == CreaturePartDef.Leg).ToList();
        Assert.Equal(4, legs.Count);
        Assert.Equal(legs[0].SocketX, -legs[1].SocketX, 5);
        Assert.Equal(0.5f, Math.Abs(legs[0].Phase - legs[1].Phase), 5);
        Assert.True(legs[0].SocketZ < 0 && legs[2].SocketZ > 0);
        int head = body.Parts.FindIndex(p => p.Role == CreaturePartDef.Head);
        var ears = body.Parts.Where(p => p.Role == CreaturePartDef.Ear).ToList();
        Assert.Equal(2, ears.Count);
        Assert.All(ears, e => Assert.Equal(head, e.Parent));
        Assert.Equal(ears[0].SocketX, -ears[1].SocketX, 5);
        Assert.Equal(ears[0].RestY, -ears[1].RestY, 5);
        Assert.All(body.Parts.Where(p => p.Role == CreaturePartDef.Eye), e => Assert.Equal(head, e.Parent));
        var first = body.Segments[legs[0].FirstSegment];
        var mirror = body.Segments[legs[1].FirstSegment];
        Assert.Equal(first.Shape, mirror.Shape);
        Assert.Equal(first.Idle[0], mirror.Idle[0]);
    }
}
