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
                while (true)
                {
                    int best = Best(ref slots, Pending + t, t, false);
                    if (best < 0) break;
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
            int taken = 0;
            while (taken < picks)
            {
                int best = Best(ref slots, Unranked, tier, true);
                if (best < 0) break;
                var b = slots[best];
                b.Target = (byte)tier;
                slots[best] = b;
                taken++;
            }
            return taken;
        }

        static int Best<T>(ref T slots, int mark, int tier, bool ranking) where T : struct, ISlots
        {
            int best = -1;
            float top = -1f;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (ranking ? s.Target != mark || s.Wanted > tier : s.Tier != mark) continue;
                float score = s.Pixels * s.Importance * (s.Previous <= tier ? Stickiness : 1f);
                if (score > top) { top = score; best = i; }
            }
            return best;
        }

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
