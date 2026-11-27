using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class LiquidPool
    {
        public float X, Z, Reach, Designed, Level, Rim, Floor;
        public int Kind, Liquid, I0, J0, Nx, Nz, Centre, WetCount;
        public bool Leaks;
        public float[] Ground;
        public bool[] Wet;

        public float NodeX(int i) => (I0 + i) * ObjectField.PoolStepM;
        public float NodeZ(int j) => (J0 + j) * ObjectField.PoolStepM;
    }

    public sealed partial class ObjectField
    {
        public const float PoolStepM = 0.5f, PoolSlopeM = 1f, PoolPadM = 1f, PoolGapM = 0.04f, PoolMinDepthM = 0.05f;
        public const int BogPool = -1, OasisPool = -4;
        readonly object liquidLock = new object();
        LiquidPool[] liquidPools;

        public LiquidPool[] LiquidPools()
        {
            lock (liquidLock) return liquidPools ??= BuildLiquidPools();
        }

        LiquidPool[] BuildLiquidPools()
        {
            var list = new List<LiquidPool>();
            var pools = new List<float>();
            foreach (var o in Discoveries())
            {
                if (!IsVent(o)) continue;
                SpringPoolsAt(o, pools);
                int kind = VentKind(o), liquid = LiquidOf(o);
                for (int k = 0; k < pools.Count; k += SpringStride)
                    list.Add(Pool(kind, liquid, pools[k], pools[k + 1], pools[k + 3], pools[k + 4], pools[k + 7], pools[k + 8]));
            }
            var b = BogPools();
            int peat = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("peat"));
            for (int k = 0; k < b.Length; k += BogStride)
            {
                float top = b[k + 5] + BogHeadM;
                list.Add(Pool(BogPool, peat, b[k], b[k + 1], BogReach(b[k + 2], b[k + 3], top), b[k + 3], b[k + 4] - BogHeadM, top));
            }
            var q = QuicksandPools();
            int sand = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("quicksand"));
            for (int k = 0; k < q.Length; k += BogStride)
            {
                float top = q[k + 5] + BogHeadM;
                list.Add(Pool(QuicksandPool, sand, q[k], q[k + 1], BogReach(q[k + 2], q[k + 3], top), q[k + 3], q[k + 4] - BogHeadM, top));
            }
            var oa = OasisPools();
            int water = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("water"));
            for (int k = 0; k < oa.Length; k += OasisStride)
            {
                float top = oa[k + 5] + OasisHeadM;
                list.Add(Pool(OasisPool, water, oa[k], oa[k + 1], OasisReach(oa[k + 2], oa[k + 3], top), oa[k + 3], oa[k + 4] - OasisHeadM, top));
            }
            list.AddRange(CavePools());
            return list.ToArray();
        }

        LiquidPool Pool(int kind, int liquid, float x, float z, float reach, float designed, float bottom, float top) => Pool(kind, liquid, x, z, reach, designed, bottom, top, null);

        LiquidPool Pool(int kind, int liquid, float x, float z, float reach, float designed, float bottom, float top, FeatureEdit[] own)
        {
            float half = reach + PoolPadM;
            int i0 = (int)Math.Floor((x - half) / PoolStepM), j0 = (int)Math.Floor((z - half) / PoolStepM);
            int nx = (int)Math.Ceiling((x + half) / PoolStepM) - i0 + 1, nz = (int)Math.Ceiling((z + half) / PoolStepM) - j0 + 1;
            var p = new LiquidPool { X = x, Z = z, Reach = reach, Designed = designed, Kind = kind, Liquid = liquid, I0 = i0, J0 = j0, Nx = nx, Nz = nz };
            float x0 = i0 * PoolStepM, z0 = j0 * PoolStepM, x1 = x0 + (nx - 1) * PoolStepM, z1 = z0 + (nz - 1) * PoolStepM;
            var near = new List<FeatureEdit>();
            if (own != null) near.AddRange(own);
            foreach (var f in edits)
                if ((own == null || f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring) && f.MaxX >= x0 - PoolSlopeM && f.MinX <= x1 + PoolSlopeM && f.MaxZ >= z0 - PoolSlopeM && f.MinZ <= z1 + PoolSlopeM) near.Add(f);
            var list = new EditArray { Items = near.ToArray() };
            var g = new DetailGround { Detail = detail };
            p.Ground = new float[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    p.Ground[j * nx + i] = Walk.Surface(g, list, PoolSlopeM, p.NodeX(i), p.NodeZ(j), top, bottom);
            int ci = Math.Max(0, Math.Min(nx - 1, (int)Math.Round(x / PoolStepM) - i0)), cj = Math.Max(0, Math.Min(nz - 1, (int)Math.Round(z / PoolStepM) - j0));
            p.Centre = cj * nx + ci;
            p.Rim = Spill(p.Ground, nx, nz, p.Centre);
            p.Level = Math.Min(designed, p.Rim - (kind == QuicksandPool ? QuicksandGapM : PoolGapM));
            Flood(p);
            return p;
        }

        static float Spill(float[] ground, int nx, int nz, int start)
        {
            int count = nx * nz;
            var order = new int[count];
            var keys = new float[count];
            var parent = new int[count];
            var edge = new bool[count];
            for (int k = 0; k < count; k++)
            {
                order[k] = k; keys[k] = ground[k]; parent[k] = -1;
                int i = k % nx, j = k / nx;
                edge[k] = i == 0 || j == 0 || i == nx - 1 || j == nz - 1;
            }
            Array.Sort(keys, order);
            int Root(int k)
            {
                while (parent[k] != k) { parent[k] = parent[parent[k]]; k = parent[k]; }
                return k;
            }
            for (int q = 0; q < count; q++)
            {
                int k = order[q], i = k % nx, j = k / nx;
                parent[k] = k;
                for (int d = 0; d < 9; d++)
                {
                    if (d == 4) continue;
                    int ni = i + d % 3 - 1, nj = j + d / 3 - 1;
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                    int m = nj * nx + ni;
                    if (parent[m] < 0) continue;
                    int a = Root(k), b = Root(m);
                    if (a == b) continue;
                    parent[b] = a;
                    edge[a] |= edge[b];
                }
                if (parent[start] >= 0 && edge[Root(start)]) return keys[q];
            }
            return float.PositiveInfinity;
        }

        static void Flood(LiquidPool p)
        {
            int nx = p.Nx, nz = p.Nz;
            p.Wet = new bool[nx * nz];
            p.WetCount = 0; p.Leaks = false; p.Floor = p.Ground[p.Centre];
            if (!(p.Ground[p.Centre] < p.Level)) return;
            var stack = new Stack<int>();
            stack.Push(p.Centre);
            p.Wet[p.Centre] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop(), i = k % nx, j = k / nx;
                p.WetCount++;
                p.Floor = Math.Min(p.Floor, p.Ground[k]);
                if (i == 0 || j == 0 || i == nx - 1 || j == nz - 1) p.Leaks = true;
                for (int d = 0; d < 9; d++)
                {
                    if (d == 4) continue;
                    int ni = i + d % 3 - 1, nj = j + d / 3 - 1;
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                    int m = nj * nx + ni;
                    if (p.Wet[m] || !(p.Ground[m] < p.Level)) continue;
                    p.Wet[m] = true;
                    stack.Push(m);
                }
            }
        }
    }
}
