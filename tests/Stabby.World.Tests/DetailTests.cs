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
}
