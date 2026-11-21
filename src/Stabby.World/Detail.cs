using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class TerrainDetail
    {
        public const float WetFadeCells = 3f;
        public const int BandSamples = 64;

        readonly NearArrays cells;
        readonly float cm;
        readonly int n;

        public TerrainDetail(GeneratedWorld w)
        {
            n = w.Config.Size;
            cm = w.Config.CellMeters;
            var h = w.Height;
            float[] dist = null, offsets = null, bands;
            List<RiverPiece> pieces = null;
            int[] index = null, items = null;
            bool strata = w.Materials != null && w.Materials.Strata.Count > 0;
            Parallel.Invoke(
                () => dist = WetDistance(w),
                () => offsets = strata ? w.Materials.Offsets(w.Config) : new float[n * n],
                () => { pieces = NearField.Pieces(w); NearField.Index(pieces, n, cm, out index, out items); });
            var amp = new float[n * n];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float v = h[Clamp(y + dy) * n + Clamp(x + dx)];
                            lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                        }
                    if (lo <= 0) continue;
                    float a = 2f + 0.12f * (hi - lo);
                    a *= Grid.Smoothstep(0.5f, 0.5f + WetFadeCells, dist[i]);
                    amp[i] = Math.Min(Math.Min(a, 40f), 0.6f * lo);
                }
            });
            float period;
            if (strata)
            {
                var field = new StrataField(w.Config, w.Materials);
                period = w.Materials.PeriodM;
                bands = new float[BandSamples];
                for (int k = 0; k < BandSamples; k++) bands[k] = field.Hardness(0, k * period / BandSamples - offsets[0]);
            }
            else
            {
                period = 1f;
                bands = new[] { 0.5f };
            }
            cells = new NearArrays(n, cm, Rng.Stream(w.Config.Seed, "detail").NextU32(), Rng.Stream(w.Config.Seed, "near-gully").NextU32(), Rng.Stream(w.Config.Seed, "near-rock").NextU32(),
                h, amp, offsets, period, bands, pieces.ToArray(), index, items);
        }

        public int Size => n;
        public float CellMeters => cm;
        public NearArrays Cells => cells;
        public uint Seed => cells.Seed;
        public uint GullySeed => cells.GullySeed;
        public uint RockSeed => cells.RockSeed;
        public float Height(int cell) => cells.Height(cell);
        public float Amplitude(int cell) => cells.Amp(cell);

        public float Sample(float x, float z) => NearField.Sample(cells, x, z);
        public float Smooth(float x, float z) => NearField.Smooth(cells, x, z);
        public float HeightAt(float gx, float gy) => Sample(gx * cm, gy * cm);

        public void ChunkRange<TEdits>(TEdits edits, float x0, float z0, float size, float pad, out float lo, out float hi) where TEdits : struct, IEditList
        {
            NearField.Range(cells, x0, z0, x0 + size, z0 + size, out lo, out hi);
            for (int q = 0, count = edits.Count; q < count; q++)
            {
                var e = edits.At(q);
                if (e.MaxX < x0 || e.MinX > x0 + size || e.MaxZ < z0 || e.MinZ > z0 + size) continue;
                lo = Math.Min(lo, e.MinY); hi = Math.Max(hi, e.MaxY);
            }
            lo -= pad; hi += pad;
        }

        int Clamp(int v) => v < 0 ? 0 : v >= n ? n - 1 : v;

        static float[] WetDistance(GeneratedWorld w)
        {
            int n = w.Config.Size;
            var d = new float[n * n];
            for (int i = 0; i < d.Length; i++)
                d[i] = w.Sea[i] || (w.Water != null && w.Water.LakeId[i] >= 0) ? 0f : float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? 0 : n * n - 1, step = pass == 0 ? 1 : -1;
                for (int i = start; i >= 0 && i < n * n; i += step)
                {
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        bool back = Grid.DY[k] < 0 || (Grid.DY[k] == 0 && Grid.DX[k] < 0);
                        if (back != (pass == 0)) continue;
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        float v = d[ny * n + nx];
                        if (v != float.MaxValue) d[i] = Math.Min(d[i], v + Grid.Dist[k]);
                    }
                }
            }
            return d;
        }
    }

    public readonly struct NearArrays : INearCells
    {
        readonly float[] h, amp, off, bands;
        readonly RiverPiece[] pieces;
        readonly int[] start, items;
        readonly int n;
        readonly float cm, period;
        readonly uint seed, gully, rock;

        public NearArrays(int n, float cm, uint seed, uint gully, uint rock, float[] h, float[] amp, float[] off, float period, float[] bands, RiverPiece[] pieces, int[] start, int[] items)
        {
            this.n = n; this.cm = cm; this.seed = seed; this.gully = gully; this.rock = rock;
            this.h = h; this.amp = amp; this.off = off; this.period = period; this.bands = bands;
            this.pieces = pieces; this.start = start; this.items = items;
        }

        public int N => n;
        public float CellMeters => cm;
        public uint Seed => seed;
        public uint GullySeed => gully;
        public uint RockSeed => rock;
        public float Period => period;
        public int Bands => bands.Length;
        public int PieceCount => pieces.Length;
        public int IndexLength => items.Length;
        public float Height(int i) => h[i];
        public float Amp(int i) => amp[i];
        public float Offset(int i) => off[i];
        public float Band(int k) => bands[k];
        public int PieceStart(int cell) => start[cell];
        public int PieceAt(int k) => items[k];
        public RiverPiece Piece(int p) => pieces[p];
    }
}

namespace Stabby.World
{
    public static class Detail
    {
        public const float ReferenceLines = 1080f;
        public const float ReferenceFov = 60f;
        public const float Hysteresis = 0.15f;
        public const float MaxZoom = 1.5f;
        public const float ReferencePixelsPerMetre = 935.3074f;

        public static float PixelsPerMetreAt(float fovDegrees)
        {
            double f = Math.Min(Math.Max(fovDegrees, 5f), 170f) * Math.PI / 360.0;
            return (float)(ReferenceLines / (2.0 * Math.Tan(f)));
        }

        public static float ScreenSize(float sizeM, float distanceM, float pixelsPerMetre) =>
            sizeM / Math.Max(distanceM, 0.01f) * pixelsPerMetre;

        public static float Reach(float sizeM, float pixels, float pixelsPerMetre) =>
            sizeM * pixelsPerMetre / Math.Max(pixels, 1e-3f);

        public static float SimulationReach(float sizeM, float pixels) => Reach(sizeM, pixels, ReferencePixelsPerMetre);

        public struct Thresholds
        {
            public float Near, Mid, Far;

            public Thresholds(float near, float mid, float far) { Near = near; Mid = mid; Far = far; }

            public Thresholds Scaled(float quality)
            {
                float q = Math.Max(quality, 0.1f);
                return new Thresholds(Near / q, Mid / q, Far / q);
            }
        }

        public static int Tier(float pixels, in Thresholds t, int previous)
        {
            int plain = pixels > t.Near ? 0 : pixels > t.Mid ? 1 : pixels > t.Far ? 2 : 3;
            if (previous < 0 || plain <= previous) return plain;
            float k = 1f - Hysteresis;
            int held = pixels > t.Near * k ? 0 : pixels > t.Mid * k ? 1 : pixels > t.Far * k ? 2 : 3;
            return Math.Max(held, previous);
        }

        public static bool Holds(float value, float boundary, bool was) =>
            value > boundary || was && value > boundary * (1f - Hysteresis);

        public static float Crossfade(float value, float from, float to)
        {
            float t = Math.Min(Math.Max((value - from) / (to - from), 0f), 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
