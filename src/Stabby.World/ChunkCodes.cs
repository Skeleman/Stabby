using System;

namespace Stabby.World
{
    public static class ChunkCodes
    {
        public const double HeightStep = 0.02;
        public const int MaxHeightScale = 16, NoLake = 0xFFFF, NoBiome = 0xFF;
        const int DischargeSteps = 2048, DischargeLowOctave = 16;

        public static readonly float[] Discharge = BuildDischarge();
        public static readonly float[] Temperature = Build(256, c => -50 + 0.4 * c);
        public static readonly float[] Precipitation = Build(256, c => 0.1 * c * c);

        public static double Step(int scale) => HeightStep * (1 << scale);

        public static long Height(float v, double step)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidOperationException($"height {v} cannot be stored");
            return (long)Math.Floor(v / step + 0.5);
        }

        public static int Nearest(float[] table, float v)
        {
            if (float.IsNaN(v)) throw new InvalidOperationException("NaN cannot be stored");
            if (v <= table[0]) return 0;
            int last = table.Length - 1;
            if (v >= table[last]) return last;
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (table[mid] <= v) lo = mid; else hi = mid;
            }
            return 2.0 * v >= (double)table[lo] + table[hi] ? hi : lo;
        }

        static float[] Build(int count, Func<int, double> value)
        {
            var t = new float[count];
            for (int c = 0; c < count; c++) t[c] = (float)value(c);
            return t;
        }

        static float[] BuildDischarge()
        {
            var mantissa = new double[DischargeSteps];
            for (int m = 0; m < DischargeSteps; m++) mantissa[m] = FMath.Exp(m * 0.69314718055994530942 / DischargeSteps);
            var t = new float[65536];
            double octave = 1.0 / (1 << DischargeLowOctave);
            for (int c = 1; c < t.Length; c++)
            {
                int m = (c - 1) % DischargeSteps;
                if (c > 1 && m == 0) octave *= 2;
                t[c] = (float)(mantissa[m] * octave);
            }
            return t;
        }
    }
}
