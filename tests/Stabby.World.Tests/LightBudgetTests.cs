using Stabby.World;

namespace Stabby.World.Tests;

public class LightBudgetTests
{
    struct Slots : LightBudget.ISlots
    {
        public LightBudget.Slot[] Items;
        public int Count => Items.Length;
        public LightBudget.Slot this[int i] { get => Items[i]; set => Items[i] = value; }
    }

    static Slots Make(int n, float importance = 1f)
    {
        var s = new Slots { Items = new LightBudget.Slot[n] };
        for (int i = 0; i < n; i++)
        {
            var x = LightBudget.Slot.Fresh;
            x.Pixels = 1000f - i * 10f;
            x.Importance = importance;
            x.Wanted = Detail.LightShadowed;
            s.Items[i] = x;
        }
        return s;
    }

    static LightBudget.Counts Run(ref Slots s, in LightBudget.Caps caps, int frames, float dt = 1f / 60f)
    {
        var c = default(LightBudget.Counts);
        for (int f = 0; f < frames; f++)
        {
            LightBudget.Rank(ref s, caps);
            c = LightBudget.Step(ref s, dt);
            Assert.True(c.CastingShadow <= caps.Shadowed);
            Assert.True(c.Lit <= caps.Real);
            Assert.True(c.Glowing <= caps.Glow);
        }
        return c;
    }

    [Fact]
    public void CapsHoldAndLightsStepDownInsteadOfVanishing()
    {
        var s = Make(40);
        var caps = new LightBudget.Caps(2, 12, 16);
        var c = Run(ref s, caps, 120);
        Assert.Equal(2, c.Shadowed);
        Assert.Equal(12, c.Unshadowed);
        Assert.Equal(16, c.Glow);
        Assert.Equal(10, c.Emissive);
        Assert.Equal(new byte[] { 0, 0, 1 }, new[] { s.Items[0].Tier, s.Items[1].Tier, s.Items[2].Tier });
        Assert.Equal(Detail.LightGlow, s.Items[14].Tier);
        Assert.Equal(Detail.LightEmissive, s.Items[39].Tier);
        Assert.Equal(1f, s.Items[0].ShadowWeight);
        Assert.Equal(1f, s.Items[13].RealWeight);
        Assert.Equal(0f, s.Items[13].ShadowWeight);
    }

    [Fact]
    public void ImportanceScalesTheRankingAndScreenSizeLimitsTheTier()
    {
        var s = Make(6);
        s.Items[5].Importance = 10f;
        s.Items[0].Wanted = Detail.LightGlow;
        LightBudget.Rank(ref s, new LightBudget.Caps(1, 1, 8));
        Assert.Equal(Detail.LightShadowed, s.Items[5].Tier);
        Assert.Equal(Detail.LightGlow, s.Items[0].Tier);
        Assert.Equal(Detail.LightUnshadowed, s.Items[1].Tier);
    }

    [Fact]
    public void HeldTiersAreStickyAgainstSmallReshuffles()
    {
        var s = Make(2);
        var caps = new LightBudget.Caps(1, 0, 4);
        Run(ref s, caps, 60);
        Assert.Equal(Detail.LightShadowed, s.Items[0].Tier);
        s.Items[1].Pixels = s.Items[0].Pixels * 1.1f;
        Run(ref s, caps, 60);
        Assert.Equal(Detail.LightShadowed, s.Items[0].Tier);
        s.Items[1].Pixels = s.Items[0].Pixels * 1.3f;
        Run(ref s, caps, 1);
        Assert.Equal(Detail.LightShadowed, s.Items[1].Target);
        Assert.Equal(Detail.LightGlow, s.Items[1].Tier);
        Assert.Equal(Detail.LightGlow, s.Items[0].Tier);
        Run(ref s, caps, 60);
        Assert.Equal(Detail.LightShadowed, s.Items[1].Tier);
    }

    [Fact]
    public void SwapsCrossfadeAndNeverExceedTheCapMidFade()
    {
        var s = Make(2);
        var caps = new LightBudget.Caps(0, 1, 4);
        Run(ref s, caps, 60);
        Assert.Equal(Detail.LightUnshadowed, s.Items[0].Tier);
        s.Items[1].Pixels = s.Items[0].Pixels * 3f;
        float lastReal = s.Items[0].RealWeight, lastGlow = s.Items[0].GlowWeight;
        for (int f = 0; f < 120; f++)
        {
            Run(ref s, caps, 1);
            float real = s.Items[0].RealWeight, glow = s.Items[0].GlowWeight;
            Assert.True(lastReal - real <= 0.05f && real <= lastReal);
            Assert.True(glow - lastGlow <= 0.05f && glow >= lastGlow);
            lastReal = real;
            lastGlow = glow;
        }
        Assert.Equal(Detail.LightGlow, s.Items[0].Tier);
        Assert.Equal(Detail.LightUnshadowed, s.Items[1].Tier);
        Assert.Equal(1f, s.Items[1].RealWeight);
        Assert.Equal(0f, s.Items[0].RealWeight);
    }

    [Fact]
    public void NoCapsLeavesEveryLightAtItsScreenSizeTier()
    {
        var s = Make(40);
        var c = Run(ref s, LightBudget.Caps.None, 2);
        Assert.Equal(40, c.Shadowed);
        var a = Make(40);
        var b = Make(40);
        Run(ref a, new LightBudget.Caps(2, 12, 16), 30);
        Run(ref b, new LightBudget.Caps(2, 12, 16), 30);
        Assert.Equal(a.Items, b.Items);
    }
}
