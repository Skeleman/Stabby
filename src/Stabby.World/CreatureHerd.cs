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
        bool Crosses(float x0, float z0, float x1, float z1);
    }

    public interface IWetCells
    {
        bool WetCell(int cell);
    }

    public struct OpenGround : IHerdGround
    {
        public bool Blocked(float x, float z) => false;
        public bool Crosses(float x0, float z0, float x1, float z1) => false;
    }

    public struct WaterGround : IHerdGround, IWetCells
    {
        public byte[] Wet;
        public int Size;
        public float CellMeters;

        public bool WetCell(int cell) => Wet[cell] != 0;
        public bool Blocked(float x, float z) => HerdMind.OffMap(Size, CellMeters, x, z, out int c) || Wet[c] != 0;
        public bool Crosses(float x0, float z0, float x1, float z1) => HerdMind.CrossesWet(ref this, Size, CellMeters, x0, z0, x1, z1);
    }

    public static class Footing
    {
        public const float FeelM = 1.5f;

        public static float Grade(float maxSlopeDegrees) => (float)Math.Tan(maxSlopeDegrees * Math.PI / 180);

        public static bool SteepAt<T>(T cells, float x, float z, float grade) where T : struct, INearCells
        {
            float gx = NearField.Sample(cells, x + FeelM, z) - NearField.Sample(cells, x - FeelM, z);
            float gz = NearField.Sample(cells, x, z + FeelM) - NearField.Sample(cells, x, z - FeelM);
            return gx * gx + gz * gz > 4 * FeelM * FeelM * grade * grade;
        }

        public static bool SteepStep<T>(T cells, float x0, float z0, float x1, float z1, float grade) where T : struct, INearCells
        {
            float dx = x1 - x0, dz = z1 - z0, dh = NearField.Sample(cells, x1, z1) - NearField.Sample(cells, x0, z0);
            return dh * dh > grade * grade * (dx * dx + dz * dz);
        }
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

        public static bool CrossesWet<T>(ref T cells, int size, float cellMeters, float x0, float z0, float x1, float z1) where T : struct, IWetCells
        {
            if (OffMap(size, cellMeters, x0, z0, out _) || OffMap(size, cellMeters, x1, z1, out _)) return true;
            int cx = (int)(x0 / cellMeters), cz = (int)(z0 / cellMeters), ex = (int)(x1 / cellMeters), ez = (int)(z1 / cellMeters);
            float dx = x1 - x0, dz = z1 - z0;
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float stepX = dx != 0 ? cellMeters / Math.Abs(dx) : float.MaxValue, stepZ = dz != 0 ? cellMeters / Math.Abs(dz) : float.MaxValue;
            float tx = dx != 0 ? (sx > 0 ? (cx + 1) * cellMeters - x0 : x0 - cx * cellMeters) / Math.Abs(dx) : float.MaxValue;
            float tz = dz != 0 ? (sz > 0 ? (cz + 1) * cellMeters - z0 : z0 - cz * cellMeters) / Math.Abs(dz) : float.MaxValue;
            for (int guard = Math.Abs(ex - cx) + Math.Abs(ez - cz) + 2; guard > 0; guard--)
            {
                if (cx < 1 || cz < 1 || cx > size - 2 || cz > size - 2 || cells.WetCell(cz * size + cx)) return true;
                if (cx == ex && cz == ez) return false;
                if (tx < tz) { tx += stepX; cx += sx; }
                else { tz += stepZ; cz += sz; }
            }
            return true;
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
                if (ground.Crosses(x + dx * (p - 1), z + dz * (p - 1), x + dx * p, z + dz * p)) return p - 1;
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

    public struct HerdRoute
    {
        public const int Points = 4, Cells = 2;
        public const float GrazeRatio = 0.25f, DwellMin = 20f, DwellMax = 60f, PeakRatio = 1.5f;

        public float X0, Z0, X1, Z1, X2, Z2, X3, Z3, Speed, Dwell, Phase;

        public float X(int i) => i == 0 ? X0 : i == 1 ? X1 : i == 2 ? X2 : X3;
        public float Z(int i) => i == 0 ? Z0 : i == 1 ? Z1 : i == 2 ? Z2 : Z3;

        public void Set(int i, float x, float z)
        {
            if (i == 0) { X0 = x; Z0 = z; }
            else if (i == 1) { X1 = x; Z1 = z; }
            else if (i == 2) { X2 = x; Z2 = z; }
            else { X3 = x; Z3 = z; }
        }

        public float Leg(int i)
        {
            int j = (i + 1) % Points;
            float dx = X(j) - X(i), dz = Z(j) - Z(i);
            return (float)Math.Sqrt(dx * dx + dz * dz) / Speed;
        }

        public float Period => Leg(0) + Leg(1) + Leg(2) + Leg(3) + Points * Dwell;

        public float Extent
        {
            get
            {
                float e = 0;
                for (int i = 1; i < Points; i++) e = Math.Max(e, (float)Math.Sqrt((X(i) - X0) * (X(i) - X0) + (Z(i) - Z0) * (Z(i) - Z0)));
                return e;
            }
        }

        public void At(double t, out float x, out float z, out float heading)
        {
            if (!(Speed > 0)) { x = X0; z = Z0; heading = 0; return; }
            double period = Period;
            double u = (t + Phase * period) % period;
            if (u < 0) u += period;
            float r = (float)u;
            for (int i = 0; i < Points; i++)
            {
                int j = (i + 1) % Points, back = (i + Points - 1) % Points;
                if (r < Dwell)
                {
                    x = X(i); z = Z(i);
                    heading = (float)Math.Atan2(X(i) - X(back), Z(i) - Z(back));
                    return;
                }
                r -= Dwell;
                float leg = Leg(i);
                if (r < leg)
                {
                    float s = r / leg;
                    s = s * s * (3 - 2 * s);
                    x = X(i) + (X(j) - X(i)) * s; z = Z(i) + (Z(j) - Z(i)) * s;
                    heading = (float)Math.Atan2(X(j) - X(i), Z(j) - Z(i));
                    return;
                }
                r -= leg;
            }
            x = X0; z = Z0;
            heading = (float)Math.Atan2(X0 - X3, Z0 - Z3);
        }
    }
}
