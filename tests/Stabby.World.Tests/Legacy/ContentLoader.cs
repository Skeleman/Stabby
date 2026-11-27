using System.Globalization;
using System.Text.Json;
using Stabby.World;

namespace Stabby.World.Tests.Legacy;

public static class ContentLoader
{
    public const int BiomeSchemaVersion = 1;
    public const int MaterialSchemaVersion = 5;

    sealed class MaterialFile
    {
        public int SchemaVersion { get; set; }
        public float OffsetAmplitudeM { get; set; }
        public float OffsetWavelengthM { get; set; }
        public float TiltPerKm { get; set; }
        public SurfaceEntry? Surface { get; set; }
        public List<LiquidEntry> Liquids { get; set; } = new();
        public SinterEntry? Sinter { get; set; }
        public MudEntry? Mud { get; set; }
        public PeatEntry? Peat { get; set; }
        public LookEntry? Look { get; set; }
        public List<StratumEntry> Strata { get; set; } = new();
        public List<MaterialEntry> Materials { get; set; } = new();
        public Dictionary<string, List<string>> Kit { get; set; } = new();
    }

    sealed class MaterialEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        public float Glow { get; set; }
        public float Translucency { get; set; }
        public string GlowPattern { get; set; } = "facet";
    }

    sealed class LookEntry
    {
        public float NoiseContrast { get; set; }
        public float AlbedoVar { get; set; }
        public float DetailWavelengthM { get; set; }
        public float DetailLacunarity { get; set; }
        public int DetailOctaves { get; set; }
        public float DetailGain { get; set; }
        public float Saturation { get; set; }
        public float Roughness { get; set; }
        public float Specular { get; set; }
        public float RoughnessVar { get; set; }
    }

    sealed class MudEntry
    {
        public string Surface { get; set; } = "";
        public string Clay { get; set; } = "";
        public string Grey { get; set; } = "";
        public float Roughness { get; set; }
    }

    sealed class PeatEntry
    {
        public string Wet { get; set; } = "";
        public string Dark { get; set; } = "";
        public string Fibre { get; set; } = "";
        public float Roughness { get; set; }
    }

    sealed class SinterEntry
    {
        public string White { get; set; } = "";
        public string Ochre { get; set; } = "";
        public string Orange { get; set; } = "";
        public float Roughness { get; set; }
    }

    sealed class FallEntry
    {
        public string Spray { get; set; } = "";
        public float Speed { get; set; }
        public float SheetOpacity { get; set; }
        public string SprayColor { get; set; } = "#ffffff";
        public float SprayAmount { get; set; }
        public float Froth { get; set; }
    }

    sealed class LiquidEntry
    {
        public string Id { get; set; } = "";
        public string DeepColor { get; set; } = "";
        public string ShallowColor { get; set; } = "";
        public string FilmColor { get; set; } = "";
        public string EmissionColor { get; set; } = "";
        public float Silt { get; set; }
        public float Opacity { get; set; }
        public float Viscosity { get; set; }
        public float Bubbles { get; set; }
        public float Film { get; set; }
        public float Emission { get; set; }
        public float Roughness { get; set; }
        public FallEntry? Fall { get; set; }
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
        public List<string>? Biomes { get; set; }
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
        public float? GrainAmp { get; set; }
        public float FeatureAmp { get; set; }
        public float FeatureWavelengthM { get; set; }
        public float Parallax { get; set; }
        public float? AlbedoVar { get; set; }
        public float? Roughness { get; set; }
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
        public float? WaterRiseM { get; set; }
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
            WaterWithinM = b.WaterWithinM ?? float.NaN, WaterRiseM = b.WaterRiseM ?? float.NaN, CoastWithinM = b.CoastWithinM ?? float.NaN,
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
            Liquids = file.Liquids.Select(l => Liquid(l, path)).ToList(),
            Sinter = file.Sinter == null ? null! : new SinterDef
            {
                White = Hex(file.Sinter.White, path, "sinter.white"), Ochre = Hex(file.Sinter.Ochre, path, "sinter.ochre"), Orange = Hex(file.Sinter.Orange, path, "sinter.orange"),
                Roughness = file.Sinter.Roughness,
            },
            Mud = file.Mud == null ? null! : new MudDef
            {
                Surface = Hex(file.Mud.Surface, path, "mud.surface"),
                Clay = Hex(file.Mud.Clay, path, "mud.clay"), Grey = Hex(file.Mud.Grey, path, "mud.grey"), Roughness = file.Mud.Roughness,
            },
            Peat = file.Peat == null ? null! : new PeatDef
            {
                Wet = Hex(file.Peat.Wet, path, "peat.wet"), Dark = Hex(file.Peat.Dark, path, "peat.dark"),
                Fibre = Hex(file.Peat.Fibre, path, "peat.fibre"), Roughness = file.Peat.Roughness,
            },
            Look = file.Look == null ? null! : new LookDef
            {
                NoiseContrast = file.Look.NoiseContrast, AlbedoVar = file.Look.AlbedoVar, DetailWavelengthM = file.Look.DetailWavelengthM, DetailLacunarity = file.Look.DetailLacunarity, DetailOctaves = file.Look.DetailOctaves, DetailGain = file.Look.DetailGain,
                Saturation = file.Look.Saturation, Roughness = file.Look.Roughness, Specular = file.Look.Specular, RoughnessVar = file.Look.RoughnessVar,
            },
            Strata = file.Strata.Select(s => new StratumDef
            {
                Id = s.Id, Name = s.Name, ThicknessM = s.ThicknessM, Hardness = s.Hardness,
                Color = uint.Parse(s.Color.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            }).ToList(),
            Materials = file.Materials.Select(m => new LibraryMaterial { Id = m.Id, Name = m.Name, Color = Hex(m.Color, path, "materials.color"), Glow = m.Glow, Translucency = m.Translucency, GlowPattern = Array.IndexOf(LibraryMaterial.GlowPatterns, m.GlowPattern) }).ToList(),
        };
        foreach (var name in MaterialLibrary.KitNames)
            if (file.Kit.TryGetValue(name, out var ids)) lib.Kit[name] = ids;
        foreach (var name in file.Kit.Keys)
            if (!lib.Kit.ContainsKey(name)) lib.Kit[name] = file.Kit[name];
        lib.FillCoats();
        var errors = MaterialLibrary.Validate(lib);
        if (errors.Count > 0) throw new InvalidDataException(path + ": " + string.Join("; ", errors));
        return lib;
    }

    static LiquidDef Liquid(LiquidEntry e, string path) => new LiquidDef
    {
        Id = e.Id,
        DeepColor = Hex(e.DeepColor, path, e.Id + ".deepColor"), ShallowColor = Hex(e.ShallowColor, path, e.Id + ".shallowColor"),
        FilmColor = Hex(e.FilmColor, path, e.Id + ".filmColor"), EmissionColor = Hex(e.EmissionColor, path, e.Id + ".emissionColor"),
        Silt = e.Silt, Opacity = e.Opacity, Viscosity = e.Viscosity, Bubbles = e.Bubbles, Film = e.Film, Emission = e.Emission, Roughness = e.Roughness,
        Spray = e.Fall == null ? -1 : Array.IndexOf(LiquidDef.SprayNames, e.Fall.Spray),
        FallSpeed = e.Fall?.Speed ?? 0, SheetOpacity = e.Fall?.SheetOpacity ?? 0,
        SprayColor = e.Fall == null ? 0 : Hex(e.Fall.SprayColor, path, e.Id + ".fall.sprayColor"), SprayAmount = e.Fall?.SprayAmount ?? 0, Froth = e.Fall?.Froth ?? 0,
        ClarityM = e.ClarityM, ShoreM = e.ShoreM, FoamWidthM = e.FoamWidthM,
        WaveScaleM = e.WaveScaleM, WaveSpeed = e.WaveSpeed, WaveStrength = e.WaveStrength,
        SwellScaleM = e.SwellScaleM, SwellStrength = e.SwellStrength, LakeSwell = e.LakeSwell,
        FlowSpeed = e.FlowSpeed, RapidsSlope = e.RapidsSlope, BedFieldM = e.BedFieldM,
        Biomes = e.Biomes ?? new List<string>(),
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
                GrainAmp = e.Rock.GrainAmp,
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
                GrainAmp = c.GrainAmp ?? float.NaN,
                FeatureAmp = c.FeatureAmp, FeatureWavelengthM = c.FeatureWavelengthM,
                Parallax = c.Parallax, AlbedoVar = c.AlbedoVar ?? float.NaN, Roughness = c.Roughness ?? float.NaN,
            };
        }
        return d;
    }

    public const int ObjectSchemaVersion = 6;

    sealed class ObjectFile
    {
        public int SchemaVersion { get; set; }
        public List<LightEntry> Lights { get; set; } = new();
        public BudgetEntry LightBudget { get; set; } = new();
        public List<KindEntry> Kinds { get; set; } = new();
        public List<StyleEntry> Styles { get; set; } = new();
        public List<ObjectEntry> Objects { get; set; } = new();
        public TerrainEntry? Terrain { get; set; }
        public FoliageEntry? Foliage { get; set; }
        public QualityEntry? Quality { get; set; }
        public List<TownEntry> Towns { get; set; } = new();
    }

    sealed class TownEntry
    {
        public string Id { get; set; } = "";
        public List<string> Biomes { get; set; } = new();
        public float SeekM { get; set; }
        public float ReliefM { get; set; }
        public float WaterM { get; set; }
        public float PlazaM { get; set; }
        public float LaneM { get; set; }
        public float LaneWidthM { get; set; }
        public List<TownSiteEntry> Sites { get; set; } = new();
    }

    sealed class TownSiteEntry
    {
        public string Object { get; set; } = "";
        public string Place { get; set; } = "";
        public float[]? Count { get; set; }
    }

    sealed class QualityEntry
    {
        public string Default { get; set; } = "";
        public List<QualityLevelEntry> Levels { get; set; } = new();
    }

    sealed class QualityLevelEntry
    {
        public string Id { get; set; } = "";
        public float Scale { get; set; }
    }

    sealed class TerrainEntry
    {
        public float QuadPixels { get; set; } = new TerrainViewDef().QuadPixels;
        public float ContactPixels { get; set; } = new TerrainViewDef().ContactPixels;
        public float ContactTexelPixels { get; set; } = new TerrainViewDef().ContactTexelPixels;
        public float MistFadePixels { get; set; } = new TerrainViewDef().MistFadePixels;
        public float MistFullPixels { get; set; } = new TerrainViewDef().MistFullPixels;
        public float SteamFadePixels { get; set; } = new TerrainViewDef().SteamFadePixels;
        public float SteamFullPixels { get; set; } = new TerrainViewDef().SteamFullPixels;
        public float ColumnFadePixels { get; set; } = new TerrainViewDef().ColumnFadePixels;
        public float ColumnFullPixels { get; set; } = new TerrainViewDef().ColumnFullPixels;
        public float ShadowTexels { get; set; } = new TerrainViewDef().ShadowTexels;
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
        public float NearPixels { get; set; } = D.NearPixels;
        public float ReachPixels { get; set; } = D.ReachPixels;
        public float ThinPow { get; set; } = D.ThinPow;
        public float Widen { get; set; } = D.Widen;
        public float WidthMaxM { get; set; } = D.WidthMaxM;
        public float Lod1Pixels { get; set; } = D.Lod1Pixels;
        public float Lod2Pixels { get; set; } = D.Lod2Pixels;
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

    sealed class LightEntry
    {
        public string Id { get; set; } = "";
        public float ShadowedPixels { get; set; }
        public float UnshadowedPixels { get; set; }
        public float GlowPixels { get; set; }
        public float Importance { get; set; }
    }

    sealed class BudgetEntry
    {
        public int Shadowed { get; set; }
        public int Unshadowed { get; set; }
        public int Glow { get; set; }
    }

    sealed class KindEntry
    {
        public string Id { get; set; } = "";
        public int Lods { get; set; }
        public float[] LodPixels { get; set; } = Array.Empty<float>();
        public float FadePixels { get; set; }
        public bool Shadows { get; set; }
        public float ShadowPixels { get; set; }
        public float ShadowCascadePixels { get; set; }
        public string Light { get; set; } = "";
        public bool Wind { get; set; }
        public float SwayPixels { get; set; } = 16.2f;
        public float FlutterPixels { get; set; } = 54f;
        public float ImpostorPixels { get; set; }
        public bool Animated { get; set; }
        public bool Solid { get; set; }
    }

    sealed class StyleEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Stone { get; set; } = "";
        public string Trim { get; set; } = "";
    }

    sealed class ObjectEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Material { get; set; } = "";
        public string? Style { get; set; }
        public int Variants { get; set; } = 1;
        public InteractEntry? Interact { get; set; }
        public ObjectLightEntry? Light { get; set; }
        public List<PartEntry> Parts { get; set; } = new();
        public PlacementEntry Placement { get; set; } = new();
    }

    sealed class ObjectLightEntry
    {
        public string Category { get; set; } = "";
        public string Material { get; set; } = "";
        public float Intensity { get; set; }
        public float RadiusM { get; set; }
        public float Importance { get; set; }
        public float Flicker { get; set; }
        public float FlickerHz { get; set; }
        public float LiftM { get; set; }
        public float ConeDeg { get; set; }
        public bool Daylight { get; set; }
        public bool Core { get; set; } = true;
    }

    sealed class InteractEntry
    {
        public string Verb { get; set; } = "";
        public float ReachM { get; set; }
    }

    sealed class PartEntry
    {
        public string Id { get; set; } = "";
        public string Shape { get; set; } = "";
        public float[]? Offset { get; set; }
        public float[]? Scale { get; set; }
        public string Solid { get; set; } = "";
        public string Shadows { get; set; } = "";
        public string? Material { get; set; }
        public string? LeafMaterial { get; set; }
        public string Ends { get; set; } = "auto";
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
        public float FanM { get; set; }
        public float EditMinSizeM { get; set; }
        public int ChainMin { get; set; } = 1;
        public int ChainMax { get; set; } = 1;
        public float ChainCorner { get; set; }
        public float ChainGate { get; set; }
        public string? ChainHead { get; set; }
        public string? Bank { get; set; }
        public float PerHundredM { get; set; }
        public string? Discovery { get; set; }
        public string? Cave { get; set; }
        public bool CaveIce { get; set; }
        public string? Liquid { get; set; }
        public string? Oasis { get; set; }
        public float SpacingM { get; set; }
        public float Chance { get; set; } = 1f;
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
        var materials = LoadMaterials(contentRoot);
        uint Mat(string id) => materials.Find(id)?.Color ?? 0;
        float Glow(string id) => materials.Find(id ?? "")?.Glow ?? 0f;
        float Translucency(string id) => materials.Find(id ?? "")?.Translucency ?? 0f;
        int Pattern(string id) => materials.Find(id ?? "")?.GlowPattern ?? 0;
        foreach (var l in file.Lights)
            lib.Lights.Add(new LightKind { Id = l.Id, ShadowedPixels = l.ShadowedPixels, UnshadowedPixels = l.UnshadowedPixels, GlowPixels = l.GlowPixels, Importance = l.Importance });
        lib.LightCaps = new LightBudget.Caps(file.LightBudget.Shadowed, file.LightBudget.Unshadowed, file.LightBudget.Glow);
        foreach (var k in file.Kinds)
            lib.Kinds.Add(new ObjectKind
            {
                Id = k.Id, Lods = k.Lods, LodPixels = k.LodPixels, FadePixels = k.FadePixels,
                Shadows = k.Shadows, ShadowPixels = k.ShadowPixels, ShadowCascadePixels = k.ShadowCascadePixels, Light = k.Light, Wind = k.Wind, SwayPixels = k.SwayPixels, FlutterPixels = k.FlutterPixels, ImpostorPixels = k.ImpostorPixels, Animated = k.Animated, Solid = k.Solid,
            });
        foreach (var st in file.Styles)
            lib.Styles.Add(new ArchStyle { Id = st.Id, Name = st.Name, Stone = st.Stone, Trim = st.Trim, StoneColor = Mat(st.Stone) });
        foreach (var o in file.Objects)
        {
            var style = o.Style == null ? null : lib.Styles.Find(st => st.Id == o.Style);
            var def = new ObjectDef { Id = o.Id, Name = o.Name, Kind = o.Kind, Color = style != null ? style.StoneColor : Mat(o.Material), Glow = o.Style == null ? Glow(o.Material) : 0, Translucency = o.Style == null ? Translucency(o.Material) : 0, GlowPattern = o.Style == null ? Pattern(o.Material) : 0, Variants = o.Variants, Verb = o.Interact?.Verb, ReachM = o.Interact?.ReachM ?? 0, Style = o.Style };
            int trim = style == null ? 0 : Array.IndexOf(MaterialLibrary.KitNames, style.Trim) + 1;
            if (o.Light != null)
                def.Light = new ObjectLight { Category = o.Light.Category, Color = Mat(o.Light.Material), Intensity = o.Light.Intensity, RangeM = o.Light.RadiusM, Importance = o.Light.Importance, Flicker = o.Light.Flicker, FlickerHz = o.Light.FlickerHz, LiftM = o.Light.LiftM, ConeDeg = o.Light.ConeDeg, Daylight = o.Light.Daylight, Core = o.Light.Core };
            foreach (var p in o.Parts)
            {
                if ((p.Offset != null && p.Offset.Length != 3) || (p.Scale != null && p.Scale.Length != 3))
                    errors.Add(o.Id + ": part " + p.Id + " offset/scale need 3 numbers");
                var part = new ObjectPart
                {
                    Id = p.Id, Shape = ObjectLibrary.ShapeOf(p.Shape),
                    Solid = ObjectLibrary.Tri(p.Solid), Shadows = ObjectLibrary.Tri(p.Shadows),
                    HasColor = p.Material != null, Color = p.Material != null ? Mat(p.Material) : def.Color, Glow = p.Material != null ? Glow(p.Material) : def.Glow, Translucency = p.Material != null ? Translucency(p.Material) : def.Translucency, GlowPattern = p.Material != null ? Pattern(p.Material) : def.GlowPattern,
                    HasLeafColor = p.LeafMaterial != null, LeafColor = p.LeafMaterial != null ? Mat(p.LeafMaterial) : 0, Trim = trim,
                };
                part.Ends = Array.IndexOf(ObjectPart.EndNames, p.Ends);
                if (part.Ends < 0) errors.Add(o.Id + ": part " + p.Id + " has unknown ends \"" + p.Ends + "\"");
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
                FootRiseM = pl.FootRiseM, FootReachM = pl.FootReachM, FanM = pl.FanM, EditMinSizeM = pl.EditMinSizeM,
                ChainMin = pl.ChainMin, ChainMax = pl.ChainMax, ChainCorner = pl.ChainCorner, ChainGate = pl.ChainGate, ChainHead = pl.ChainHead, Bank = pl.Bank, PerHundredM = pl.PerHundredM,
                Discovery = Array.IndexOf(ObjectField.DiscoveryNames, pl.Discovery ?? ""), Cave = Array.IndexOf(ObjectField.CaveNames, pl.Cave ?? ""), CaveIce = pl.CaveIce, Liquid = pl.Liquid, Oasis = pl.Oasis, SpacingM = pl.SpacingM, Chance = pl.Chance,
                BiomeDensity = pl.BiomeDensity.Select(b => new BiomeDensity { Biome = b.Biome, Scale = b.Scale }).ToList(),
            };
            lib.Objects.Add(def);
        }
        foreach (var t in file.Towns)
        {
            var town = new TownDef { Id = t.Id, Biomes = t.Biomes, SeekM = t.SeekM, ReliefM = t.ReliefM, WaterM = t.WaterM, PlazaM = t.PlazaM, LaneM = t.LaneM, LaneWidthM = t.LaneWidthM };
            foreach (var st in t.Sites)
            {
                var count = st.Count is { Length: 2 } ? st.Count : new float[] { 0, 0 };
                if (st.Count is not { Length: 2 }) errors.Add("town " + town.Id + ": count needs 2 numbers");
                town.Sites.Add(new TownSite { ObjectId = st.Object, Place = Array.IndexOf(TownSite.PlaceNames, st.Place), Min = (int)count[0], Max = (int)count[1] });
            }
            lib.Towns.Add(town);
        }
        if (file.Terrain != null) lib.Terrain = new TerrainViewDef { QuadPixels = file.Terrain.QuadPixels, ContactPixels = file.Terrain.ContactPixels, ContactTexelPixels = file.Terrain.ContactTexelPixels, MistFadePixels = file.Terrain.MistFadePixels, MistFullPixels = file.Terrain.MistFullPixels, SteamFadePixels = file.Terrain.SteamFadePixels, SteamFullPixels = file.Terrain.SteamFullPixels, ColumnFadePixels = file.Terrain.ColumnFadePixels, ColumnFullPixels = file.Terrain.ColumnFullPixels, ShadowTexels = file.Terrain.ShadowTexels };
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
                    PerM2 = g.PerM2, NearPixels = g.NearPixels, ReachPixels = g.ReachPixels, ThinPow = g.ThinPow, Widen = g.Widen, WidthMaxM = g.WidthMaxM,
                    Lod1Pixels = g.Lod1Pixels, Lod2Pixels = g.Lod2Pixels, HeightMinM = g.HeightMinM, HeightMaxM = g.HeightMaxM,
                    WidthM = g.WidthM, Bend = g.Bend, TipLight = g.TipLight, Translucency = g.Translucency,
                    CoatMin = g.CoatMin, SlopeMax = g.SlopeMax, MaxBlades = g.MaxBlades,
                },
            };
        }
        if (file.Quality != null) lib.Quality = new QualityDef { Default = file.Quality.Default, Levels = file.Quality.Levels.Select(l => new QualityLevel { Id = l.Id, Scale = l.Scale }).ToList() };
        errors.AddRange(ObjectLibrary.Validate(lib, biomeIds));
        if (errors.Count > 0) throw new InvalidDataException(path + ": " + string.Join("; ", errors));
        return lib;
    }

    public static WorldSketch LoadSketch(string contentRoot, string relativePath, WorldConfig c) =>
        SketchFile.Read(File.ReadAllText(Path.Combine(contentRoot, relativePath)), c);
}
