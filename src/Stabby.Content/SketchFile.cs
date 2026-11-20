using System.Text.Json;
using Stabby.World;

namespace Stabby.Content;

// The hand-editable sketch on disk. Only the authored lists are stored; the
// derived masks are rebuilt by WorldSketch.Build on load.
public static class SketchFile
{
    public const int SchemaVersion = 2;

    sealed class Dto
    {
        public int SchemaVersion { get; set; }
        public ulong Seed { get; set; }
        public int Size { get; set; }
        public float CellMeters { get; set; }
        public List<LandmassDto> Landmasses { get; set; } = new();
        public List<LandmassDto> Islets { get; set; } = new();
        public List<LineDto> Ranges { get; set; } = new();
        public List<LineDto> Corridors { get; set; } = new();
        public List<RegionDto> Regions { get; set; } = new();
        public List<LandmarkDto> Landmarks { get; set; } = new();
    }

    sealed class LandmassDto { public float X { get; set; } public float Y { get; set; } public float Radius { get; set; } public List<LobeDto>? Lobes { get; set; } }
    sealed class LobeDto { public float X { get; set; } public float Y { get; set; } public float Radius { get; set; } }
    sealed class LineDto { public float[] X { get; set; } = []; public float[] Y { get; set; } = []; public float Width { get; set; } public float? Strength { get; set; } }
    sealed class RegionDto { public string Biome { get; set; } = ""; public float X { get; set; } public float Y { get; set; } public float Radius { get; set; } }
    sealed class LandmarkDto { public string Kind { get; set; } = ""; public float X { get; set; } public float Y { get; set; } }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static string Write(WorldSketch s, WorldConfig c) => JsonSerializer.Serialize(new Dto
    {
        SchemaVersion = SchemaVersion, Seed = c.Seed, Size = s.Size, CellMeters = c.CellMeters,
        Landmasses = s.Landmasses.Select(ToDto).ToList(),
        Islets = s.Islets.Select(ToDto).ToList(),
        Ranges = s.Ranges.Select(r => new LineDto { X = r.X, Y = r.Y, Width = r.Width, Strength = r.Strength }).ToList(),
        Corridors = s.Corridors.Select(r => new LineDto { X = r.X, Y = r.Y, Width = r.Width }).ToList(),
        Regions = s.Regions.Select(r => new RegionDto { Biome = r.Biome, X = r.X, Y = r.Y, Radius = r.Radius }).ToList(),
        Landmarks = s.Landmarks.Select(l => new LandmarkDto { Kind = l.Kind, X = l.X, Y = l.Y }).ToList(),
    }, Options);

    // The file's seed, size and cell size win over the caller's, so an edited sketch
    // always regenerates the world it was drawn on.
    public static WorldSketch Read(string json, WorldConfig c)
    {
        var d = JsonSerializer.Deserialize<Dto>(json, Options) ?? throw new InvalidDataException("empty sketch");
        if (d.Size < 16 || d.CellMeters <= 0) throw new InvalidDataException("sketch needs size and cellMeters");
        if (d.SchemaVersion != SchemaVersion) throw new InvalidDataException($"sketch schemaVersion {d.SchemaVersion}, expected {SchemaVersion}");
        var errors = new List<string>();
        foreach (var l in d.Ranges.Concat(d.Corridors))
            if (l.X.Length < 2 || l.X.Length != l.Y.Length) errors.Add("a line needs matching x and y with at least 2 points");
        if (d.Ranges.Any(r => r.Strength is null)) errors.Add("ranges need strength");
        if (d.Corridors.Any(r => r.Strength is not null)) errors.Add("corridors have no strength");
        if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));

        c.Seed = d.Seed; c.Size = d.Size; c.CellMeters = d.CellMeters;
        var s = new WorldSketch
        {
            Landmasses = d.Landmasses.Select(FromDto).ToList(),
            Islets = d.Islets.Select(FromDto).ToList(),
            Ranges = d.Ranges.Select(r => new RangeSketch { X = r.X, Y = r.Y, Width = r.Width, Strength = r.Strength!.Value }).ToList(),
            Corridors = d.Corridors.Select(r => new CorridorSketch { X = r.X, Y = r.Y, Width = r.Width }).ToList(),
            Regions = d.Regions.Select(r => new RegionSketch { Biome = r.Biome, X = r.X, Y = r.Y, Radius = r.Radius }).ToList(),
            Landmarks = d.Landmarks.Select(l => new LandmarkSketch { Kind = l.Kind, X = l.X, Y = l.Y }).ToList(),
        };
        s.Build(c);
        return s;
    }

    static LandmassDto ToDto(LandmassSketch m) => new()
    {
        X = m.X, Y = m.Y, Radius = m.Radius,
        Lobes = m.Lobes.Count == 0 ? null : m.Lobes.Select(l => new LobeDto { X = l.X, Y = l.Y, Radius = l.Radius }).ToList(),
    };

    static LandmassSketch FromDto(LandmassDto d) => new()
    {
        X = d.X, Y = d.Y, Radius = d.Radius,
        Lobes = (d.Lobes ?? new()).Select(l => new LobeSketch { X = l.X, Y = l.Y, Radius = l.Radius }).ToList(),
    };
}
