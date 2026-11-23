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
            float[] amp = null, offsets = null, bands;
            List<RiverPiece> pieces = null;
            int[] index = null, items = null;
            bool strata = w.Materials != null && w.Materials.Strata.Count > 0;
            Parallel.Invoke(
                () => amp = WetDistance(w),
                () => offsets = strata ? w.Materials.Offsets(w.Config) : new float[n * n],
                () => { pieces = NearField.Pieces(w); NearField.Index(pieces, n, cm, out index, out items); });
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float dist = amp[i], lo = float.MaxValue, hi = float.MinValue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float v = h[Clamp(y + dy) * n + Clamp(x + dx)];
                            lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                        }
                    if (lo <= 0) { amp[i] = 0; continue; }
                    float a = 2f + 0.12f * (hi - lo);
                    a *= Grid.Smoothstep(0.5f, 0.5f + WetFadeCells, dist);
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
            cells = new NearArrays(n, cm, Rng.Stream(w.Config.Seed, "detail").NextU32(), Rng.Stream(w.Config.Seed, "near-gully").NextU32(), Rng.Stream(w.Config.Seed, "near-rock").NextU32(), Rng.Stream(w.Config.Seed, "near-coast").NextU32(), Rng.Stream(w.Config.Seed, "near-crag").NextU32(), Rng.Stream(w.Config.Seed, "near-river").NextU32(),
                h, amp, offsets, period, bands, pieces.ToArray(), index, items);
        }

        public int Size => n;
        public float CellMeters => cm;
        public NearArrays Cells => cells;
        public uint Seed => cells.Seed;
        public uint GullySeed => cells.GullySeed;
        public uint RockSeed => cells.RockSeed;
        public uint CoastSeed => cells.CoastSeed;
        public uint CragSeed => cells.CragSeed;
        public float Height(int cell) => cells.Height(cell);
        public float Amplitude(int cell) => cells.Amp(cell);
        public float[] Offsets => cells.Offsets;

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
        readonly uint seed, gully, rock, coast, crag, river;

        public NearArrays(int n, float cm, uint seed, uint gully, uint rock, uint coast, uint crag, uint river, float[] h, float[] amp, float[] off, float period, float[] bands, RiverPiece[] pieces, int[] start, int[] items)
        {
            this.n = n; this.cm = cm; this.seed = seed; this.gully = gully; this.rock = rock; this.coast = coast; this.crag = crag; this.river = river;
            this.h = h; this.amp = amp; this.off = off; this.period = period; this.bands = bands;
            this.pieces = pieces; this.start = start; this.items = items;
        }

        public int N => n;
        public float CellMeters => cm;
        public uint Seed => seed;
        public uint GullySeed => gully;
        public uint RockSeed => rock;
        public uint CoastSeed => coast;
        public uint CragSeed => crag;
        public uint RiverSeed => river;
        public float Period => period;
        public int Bands => bands.Length;
        public int PieceCount => pieces.Length;
        public int IndexLength => items.Length;
        public float Height(int i) => h[i];
        public float Amp(int i) => amp[i];
        public float Offset(int i) => off[i];
        public float[] Offsets => off;
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
        public const float CrossfadeSeconds = 0.75f;
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

        public const int ShadowCascade = 0, ShadowLow = 1, ShadowNone = 2, ShadowFarMap = 3, ShadowCanopy = 4;
        public const int AnimFull = 0, AnimDecimated = 1, AnimSway = 2, AnimFrozen = 3;
        public const int LightShadowed = 0, LightUnshadowed = 1, LightGlow = 2, LightEmissive = 3;
        public const int GeometryGone = 3;
        public const float Never = float.MaxValue;

        public struct Rule
        {
            public Thresholds Geometry, Animation, Light;
            public float ShadowCascadePixels, ShadowPixels;

            public Rule(Thresholds geometry, float shadowCascadePixels, float shadowPixels, Thresholds animation, Thresholds light)
            {
                Geometry = geometry;
                ShadowCascadePixels = shadowCascadePixels;
                ShadowPixels = shadowPixels;
                Animation = animation;
                Light = light;
            }
        }

        public struct Decision
        {
            public ushort Bits;

            public Decision(int geometry, int shadow, int animation, int light) =>
                Bits = (ushort)((geometry & 3 | (shadow & 7) << 2 | (animation & 3) << 5 | (light & 3) << 7) + 1);

            public bool IsFirst => Bits == 0;
            public int Geometry => IsFirst ? -1 : Bits - 1 & 3;
            public int Shadow => IsFirst ? -1 : Bits - 1 >> 2 & 7;
            public int Animation => IsFirst ? -1 : Bits - 1 >> 5 & 3;
            public int Light => IsFirst ? -1 : Bits - 1 >> 7 & 3;
            public bool Casts => !IsFirst && Shadow < ShadowNone;
        }

        public static Decision Decide(in Rule rule, float sizeM, float distanceM, float pixelsPerMetre, float quality, Decision previous)
        {
            float px = ScreenSize(sizeM, distanceM, pixelsPerMetre);
            int geometry = Tier(px, rule.Geometry.Scaled(quality), previous.Geometry);
            int shadow = geometry == GeometryGone ? ShadowNone : ShadowTier(px, rule, quality, previous.Shadow);
            int animation = Tier(px, rule.Animation.Scaled(quality), previous.Animation);
            int light = Tier(px, rule.Light.Scaled(quality), previous.Light);
            return new Decision(geometry, shadow, animation, light);
        }

        static int ShadowTier(float px, in Rule rule, float quality, int previous)
        {
            var t = new Thresholds(rule.ShadowCascadePixels, rule.ShadowPixels, rule.ShadowPixels).Scaled(quality);
            int was = previous == ShadowCascade ? 0 : previous == ShadowLow ? 1 : previous < 0 ? -1 : 3;
            int tier = Tier(px, t, was);
            return tier == 0 ? ShadowCascade : tier == 1 ? ShadowLow : ShadowNone;
        }

        public static float ShadowReach(float sizeM, in Rule rule, float pixelsPerMetre, float quality) =>
            rule.ShadowPixels >= Never ? 0 : Reach(sizeM, rule.ShadowPixels / Math.Max(quality, 0.1f), pixelsPerMetre);

        public static float CascadeReach(float sizeM, in Rule rule, float pixelsPerMetre, float quality) =>
            rule.ShadowCascadePixels >= Never ? 0 : Reach(sizeM, rule.ShadowCascadePixels / Math.Max(quality, 0.1f), pixelsPerMetre);

        public struct ShadowSpan
        {
            public float Near, Range;

            public void Add(float sizeM, in Rule rule, float pixelsPerMetre, float quality)
            {
                float far = ShadowReach(sizeM, rule, pixelsPerMetre, quality) / (1f - Hysteresis);
                if (far <= 0) return;
                Range = Math.Max(Range, far);
                float near = CascadeReach(sizeM, rule, pixelsPerMetre, quality);
                if (near > 0) Near = Near > 0 ? Math.Min(Near, near) : near;
            }
        }

        public const float RangeStep = 1.0905077f;

        public static float SnapRange(float range, float previous)
        {
            if (range <= 0) return 0;
            if (previous > 0 && range <= previous && range > previous * (1f - Hysteresis)) return previous;
            float steps = (float)Math.Ceiling(FMath.Log(range) / FMath.Log(RangeStep));
            return (float)FMath.Pow(RangeStep, steps);
        }

        public struct Cascades
        {
            public int Count;
            public float Split1, Split2, Split3;
        }

        public static Cascades CascadeSplits(float nearM, float rangeM)
        {
            float ratio = Math.Max(rangeM / Math.Max(nearM, 1e-3f), 4f);
            int count = Math.Min(Math.Max((int)Math.Ceiling(FMath.Log(ratio) / FMath.Log(4.0)), 2), 4);
            double a = FMath.Pow(1.0 / ratio, 1.0 / count);
            return new Cascades
            {
                Count = count,
                Split1 = (float)FMath.Pow(a, count - 1),
                Split2 = count > 2 ? (float)FMath.Pow(a, count - 2) : 0,
                Split3 = count > 3 ? (float)a : 0,
            };
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
