using System;

namespace Stabby.World
{
    public static class LightBudget
    {
        public const byte Unranked = 255;
        const int Pending = 100;
        public const float Stickiness = 1f / (1f - Detail.Hysteresis);
        public const int Unlimited = 1 << 20;

        public struct Caps
        {
            public int Shadowed, Unshadowed, Glow;

            public Caps(int shadowed, int unshadowed, int glow) { Shadowed = shadowed; Unshadowed = unshadowed; Glow = glow; }

            public static Caps None => new Caps(Unlimited, Unlimited, Unlimited);
            public int Real => Shadowed + Unshadowed;
        }

        public struct Slot
        {
            public float Pixels, Importance, Shadow, Real, Glow;
            public byte Wanted, Previous, Target, Tier;

            public static Slot Fresh => new Slot { Wanted = Detail.LightEmissive, Previous = Detail.LightEmissive, Target = Detail.LightEmissive, Tier = Detail.LightEmissive };

            public float ShadowWeight => Detail.Crossfade(Shadow, 0f, 1f);
            public float RealWeight => Detail.Crossfade(Real, 0f, 1f);
            public float GlowWeight => Detail.Crossfade(Glow, 0f, 1f);
        }

        public struct Counts
        {
            public int Shadowed, Unshadowed, Glow, Emissive, CastingShadow, Lit, Glowing;
        }

        public interface ISlots
        {
            int Count { get; }
            Slot this[int i] { get; set; }
            int GetOrder(int i);
            void SetOrder(int i, int v);
        }

        public static Counts Rank<T>(ref T slots, in Caps caps) where T : struct, ISlots
        {
            int n = slots.Count, heldS = 0, heldR = 0, heldG = 0;
            for (int i = 0; i < n; i++)
            {
                var s = slots[i];
                s.Previous = s.Target;
                s.Target = Unranked;
                if (s.Shadow > 0f) heldS++;
                if (s.Real > 0f) heldR++;
                if (s.Glow > 0f) heldG++;
                slots[i] = s;
            }
            int shadowed = Choose(ref slots, Detail.LightShadowed, Math.Min(caps.Shadowed, caps.Real));
            Choose(ref slots, Detail.LightUnshadowed, Math.Max(caps.Real - shadowed, 0));
            Choose(ref slots, Detail.LightGlow, caps.Glow);
            for (int i = 0; i < n; i++)
            {
                var s = slots[i];
                if (s.Target == Unranked) s.Target = Detail.LightEmissive;
                s.Tier = (byte)(s.Target == Detail.LightEmissive ? Detail.LightEmissive : Pending + s.Target);
                slots[i] = s;
            }
            int freeS = Math.Max(caps.Shadowed - heldS, 0), freeR = Math.Max(caps.Real - heldR, 0), freeG = Math.Max(caps.Glow - heldG, 0);
            var c = new Counts();
            for (int t = Detail.LightShadowed; t < Detail.LightEmissive; t++)
            {
                int m = 0;
                for (int i = 0; i < n; i++)
                {
                    var s = slots[i];
                    if (s.Tier == Pending + t && Score(s, t) > -1f) slots.SetOrder(m++, i);
                }
                for (int k = m / 2 - 1; k >= 0; k--) SiftDown(ref slots, k, m, t);
                for (int end = m - 1; end > 0; end--)
                {
                    int worst = slots.GetOrder(0);
                    slots.SetOrder(0, slots.GetOrder(end));
                    slots.SetOrder(end, worst);
                    SiftDown(ref slots, 0, end, t);
                }
                for (int j = 0; j < m; j++)
                {
                    int best = slots.GetOrder(j);
                    var b = slots[best];
                    bool needS = t == Detail.LightShadowed && b.Shadow <= 0f;
                    bool needR = t <= Detail.LightUnshadowed && b.Real <= 0f;
                    bool needG = t == Detail.LightGlow && b.Glow <= 0f;
                    if ((needS && freeS <= 0) || (needR && freeR <= 0) || (needG && freeG <= 0))
                    {
                        b.Tier = (byte)(t + 1 == Detail.LightEmissive ? Detail.LightEmissive : Pending + t + 1);
                    }
                    else
                    {
                        if (needS) freeS--;
                        if (needR) freeR--;
                        if (needG) freeG--;
                        b.Tier = (byte)t;
                    }
                    slots[best] = b;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var t = slots[i].Tier;
                if (t == Detail.LightShadowed) c.Shadowed++;
                else if (t == Detail.LightUnshadowed) c.Unshadowed++;
                else if (t == Detail.LightGlow) c.Glow++;
                else c.Emissive++;
            }
            return c;
        }

        static int Choose<T>(ref T slots, int tier, int picks) where T : struct, ISlots
        {
            if (picks <= 0) return 0;
            int size = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.Target != Unranked || s.Wanted > tier || !(Score(s, tier) > -1f)) continue;
                if (size < picks)
                {
                    int at = size++;
                    while (at > 0)
                    {
                        int up = (at - 1) / 2;
                        if (!Better(ref slots, slots.GetOrder(up), i, tier)) break;
                        slots.SetOrder(at, slots.GetOrder(up));
                        at = up;
                    }
                    slots.SetOrder(at, i);
                }
                else if (Better(ref slots, i, slots.GetOrder(0), tier))
                {
                    slots.SetOrder(0, i);
                    SiftDown(ref slots, 0, size, tier);
                }
            }
            for (int j = 0; j < size; j++)
            {
                int i = slots.GetOrder(j);
                var b = slots[i];
                b.Target = (byte)tier;
                slots[i] = b;
            }
            return size;
        }

        static void SiftDown<T>(ref T slots, int at, int size, int tier) where T : struct, ISlots
        {
            int item = slots.GetOrder(at);
            while (true)
            {
                int child = 2 * at + 1;
                if (child >= size) break;
                if (child + 1 < size && Better(ref slots, slots.GetOrder(child), slots.GetOrder(child + 1), tier)) child++;
                if (!Better(ref slots, item, slots.GetOrder(child), tier)) break;
                slots.SetOrder(at, slots.GetOrder(child));
                at = child;
            }
            slots.SetOrder(at, item);
        }

        static bool Better<T>(ref T slots, int a, int b, int tier) where T : struct, ISlots
        {
            float sa = Score(slots[a], tier), sb = Score(slots[b], tier);
            return sa > sb || (sa == sb && a < b);
        }

        static float Score(in Slot s, int tier) => s.Pixels * s.Importance * (s.Previous <= tier ? Stickiness : 1f);

        public static Counts Step<T>(ref T slots, float dt) where T : struct, ISlots
        {
            float rate = Math.Max(dt, 0f) / Detail.CrossfadeSeconds;
            var c = new Counts();
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                s.Shadow = Toward(s.Shadow, s.Tier == Detail.LightShadowed, rate);
                s.Real = Toward(s.Real, s.Tier <= Detail.LightUnshadowed, rate);
                s.Glow = Toward(s.Glow, s.Tier == Detail.LightGlow, rate);
                slots[i] = s;
                if (s.Tier == Detail.LightShadowed) c.Shadowed++;
                else if (s.Tier == Detail.LightUnshadowed) c.Unshadowed++;
                else if (s.Tier == Detail.LightGlow) c.Glow++;
                else c.Emissive++;
                if (s.Shadow > 0f) c.CastingShadow++;
                if (s.Real > 0f) c.Lit++;
                if (s.Glow > 0f) c.Glowing++;
            }
            return c;
        }

        static float Toward(float v, bool on, float rate) => on ? Math.Min(v + rate, 1f) : Math.Max(v - rate, 0f);
    }
}
