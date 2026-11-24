using Stabby.World;

namespace Stabby.World.Tests;

public class LightBudgetTests
{
    struct Slots : LightBudget.ISlots
    {
        public LightBudget.Slot[] Items;
        public int Count => Items.Length;
        public LightBudget.Slot this[int i] { get => Items[i]; set => Items[i] = value; }
        public int[] Order;
        public int GetOrder(int i) => Order[i];
        public void SetOrder(int i, int v) => (Order ??= new int[Items.Length])[i] = v;
    }

    [Fact]
    public void TopKRankingMatchesRepeatedBestScans()
    {
        var rng = Rng.Stream(7, "light-topk-test");
        for (int trial = 0; trial < 400; trial++)
        {
            int n = 1 + (int)(rng.NextU32() % 120);
            var items = new LightBudget.Slot[n];
            for (int i = 0; i < n; i++)
            {
                uint r = rng.NextU32();
                var x = LightBudget.Slot.Fresh;
                x.Pixels = (r % 4) == 0 ? 50f * (1 + rng.NextU32() % 4) : rng.NextU32() % 2000;
                x.Importance = (r >> 3) % 5 == 0 ? 0f : 0.5f + (rng.NextU32() % 4) * 0.5f;
                x.Wanted = (byte)(rng.NextU32() % (Detail.LightEmissive + 1));
                x.Target = (byte)(rng.NextU32() % (Detail.LightEmissive + 1));
                x.Shadow = (r >> 6) % 3 == 0 ? 1f : (r >> 6) % 3 == 1 ? 0.5f : 0f;
                x.Real = Math.Max(x.Shadow, (r >> 9) % 2 == 0 ? 1f : 0f);
                x.Glow = (r >> 10) % 4 == 0 ? 0.3f : 0f;
                items[i] = x;
            }
            var caps = trial % 10 == 0 ? LightBudget.Caps.None : new LightBudget.Caps((int)(rng.NextU32() % 6), (int)(rng.NextU32() % 20), (int)(rng.NextU32() % 30));
            var fast = new Slots { Items = (LightBudget.Slot[])items.Clone() };
            var slow = (LightBudget.Slot[])items.Clone();
            for (int frame = 0; frame < 3; frame++)
            {
                var a = LightBudget.Rank(ref fast, caps);
                var b = ReferenceRank(slow, caps);
                Assert.Equal(b, a);
                Assert.Equal(slow, fast.Items);
                LightBudget.Step(ref fast, 0.1f);
                var ref2 = new Slots { Items = slow };
                LightBudget.Step(ref ref2, 0.1f);
            }
        }
    }

    const int Pending = 100;

    static LightBudget.Counts ReferenceRank(LightBudget.Slot[] slots, LightBudget.Caps caps)
    {
        int n = slots.Length, heldS = 0, heldR = 0, heldG = 0;
        for (int i = 0; i < n; i++)
        {
            slots[i].Previous = slots[i].Target;
            slots[i].Target = LightBudget.Unranked;
            if (slots[i].Shadow > 0f) heldS++;
            if (slots[i].Real > 0f) heldR++;
            if (slots[i].Glow > 0f) heldG++;
        }
        int shadowed = ReferenceChoose(slots, Detail.LightShadowed, Math.Min(caps.Shadowed, caps.Real));
        ReferenceChoose(slots, Detail.LightUnshadowed, Math.Max(caps.Real - shadowed, 0));
        ReferenceChoose(slots, Detail.LightGlow, caps.Glow);
        for (int i = 0; i < n; i++)
        {
            if (slots[i].Target == LightBudget.Unranked) slots[i].Target = Detail.LightEmissive;
            slots[i].Tier = (byte)(slots[i].Target == Detail.LightEmissive ? Detail.LightEmissive : Pending + slots[i].Target);
        }
        int freeS = Math.Max(caps.Shadowed - heldS, 0), freeR = Math.Max(caps.Real - heldR, 0), freeG = Math.Max(caps.Glow - heldG, 0);
        var c = new LightBudget.Counts();
        for (int t = Detail.LightShadowed; t < Detail.LightEmissive; t++)
        {
            while (true)
            {
                int best = ReferenceBest(slots, Pending + t, t, false);
                if (best < 0) break;
                ref var b = ref slots[best];
                bool needS = t == Detail.LightShadowed && b.Shadow <= 0f;
                bool needR = t <= Detail.LightUnshadowed && b.Real <= 0f;
                bool needG = t == Detail.LightGlow && b.Glow <= 0f;
                if ((needS && freeS <= 0) || (needR && freeR <= 0) || (needG && freeG <= 0))
                    b.Tier = (byte)(t + 1 == Detail.LightEmissive ? Detail.LightEmissive : Pending + t + 1);
                else
                {
                    if (needS) freeS--;
                    if (needR) freeR--;
                    if (needG) freeG--;
                    b.Tier = (byte)t;
                }
            }
        }
        foreach (var s in slots)
        {
            if (s.Tier == Detail.LightShadowed) c.Shadowed++;
            else if (s.Tier == Detail.LightUnshadowed) c.Unshadowed++;
            else if (s.Tier == Detail.LightGlow) c.Glow++;
            else c.Emissive++;
        }
        return c;
    }

    static int ReferenceChoose(LightBudget.Slot[] slots, int tier, int picks)
    {
        int taken = 0;
        while (taken < picks)
        {
            int best = ReferenceBest(slots, LightBudget.Unranked, tier, true);
            if (best < 0) break;
            slots[best].Target = (byte)tier;
            taken++;
        }
        return taken;
    }

    static int ReferenceBest(LightBudget.Slot[] slots, int mark, int tier, bool ranking)
    {
        int best = -1;
        float top = -1f;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (ranking ? s.Target != mark || s.Wanted > tier : s.Tier != mark) continue;
            float score = s.Pixels * s.Importance * (s.Previous <= tier ? LightBudget.Stickiness : 1f);
            if (score > top) { top = score; best = i; }
        }
        return best;
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
