using System;

namespace Stabby.World
{
    public struct SwellParams
    {
        public float Height, Length, ShorePeriod, ShoreHeight;

        public SwellParams(float height, float length, float shorePeriod, float shoreHeight)
        {
            Height = height; Length = length; ShorePeriod = shorePeriod; ShoreHeight = shoreHeight;
        }

        public float Period => (float)Math.Sqrt(2 * Math.PI * Length / WaterSwell.Gravity);
    }

    public static class WaterSwell
    {
        public const float Gravity = 9.81f;
        public const float DirX = 0.8f, DirZ = 0.6f, CrossX = 0.96f, CrossZ = -0.28f, CrossLength = 0.61f, CrossHeight = 0.45f;
        public const float ThirdX = 0.28f, ThirdZ = 0.96f, ThirdLength = 0.79f, CrossPart = 0.30f, ThirdPart = 0.15f;
        public const float MainSharp = 0.6f, CrossSharp = 0.45f, ThirdSharp = 0.3f;
        public const float ShoreStart = 12f, ShoreFull = 4f, ShoreRef = 8f, Breaking = 0.39f, SharpDepth = 6f, MinDepth = 0.05f, MinSlope = 0.03f;

        public static readonly SwellParams Sea = new SwellParams(0.35f, 40f, 7f, 0.22f);
        public static readonly SwellParams Lake = new SwellParams(0.07f, 28f, 3f, 0.07f);

        public static float ShoreDistance(float depth, float slope) => Math.Max(depth, 0) / Math.Max(slope, MinSlope);

        public static float ShoreWeight(float depth) => Saturate((ShoreStart - depth) / (ShoreStart - ShoreFull));

        public static float ShoreAmplitude(in SwellParams p, float depth)
        {
            if (depth <= 0) return 0;
            float grown = p.ShoreHeight * (float)FMath.Pow(ShoreRef / depth, 0.25);
            return Math.Min(grown, Breaking * depth) * ShoreWeight(depth);
        }

        public static float ShoreWavelength(in SwellParams p, float depth) => p.ShorePeriod * (float)Math.Sqrt(Gravity * Math.Max(depth, MinDepth));

        public static float Height(in SwellParams p, float x, float z, float t, float depth, float shoreDist) =>
            Sample(p, x, z, t, depth, shoreDist, 0, 0, out _, out _);

        public static float Sample(in SwellParams p, float x, float z, float t, float depth, float shoreDist, float shoreDirX, float shoreDirZ, out float dhdx, out float dhdz)
        {
            dhdx = 0; dhdz = 0;
            if (depth <= 0) return 0;
            float h = 0, open = 1 - ShoreWeight(depth);
            if (open > 0 && p.Height > 0)
            {
                float l1 = p.Length, l2 = p.Length * CrossLength, l3 = p.Length * ThirdLength;
                float t1 = p.Period, t2 = (float)Math.Sqrt(2 * Math.PI * l2 / Gravity), t3 = (float)Math.Sqrt(2 * Math.PI * l3 / Gravity);
                float a1 = p.Height * open, a2 = p.Height * CrossPart * open, a3 = p.Height * ThirdPart * open;
                h += a1 * Wave((x * DirX + z * DirZ) / l1 - t / t1, MainSharp, out float d1);
                h += a2 * Wave((x * CrossX + z * CrossZ) / l2 - t / t2 + 0.37f, CrossSharp, out float d2);
                h += a3 * Wave((x * ThirdX + z * ThirdZ) / l3 - t / t3 + 0.71f, ThirdSharp, out float d3);
                dhdx += a1 * d1 * DirX / l1 + a2 * d2 * CrossX / l2 + a3 * d3 * ThirdX / l3;
                dhdz += a1 * d1 * DirZ / l1 + a2 * d2 * CrossZ / l2 + a3 * d3 * ThirdZ / l3;
            }
            float amp = ShoreAmplitude(p, depth);
            if (amp > 0)
            {
                float c = p.ShorePeriod * (float)Math.Sqrt(Gravity * Math.Max(depth, MinDepth));
                float sharp = Saturate(1 - depth / SharpDepth);
                h += amp * Wave(2 * Math.Max(shoreDist, 0) / c + t / p.ShorePeriod, sharp, out float ds);
                dhdx -= 2 * amp * ds * shoreDirX / c;
                dhdz -= 2 * amp * ds * shoreDirZ / c;
            }
            return h;
        }

        public static float Wave(float phase, float sharp, out float slope)
        {
            float f = phase - (float)Math.Floor(phase);
            float sign = f < 0.5f ? -1 : 1;
            float tri = Math.Abs(f - 0.5f) * 4 - 1;
            float s = tri * (1.5f - 0.5f * tri * tri);
            float dsdtri = 1.5f - 1.5f * tri * tri;
            slope = (1 + sharp * s) * dsdtri * 4 * sign;
            return s + sharp * ((s + 1) * (s + 1) * 0.5f - 1 - s);
        }

        static float Saturate(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
