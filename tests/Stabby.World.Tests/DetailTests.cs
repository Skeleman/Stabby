using Stabby.World;

namespace Stabby.World.Tests;

public class DetailTests
{
    [Fact]
    public void ReferencePixelsMatchTheReferenceView() =>
        Assert.Equal(Detail.ReferencePixelsPerMetre, Detail.PixelsPerMetreAt(Detail.ReferenceFov), 2);

    [Theory]
    [InlineData(0.6f, 80f)]
    [InlineData(2f, 5f)]
    [InlineData(30f, 900f)]
    public void ReachInvertsScreenSize(float size, float distance)
    {
        float ppm = Detail.PixelsPerMetreAt(75f);
        float px = Detail.ScreenSize(size, distance, ppm);
        Assert.Equal(distance, Detail.Reach(size, px, ppm), 2);
        Assert.Equal(px, Detail.ScreenSize(size, Detail.Reach(size, px, ppm), ppm), 2);
    }

    [Fact]
    public void NarrowerViewLooksBigger() =>
        Assert.True(Detail.ScreenSize(1f, 50f, Detail.PixelsPerMetreAt(30f)) > Detail.ScreenSize(1f, 50f, Detail.PixelsPerMetreAt(60f)));

    [Fact]
    public void TierHasHysteresis()
    {
        var t = new Detail.Thresholds(100f, 40f, 10f);
        Assert.Equal(1, Detail.Tier(90f, t, -1));
        Assert.Equal(0, Detail.Tier(101f, t, 1));
        Assert.Equal(0, Detail.Tier(90f, t, 0));
        Assert.Equal(1, Detail.Tier(84f, t, 0));
        Assert.Equal(1, Detail.Tier(36f, t, 1));
        Assert.Equal(2, Detail.Tier(33f, t, 1));
        Assert.Equal(3, Detail.Tier(8f, t, 2));
        Assert.Equal(2, Detail.Tier(9f, t, 2));
        Assert.Equal(2, Detail.Tier(11f, t, 3));
        Assert.Equal(3, Detail.Tier(9f, t, 3));
    }

    [Fact]
    public void ScaledMovesBoundaries()
    {
        var t = new Detail.Thresholds(100f, 40f, 10f).Scaled(2f);
        Assert.Equal(50f, t.Near);
        Assert.Equal(20f, t.Mid);
        Assert.Equal(5f, t.Far);
        Assert.Equal(0, Detail.Tier(60f, t, -1));
        Assert.Equal(1, Detail.Tier(60f, new Detail.Thresholds(100f, 40f, 10f), -1));
    }

    [Fact]
    public void HoldsAndCrossfade()
    {
        Assert.False(Detail.Holds(90f, 100f, false));
        Assert.True(Detail.Holds(90f, 100f, true));
        Assert.False(Detail.Holds(84f, 100f, true));
        Assert.Equal(0f, Detail.Crossfade(1f, 2f, 4f));
        Assert.Equal(0.5f, Detail.Crossfade(3f, 2f, 4f), 4);
        Assert.Equal(1f, Detail.Crossfade(5f, 2f, 4f));
    }

    [Fact]
    public void PixelsMatchTheOldScreenFractions()
    {
        float invTan = 1f / (2f * MathF.Tan(MathF.PI / 6f));
        Assert.Equal(2f * 3f * invTan / 0.006f, Detail.SimulationReach(2f * 3f, 0.006f * Detail.ReferenceLines), 2);
        Assert.Equal(0.01f * 2f * MathF.Tan(MathF.PI / 6f), new TerrainViewDef().Allowed(Detail.ReferencePixelsPerMetre, 1f), 5);
        var grass = new GrassDef();
        Assert.Equal(5f, grass.ReachAt(grass.NearPixels, Detail.ReferencePixelsPerMetre), 1);
        Assert.Equal(10f, grass.ReachAt(grass.Lod1Pixels, Detail.ReferencePixelsPerMetre), 1);
        Assert.Equal(25f, grass.ReachAt(grass.Lod2Pixels, Detail.ReferencePixelsPerMetre), 1);
    }

    [Fact]
    public void QualityShrinksTerrainError() =>
        Assert.True(new TerrainViewDef().Allowed(Detail.ReferencePixelsPerMetre, 2f) < new TerrainViewDef().Allowed(Detail.ReferencePixelsPerMetre, 1f));
    static readonly Detail.Rule Tree = new Detail.Rule(new Detail.Thresholds(100f, 40f, 10f), 60f, 30f,
        new Detail.Thresholds(50f, 50f, 15f), new Detail.Thresholds(400f, 100f, 10f));

    static float At(float px) => Detail.Reach(2f, px, Detail.ReferencePixelsPerMetre);

    static Detail.Decision Decide(float px, Detail.Decision previous, float quality = 1f) =>
        Detail.Decide(Tree, 2f, At(px), Detail.ReferencePixelsPerMetre, quality, previous);

    [Fact]
    public void DecisionPacksEveryAnswer()
    {
        var d = new Detail.Decision(2, Detail.ShadowCanopy, Detail.AnimSway, Detail.LightEmissive);
        Assert.False(d.IsFirst);
        Assert.Equal(2, d.Geometry);
        Assert.Equal(Detail.ShadowCanopy, d.Shadow);
        Assert.Equal(Detail.AnimSway, d.Animation);
        Assert.Equal(Detail.LightEmissive, d.Light);
        Assert.True(default(Detail.Decision).IsFirst);
        Assert.Equal(-1, default(Detail.Decision).Shadow);
        Assert.False(default(Detail.Decision).Casts);
    }

    [Theory]
    [InlineData(500f, 0, Detail.ShadowCascade, Detail.AnimFull, Detail.LightShadowed)]
    [InlineData(80f, 1, Detail.ShadowCascade, Detail.AnimFull, Detail.LightGlow)]
    [InlineData(45f, 1, Detail.ShadowLow, Detail.AnimSway, Detail.LightGlow)]
    [InlineData(20f, 2, Detail.ShadowLow, Detail.AnimSway, Detail.LightGlow)]
    [InlineData(12f, 2, Detail.ShadowLow, Detail.AnimFrozen, Detail.LightGlow)]
    [InlineData(5f, 3, Detail.ShadowNone, Detail.AnimFrozen, Detail.LightEmissive)]
    public void DecideAnswersEachTierBySize(float px, int geometry, int shadow, int anim, int light)
    {
        var d = Decide(px, default);
        Assert.Equal(geometry, d.Geometry);
        Assert.Equal(shadow, d.Shadow);
        Assert.Equal(anim, d.Animation);
        Assert.Equal(light, d.Light);
        Assert.Equal(shadow < Detail.ShadowNone, d.Casts);
    }

    [Fact]
    public void DecideHoldsShadowTiersWithHysteresis()
    {
        var cascade = Decide(61f, default);
        Assert.Equal(Detail.ShadowCascade, cascade.Shadow);
        Assert.Equal(Detail.ShadowCascade, Decide(55f, cascade).Shadow);
        Assert.Equal(Detail.ShadowLow, Decide(50f, cascade).Shadow);
        var low = Decide(31f, default);
        Assert.Equal(Detail.ShadowLow, Decide(15f, low).Shadow);
        Assert.Equal(Detail.ShadowCascade, Decide(61f, low).Shadow);
    }

    static Detail.ShadowMap Flat(float texelM) => new Detail.ShadowMap
    {
        Count = 1, Range = 1000f, Far0 = 1000f, Far1 = 1000f, Far2 = 1000f,
        Texel0 = texelM, Texel1 = texelM, Texel2 = texelM, Texel3 = texelM, Texels = 1f,
    };

    static Detail.Decision Decide(float px, in Detail.ShadowMap map, Detail.Decision previous, float quality = 1f) =>
        Detail.Decide(Tree, 2f, At(px), Detail.ReferencePixelsPerMetre, quality, map, previous);

    [Fact]
    public void ShadowsStopOnlyUnderAboutOneTexelWithHysteresis()
    {
        var on = Decide(45f, Flat(1.9f), default);
        Assert.Equal(Detail.ShadowLow, on.Shadow);
        Assert.Equal(Detail.ShadowNone, Decide(45f, Flat(2.2f), default).Shadow);
        Assert.Equal(Detail.ShadowLow, Decide(45f, Flat(2.2f), on).Shadow);
        Assert.Equal(Detail.ShadowNone, Decide(45f, Flat(2.5f), on).Shadow);
        Assert.True(Decide(45f, Flat(2.2f), default, 2f).Casts);
        Assert.Equal(Detail.ShadowLow, Decide(15f, Flat(1.9f), on).Shadow);
        var never = new Detail.Rule(Tree.Geometry, Detail.Never, Detail.Never, Tree.Animation, Tree.Light);
        Assert.Equal(Detail.ShadowNone, Detail.Decide(never, 2f, 1f, Detail.ReferencePixelsPerMetre, 1f, Flat(0.01f), default).Shadow);
    }

    [Fact]
    public void ShadowMapTexelGrowsPerCascadeAndShrinksWithResolution()
    {
        var c = Detail.CascadeSplits(10f, 400f);
        var m = Detail.ShadowMap.Build(c, 400f, 1024, Detail.ReferencePixelsPerMetre, 16f / 9f, 1f);
        Assert.Equal(c.Count, m.Count);
        Assert.True(m.Texel0 > 0 && m.Texel0 < m.Texel1 && m.Texel1 < m.Texel2);
        Assert.Equal(m.Texel0, m.TexelAt(1f));
        Assert.Equal(m.Texel2, m.TexelAt(399f));
        Assert.Equal(m.Texel2, m.TexelAt(5000f));
        var fine = Detail.ShadowMap.Build(c, 400f, 2048, Detail.ReferencePixelsPerMetre, 16f / 9f, 1f);
        Assert.Equal(0.5f * m.Texel2, fine.Texel2, 4);
        var one = Detail.ShadowMap.Build(new Detail.Cascades { Count = 1 }, 60f, 2048, Detail.ReferencePixelsPerMetre, 16f / 9f, 1f);
        Assert.Equal(one.Texel0, one.TexelAt(59f));
        Assert.True(default(Detail.ShadowMap).Holds(1e-4f, 1e4f, 1f, false));
        Assert.True(m.Holds(2f * m.Texel2, 399f, 1f, false));
        Assert.False(m.Holds(0.5f * m.Texel2, 399f, 1f, true));
        Assert.True(m.Holds(0.5f * m.Texel2, 1f, 1f, false));
    }

    [Fact]
    public void DecideDropsShadowsOnceGeometryIsGone()
    {
        var always = new Detail.Rule(Tree.Geometry, 0.001f, 0.001f, Tree.Animation, Tree.Light);
        var d = Detail.Decide(always, 2f, At(5f), Detail.ReferencePixelsPerMetre, 1f, default);
        Assert.Equal(Detail.GeometryGone, d.Geometry);
        Assert.Equal(Detail.ShadowNone, d.Shadow);
    }

    [Fact]
    public void QualityScalesEveryAnswer()
    {
        var d = Decide(45f, default, 3f);
        Assert.Equal(0, d.Geometry);
        Assert.Equal(Detail.ShadowCascade, d.Shadow);
        Assert.Equal(Detail.AnimFull, d.Animation);
        Assert.Equal(Detail.LightUnshadowed, d.Light);
    }

    [Fact]
    public void NeverRulesStayAtTheLastTier()
    {
        var never = new Detail.Thresholds(Detail.Never, Detail.Never, Detail.Never);
        var r = new Detail.Rule(Tree.Geometry, Detail.Never, Detail.Never, never, never);
        var d = Detail.Decide(r, 2f, 0.5f, Detail.ReferencePixelsPerMetre, 0.5f, default);
        Assert.Equal(Detail.ShadowNone, d.Shadow);
        Assert.Equal(Detail.AnimFrozen, d.Animation);
        Assert.Equal(Detail.LightEmissive, d.Light);
        Assert.Equal(0f, Detail.ShadowReach(2f, r, Detail.ReferencePixelsPerMetre, 1f));
    }

    [Fact]
    public void ShadowReachInvertsTheShadowThresholds()
    {
        float ppm = Detail.ReferencePixelsPerMetre;
        float far = Detail.ShadowReach(2f, Tree, ppm, 1f), near = Detail.CascadeReach(2f, Tree, ppm, 1f);
        Assert.Equal(2f * ppm / 30f, far, 2);
        Assert.Equal(2f * ppm / 60f, near, 2);
        Assert.Equal(2f * far, Detail.ShadowReach(2f, Tree, ppm, 2f), 2);
        Assert.Equal(Detail.ShadowLow, Detail.Decide(Tree, 2f, far * 1.01f, ppm, 1f, default).Shadow);
    }

    [Fact]
    public void SnapRangeStepsAndHolds()
    {
        float a = Detail.SnapRange(100f, 0f);
        Assert.True(a >= 100f && a < 100f * Detail.RangeStep);
        Assert.Equal(a, Detail.SnapRange(99.5f, 0f));
        Assert.Equal(a, Detail.SnapRange(95f, a));
        Assert.True(Detail.SnapRange(80f, a) < a);
        Assert.True(Detail.SnapRange(a * 1.01f, a) > a);
        Assert.Equal(0f, Detail.SnapRange(0f, a));
    }

    [Theory]
    [InlineData(40f, 120f, 2)]
    [InlineData(10f, 400f, 3)]
    [InlineData(2f, 1000f, 4)]
    [InlineData(500f, 400f, 2)]
    public void CascadeSplitsAreGeometricFromNearToRange(float near, float range, int count)
    {
        var c = Detail.CascadeSplits(near, range);
        Assert.Equal(count, c.Count);
        float[] s = { c.Split1, c.Split2, c.Split3 };
        for (int i = 0; i < count - 1; i++)
        {
            Assert.True(s[i] > 0f && s[i] < 1f);
            if (i > 0) Assert.True(s[i] > s[i - 1]);
        }
        for (int i = count - 1; i < 3; i++) Assert.Equal(0f, s[i]);
        if (count > 2) Assert.Equal(c.Split2 / c.Split1, c.Split3 > 0 ? c.Split3 / c.Split2 : 1f / c.Split2, 3);
    }

    [Fact]
    public void KindAndCreatureRulesComeFromContent()
    {
        var kind = new ObjectKind { Id = "animated", Lods = 3, LodPixels = new[] { 108f, 32.4f }, FadePixels = 6.48f, ShadowPixels = 43.2f, ShadowCascadePixels = 86.4f, Wind = true };
        var light = new LightKind { Id = "point" };
        var r = kind.Rule(light);
        Assert.Equal(108f, r.Geometry.Near);
        Assert.Equal(32.4f, r.Geometry.Mid);
        Assert.Equal(6.48f, r.Geometry.Far);
        Assert.Equal(86.4f, r.ShadowCascadePixels);
        Assert.Equal(43.2f, r.ShadowPixels);
        Assert.Equal(kind.FlutterPixels, r.Animation.Near);
        Assert.Equal(kind.SwayPixels, r.Animation.Far);
        Assert.Equal(432f, r.Light.Near);
        Assert.Equal(108f, r.Light.Mid);
        Assert.Equal(10.8f, r.Light.Far);
        Assert.Equal(Detail.Never, kind.Rule(null).Light.Near);
        kind.Wind = false;
        Assert.Equal(Detail.Never, kind.Rule(light).Animation.Far);
        var c = new CreatureDef().Rule();
        Assert.Equal(64.8f, c.ShadowCascadePixels);
        Assert.Equal(64.8f, c.Animation.Near);
        Assert.Equal(10.8f, c.Animation.Mid);
    }

    [Fact]
    public void ShadowSpanCoversEveryCasterFromContent()
    {
        string root = Stabby.Content.ContentLoader.FindContentRoot(AppContext.BaseDirectory);
        var biomes = Stabby.Content.ContentLoader.LoadBiomes(root).Select(b => b.Id).ToList();
        var objects = Stabby.Content.ContentLoader.LoadObjects(root, biomes);
        var creatures = Stabby.Content.ContentLoader.LoadCreatures(root, biomes);
        float ppm = Detail.ReferencePixelsPerMetre;
        var span = new Detail.ShadowSpan();
        objects.AddShadows(ref span, ppm, 1f);
        creatures.AddShadows(ref span, ppm, 1f);
        Assert.True(span.Near > 0 && span.Near < span.Range);
        foreach (var d in objects.Objects)
        {
            float size = objects.CasterSize(d);
            if (size <= 0) continue;
            var rule = objects.RuleOf(objects.KindOf(d));
            float far = Detail.ShadowReach(size, rule, ppm, 1f);
            Assert.True(far / (1f - Detail.Hysteresis) <= span.Range * 1.0001f, d.Id);
            Assert.True(Detail.CascadeReach(size, rule, ppm, 1f) >= span.Near * 0.9999f, d.Id);
        }
        float range = Detail.SnapRange(span.Range, 0);
        var map = Detail.ShadowMap.Build(Detail.CascadeSplits(span.Near, range), range, 1024, ppm, 16f / 9f, objects.Terrain.ShadowTexels);
        Assert.Contains(objects.Objects, d => objects.CasterSize(d) > 0 && map.Holds(objects.CasterSize(d), range, 1f, false));
        var wide = new Detail.ShadowSpan();
        objects.AddShadows(ref wide, 2f * ppm, 1f);
        creatures.AddShadows(ref wide, 2f * ppm, 1f);
        Assert.Equal(2f * span.Range, wide.Range, 1);
        var c = Detail.CascadeSplits(span.Near, Detail.SnapRange(span.Range, 0));
        Assert.InRange(c.Count, 2, 4);
    }
}
