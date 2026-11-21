using System.Globalization;
using System.Text.Json;
using Stabby.World;

namespace Stabby.Content;

public static class ContentLoader
{
    public const int BiomeSchemaVersion = 1;
    public const int MaterialSchemaVersion = 2;

    sealed class MaterialFile
    {
        public int SchemaVersion { get; set; }
        public float OffsetAmplitudeM { get; set; }
        public float OffsetWavelengthM { get; set; }
        public float TiltPerKm { get; set; }
        public SurfaceEntry? Surface { get; set; }
        public WaterEntry? Water { get; set; }
        public List<StratumEntry> Strata { get; set; } = new();
    }

    sealed class WaterEntry
    {
        public string DeepColor { get; set; } = "";
        public string ShallowColor { get; set; } = "";
        public float ClarityM { get; set; }
        public float ShoreM { get; set; }
        public float FoamWidthM { get; set; }
        public float WaveScaleM { get; set; }
        public float WaveSpeed { get; set; }
        public float WaveStrength { get; set; }
        public float SwellScaleM { get; set; }
        public float SwellStrength { get; set; }
        public float LakeSwell { get; set; }
        public float FlowSpeed { get; set; }
        public float RapidsSlope { get; set; }
        public float BedFieldM { get; set; }
    }

    sealed class SurfaceEntry
    {
        public float FadePixels { get; set; }
        public float ParallaxFootprintM { get; set; }
        public float WindDeg { get; set; }
        public float StrataWobbleM { get; set; }
        public float StrataWobbleWavelengthM { get; set; }
        public float CoatPatchAmp { get; set; }
        public float CoatPatchWavelengthM { get; set; }
        public RockEntry? Rock { get; set; }
        public CoatsEntry? Coats { get; set; }
    }

    sealed class RockEntry
    {
        public float GrainAmp { get; set; }
        public float GrainWavelengthM { get; set; }
        public float BumpAmp { get; set; }
        public float BumpWavelengthM { get; set; }
        public float BedSpacingM { get; set; }
        public float BedDepthM { get; set; }
        public float JointSpacingM { get; set; }
        public float JointDepthM { get; set; }
        public float Parallax { get; set; }
    }

    sealed class CoatsEntry
    {
        public CoatEntry? Grass { get; set; }
        public CoatEntry? Soil { get; set; }
        public CoatEntry? Sand { get; set; }
        public CoatEntry? Snow { get; set; }
    }

    sealed class CoatEntry
    {
        public List<string> Biomes { get; set; } = new();
        public float BumpAmp { get; set; }
        public float BumpWavelengthM { get; set; }
        public float GrainAmp { get; set; }
        public float GrainWavelengthM { get; set; }
        public float FeatureAmp { get; set; }
        public float FeatureWavelengthM { get; set; }
        public float Parallax { get; set; }
        public float AlbedoVar { get; set; }
    }

    sealed class StratumEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#ff00ff";
        public float ThicknessM { get; set; }
        public float Hardness { get; set; }
    }

    sealed class BiomeFile
    {
        public int SchemaVersion { get; set; }
        public List<BiomeEntry> Biomes { get; set; } = new();
    }

    sealed class BiomeEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public float TempMinC { get; set; }
        public float TempMaxC { get; set; }
        public float PrecipMinMm { get; set; }
        public float PrecipMaxMm { get; set; }
        public string Color { get; set; } = "#ff00ff";
        public float? HeightMinM { get; set; }
        public float? HeightMaxM { get; set; }
        public float? WaterWithinM { get; set; }
        public float? CoastWithinM { get; set; }
    }

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static string FindContentRoot(string start)
    {
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "content", "biomes.json")))
                return Path.Combine(d.FullName, "content");
        throw new DirectoryNotFoundException("content/biomes.json not found above " + start);
    }

    public static List<BiomeDef> LoadBiomes(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "biomes.json");
        var file = JsonSerializer.Deserialize<BiomeFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidDataException(path + " is empty");
        if (file.SchemaVersion != BiomeSchemaVersion)
            throw new InvalidDataException($"{path}: schemaVersion {file.SchemaVersion}, expected {BiomeSchemaVersion}");
        var defs = file.Biomes.Select(b => new BiomeDef
        {
            Id = b.Id, Name = b.Name,
            TempMinC = b.TempMinC, TempMaxC = b.TempMaxC,
            PrecipMinMm = b.PrecipMinMm, PrecipMaxMm = b.PrecipMaxMm,
            HeightMinM = b.HeightMinM ?? float.NaN, HeightMaxM = b.HeightMaxM ?? float.NaN,
            WaterWithinM = b.WaterWithinM ?? float.NaN, CoastWithinM = b.CoastWithinM ?? float.NaN,
            Color = uint.Parse(b.Color.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        }).ToList();
        var errors = Biomes.Validate(defs);
        if (errors.Count > 0) throw new InvalidDataException(path + ": " + string.Join("; ", errors));
        return defs;
    }

    public static MaterialLibrary LoadMaterials(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "materials.json");
        var file = JsonSerializer.Deserialize<MaterialFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidDataException(path + " is empty");
        if (file.SchemaVersion != MaterialSchemaVersion)
            throw new InvalidDataException($"{path}: schemaVersion {file.SchemaVersion}, expected {MaterialSchemaVersion}");
        var lib = new MaterialLibrary
        {
            OffsetAmplitudeM = file.OffsetAmplitudeM, OffsetWavelengthM = file.OffsetWavelengthM, TiltPerKm = file.TiltPerKm,
            Surface = file.Surface == null ? null! : Surface(file.Surface),
            Water = file.Water == null ? null! : Water(file.Water, path),
            Strata = file.Strata.Select(s => new StratumDef
            {
                Id = s.Id, Name = s.Name, ThicknessM = s.ThicknessM, Hardness = s.Hardness,
                Color = uint.Parse(s.Color.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            }).ToList(),
        };
        var errors = MaterialLibrary.Validate(lib);
        if (errors.Count > 0) throw new InvalidDataException(path + ": " + string.Join("; ", errors));
        return lib;
    }

    static WaterDef Water(WaterEntry e, string path) => new WaterDef
    {
        DeepColor = Hex(e.DeepColor, path, "water.deepColor"), ShallowColor = Hex(e.ShallowColor, path, "water.shallowColor"),
        ClarityM = e.ClarityM, ShoreM = e.ShoreM, FoamWidthM = e.FoamWidthM,
        WaveScaleM = e.WaveScaleM, WaveSpeed = e.WaveSpeed, WaveStrength = e.WaveStrength,
        SwellScaleM = e.SwellScaleM, SwellStrength = e.SwellStrength, LakeSwell = e.LakeSwell,
        FlowSpeed = e.FlowSpeed, RapidsSlope = e.RapidsSlope, BedFieldM = e.BedFieldM,
    };

    static uint Hex(string s, string path, string field) =>
        s.Length == 7 && s[0] == '#' && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? v : throw new InvalidDataException($"{path}: {field} '{s}' is not #rrggbb");

    static SurfaceDetail Surface(SurfaceEntry e)
    {
        var d = new SurfaceDetail
        {
            FadePixels = e.FadePixels, ParallaxFootprintM = e.ParallaxFootprintM, WindDeg = e.WindDeg,
            StrataWobbleM = e.StrataWobbleM, StrataWobbleWavelengthM = e.StrataWobbleWavelengthM,
            CoatPatchAmp = e.CoatPatchAmp, CoatPatchWavelengthM = e.CoatPatchWavelengthM,
            Rock = e.Rock == null ? null! : new RockSurface
            {
                GrainAmp = e.Rock.GrainAmp, GrainWavelengthM = e.Rock.GrainWavelengthM,
                BumpAmp = e.Rock.BumpAmp, BumpWavelengthM = e.Rock.BumpWavelengthM,
                BedSpacingM = e.Rock.BedSpacingM, BedDepthM = e.Rock.BedDepthM,
                JointSpacingM = e.Rock.JointSpacingM, JointDepthM = e.Rock.JointDepthM,
                Parallax = e.Rock.Parallax,
            },
        };
        var coats = new[] { e.Coats?.Grass, e.Coats?.Soil, e.Coats?.Sand, e.Coats?.Snow };
        for (int k = 0; k < coats.Length; k++)
        {
            var c = coats[k];
            d.Coats[k] = c == null ? null! : new CoatDef
            {
                Id = SurfaceDetail.CoatKinds[k], Biomes = c.Biomes,
                BumpAmp = c.BumpAmp, BumpWavelengthM = c.BumpWavelengthM,
                GrainAmp = c.GrainAmp, GrainWavelengthM = c.GrainWavelengthM,
                FeatureAmp = c.FeatureAmp, FeatureWavelengthM = c.FeatureWavelengthM,
                Parallax = c.Parallax, AlbedoVar = c.AlbedoVar,
            };
        }
        return d;
    }

    public const int ObjectSchemaVersion = 1;

    sealed class ObjectFile
    {
        public int SchemaVersion { get; set; }
        public List<KindEntry> Kinds { get; set; } = new();
        public List<ObjectEntry> Objects { get; set; } = new();
        public FoliageEntry? Foliage { get; set; }
    }

    sealed class FoliageEntry
    {
        public WindEntry Wind { get; set; } = new();
        public GrassEntry Grass { get; set; } = new();
    }

    sealed class WindEntry
    {
        static readonly WindDef D = new();
        public float DirectionDeg { get; set; } = D.DirectionDeg;
        public float Strength { get; set; } = D.Strength;
        public float GustStrength { get; set; } = D.GustStrength;
        public float GustScaleM { get; set; } = D.GustScaleM;
        public float GustSpeed { get; set; } = D.GustSpeed;
        public float SwayHz { get; set; } = D.SwayHz;
    }

    sealed class GrassEntry
    {
        static readonly GrassDef D = new();
        public float PerM2 { get; set; } = D.PerM2;
        public float NearM { get; set; } = D.NearM;
        public float ReachM { get; set; } = D.ReachM;
        public float HeightMinM { get; set; } = D.HeightMinM;
        public float HeightMaxM { get; set; } = D.HeightMaxM;
        public float WidthM { get; set; } = D.WidthM;
        public float Bend { get; set; } = D.Bend;
        public float TipLight { get; set; } = D.TipLight;
        public float Translucency { get; set; } = D.Translucency;
        public float CoatMin { get; set; } = D.CoatMin;
        public float SlopeMax { get; set; } = D.SlopeMax;
        public int MaxBlades { get; set; } = D.MaxBlades;
    }

    sealed class KindEntry
    {
        public string Id { get; set; } = "";
        public int Lods { get; set; }
        public float[] LodScreen { get; set; } = Array.Empty<float>();
        public float FadeScreen { get; set; }
        public bool Shadows { get; set; }
        public float ShadowScreen { get; set; }
        public bool Wind { get; set; }
        public bool Animated { get; set; }
        public bool Solid { get; set; }
    }

    sealed class ObjectEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Color { get; set; } = "#ff00ff";
        public int Variants { get; set; } = 1;
        public List<PartEntry> Parts { get; set; } = new();
        public PlacementEntry Placement { get; set; } = new();
    }

    sealed class PartEntry
    {
        public string Id { get; set; } = "";
        public string Shape { get; set; } = "";
        public float[]? Offset { get; set; }
        public float[]? Scale { get; set; }
        public string Solid { get; set; } = "";
        public string Shadows { get; set; } = "";
        public string? Color { get; set; }
    }

    sealed class PlacementEntry
    {
        public List<string> Biomes { get; set; } = new();
        public float PerHectare { get; set; }
        public float SlopeMin { get; set; }
        public float SlopeMax { get; set; } = 10f;
        public float HardnessMin { get; set; }
        public float HardnessMax { get; set; } = 1f;
        public float SizeMinM { get; set; } = 1f;
        public float SizeMaxM { get; set; } = 1f;
        public int ClusterCount { get; set; } = 1;
        public float ClusterRadiusM { get; set; }
        public float Sink { get; set; }
        public float AlignToSlope { get; set; }
        public float FootRiseM { get; set; }
        public float FootReachM { get; set; } = 12f;
        public float EditMinSizeM { get; set; }
        public List<BiomeDensityEntry> BiomeDensity { get; set; } = new();
    }

    sealed class BiomeDensityEntry
    {
        public string Biome { get; set; } = "";
        public float Scale { get; set; } = 1f;
    }

    static uint Hex(string s) => uint.Parse(s.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public static ObjectLibrary LoadObjects(string contentRoot, IReadOnlyList<string>? biomeIds = null)
    {
        var path = Path.Combine(contentRoot, "objects.json");
        var file = JsonSerializer.Deserialize<ObjectFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidDataException(path + " is empty");
        if (file.SchemaVersion != ObjectSchemaVersion)
            throw new InvalidDataException($"{path}: schemaVersion {file.SchemaVersion}, expected {ObjectSchemaVersion}");
        var errors = new List<string>();
        var lib = new ObjectLibrary();
        foreach (var k in file.Kinds)
            lib.Kinds.Add(new ObjectKind
            {
                Id = k.Id, Lods = k.Lods, LodScreen = k.LodScreen, FadeScreen = k.FadeScreen,
                Shadows = k.Shadows, ShadowScreen = k.ShadowScreen, Wind = k.Wind, Animated = k.Animated, Solid = k.Solid,
            });
        foreach (var o in file.Objects)
        {
            var def = new ObjectDef { Id = o.Id, Name = o.Name, Kind = o.Kind, Color = Hex(o.Color), Variants = o.Variants };
            foreach (var p in o.Parts)
            {
                if ((p.Offset != null && p.Offset.Length != 3) || (p.Scale != null && p.Scale.Length != 3))
                    errors.Add(o.Id + ": part " + p.Id + " offset/scale need 3 numbers");
                var part = new ObjectPart
                {
                    Id = p.Id, Shape = ObjectLibrary.ShapeOf(p.Shape),
                    Solid = ObjectLibrary.Tri(p.Solid), Shadows = ObjectLibrary.Tri(p.Shadows),
                    HasColor = p.Color != null, Color = p.Color != null ? Hex(p.Color) : def.Color,
                };
                if (part.Solid < -1 || part.Shadows < -1) errors.Add(o.Id + ": part " + p.Id + " solid/shadows must be \"\", \"on\" or \"off\"");
                if (p.Offset is { Length: 3 }) { part.OffsetX = p.Offset[0]; part.OffsetY = p.Offset[1]; part.OffsetZ = p.Offset[2]; }
                if (p.Scale is { Length: 3 }) { part.ScaleX = p.Scale[0]; part.ScaleY = p.Scale[1]; part.ScaleZ = p.Scale[2]; }
                def.Parts.Add(part);
            }
            var pl = o.Placement;
            def.Placement = new ObjectPlacement
            {
                Biomes = pl.Biomes, PerHectare = pl.PerHectare, SlopeMin = pl.SlopeMin, SlopeMax = pl.SlopeMax,
                HardnessMin = pl.HardnessMin, HardnessMax = pl.HardnessMax, SizeMinM = pl.SizeMinM, SizeMaxM = pl.SizeMaxM,
                ClusterCount = pl.ClusterCount, ClusterRadiusM = pl.ClusterRadiusM, Sink = pl.Sink, AlignToSlope = pl.AlignToSlope,
                FootRiseM = pl.FootRiseM, FootReachM = pl.FootReachM, EditMinSizeM = pl.EditMinSizeM,
                BiomeDensity = pl.BiomeDensity.Select(b => new BiomeDensity { Biome = b.Biome, Scale = b.Scale }).ToList(),
            };
            lib.Objects.Add(def);
        }
        if (file.Foliage != null)
        {
            var g = file.Foliage.Grass;
            var w = file.Foliage.Wind;
            lib.Foliage = new FoliageDef
            {
                Wind = new WindDef
                {
                    DirectionDeg = w.DirectionDeg, Strength = w.Strength, GustStrength = w.GustStrength,
                    GustScaleM = w.GustScaleM, GustSpeed = w.GustSpeed, SwayHz = w.SwayHz,
                },
                Grass = new GrassDef
                {
                    PerM2 = g.PerM2, NearM = g.NearM, ReachM = g.ReachM, HeightMinM = g.HeightMinM, HeightMaxM = g.HeightMaxM,
                    WidthM = g.WidthM, Bend = g.Bend, TipLight = g.TipLight, Translucency = g.Translucency,
                    CoatMin = g.CoatMin, SlopeMax = g.SlopeMax, MaxBlades = g.MaxBlades,
                },
            };
        }
        errors.AddRange(ObjectLibrary.Validate(lib, biomeIds));
        if (errors.Count > 0) throw new InvalidDataException(path + ": " + string.Join("; ", errors));
        return lib;
    }

    public static WorldSketch LoadSketch(string contentRoot, string relativePath, WorldConfig c) =>
        SketchFile.Read(File.ReadAllText(Path.Combine(contentRoot, relativePath)), c);
}
