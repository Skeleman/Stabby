using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class ObjectKind
    {
        public string Id;
        public int Lods = 3;
        public float[] LodScreen = { 0.08f, 0.025f };
        public float FadeScreen = 0.006f;
        public bool Shadows = true;
        public float ShadowScreen = 0.03f;
        public bool Wind, Animated;
        public bool Solid = true;
    }

    public sealed class ObjectPart
    {
        public const int Rock = 0, Log = 1, Stump = 2, Shapes = 3;
        public static readonly string[] ShapeNames = { "rock", "log", "stump" };

        public string Id;
        public int Shape;
        public float OffsetX, OffsetY, OffsetZ;
        public float ScaleX = 1, ScaleY = 1, ScaleZ = 1;
        public int Solid = -1, Shadows = -1;
        public uint Color;
        public bool HasColor;
    }

    public sealed class ObjectPlacement
    {
        public List<string> Biomes = new List<string>();
        public float PerHectare;
        public float SlopeMin, SlopeMax = 10f;
        public float HardnessMin, HardnessMax = 1f;
        public float SizeMinM = 1f, SizeMaxM = 1f;
        public int ClusterCount = 1;
        public float ClusterRadiusM;
        public float Sink;
        public float AlignToSlope;
        public float FootRiseM, FootReachM = 12f;
        public List<BiomeDensity> BiomeDensity = new List<BiomeDensity>();
    }

    public sealed class BiomeDensity
    {
        public string Biome;
        public float Scale = 1f;
    }

    public sealed class ObjectDef
    {
        public string Id, Name, Kind;
        public uint Color;
        public int Variants = 1;
        public List<ObjectPart> Parts = new List<ObjectPart>();
        public ObjectPlacement Placement = new ObjectPlacement();

        public float FootprintRadius()
        {
            float r = 0;
            foreach (var p in Parts)
            {
                float reach = (float)Math.Sqrt(p.OffsetX * p.OffsetX + p.OffsetZ * p.OffsetZ) + 0.5f * Math.Max(p.ScaleX, p.ScaleZ);
                if (reach > r) r = reach;
            }
            return r;
        }
    }

    public sealed class ObjectLibrary
    {
        public List<ObjectKind> Kinds = new List<ObjectKind>();
        public List<ObjectDef> Objects = new List<ObjectDef>();
        public FoliageDef Foliage = new FoliageDef();

        public ObjectKind KindOf(ObjectDef d)
        {
            foreach (var k in Kinds) if (k.Id == d.Kind) return k;
            return null;
        }

        public bool PartSolid(ObjectDef d, ObjectPart p) => p.Solid >= 0 ? p.Solid == 1 : KindOf(d).Solid;
        public bool PartShadows(ObjectDef d, ObjectPart p) => p.Shadows >= 0 ? p.Shadows == 1 : KindOf(d).Shadows;

        public static readonly string[] KindNames = { "static", "interactive", "animated", "structure" };

        public static List<string> Validate(ObjectLibrary lib, IReadOnlyList<string> biomeIds = null)
        {
            var errors = new List<string>();
            var kinds = new HashSet<string>();
            foreach (var k in lib.Kinds)
            {
                if (Array.IndexOf(KindNames, k.Id) < 0) errors.Add("unknown kind " + k.Id);
                else if (!kinds.Add(k.Id)) errors.Add("duplicate kind " + k.Id);
                if (k.Lods < 1 || k.Lods > 4) errors.Add(k.Id + ": lods must be 1-4");
                if (k.LodScreen == null || k.LodScreen.Length != k.Lods - 1) errors.Add(k.Id + ": lodScreen needs lods - 1 entries");
                else for (int i = 0; i < k.LodScreen.Length; i++)
                    if (k.LodScreen[i] <= k.FadeScreen || (i > 0 && k.LodScreen[i] >= k.LodScreen[i - 1])) errors.Add(k.Id + ": lodScreen must fall and stay above fadeScreen");
                if (k.FadeScreen <= 0 || k.FadeScreen >= 1) errors.Add(k.Id + ": fadeScreen must be in (0, 1)");
                if (k.ShadowScreen < 0) errors.Add(k.Id + ": shadowScreen below 0");
            }
            var ids = new HashSet<string>();
            foreach (var d in lib.Objects)
            {
                if (string.IsNullOrEmpty(d.Id)) { errors.Add("object without id"); continue; }
                if (!ids.Add(d.Id)) errors.Add("duplicate object id " + d.Id);
                if (!kinds.Contains(d.Kind ?? "")) errors.Add(d.Id + ": kind " + d.Kind + " is not defined");
                if (d.Variants < 1 || d.Variants > 16) errors.Add(d.Id + ": variants must be 1-16");
                if (d.Parts.Count == 0) errors.Add(d.Id + ": needs at least one part");
                foreach (var p in d.Parts)
                {
                    if (p.Shape < 0 || p.Shape >= ObjectPart.Shapes) errors.Add(d.Id + ": part " + p.Id + " has an unknown shape");
                    if (p.ScaleX <= 0 || p.ScaleY <= 0 || p.ScaleZ <= 0) errors.Add(d.Id + ": part " + p.Id + " scale must be above 0");
                }
                var pl = d.Placement;
                if (pl.PerHectare < 0 || pl.PerHectare > 400) errors.Add(d.Id + ": perHectare must be 0-400");
                if (pl.SlopeMax <= pl.SlopeMin || pl.SlopeMin < 0) errors.Add(d.Id + ": slopeMax must exceed slopeMin >= 0");
                if (pl.HardnessMax <= pl.HardnessMin || pl.HardnessMin < 0 || pl.HardnessMax > 1) errors.Add(d.Id + ": hardness range must lie in 0-1 and be increasing");
                if (pl.SizeMinM <= 0 || pl.SizeMaxM < pl.SizeMinM || pl.SizeMaxM > 20) errors.Add(d.Id + ": sizeMinM/sizeMaxM must be increasing, 0-20 m");
                if (pl.ClusterCount < 1 || pl.ClusterCount > 32) errors.Add(d.Id + ": clusterCount must be 1-32");
                if (pl.ClusterRadiusM < 0 || pl.ClusterRadiusM > 30) errors.Add(d.Id + ": clusterRadiusM must be 0-30");
                if (pl.Sink < 0 || pl.Sink > 0.9f) errors.Add(d.Id + ": sink must be 0-0.9");
                if (pl.AlignToSlope < 0 || pl.AlignToSlope > 1) errors.Add(d.Id + ": alignToSlope must be 0-1");
                if (pl.FootRiseM < 0 || pl.FootRiseM > 100) errors.Add(d.Id + ": footRiseM must be 0-100");
                if (pl.FootReachM <= 0 || pl.FootReachM > 60) errors.Add(d.Id + ": footReachM must be above 0, at most 60");
                foreach (var bd in pl.BiomeDensity)
                {
                    if (bd.Scale < 0 || bd.Scale > 1) errors.Add(d.Id + ": biomeDensity scale must be 0-1");
                    if (pl.Biomes.Count > 0 && !pl.Biomes.Contains(bd.Biome ?? "")) errors.Add(d.Id + ": biomeDensity " + bd.Biome + " is not in its biomes");
                    if (biomeIds != null && !Contains(biomeIds, bd.Biome ?? "")) errors.Add(d.Id + ": unknown biome " + bd.Biome);
                }
                if (biomeIds != null)
                    foreach (var b in pl.Biomes)
                        if (!Contains(biomeIds, b)) errors.Add(d.Id + ": unknown biome " + b);
            }
            lib.Foliage.Validate(errors);
            return errors;
        }

        static bool Contains(IReadOnlyList<string> list, string s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return true;
            return false;
        }

        public static int ShapeOf(string name) => Array.IndexOf(ObjectPart.ShapeNames, name);

        public static int Tri(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            return value == "on" || value == "solid" ? 1 : value == "off" || value == "none" ? 0 : -2;
        }
    }
}
