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
            Matches(bp.GetProperty("materials"), b.GetProperty("materials"));
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
        Assert.Equal(ContentLoader.LoadMaterials(Root).Find("fawn-hide")!.Color, deer.Base);
        Assert.Equal(2, deer.HerdMin);
        Assert.Equal(5, deer.HerdMax);
        Assert.Equal(CreaturePartDef.Leg, Lib.Part("hoof-hindleg")!.Role);
        Assert.Equal(CreatureSegmentDef.ToneDark, Lib.Part("eye")!.Segments[0].Tone);
        Assert.Empty(CreatureLibrary.Validate(Lib, BiomeIds));
        Assert.True(deer.FadePixels < deer.AnimPixels && deer.AnimPixels < deer.IkPixels);
        Assert.True(deer.LowPixels >= 2 * deer.FadePixels);
        Assert.True(deer.SkinPixels > deer.LowPixels);
        Assert.All(Lib.Bodies, b => Assert.Equal(b.AnimPixels, b.ThinkPixels));
        Assert.Equal(Detail.SimulationReach(deer.Length, deer.AnimPixels), CreaturePlan.ThinkReach(deer));
        var trunk = CreatureBody.Shapes(deer, Lib)[0];
        Assert.True(trunk.Haunch > 0 && trunk.Waist > 0 && trunk.Hump > 0);
    }

    [Fact]
    public void ValidateRejectsBrokenDefinitions()
    {
        const string json = """
        { "schemaVersion": 2,
          "parts": [ { "id": "leg", "role": "leg", "segments": [ { "length": 0.3, "radius": [0.05, 0.04] } ] } ],
          "bodies": [ { "id": "x", "name": "X", "biomes": ["nowhere"], "length": 0.1, "radius": 0.2, "hump": 0.7,
            "materials": { "base": "fawn-hide", "accent": "pale-fur", "dark": "no-such-fur" },
            "size": [1, 0.5], "walkSpeed": 1, "herd": [1, 20], "herdChance": 1.5,
            "detail": { "spawnPixels": 12, "fadePixels": 10, "shadowPixels": 20, "animPixels": 5, "ikPixels": 30, "lowPixels": 15, "skinPixels": 10 },
            "wary": { "alertPixels": 20, "fleePixels": 15, "calmPixels": 10, "runSpeed": 0.5, "runStride": 1, "dart": 90 },
            "slots": [ { "part": "tail", "socket": [0, 0, 0] }, { "part": "leg", "parent": "head", "socket": [0, 0] } ] } ] }
        """;
        var materials = ContentLoader.LoadMaterials(Root);
        var ex = Assert.Throws<InvalidDataException>(() => ContentLoader.ParseCreatures(json, BiomeIds, "test", materials));
        foreach (var bit in new[] { "knee", "unknown biome", "shorter than its ends", "size range", "detail pixels", "unknown part", "not an earlier slot", "3 numbers", "hang from the body", "lowPixels must be", "skinPixels must be", "haunch, waist and hump", "herd above", "herdChance out of", "spawnPixels must be", "thinkPixels must stay above", "fleePixels must be above", "calmPixels must stay above", "runSpeed must be above", "maxSlope out of", "unknown material \"no-such-fur\"", "dart out of" })
            Assert.Contains(bit, ex.Message);
        Assert.Throws<InvalidDataException>(() => ContentLoader.ParseCreatures(json.Replace("\"name\"", "\"nmae\""), BiomeIds, "test", materials));
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

    [Fact]
    public void YoungShareTheBodyWithAgeProportions()
    {
        var deer = Lib.Body("deer")!;
        var adult = CreatureBody.Assemble(deer, Lib, 7);
        var young = CreatureBody.Assemble(deer, Lib, 7, 0.3f);
        Assert.Equal(adult.Parts.Count, young.Parts.Count);
        Assert.Equal(adult.Segments.Select(s => s.Shape), young.Segments.Select(s => s.Shape));
        Assert.Equal(adult.WalkSpeed, young.WalkSpeed);
        Assert.InRange(young.Size / adult.Size, deer.YoungSize - 0.01f, deer.YoungSize + 0.01f);
        float Ratio(int role, Func<CreatureSegmentInstance, float> of)
        {
            int part = adult.Parts.FindIndex(p => p.Role == role);
            int seg = adult.Parts[part].FirstSegment;
            return of(young.Segments[seg]) / young.Size / (of(adult.Segments[seg]) / adult.Size);
        }
        Assert.True(Ratio(CreaturePartDef.Head, s => s.Radius) > 1.2f);
        Assert.True(Ratio(CreaturePartDef.Ear, s => s.Radius) > 1.2f);
        Assert.True(Ratio(CreaturePartDef.Leg, s => s.Length) > 1.2f);
        Assert.True(Ratio(CreaturePartDef.Neck, s => s.Length) < 0.8f);
        var boar = Lib.Body("boar")!;
        var tusks = boar.Slots.FindIndex(s => s.Adult);
        Assert.True(tusks >= 0);
        var piglet = CreatureBody.Assemble(boar, Lib, 7, 0.3f);
        var sow = CreatureBody.Assemble(boar, Lib, 7);
        Assert.Contains(piglet.Segments.Zip(sow.Segments), p => p.First.Radius < p.Second.Radius * 0.1f);
    }

    [Fact]
    public void HerdsCarryYoungLedByAnAdult()
    {
        var plan = CreaturePlan.Build(PlanWorld, Lib);
        var young = plan.Where(s => s.Lead >= 0).ToList();
        Assert.NotEmpty(young);
        Assert.Contains(young, s => Lib.Bodies[s.Body].Id == "deer");
        Assert.DoesNotContain(young, s => Lib.Bodies[s.Body].YoungShare <= 0);
        var e = new List<string>();
        WorldChecks.YoungHaveAdults(Lib, plan, e);
        Assert.Empty(e);
        var orphan = young[0];
        var broken = plan.Where(s => s.Id != orphan.Lead).ToList();
        WorldChecks.YoungHaveAdults(Lib, broken, e);
        Assert.Contains(e, m => m.Contains("young creature " + orphan.Id + " has no adult"));
    }

    static readonly GeneratedWorld PlanWorld = TestWorlds.Default.W;
    static readonly NearArrays PlanCells = TestWorlds.Default.Detail.Cells;
    static readonly float[] PlanPools = Pools(TestWorlds.Default);

    static float[] Pools(TestWorlds.Placed p) =>
        CreaturePlan.Pools(new ObjectField(p.W, p.Detail, p.Edits, TestWorlds.Objects, p.Sites), CaveBrooks.Build(p.W, p.Detail, p.Edits, p.Graphs));

    [Fact]
    public void SpringAndMudPoolsGetSparseDrinkSpots()
    {
        Assert.True(PlanPools.Length > 0, "default world has no spring or mud pool");
        CreaturePlan.Build(PlanWorld, PlanCells, Lib, PlanPools, out var drinks);
        var bare = CreaturePlan.Drinks(PlanWorld, PlanCells, Lib, null);
        int pooled = drinks.Count - bare.Count, pools = PlanPools.Length / CreaturePlan.PoolStride;
        Assert.InRange(pooled, 1, pools);
        for (int k = 0; k < drinks.Count; k++)
        {
            var s = drinks.Spots[k];
            if (CreaturePlan.Bank(PlanWorld, s.X, s.Z, s.WaterX, s.WaterZ)) continue;
            Assert.True(CreaturePlan.AtPool(PlanWorld, PlanCells, PlanPools, s.X, s.Z, s.WaterX, s.WaterZ), "pool drink spot at " + (int)s.X + "," + (int)s.Z + " is not at a pool edge");
            Assert.InRange(s.WaterX * s.WaterX + s.WaterZ * s.WaterZ, 0.99f, 1.01f);
        }
    }

    [Fact]
    public void SpawnPlanPassesWorldChecks()
    {
        Assert.Empty(WorldChecks.Creatures(PlanWorld, Lib));
        var plan = CreaturePlan.Build(PlanWorld, PlanCells, Lib, PlanPools);
        Assert.NotEmpty(plan);
        var deer = Lib.Body("deer")!;
        foreach (var s in plan) Assert.Equal(CreatureBody.Assemble(Lib.Bodies[s.Body], Lib, s.BodySeed).WalkSpeed, s.WalkSpeed);
        Assert.True(CreaturePlan.DespawnReach(deer) > CreaturePlan.SpawnReach(deer));
        Assert.InRange(CreaturePlan.SpawnReach(deer), 150f, 400f);
    }

    [Fact]
    public void HerdsFindWaterGrassAndBeds()
    {
        var plan = CreaturePlan.Build(PlanWorld, PlanCells, Lib, PlanPools);
        var herds = plan.GroupBy(s => CreaturePlan.HerdOf(s.Id)).Select(g => g.First()).ToList();
        foreach (var body in Enumerable.Range(0, Lib.Bodies.Count))
        {
            var mine = herds.Where(h => h.Body == body).ToList();
            if (mine.Count == 0) continue;
            Assert.True(mine.Count(h => h.Needs.Can(Drives.Drink)) * 10 >= mine.Count, Lib.Bodies[body].Id + " rarely finds water");
            Assert.True(mine.Count(h => h.Needs.Can(Drives.Bed)) * 10 >= mine.Count * 9, Lib.Bodies[body].Id + " rarely finds a bed");
        }
        Assert.Contains(herds, h => h.Needs.Can(Drives.Graze));
    }

    [Theory]
    [InlineData("deer")]
    [InlineData("hare")]
    [InlineData("boar")]
    public void DrivesRiseSateAndChooseByPriority(string id)
    {
        var drives = Lib.Body(id)!.Drives;
        var all = new HerdNeeds { Has = HerdNeeds.HasGraze | HerdNeeds.HasDrink | HerdNeeds.HasBed };
        var levels = new DriveLevels();
        Assert.Equal(Drives.None, Drives.Choose(levels, drives, all, Drives.None));
        levels.Hunger = 1; levels.Thirst = 1; levels.Rest = 1;
        Assert.Equal(Drives.Drink, Drives.Choose(levels, drives, all, Drives.None));
        Assert.Equal(Drives.Graze, Drives.Choose(levels, drives, new HerdNeeds { Has = HerdNeeds.HasGraze | HerdNeeds.HasBed }, Drives.None));
        Assert.Equal(Drives.Bed, Drives.Choose(levels, drives, all, Drives.Bed));
        Drives.Advance(ref levels, drives, Drives.Drink, drives.Thirst.Sate * 2);
        Assert.Equal(0f, levels.Thirst);
        Assert.Equal(1f, levels.Hunger);
        Assert.Equal(Drives.Graze, Drives.Choose(levels, drives, all, Drives.None));
        levels.Thirst = 0;
        Assert.Equal(Drives.Graze, Drives.Choose(levels, drives, all, Drives.Drink));
        for (double t = 0; t < 20000; t += 37)
        {
            float v = Drives.At(drives.Hunger, t, 0.3f, out bool acting);
            Assert.InRange(v, Drives.Done - 1e-3f, drives.Hunger.Threshold + 1e-3f);
            Drives.Wake(drives, all, t, 0x1234_5678_9ABC_DEF0UL, 0xA5A5A5A5u, out var w);
            for (int i = 0; i < Drives.Count; i++) Assert.InRange(w[i], 0f, 1f);
        }
        float a = Drives.At(drives.Thirst, 1000, 0.5f, out _), b = Drives.At(drives.Thirst, 1001, 0.5f, out _);
        Assert.True(Math.Abs(b - a) <= Math.Max(drives.Thirst.Rise, drives.Thirst.Drop) * 1.01f);
    }

    [Theory]
    [InlineData("deer")]
    [InlineData("hare")]
    [InlineData("boar")]
    public void EachSpeciesLivesInItsBiomesBelowItsMaxSlope(string id)
    {
        var cells = new TerrainDetail(PlanWorld).Cells;
        var plan = CreaturePlan.Build(PlanWorld, cells, Lib, PlanPools);
        int body = Lib.Bodies.FindIndex(b => b.Id == id);
        var def = Lib.Bodies[body];
        var mine = plan.Where(s => s.Body == body).ToList();
        Assert.NotEmpty(mine);
        float cm = PlanWorld.Config.CellMeters, grade = Footing.Grade(def.MaxSlope);
        foreach (var s in mine)
        {
            Assert.False(Footing.SteepAt(cells, s.X, s.Z, grade), id + " " + s.Id + " wakes on a slope");
            Assert.True(CreaturePlan.Habitable(PlanWorld, cells, def, (int)(s.Route.X0 / cm), (int)(s.Route.Z0 / cm)), id + " " + s.Id + " home");
        }
        var herds = mine.GroupBy(s => CreaturePlan.HerdOf(s.Id)).Select(h => h.Count()).ToList();
        Assert.All(herds, c => Assert.InRange(c, def.HerdMin, def.HerdMax));
        Assert.Empty(CreatureLibrary.Validate(Lib, BiomeIds));
    }

    [Fact]
    public void HareDartsAndFleesEarlyBoarStandsLonger()
    {
        var deer = CreaturePlan.Wary(Lib.Body("deer")!);
        var hare = CreaturePlan.Wary(Lib.Body("hare")!);
        var boar = CreaturePlan.Wary(Lib.Body("boar")!);
        Assert.True(hare.Flee > deer.Flee && boar.Flee < deer.Flee);
        Assert.True(hare.Dart > 0 && deer.Dart == 0 && boar.Dart == 0);
        Assert.True(Lib.Body("hare")!.RunSpeed > Lib.Body("deer")!.RunSpeed);
        Assert.True(Lib.Body("boar")!.HerdMax > Lib.Body("deer")!.HerdMax && Lib.Body("hare")!.HerdMax <= 2);
        foreach (var b in Lib.Bodies) Assert.True(CreatureBody.Assemble(b, Lib, 3).Segments.Count <= CreatureBody.Assemble(Lib.Body("deer")!, Lib, 3).Segments.Count, b.Id);
    }

    [Fact]
    public void HerdsWakeAndGrazeBelowTheirMaxSlope()
    {
        var cells = new TerrainDetail(PlanWorld).Cells;
        var strict = ContentLoader.LoadCreatures(Root, BiomeIds);
        var deer = strict.Body("deer")!;
        deer.MaxSlope = 8;
        var loose = CreaturePlan.Build(PlanWorld, cells, Lib, PlanPools);
        var tight = CreaturePlan.Build(PlanWorld, cells, strict, PlanPools);
        Assert.NotEmpty(tight);
        Assert.True(tight.Count < loose.Count, tight.Count + " herd members at 8 degrees, " + loose.Count + " at the content limit");
        float grade = Footing.Grade(deer.MaxSlope), cm = PlanWorld.Config.CellMeters;
        int deerIndex = strict.Bodies.IndexOf(deer);
        foreach (var s in tight.Where(s => s.Body == deerIndex))
        {
            Assert.False(Footing.SteepAt(cells, s.X, s.Z, grade), "creature " + s.Id + " wakes on a slope");
            for (int i = 0; i < HerdRoute.Points; i++)
                Assert.True(CreaturePlan.Habitable(PlanWorld, cells, deer, (int)(s.Route.X(i) / cm), (int)(s.Route.Z(i) / cm)), "creature " + s.Id + " route point " + i);
        }
        Assert.Equal(1f, Footing.Grade(45), 4);
    }

    [Fact]
    public void SpawnPlanVariesBySeed()
    {
        var other = TestWorlds.World(4);
        var a = CreaturePlan.Build(PlanWorld, PlanCells, Lib, PlanPools);
        var b = CreaturePlan.Build(other, new TerrainDetail(other).Cells, Lib, null);
        Assert.False(a.Count == b.Count && a.Zip(b).All(p => p.First.X == p.Second.X && p.First.BodySeed == p.Second.BodySeed));
    }

    [Fact]
    public void HerdsShareOneSeedFromTheirOwnStream()
    {
        var plan = CreaturePlan.Build(PlanWorld, PlanCells, Lib, PlanPools);
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
        public bool Crosses(float x0, float z0, float x1, float z1) => Blocked(x0, z0) || Blocked(x1, z1);
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

    [Fact]
    public void ClearHeadingsNeverCutAWetCorner()
    {
        const int n = 16;
        const float cm = 11.25f, look = 26f;
        var ground = new WaterGround { Wet = new byte[n * n], Size = n, CellMeters = cm };
        for (int z = 8; z < n; z++)
            for (int x = 8; x < n; x++) ground.Wet[z * n + x] = 1;
        float hx = 7.9f * cm, hz = 8.2f * cm;
        for (int k = 0; k < 720; k++)
        {
            float h = k * (float)Math.PI / 360f;
            int clear = HerdMind.ClearRun(h, hx, hz, look, ref ground);
            float reach = clear * look / HerdMind.Probes;
            for (float d = 0.05f; d <= reach; d += 0.05f)
                Assert.False(ground.Blocked(hx + (float)Math.Sin(h) * d, hz + (float)Math.Cos(h) * d), "heading " + h + " clear " + clear + " but wet at " + d + " m");
        }
    }
}
