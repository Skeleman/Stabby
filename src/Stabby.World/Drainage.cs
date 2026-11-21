using System;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class DrainageScratch
    {
        public readonly float[] Filled, Acc;
        public readonly bool[] Done;
        public readonly int[] Recv, DonorCount, Donors, Fill, Stack;
        public readonly MinHeap Heap = new MinHeap();

        public DrainageScratch(int n)
        {
            int c = n * n;
            Filled = new float[c]; Acc = new float[c]; Done = new bool[c];
            Recv = new int[c]; DonorCount = new int[c + 1]; Donors = new int[c]; Fill = new int[c + 1]; Stack = new int[c];
        }
    }

    public static class Drainage
    {
        public static float[] Fill(float[] h, bool[] sea, int n, float eps) =>
            Fill(h, sea, n, eps, new float[n * n], new bool[n * n], new MinHeap());

        public static float[] Fill(float[] h, bool[] sea, int n, float eps, DrainageScratch s) =>
            Fill(h, sea, n, eps, s.Filled, s.Done, s.Heap);

        static float[] Fill(float[] h, bool[] sea, int n, float eps, float[] filled, bool[] done, MinHeap heap)
        {
            Array.Copy(h, filled, h.Length);
            Array.Clear(done, 0, done.Length);
            heap.Clear();
            for (int i = 0; i < n * n; i++)
            {
                if (!sea[i]) continue;
                done[i] = true;
                int x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    if (!sea[ny * n + nx]) { heap.Push(filled[i], i); break; }
                }
            }
            Span<int> off = stackalloc int[8];
            for (int k = 0; k < 8; k++) off[k] = Grid.DY[k] * n + Grid.DX[k];
            while (heap.Count > 0)
            {
                int c = heap.Pop(out float level);
                int y = c / n, x = c - y * n;
                bool inner = x > 0 && y > 0 && x < n - 1 && y < n - 1;
                for (int k = 0; k < 8; k++)
                {
                    int j = c + off[k];
                    if (!inner)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    }
                    if (done[j]) continue;
                    done[j] = true;
                    float min = sea[c] ? filled[j] : level + eps;
                    if (filled[j] < min) filled[j] = min;
                    heap.Push(filled[j], j);
                }
            }
            return filled;
        }

        public static int[] Receivers(float[] surface, bool[] sea, int n, float cell) =>
            Receivers(surface, sea, n, cell, new int[n * n]);

        public static int[] Receivers(float[] surface, bool[] sea, int n, float cell, int[] recv)
        {
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    recv[i] = i;
                    if (sea[i]) continue;
                    float best = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int j = ny * n + nx;
                        float slope = (surface[i] - surface[j]) / (Grid.Dist[k] * cell);
                        if (sea[j]) slope = Math.Max(slope, 1e-6f);
                        if (slope > best) { best = slope; recv[i] = j; }
                    }
                }
            });
            return recv;
        }

        public static int[] Stack(int[] recv, int n) =>
            Stack(recv, n, new int[n * n + 1], new int[n * n], new int[n * n + 1], new int[n * n]);

        public static int[] Stack(int[] recv, int n, DrainageScratch s) =>
            Stack(recv, n, s.DonorCount, s.Donors, s.Fill, s.Stack);

        static int[] Stack(int[] recv, int n, int[] donorCount, int[] donors, int[] fill, int[] stack)
        {
            int count = n * n;
            Array.Clear(donorCount, 0, donorCount.Length);
            for (int i = 0; i < count; i++) if (recv[i] != i) donorCount[recv[i] + 1]++;
            for (int i = 0; i < count; i++) donorCount[i + 1] += donorCount[i];
            Array.Copy(donorCount, fill, donorCount.Length);
            for (int i = 0; i < count; i++) if (recv[i] != i) donors[fill[recv[i]]++] = i;

            int head = 0, tail = 0;
            for (int i = 0; i < count; i++) if (recv[i] == i) stack[tail++] = i;
            while (head < tail)
            {
                int c = stack[head++];
                for (int d = donorCount[c]; d < donorCount[c + 1]; d++) stack[tail++] = donors[d];
            }
            if (tail != count) throw new InvalidOperationException("drainage graph has a cycle");
            return stack;
        }

        public static float[] Accumulate(int[] stack, int[] recv, float[] weight) =>
            Accumulate(stack, recv, weight, new float[weight.Length]);

        public static float[] Accumulate(int[] stack, int[] recv, float[] weight, float[] acc)
        {
            Array.Copy(weight, acc, weight.Length);
            for (int s = stack.Length - 1; s >= 0; s--)
            {
                int i = stack[s];
                if (recv[i] != i) acc[recv[i]] += acc[i];
            }
            return acc;
        }

        public static float StepLength(int n, int a, int b, float cell)
        {
            int dx = Math.Abs(a % n - b % n), dy = Math.Abs(a / n - b / n);
            return (dx != 0 && dy != 0 ? 1.41421356f : 1f) * cell;
        }
    }
}
