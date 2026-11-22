using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public static readonly string[] DiscoveryNames = { "", "hilltop", "clearing" };
        public const int Hilltop = 1, Clearing = 2;
        public const float DiscoveryInset = 0.2f, TownClearM = 400f, DiscoveryProbeM = 40f, HilltopRiseM = 1f, ClearingReliefM = 1.5f;
        public const int DiscoveryTries = 16, ClimbSteps = 12;
        public const float ClimbStartM = 16f, ClimbEndM = 2f;
        readonly object discoveryLock = new object();
        ObjectInstance[] discoveries;
        ObjectInstance[][] builtSites;
        List<LandmarkSketch> towns;

        public List<LandmarkSketch> Towns => towns ?? (towns = (w.Sketch ?? WorldSketch.Generate(w.Config)).Landmarks);

        public static float DiscoveryStep(float worldM, float spacingM) => worldM / Math.Max(1, (int)Math.Floor(worldM / spacingM));

        public ObjectInstance[] Discoveries()
        {
            var found = discoveries;
            if (found != null) return found;
            lock (discoveryLock)
            {
                if (discoveries != null) return discoveries;
                builtSites = new ObjectInstance[across * across][];
                var list = new List<ObjectInstance>();
                ulong root = Rng.Stream(w.Config.Seed, "discoveries").NextU64();
                for (int d = 0; d < lib.Objects.Count; d++)
                    if (lib.Objects[d].Placement.Discovery > 0) PlaceDiscoveries(d, root, list);
                builtSites = null;
                return discoveries = list.ToArray();
            }
        }

        void PlaceDiscoveries(int d, ulong root, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            float step = DiscoveryStep(worldM, pl.SpacingM), inset = DiscoveryInset * step;
            int cells = (int)Math.Round(worldM / step);
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    var rng = new Rng(Rng.Mix(root ^ key[d] ^ ((ulong)(gz * cells + gx + 1) * 0x9E3779B97F4A7C15UL)));
                    bool roll = rng.NextFloat() < pl.Chance, any = false;
                    float best = float.MinValue;
                    ObjectInstance pick = default;
                    for (int t = 0; t < DiscoveryTries; t++)
                    {
                        float px = gx * step + inset + rng.NextFloat() * (step - 2 * inset), pz = gz * step + inset + rng.NextFloat() * (step - 2 * inset);
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        if (!roll) continue;
                        if (pl.Discovery == Hilltop) Climb(ref px, ref pz, gx * step + inset, gz * step + inset, (gx + 1) * step - inset, (gz + 1) * step - inset);
                        float fit = Fitness(pl.Discovery, px, pz);
                        if (fit <= best || !Open(d, variant, size, px, pz) || !Accept(d, px, pz, size, qa, qb, out var o)) continue;
                        o.Variant = variant;
                        if (!ClearOfBuilt(o)) continue;
                        best = fit; pick = o; any = true;
                    }
                    if (any) list.Add(pick);
                }
        }

        bool Open(int d, int variant, float size, float x, float z)
        {
            float r = SiteRadius(new ObjectInstance { Def = d, Variant = variant, Size = size });
            int n = w.Config.Size;
            for (int k = 0; k < 9; k++)
            {
                float ux = k % 3 - 1, uz = k / 3 - 1, len = ux != 0 && uz != 0 ? 0.70710678f : 1f;
                float px = x + r * ux * len, pz = z + r * uz * len;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return false;
                int i = Math.Min(n - 1, (int)(pz / cm)) * n + Math.Min(n - 1, (int)(px / cm));
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) return false;
            }
            foreach (var l in Towns)
            {
                float dx = l.X * cm - x, dz = l.Y * cm - z;
                if (dx * dx + dz * dz < TownClearM * TownClearM) return false;
            }
            return true;
        }

        bool ClearOfBuilt(ObjectInstance o)
        {
            float r = SiteRadius(o), span = WorldChunks.Size * cm;
            int reach = (int)Math.Ceiling((r + siteReach) / span);
            int cx = (int)(o.X / span), cz = (int)(o.Z / span);
            for (int z = Math.Max(0, cz - reach); z <= Math.Min(across - 1, cz + reach); z++)
                for (int x = Math.Max(0, cx - reach); x <= Math.Min(across - 1, cx + reach); x++)
                {
                    int c = z * across + x;
                    var built = builtSites[c];
                    if (built == null)
                    {
                        var list = new List<ObjectInstance>();
                        PlaceInto(x, z, false, list, null, true, true);
                        builtSites[c] = built = list.ToArray();
                    }
                    foreach (var s in built)
                        if (Math.Min(SiteGap(s, o.X, o.Z), SiteGap(o, s.X, s.Z)) < r + SiteMarginM + SiteBandM) return false;
                }
            return true;
        }

        void Climb(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float y = GroundAt(detail, cm, x, z);
            for (float s = ClimbStartM; s >= ClimbEndM; s *= 0.5f)
                for (int n = 0; n < ClimbSteps; n++)
                {
                    float bx = x, bz = z, by = y;
                    for (int k = 0; k < 8; k++)
                    {
                        FanDir(k, 0.70710678f, out float ux, out float uz);
                        float px = Math.Min(x1, Math.Max(x0, x + ux * s)), pz = Math.Min(z1, Math.Max(z0, z + uz * s));
                        float py = GroundAt(detail, cm, px, pz);
                        if (py > by) { bx = px; bz = pz; by = py; }
                    }
                    if (by <= y) break;
                    x = bx; z = bz; y = by;
                }
        }

        float Fitness(int kind, float x, float z)
        {
            float y = GroundAt(detail, cm, x, z), r = DiscoveryProbeM, rise = 0, relief = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * r, pz = z + uz * r;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
                float dy = y - GroundAt(detail, cm, px, pz);
                rise += dy;
                relief = Math.Max(relief, Math.Abs(dy));
            }
            rise *= 0.125f;
            if (kind == Hilltop) return rise >= HilltopRiseM ? rise : float.MinValue;
            return relief <= ClearingReliefM ? -relief : float.MinValue;
        }

        void EmitDiscoveries(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list)
        {
            int ordinal = 0;
            foreach (var o in Discoveries())
            {
                if (o.Def != d || o.X < x0 || o.Z < z0 || o.X >= x0 + sx || o.Z >= z0 + sz) continue;
                var s = o;
                s.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(s);
            }
        }

        public float SiteRadius(ObjectInstance s)
        {
            var box = siteBox[s.Def];
            int v = s.Variant * 4;
            float ex = Math.Max(-box[v], box[v + 1]), ez = Math.Max(-box[v + 2], box[v + 3]);
            return (float)Math.Sqrt(ex * ex + ez * ez) * s.Size;
        }
    }
}
