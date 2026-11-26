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
        public static readonly float[] TrainLength = { 1f, 0.66f, 0.47f, 0.31f, 0.21f, 0.14f, 0.095f };
        public static readonly float[] TrainX = { 0.8f, 0.96f, 0.53f, 0.99f, 0.6f, 0.894f, 0.707f };
        public static readonly float[] TrainZ = { 0.6f, -0.28f, 0.848f, 0.141f, 0.8f, 0.447f, 0.707f };
        public static readonly float[] TrainPart = { 1f, 0.72f, 0.55f, 0.4f, 0.29f, 0.2f, 0.14f };
        public static readonly float[] TrainSharp = { 0.6f, 0.6f, 0.65f, 0.7f, 0.8f, 0.85f, 0.9f };
        public static readonly float[] TrainPhase = { 0f, 0.37f, 0.71f, 0.19f, 0.53f, 0.83f, 0.29f };
        public const float GroupDepth = 0.3f, GroupX = 0.8f, GroupZ = 0.6f, GroupLength = 260f, GroupSpeed = 4f;
        public const float WarpGain = 0.25f, WarpAX = 0.6f, WarpAZ = 0.8f, WarpALength = 3100f, WarpBX = -0.8f, WarpBZ = 0.6f, WarpBLength = 2700f;
        public const float ShoreStart = 12f, ShoreFull = 4f, ShoreRef = 8f, Breaking = 0.39f, SharpDepth = 6f, MinSlope = 0.03f, ShoreEase = 0.125f;

        public static readonly SwellParams Sea = new SwellParams(0.5f, 44f, 7f, 0.22f);
        public static readonly SwellParams Lake = new SwellParams(0.07f, 28f, 3f, 0.07f);

        public const float ShoreFade = 0.3f, PeakShare = 0.5f;
        public static readonly float OpenPeak = PeakOf(TrainPart);

        static float PeakOf(float[] parts)
        {
            float s = 0;
            for (int i = 0; i < parts.Length; i++) s += parts[i];
            return s * (1 + GroupDepth);
        }

        public static float OpenScale(in SwellParams p, float depth)
        {
            if (depth <= 0 || p.Height <= 0) return 0;
            return (1 - ShoreFade * ShoreWeight(depth)) * Math.Min(1, Breaking * depth / (p.Height * OpenPeak * PeakShare));
        }

        public static float ShoreDistance(float depth, float slope) => Math.Max(depth, 0) / Math.Max(slope, MinSlope);

        public static float ShoreWeight(float depth) => Saturate((ShoreStart - depth) / (ShoreStart - ShoreFull));

        public static float ShoreAmplitude(in SwellParams p, float depth)
        {
            if (depth <= 0) return 0;
            float grown = p.ShoreHeight * (float)FMath.Pow(ShoreRef / depth, 0.25);
            return Math.Min(grown, Breaking * depth) * ShoreWeight(depth);
        }

        public static float ShoreWavelength(in SwellParams p) => p.ShorePeriod * (float)Math.Sqrt(Gravity * ShoreFull);

        public static float ShorePhaseDistance(float shoreDist, out float rate)
        {
            float sd = Math.Max(shoreDist, 0), near = Math.Max(0, 1 - sd);
            rate = 1 + 2 * ShoreEase * near;
            return sd - ShoreEase * near * near;
        }

        public static float Height(in SwellParams p, float x, float z, float t, float depth, float shoreDist) =>
            Sample(p, x, z, t, depth, shoreDist, 0, 0, out _, out _);

        public static float Sample(in SwellParams p, float x, float z, float t, float depth, float shoreDist, float shoreDirX, float shoreDirZ, out float dhdx, out float dhdz)
        {
            dhdx = 0; dhdz = 0;
            if (depth <= 0) return 0;
            float h = 0, open = OpenScale(p, depth);
            if (open > 0)
            {
                float g = Wave((x * GroupX + z * GroupZ - t * GroupSpeed) / GroupLength, 0, out float dg);
                float group = 1 + GroupDepth * g;
                float wa = Wave((x * WarpAX + z * WarpAZ) / WarpALength, 0, out float dwa);
                float wb = Wave((x * WarpBX + z * WarpBZ) / WarpBLength, 0, out float dwb);
                float warp = WarpGain * (wa + wb);
                float wx = WarpGain * (dwa * WarpAX / WarpALength + dwb * WarpBX / WarpBLength);
                float wz = WarpGain * (dwa * WarpAZ / WarpALength + dwb * WarpBZ / WarpBLength);
                float sum = 0, sx = 0, sz = 0;
                for (int i = 0; i < TrainLength.Length; i++)
                {
                    float l = p.Length * TrainLength[i];
                    float period = (float)Math.Sqrt(2 * Math.PI * l / Gravity);
                    float a = p.Height * TrainPart[i] * open;
                    float w = Wave((x * TrainX[i] + z * TrainZ[i]) / l - t / period + TrainPhase[i] + warp * (i + 1), TrainSharp[i], out float d);
                    sum += a * w;
                    sx += a * d * (TrainX[i] / l + wx * (i + 1));
                    sz += a * d * (TrainZ[i] / l + wz * (i + 1));
                }
                h += group * sum;
                dhdx += group * sx + GroupDepth * dg * GroupX / GroupLength * sum;
                dhdz += group * sz + GroupDepth * dg * GroupZ / GroupLength * sum;
            }
            float amp = ShoreAmplitude(p, depth);
            if (amp > 0)
            {
                float c = ShoreWavelength(p), u = ShorePhaseDistance(shoreDist, out float rate);
                float sharp = Saturate(1 - depth / SharpDepth);
                h += amp * Wave(2 * u / c + t / p.ShorePeriod, sharp, out float ds);
                float k = 2 * amp * ds * rate / c;
                dhdx -= k * shoreDirX;
                dhdz -= k * shoreDirZ;
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
