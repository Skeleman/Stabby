using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct Rng
    {
        ulong state;

        public Rng(ulong seed) { state = seed; }

        public static Rng Stream(ulong worldSeed, string name)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in name) { h ^= c; h *= 1099511628211UL; }
            return new Rng(Mix(worldSeed ^ Mix(h)));
        }

        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextU64()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public float NextFloat() => (NextU64() >> 40) * (1f / 16777216f);
        public float Range(float a, float b) => a + (b - a) * NextFloat();
        public int Range(int a, int b) => a + (int)(NextU64() % (ulong)(b - a));
        public uint NextU32() => (uint)(NextU64() >> 32);
    }

    public static class Noise
    {
        static readonly float[] GX = { 1, -1, 0, 0, 0.70710678f, -0.70710678f, 0.70710678f, -0.70710678f };
        static readonly float[] GY = { 0, 0, 1, -1, 0.70710678f, 0.70710678f, -0.70710678f, -0.70710678f };

        public static uint Hash(uint seed, int x, int y)
        {
            uint h = seed ^ ((uint)x * 0x9E3779B1u) ^ ((uint)y * 0x85EBCA77u);
            h ^= h >> 16; h *= 0x7FEB352Du;
            h ^= h >> 15; h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        static float Dot(uint seed, int ix, int iy, float dx, float dy)
        {
            uint g = Hash(seed, ix, iy) & 7;
            return GX[g] * dx + GY[g] * dy;
        }

        public static float Gradient(uint seed, float x, float y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float u = Fade(fx), v = Fade(fy);
            float a = Dot(seed, x0, y0, fx, fy);
            float b = Dot(seed, x0 + 1, y0, fx - 1, fy);
            float c = Dot(seed, x0, y0 + 1, fx, fy - 1);
            float d = Dot(seed, x0 + 1, y0 + 1, fx - 1, fy - 1);
            float ab = a + (b - a) * u, cd = c + (d - c) * u;
            return (ab + (cd - ab) * v) * 1.4f;
        }

        public static float Fbm(uint seed, float x, float y, int octaves, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Gradient(seed + (uint)i * 101u, x, y);
                norm += amp;
                amp *= gain; x *= 2; y *= 2;
            }
            return sum / norm;
        }
    }

    public static class Grid
    {
        public static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        public static readonly int[] DY = { 0, 1, 1, 1, 0, -1, -1, -1 };
        public static readonly float[] Dist = { 1, 1.41421356f, 1, 1.41421356f, 1, 1.41421356f, 1, 1.41421356f };

        public static bool Adjacent(int n, int a, int b)
        {
            int dx = Math.Abs(a % n - b % n), dy = Math.Abs(a / n - b / n);
            return Math.Max(dx, dy) == 1;
        }

        public static float Smoothstep(float a, float b, float x)
        {
            float t = Math.Max(0, Math.Min(1, (x - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }
    }

    public sealed class MinHeap
    {
        float[] keys = new float[1024];
        int[] vals = new int[1024];
        public int Count { get; private set; }

        public void Clear() => Count = 0;

        static bool Less(float ka, int va, float kb, int vb) => ka < kb || (ka == kb && va < vb);

        public void Push(float key, int val)
        {
            if (Count == keys.Length)
            {
                Array.Resize(ref keys, Count * 2);
                Array.Resize(ref vals, Count * 2);
            }
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(key, val, keys[p], vals[p])) break;
                keys[i] = keys[p]; vals[i] = vals[p]; i = p;
            }
            keys[i] = key; vals[i] = val;
        }

        public int Pop(out float key)
        {
            int top = vals[0]; key = keys[0];
            float k = keys[--Count]; int v = vals[Count];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= Count) break;
                int c = (l + 1 < Count && Less(keys[l + 1], vals[l + 1], keys[l], vals[l])) ? l + 1 : l;
                if (!Less(keys[c], vals[c], k, v)) break;
                keys[i] = keys[c]; vals[i] = vals[c]; i = c;
            }
            keys[i] = k; vals[i] = v;
            return top;
        }
    }
}
