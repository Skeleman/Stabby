using System;

namespace Stabby.World
{
    public static class FMath
    {
        const double Ln2Hi = 6.93147180369123816490e-01;
        const double Ln2Lo = 1.90821492927058770002e-10;
        const double InvLn2 = 1.44269504088896338700e+00;
        const double Sqrt2 = 1.41421356237309514547e+00;
        const double ExpMax = 709.782712893383973096;
        const double ExpMin = -745.13321910194110842;

        public static double Exp(double x)
        {
            if (double.IsNaN(x)) return x;
            if (x > ExpMax) return double.PositiveInfinity;
            if (x < ExpMin) return 0;

            double t = x * InvLn2;
            long k = (long)(t >= 0 ? t + 0.5 : t - 0.5);
            double r = (x - k * Ln2Hi) - k * Ln2Lo;

            double p = 1.0 / 1307674368000.0;
            p = p * r + 1.0 / 87178291200.0;
            p = p * r + 1.0 / 6227020800.0;
            p = p * r + 1.0 / 479001600.0;
            p = p * r + 1.0 / 39916800.0;
            p = p * r + 1.0 / 3628800.0;
            p = p * r + 1.0 / 362880.0;
            p = p * r + 1.0 / 40320.0;
            p = p * r + 1.0 / 5040.0;
            p = p * r + 1.0 / 720.0;
            p = p * r + 1.0 / 120.0;
            p = p * r + 1.0 / 24.0;
            p = p * r + 1.0 / 6.0;
            p = p * r + 0.5;
            p = p * r + 1.0;
            p = p * r + 1.0;
            return Scale(p, k);
        }

        public static double Log(double x)
        {
            if (double.IsNaN(x) || x < 0) return double.NaN;
            if (x == 0) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x)) return x;

            long bits = BitConverter.DoubleToInt64Bits(x);
            long e = 0;
            if ((bits >> 52) == 0)
            {
                bits = BitConverter.DoubleToInt64Bits(x * 18014398509481984.0);
                e = -54;
            }
            e += (bits >> 52) - 1023;
            double m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L);
            if (m > Sqrt2) { m *= 0.5; e++; }

            double s = (m - 1) / (m + 1);
            double z = s * s;
            double p = 1.0 / 25;
            p = p * z + 1.0 / 23;
            p = p * z + 1.0 / 21;
            p = p * z + 1.0 / 19;
            p = p * z + 1.0 / 17;
            p = p * z + 1.0 / 15;
            p = p * z + 1.0 / 13;
            p = p * z + 1.0 / 11;
            p = p * z + 1.0 / 9;
            p = p * z + 1.0 / 7;
            p = p * z + 1.0 / 5;
            p = p * z + 1.0 / 3;
            double tail = 2 * s * z * p;
            return e * Ln2Hi + ((2 * s + tail) + e * Ln2Lo);
        }

        public static double Pow(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || x < 0) return double.NaN;
            if (y == 0 || x == 1) return 1;
            if (x == 0) return y > 0 ? 0 : double.PositiveInfinity;
            if (y == 1) return x;
            return Exp(y * Log(x));
        }

        static double Scale(double p, long k)
        {
            if (k > 1023) { p *= TwoTo(1023); k -= 1023; }
            else if (k < -1022) { p *= TwoTo(-1022); k += 1022; }
            if (k < -1022) { p *= TwoTo(-1022); k += 1022; }
            return p * TwoTo((int)k);
        }

        static double TwoTo(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
    }
}
