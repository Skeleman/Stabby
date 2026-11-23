using System;

namespace Stabby.World
{
    public struct DriveDef
    {
        public float Fill, Threshold, Sate;

        public static DriveDef Of(float fill, float threshold, float sate) => new DriveDef { Fill = fill, Threshold = threshold, Sate = sate };

        public float Rise => 1f / Fill;
        public float Drop => 1f / Sate;
    }

    public struct CreatureDrives
    {
        public DriveDef Hunger, Thirst, Rest;

        public DriveDef this[int i] => i == 0 ? Hunger : i == 1 ? Thirst : Rest;
    }

    public struct DriveLevels
    {
        public float Hunger, Thirst, Rest;

        public float this[int i]
        {
            get => i == 0 ? Hunger : i == 1 ? Thirst : Rest;
            set { if (i == 0) Hunger = value; else if (i == 1) Thirst = value; else Rest = value; }
        }
    }

    public struct HerdNeeds
    {
        public const byte HasGraze = 1, HasDrink = 2, HasBed = 4;

        public float GrazeX, GrazeZ, DrinkX, DrinkZ, WaterX, WaterZ, BedX, BedZ;
        public byte Has;

        public bool Can(byte act) => act != Drives.None && (Has & (1 << (act - 1))) != 0;

        public void Target(byte act, out float x, out float z)
        {
            x = act == Drives.Graze ? GrazeX : act == Drives.Drink ? DrinkX : BedX;
            z = act == Drives.Graze ? GrazeZ : act == Drives.Drink ? DrinkZ : BedZ;
        }
    }

    public static class Drives
    {
        public const byte None = 0, Graze = 1, Drink = 2, Bed = 3;
        public const int Count = 3;
        public const float Done = 0.05f, GiveUp = 40f, Arrive = 0.6f, Reach = 4f, Shore = 12f, Jitter = 0.03f;

        public static void Advance(ref DriveLevels levels, in CreatureDrives drives, byte act, float dt)
        {
            for (int i = 0; i < Count; i++)
            {
                var d = drives[i];
                float v = act == i + 1 ? levels[i] - d.Drop * dt : levels[i] + d.Rise * dt;
                levels[i] = v < 0 ? 0 : v > 1 ? 1 : v;
            }
        }

        public static byte Choose(in DriveLevels levels, in CreatureDrives drives, in HerdNeeds needs, byte want)
        {
            if (needs.Can(want) && levels[want - 1] > Done) return want;
            if (needs.Can(Drink) && levels.Thirst >= drives.Thirst.Threshold) return Drink;
            if (needs.Can(Graze) && levels.Hunger >= drives.Hunger.Threshold) return Graze;
            if (needs.Can(Bed) && levels.Rest >= drives.Rest.Threshold) return Bed;
            return None;
        }

        public static float At(in DriveDef d, double t, float phase, out bool acting)
        {
            float span = d.Threshold - Done;
            double rise = span * d.Fill, period = rise + span * d.Sate;
            double u = t / period + phase;
            u = (u - Math.Floor(u)) * period;
            acting = u >= rise;
            return acting ? d.Threshold - (float)((u - rise) / d.Sate) : Done + (float)(u / d.Fill);
        }

        public static byte Wake(in CreatureDrives drives, in HerdNeeds needs, double t, ulong herdSeed, uint memberSeed, out DriveLevels levels)
        {
            levels = default;
            byte want = None;
            for (int i = Count - 1; i >= 0; i--)
            {
                float phase = (herdSeed >> (16 * i) & 0xFFFF) / 65536f;
                float jitter = ((memberSeed >> (8 * i) & 0xFF) / 255f * 2 - 1) * Jitter;
                float v = At(drives[i], t, phase, out bool acting) + jitter;
                levels[i] = v < 0 ? 0 : v > 1 ? 1 : v;
                byte act = (byte)(i + 1);
                if (acting && needs.Can(act) && (want == None || act == Drink || act == Graze && want == Bed)) want = act;
            }
            return want == None ? Choose(levels, drives, needs, None) : want;
        }
    }
}
