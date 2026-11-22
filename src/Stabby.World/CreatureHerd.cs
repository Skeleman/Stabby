using System;

namespace Stabby.World
{
    public enum HerdMode : byte { Calm, Alert, Flee }

    public struct CreatureWary
    {
        public float Alert, Flee, Calm, RunRatio;
    }

    public struct HerdMind
    {
        public const float AlertHold = 3f, FleeMin = 2f, FleeMax = 25f, Rethink = 4f, Spread = 0.6f;

        public HerdMode Mode;
        public float Timer, Heading;
        public Rng Random;

        public static HerdMind Start(ulong herdSeed) => new HerdMind { Random = new Rng(herdSeed) };

        public bool Step(in CreatureWary w, float threat, float awayX, float awayZ, float dt)
        {
            Timer += dt;
            switch (Mode)
            {
                case HerdMode.Calm:
                    if (threat < w.Alert) { Mode = HerdMode.Alert; Timer = 0; }
                    return false;
                case HerdMode.Alert:
                    if (threat < w.Flee) { Bolt(awayX, awayZ); return false; }
                    if (threat < w.Alert / (1f - Detail.Hysteresis)) Timer = 0;
                    else if (Timer > AlertHold) { Mode = HerdMode.Calm; Timer = 0; }
                    return false;
                default:
                    if (Timer > FleeMax || threat > w.Calm && Timer > FleeMin) { Mode = HerdMode.Alert; Timer = 0; return true; }
                    if (threat < w.Flee && Timer > Rethink) Bolt(awayX, awayZ);
                    return false;
            }
        }

        void Bolt(float awayX, float awayZ)
        {
            Mode = HerdMode.Flee;
            Timer = 0;
            Heading = (float)Math.Atan2(awayX, awayZ) + Random.Range(-Spread, Spread);
        }

        public static float MemberOffset(uint seed) => ((seed >> 8 & 0xFF) / 255f - 0.5f) * 0.5f;

        public static float Toward(float heading, float target, float maxStep)
        {
            float d = target - heading;
            d -= (float)Math.Floor((d + Math.PI) / (2 * Math.PI)) * 2f * (float)Math.PI;
            return heading + Math.Max(-maxStep, Math.Min(maxStep, d));
        }
    }
}
