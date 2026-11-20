using System;

namespace Stabby.World
{
    // Flow routing shared by erosion and hydrology, so terrain shaping and the
    // river network are always computed from the same drainage graph.
    public static class Drainage
    {
        // Priority-flood (Barnes et al. 2014). With eps > 0 every land cell gets a
        // strictly downhill path to the sea; with eps = 0 the result is the water
        // level each depression fills to.
        public static float[] Fill(float[] h, bool[] sea, int n, float eps)
        {
            var filled = (float[])h.Clone();
            var done = new bool[n * n];
            var heap = new MinHeap();
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
            while (heap.Count > 0)
            {
                int c = heap.Pop(out float level);
                int x = c % n, y = c / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int j = ny * n + nx;
                    if (done[j]) continue;
                    done[j] = true;
                    float min = sea[c] ? filled[j] : level + eps;
                    if (filled[j] < min) filled[j] = min;
                    heap.Push(filled[j], j);
                }
            }
            return filled;
        }

        // D8 steepest descent; sea cells receive themselves.
        public static int[] Receivers(float[] surface, bool[] sea, int n, float cell)
        {
            var recv = new int[n * n];
            for (int i = 0; i < n * n; i++)
            {
                recv[i] = i;
                if (sea[i]) continue;
                int x = i % n, y = i / n;
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
            return recv;
        }

        // Cells ordered downstream-first (Braun & Willett 2013): every cell
        // appears after its receiver.
        public static int[] Stack(int[] recv, int n)
        {
            int count = n * n;
            var donorCount = new int[count + 1];
            for (int i = 0; i < count; i++) if (recv[i] != i) donorCount[recv[i] + 1]++;
            for (int i = 0; i < count; i++) donorCount[i + 1] += donorCount[i];
            var donors = new int[count];
            var fill = (int[])donorCount.Clone();
            for (int i = 0; i < count; i++) if (recv[i] != i) donors[fill[recv[i]]++] = i;

            var stack = new int[count];
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

        public static float[] Accumulate(int[] stack, int[] recv, float[] weight)
        {
            var acc = (float[])weight.Clone();
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
