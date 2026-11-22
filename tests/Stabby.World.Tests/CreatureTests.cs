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
            Matches(bp.GetProperty("wary"), b.GetProperty("wary"));
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
        Assert.True(deer.LowPixels >= 2 * deer.FadePixels);
        var trunk = CreatureBody.Shapes(deer, Lib)[0];
        Assert.True(trunk.Haunch > 0 && trunk.Waist > 0 && trunk.Hump > 0);
    }

    [Fact]
    public void ValidateRejectsBrokenDefinitions()
    {
        const string json = """
        { "schemaVersion": 1,
          "parts": [ { "id": "leg", "role": "leg", "segments": [ { "length": 0.3, "radius": [0.05, 0.04] } ] } ],
          "bodies": [ { "id": "x", "name": "X", "biomes": ["nowhere"], "length": 0.1, "radius": 0.2, "hump": 0.7,
            "colors": { "base": "#000000", "accent": "#ffffff", "dark": "#101010" },
            "size": [1, 0.5], "walkSpeed": 1, "herd": [1, 20], "herdChance": 1.5,
            "detail": { "spawnPixels": 12, "fadePixels": 10, "shadowPixels": 20, "animPixels": 5, "ikPixels": 30, "lowPixels": 15 },
            "wary": { "alertPixels": 20, "fleePixels": 15, "calmPixels": 10, "runSpeed": 0.5, "runStride": 1 },
            "slots": [ { "part": "tail", "socket": [0, 0, 0] }, { "part": "leg", "parent": "head", "socket": [0, 0] } ] } ] }
        """;
        var ex = Assert.Throws<InvalidDataException>(() => ContentLoader.ParseCreatures(json, BiomeIds, "test"));
        foreach (var bit in new[] { "knee", "unknown biome", "shorter than its ends", "size range", "detail pixels", "unknown part", "not an earlier slot", "3 numbers", "hang from the body", "lowPixels must be", "haunch, waist and hump", "herd above", "herdChance out of", "spawnPixels must be", "fleePixels must be above", "calmPixels must stay above", "runSpeed must be above" })
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

    static readonly GeneratedWorld PlanWorld = WorldGenerator.Generate(
        new WorldConfig { Seed = 5, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) },
        ContentLoader.LoadBiomes(Root), ContentLoader.LoadMaterials(Root), null, null, ContentLoader.LoadObjects(Root, BiomeIds));

    [Fact]
    public void SpawnPlanPassesWorldChecks()
    {
        Assert.Empty(WorldChecks.Creatures(PlanWorld, Lib));
        var plan = CreaturePlan.Build(PlanWorld, Lib);
        Assert.NotEmpty(plan);
        var deer = Lib.Body("deer")!;
        foreach (var s in plan) Assert.Equal(CreatureBody.Assemble(deer, Lib, s.BodySeed).WalkSpeed, s.WalkSpeed);
        Assert.True(CreaturePlan.DespawnReach(deer) > CreaturePlan.SpawnReach(deer));
        Assert.InRange(CreaturePlan.SpawnReach(deer), 150f, 400f);
    }

    [Fact]
    public void SpawnPlanVariesBySeed()
    {
        var other = WorldGenerator.Generate(
            new WorldConfig { Seed = 6, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) },
            ContentLoader.LoadBiomes(Root), ContentLoader.LoadMaterials(Root), null, null, ContentLoader.LoadObjects(Root, BiomeIds));
        var a = CreaturePlan.Build(PlanWorld, Lib);
        var b = CreaturePlan.Build(other, Lib);
        Assert.False(a.Count == b.Count && a.Zip(b).All(p => p.First.X == p.Second.X && p.First.BodySeed == p.Second.BodySeed));
    }

    [Fact]
    public void HerdsShareOneSeedFromTheirOwnStream()
    {
        var plan = CreaturePlan.Build(PlanWorld, Lib);
        var herds = plan.GroupBy(s => CreaturePlan.HerdOf(s.Id)).ToList();
        Assert.Contains(herds, h => h.Count() > 1);
        foreach (var h in herds) Assert.Single(h.Select(s => s.HerdSeed).Distinct());
        Assert.Equal(herds.Count, herds.Select(h => h.First().HerdSeed).Distinct().Count());
        ulong root = CreaturePlan.Root(PlanWorld);
        Assert.All(herds, h => Assert.Equal(CreaturePlan.HerdSeed(root, h.Key), h.First().HerdSeed));
    }

    [Fact]
    public void WaryRadiiComeFromReachPixels()
    {
        var deer = Lib.Body("deer")!;
        var w = CreaturePlan.Wary(deer);
        Assert.True(w.Flee < w.Alert && w.Alert < w.Calm && w.Calm < CreaturePlan.SpawnReach(deer));
        Assert.Equal(Detail.SimulationReach(deer.Length, deer.AlertPixels), w.Alert);
        Assert.InRange(w.Alert, 30f, 60f);
        Assert.True(w.RunRatio > 3);
        var leg = CreatureBody.Assemble(deer, Lib, 7).Parts.Where(p => p.Role == CreaturePartDef.Leg).ToList();
        Assert.Equal(0.1f, Math.Abs(leg[0].RunPhase - leg[1].RunPhase), 5);
    }

    [Fact]
    public void HerdNoticesFreezesBoltsAndSettles()
    {
        var w = CreaturePlan.Wary(Lib.Body("deer")!);
        var mind = HerdMind.Start(CreaturePlan.HerdSeed(1, 2));
        var twin = mind;
        const float dt = 0.05f;
        float player = -90, herd = 0;
        var seen = new List<HerdMode>();
        bool rehomed = false;
        for (int i = 0; i < 2400 && !rehomed; i++)
        {
            if (mind.Mode != HerdMode.Flee) player += 4 * dt;
            if (mind.Mode == HerdMode.Flee) herd += 6.5f * dt * (float)Math.Cos(mind.Heading - Math.PI / 2);
            float threat = herd - player;
            rehomed = mind.Step(w, threat, threat, 0, dt);
            twin.Step(w, threat, threat, 0, dt);
            if (seen.Count == 0 || seen[^1] != mind.Mode) seen.Add(mind.Mode);
            if (mind.Mode == HerdMode.Alert && seen.Count == 2) Assert.InRange(threat, w.Flee, w.Alert);
        }
        Assert.True(rehomed);
        Assert.Equal(new[] { HerdMode.Calm, HerdMode.Alert, HerdMode.Flee, HerdMode.Alert }, seen);
        Assert.InRange(mind.Heading, Math.PI / 2 - HerdMind.Spread, Math.PI / 2 + HerdMind.Spread);
        Assert.Equal(mind.Heading, twin.Heading);
        Assert.True(herd - player > w.Calm);
        for (int i = 0; i < 200; i++) mind.Step(w, w.Calm * 2, 1, 0, dt);
        Assert.Equal(HerdMode.Calm, mind.Mode);
    }
    struct Shore : IHerdGround
    {
        public bool Blocked(float x, float z) => z > 30 || x < -200 || x > 200;
    }

    [Fact]
    public void FleeingHerdTurnsAlongTheShore()
    {
        var w = CreaturePlan.Wary(Lib.Body("deer")!);
        var ground = new Shore();
        const float dt = 0.05f;
        float x = 0, z = 0, maxZ = 0, run = 0;
        var mind = HerdMind.Start(CreaturePlan.HerdSeed(1, 3));
        for (int i = 0; i < 1200; i++)
        {
            float ax = x, az = z + w.Flee * 0.5f;
            mind.Step(w, (float)Math.Sqrt(ax * ax + az * az), ax, az, x, z, ref ground, dt);
            if (mind.Mode != HerdMode.Flee) continue;
            x += (float)Math.Sin(mind.Heading) * 6.5f * dt;
            z += (float)Math.Cos(mind.Heading) * 6.5f * dt;
            run += 6.5f * dt;
            maxZ = Math.Max(maxZ, z);
            Assert.False(ground.Blocked(x, z));
        }
        Assert.True(run > 40);
        Assert.True(maxZ > 5);
        Assert.True(Math.Abs(x) > 20);
    }
}
