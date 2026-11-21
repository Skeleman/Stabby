using System.Globalization;
using System.Text.Json;
using Stabby.World;

namespace Stabby.Content;

public static class ContentLoader
{
    public const int BiomeSchemaVersion = 1;
    public const int MaterialSchemaVersion = 1;

    sealed class MaterialFile
    {
        public int SchemaVersion { get; set; }
        public float OffsetAmplitudeM { get; set; }
        public float OffsetWavelengthM { get; set; }
        public float TiltPerKm { get; set; }
        public SurfaceEntry? Surface { get; set; }
        public List<StratumEntry> Strata { get; set; } = new();
    }

    sealed class SurfaceEntry
    {
        public float StrataWobbleM { get; set; }
        public float StrataWobbleWavelengthM { get; set; }
        public float CoatPatchAmp { get; set; }
        public float CoatPatchWavelengthM { get; set; }
        public float CoatGrainAmp { get; set; }
        public float CoatGrainWavelengthM { get; set; }
        public float RockGrainAmp { get; set; }
        public float RockGrainWavelengthM { get; set; }
    }

    sealed class StratumEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#ff00ff";
        public float ThicknessM { get; set; }
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
            Surface = file.Surface == null ? null! : new SurfaceDetail
            {
                StrataWobbleM = file.Surface.StrataWobbleM, StrataWobbleWavelengthM = file.Surface.StrataWobbleWavelengthM,
                CoatPatchAmp = file.Surface.CoatPatchAmp, CoatPatchWavelengthM = file.Surface.CoatPatchWavelengthM,
                CoatGrainAmp = file.Surface.CoatGrainAmp, CoatGrainWavelengthM = file.Surface.CoatGrainWavelengthM,
                RockGrainAmp = file.Surface.RockGrainAmp, RockGrainWavelengthM = file.Surface.RockGrainWavelengthM,
            },
            Strata = file.Strata.Select(s => new StratumDef
            {
                Id = s.Id, Name = s.Name, ThicknessM = s.ThicknessM,
                Color = uint.Parse(s.Color.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            }).ToList(),
        };
        var errors = MaterialLibrary.Validate(lib);
        if (errors.Count > 0) throw new InvalidDataException(path + ": " + string.Join("; ", errors));
        return lib;
    }

    public static WorldSketch LoadSketch(string contentRoot, string relativePath, WorldConfig c) =>
        SketchFile.Read(File.ReadAllText(Path.Combine(contentRoot, relativePath)), c);
}
