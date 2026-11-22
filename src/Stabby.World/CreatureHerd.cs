using System;

namespace Stabby.World
{
    public enum HerdMode : byte { Calm, Alert, Flee }

    public struct CreatureWary
    {
        public float Alert, Flee, Calm, RunRatio, Look;
    }

    public interface IHerdGround
    {
        bool Blocked(float x, float z);
    }

    public struct OpenGround : IHerdGround
    {
        public bool Blocked(float x, float z) => false;
    }

    public struct WaterGround : IHerdGround
    {
        public byte[] Wet;
        public int Size;
        public float CellMeters;

        public bool Blocked(float x, float z) => HerdMind.OffMap(Size, CellMeters, x, z, out int c) || Wet[c] != 0;
    }

    public struct HerdMind
    {
        public const float AlertHold = 3f, FleeMin = 2f, FleeMax = 25f, Rethink = 4f, Spread = 0.6f, LookSeconds = 4f;
        public const int Probes = 4, Turns = 12;

        public HerdMode Mode;
        public float Timer, Heading;
        public Rng Random;

        public static HerdMind Start(ulong herdSeed) => new HerdMind { Random = new Rng(herdSeed) };

        public bool Step(in CreatureWary w, float threat, float awayX, float awayZ, float dt)
        {
            var open = new OpenGround();
            return Step(w, threat, awayX, awayZ, 0, 0, ref open, dt);
        }

        public static bool OffMap(int size, float cellMeters, float x, float z, out int cell)
        {
            float edge = (size - 1) * cellMeters;
            cell = 0;
            if (!(x >= cellMeters && z >= cellMeters && x < edge && z < edge)) return true;
            cell = (int)(z / cellMeters) * size + (int)(x / cellMeters);
            return false;
        }

        public bool Step<T>(in CreatureWary w, float threat, float awayX, float awayZ, float x, float z, ref T ground, float dt) where T : struct, IHerdGround
        {
            Timer += dt;
            switch (Mode)
            {
                case HerdMode.Calm:
                    if (threat < w.Alert) { Mode = HerdMode.Alert; Timer = 0; }
                    return false;
                case HerdMode.Alert:
                    if (threat < w.Flee) { Bolt(awayX, awayZ, x, z, w.Look, ref ground); return false; }
                    if (threat < w.Alert / (1f - Detail.Hysteresis)) Timer = 0;
                    else if (Timer > AlertHold) { Mode = HerdMode.Calm; Timer = 0; }
                    return false;
                default:
                    if (Timer > FleeMax || threat > w.Calm && Timer > FleeMin) { Mode = HerdMode.Alert; Timer = 0; return true; }
                    if (threat < w.Flee && Timer > Rethink) { Bolt(awayX, awayZ, x, z, w.Look, ref ground); return Mode != HerdMode.Flee; }
                    if (ClearRun(Heading, x, z, w.Look, ref ground) < Probes && !Steer(Heading, x, z, w.Look, ref ground)) { Mode = HerdMode.Alert; Timer = 0; return true; }
                    return false;
            }
        }

        void Bolt<T>(float awayX, float awayZ, float x, float z, float look, ref T ground) where T : struct, IHerdGround
        {
            float want = (float)Math.Atan2(awayX, awayZ) + Random.Range(-Spread, Spread);
            Timer = 0;
            Mode = Steer(want, x, z, look, ref ground) ? HerdMode.Flee : HerdMode.Alert;
        }

        bool Steer<T>(float want, float x, float z, float look, ref T ground) where T : struct, IHerdGround
        {
            int best = -1;
            float pick = want;
            for (int k = 0; k <= Turns && best < Probes; k++)
                for (int side = k == 0 ? 1 : -1; side <= 1 && best < Probes; side += 2)
                {
                    float h = want + side * k * (float)Math.PI / Turns;
                    int clear = ClearRun(h, x, z, look, ref ground);
                    if (clear > best) { best = clear; pick = h; }
                }
            Heading = pick;
            return best > 0;
        }

        public static int ClearRun<T>(float heading, float x, float z, float look, ref T ground) where T : struct, IHerdGround
        {
            float dx = (float)Math.Sin(heading) * look / Probes, dz = (float)Math.Cos(heading) * look / Probes;
            for (int p = 1; p <= Probes; p++)
                if (ground.Blocked(x + dx * p, z + dz * p)) return p - 1;
            return Probes;
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
