using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class ObjectKind
    {
        public string Id;
        public int Lods = 3;
        public float[] LodPixels = { 86.4f, 27f };
        public float FadePixels = 6.48f;
        public bool Shadows = true;
        public float ShadowPixels = 32.4f;
        public bool Wind, Animated;
        public float SwayPixels = 16.2f, FlutterPixels = 54f, ImpostorPixels;
        public float ShadowCascadePixels = 64.8f;
        public string Light = "point";
        public bool Solid = true;

        public Detail.Rule Rule()
        {
            float near = LodPixels != null && LodPixels.Length > 0 ? LodPixels[0] : FadePixels;
            float mid = LodPixels != null && LodPixels.Length > 1 ? LodPixels[1] : FadePixels;
            var never = new Detail.Thresholds(Detail.Never, Detail.Never, Detail.Never);
            return new Detail.Rule(new Detail.Thresholds(near, mid, FadePixels), ShadowCascadePixels, ShadowPixels,
                Wind ? new Detail.Thresholds(FlutterPixels, FlutterPixels, SwayPixels) : never, never);
        }
    }

    public sealed class LightKind
    {
        public string Id;
        public float ShadowedPixels = 432f, UnshadowedPixels = 108f, GlowPixels = 10.8f, Importance = 1f;

        public Detail.Thresholds Thresholds() => new Detail.Thresholds(ShadowedPixels, UnshadowedPixels, GlowPixels);
    }

    public sealed class ObjectPart
    {
        public const int Rock = 0, Log = 1, Stump = 2, Shrub = 3, Broadleaf = 4, Conifer = 5, Wall = 6, Cairn = 7, Reed = 8, Inn = 9, InnYard = 10, Table = 11, Bench = 12, Barrel = 13, Crate = 14, Stall = 15, Well = 16, StoneRing = 17, Tent = 18, FirePit = 19, LogSeat = 20, Campsite = 21, HermitHut = 22, Hermitage = 23, RoadShrine = 24, Wayside = 25, Ruin = 26, HotSpring = 27, Mushroom = 28, FairyRing = 29, Geyser = 30, MudPot = 31, BasaltColumns = 32, Crystals = 33, WolfDen = 34, Dripstone = 35, DripColumn = 36, DungeonCorridor = 37, DungeonRoom = 38, DungeonStairs = 39, DungeonDoor = 40, DungeonPillar = 41, DungeonCollapse = 42, Dungeon = 43, MineTimber = 44, MineRail = 45, MineCart = 46, WallTorch = 47, House = 48, WallHead = 49, Overlook = 50, Bones = 51, SaltTower = 52, Bridge = 53, DatePalm = 54, InnRoadside = 55, FallenTree = 56, Monument = 57, Shipwreck = 58, Watchtower = 59, Shapes = 60;
        public static readonly string[] ShapeNames = { "rock", "log", "stump", "shrub", "broadleaf", "conifer", "dry-stone-wall", "cairn", "reed", "inn", "inn-yard", "table", "bench", "barrel", "crate", "market-stall", "well", "stone-ring", "tent", "fire-pit", "log-seat", "campsite", "hermit-hut", "hermitage", "road-shrine", "wayside", "ruin", "hot-spring", "mushroom", "fairy-ring", "geyser", "mud-pot", "basalt-columns", "crystal-cluster", "wolf-den", "dripstone", "dripstone-column", "dungeon-corridor", "dungeon-room", "dungeon-stairs", "dungeon-door", "dungeon-pillar", "dungeon-collapse", "dungeon", "mine-timber", "mine-rail", "mine-cart", "wall-torch", "house", "wall-head", "overlook", "bones", "salt-tower", "road-bridge", "date-palm", "inn-roadside", "fallen-tree", "monument", "shipwreck", "watchtower" };
        public static bool IsFoliage(int shape) => (shape >= Shrub && shape <= Conifer) || shape == Reed || shape == DatePalm;
        public static bool IsStructure(int shape) => (shape >= Wall && shape <= Cairn) || (shape >= Inn && shape <= MudPot) || shape == WolfDen || shape == WallTorch || shape == House || shape == WallHead || shape == Overlook || shape == Bones || shape == SaltTower || shape == Bridge || shape == InnRoadside || shape == FallenTree || shape == Monument || shape == Shipwreck || shape == Watchtower || IsDungeon(shape) || IsMine(shape);
        public static bool IsDungeon(int shape) => shape >= DungeonCorridor && shape <= Dungeon;
        public static bool IsMine(int shape) => shape >= MineTimber && shape <= MineCart;
        public static bool IsStone(int shape) => shape == Rock || shape == BasaltColumns || shape == Crystals || shape == Dripstone || shape == DripColumn || IsStructure(shape);
        public const int EndsAuto = 0, EndsTorn = 1, EndsSawn = 2, EndsRooted = 3, EndsSnapped = 4, EndsWorn = 5;
        public static readonly string[] EndNames = { "auto", "torn", "sawn", "rooted", "snapped", "worn" };

        public string Id;
        public int Shape;
        public float OffsetX, OffsetY, OffsetZ;
        public float ScaleX = 1, ScaleY = 1, ScaleZ = 1;
        public int Solid = -1, Shadows = -1;
        public uint Color;
        public float Glow, Translucency;
        public int GlowPattern;
        public bool HasColor;
        public uint LeafColor;
        public bool HasLeafColor;
        public int Ends;
        public int Trim;
    }

    public sealed class ArchStyle
    {
        public string Id, Name, Stone, Trim;
        public uint StoneColor;
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
        public float FanM;
        public float EditMinSizeM;
        public int ChainMin = 1, ChainMax = 1;
        public float ChainCorner, ChainGate;
        public string ChainHead;
        public string Bank;
        public string Oasis;
        public int PerCenote;
        public float PerHundredM;
        public int Discovery;
        public int Cave;
        public bool CaveIce;
        public string Liquid;
        public float SpacingM, Chance = 1f;
        public List<BiomeDensity> BiomeDensity = new List<BiomeDensity>();
    }

    public sealed class BiomeDensity
    {
        public string Biome;
        public float Scale = 1f;
    }

    public sealed class ObjectLight
    {
        public string Category;
        public uint Color;
        public float Intensity, RangeM, Importance, Flicker, FlickerHz, LiftM, ConeDeg;
        public bool Daylight, Core = true;
    }

    public sealed class ObjectDef
    {
        public string Id, Name, Kind;
        public uint Color;
        public float Glow, Translucency;
        public int GlowPattern;
        public int Variants = 1;
        public string Verb, Style;
        public float ReachM;
        public List<ObjectPart> Parts = new List<ObjectPart>();
        public ObjectPlacement Placement = new ObjectPlacement();
        public ObjectLight Light;

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

    public sealed class QualityLevel
    {
        public string Id = "";
        public float Scale = 1f;

        public float ScaleAt(float lines) =>
            Scale <= 1f || lines <= Detail.ReferenceLines ? Scale : 1f + (Scale - 1f) * Detail.ReferenceLines / lines;
    }

    public sealed class QualityDef
    {
        public string Default = "standard";
        public List<QualityLevel> Levels = new List<QualityLevel> { new QualityLevel { Id = "standard" } };

        public QualityLevel Level(string id)
        {
            foreach (var l in Levels) if (l.Id == id) return l;
            return null;
        }

        public QualityLevel Pick(string requested) => Level(requested ?? "") ?? Level(Default) ?? Levels[0];
    }

    public sealed class TownSite
    {
        public const int Centre = 0, Plaza = 1, Lane = 2;
        public static readonly string[] PlaceNames = { "centre", "plaza", "lane" };
        public string ObjectId;
        public int Place, Min, Max;
    }

    public sealed class TownDef
    {
        public string Id;
        public List<string> Biomes = new List<string>();
        public float SeekM, ReliefM, WaterM, PlazaM, LaneM, LaneWidthM;
        public List<TownSite> Sites = new List<TownSite>();
    }

    public sealed class RoadDef
    {
        public string BridgeObject = "";
        public float SpanM, RiverM, ClearM;
    }

    public sealed class ObjectLibrary
    {
        public List<LightKind> Lights = new List<LightKind>();
        public LightBudget.Caps LightCaps = new LightBudget.Caps(2, 12, 48);
        public List<ObjectKind> Kinds = new List<ObjectKind>();
        public List<ObjectDef> Objects = new List<ObjectDef>();
        public TerrainViewDef Terrain = new TerrainViewDef();
        public FoliageDef Foliage = new FoliageDef();
        public QualityDef Quality = new QualityDef();
        public List<ArchStyle> Styles = new List<ArchStyle>();
        public List<TownDef> Towns = new List<TownDef>();
        public RoadDef Roads = new RoadDef();

        public ObjectKind KindOf(ObjectDef d)
        {
            foreach (var k in Kinds) if (k.Id == d.Kind) return k;
            return null;
        }

        public LightKind LightNamed(string id)
        {
            foreach (var l in Lights) if (l.Id == id) return l;
            return null;
        }

        public ObjectDef EmitterFor(int shape)
        {
            foreach (var d in Objects)
                if (d.Light != null && d.Kind == Structure)
                    foreach (var p in d.Parts) if (p.Shape == shape) return d;
            return null;
        }

        public bool PartSolid(ObjectDef d, ObjectPart p) => p.Solid >= 0 ? p.Solid == 1 : KindOf(d).Solid;
        public bool PartShadows(ObjectDef d, ObjectPart p) => p.Shadows >= 0 ? p.Shadows == 1 : KindOf(d).Shadows;

        public float CasterSize(ObjectDef d)
        {
            float s = 0;
            foreach (var p in d.Parts)
                if (PartShadows(d, p)) s = Math.Max(s, Math.Max(p.ScaleX, Math.Max(p.ScaleY, p.ScaleZ)));
            return s * d.Placement.SizeMaxM;
        }

        public void AddShadows(ref Detail.ShadowSpan span, float pixelsPerMetre, float quality)
        {
            foreach (var d in Objects)
            {
                float size = CasterSize(d);
                if (size > 0) span.Add(size, KindOf(d).Rule(), pixelsPerMetre, quality);
            }
        }

        public static readonly string[] KindNames = { "static", "interactive", "animated", "structure" };
        public static readonly string[] Verbs = { "take", "cut" };
        public const string Interactive = "interactive", Structure = "structure";
        public const int MaxDefs = 256;

        public bool IsInteractive(int def) => Objects[def].Kind == Interactive;

        public static List<string> Validate(ObjectLibrary lib, IReadOnlyList<string> biomeIds = null)
        {
            var errors = new List<string>();
            var lights = new HashSet<string>();
            foreach (var l in lib.Lights)
            {
                if (string.IsNullOrEmpty(l.Id)) errors.Add("light without id");
                else if (!lights.Add(l.Id)) errors.Add("duplicate light " + l.Id);
                if (!(l.GlowPixels > 0 && l.UnshadowedPixels > l.GlowPixels && l.ShadowedPixels > l.UnshadowedPixels)) errors.Add("light " + l.Id + ": shadowedPixels > unshadowedPixels > glowPixels > 0");
                if (!(l.Importance > 0)) errors.Add("light " + l.Id + ": importance > 0");
            }
            var levels = new HashSet<string>();
            if (lib.Quality.Levels.Count == 0) errors.Add("quality: needs a level");
            foreach (var l in lib.Quality.Levels)
            {
                if (string.IsNullOrEmpty(l.Id)) errors.Add("quality level without id");
                else if (!levels.Add(l.Id)) errors.Add("duplicate quality level " + l.Id);
                if (!(l.Scale >= 0.1f && l.Scale <= 8f)) errors.Add("quality " + l.Id + ": scale must be 0.1-8");
            }
            if (!levels.Contains(lib.Quality.Default ?? "")) errors.Add("quality: default " + lib.Quality.Default + " is not a level");
            if (lib.LightCaps.Shadowed < 0 || lib.LightCaps.Unshadowed < 0 || lib.LightCaps.Glow < 0) errors.Add("lightBudget: caps >= 0");
            var kinds = new HashSet<string>();
            foreach (var k in lib.Kinds)
            {
                if (Array.IndexOf(KindNames, k.Id) < 0) errors.Add("unknown kind " + k.Id);
                else if (!kinds.Add(k.Id)) errors.Add("duplicate kind " + k.Id);
                if (k.Lods < 1 || k.Lods > 4) errors.Add(k.Id + ": lods must be 1-4");
                if (k.LodPixels == null || k.LodPixels.Length != k.Lods - 1) errors.Add(k.Id + ": lodPixels needs lods - 1 entries");
                else for (int i = 0; i < k.LodPixels.Length; i++)
                    if (k.LodPixels[i] <= k.FadePixels || (i > 0 && k.LodPixels[i] >= k.LodPixels[i - 1])) errors.Add(k.Id + ": lodPixels must fall and stay above fadePixels");
                if (k.FadePixels <= 0 || k.FadePixels >= Detail.ReferenceLines) errors.Add(k.Id + ": fadePixels must be above 0, below 1080");
                if (k.ShadowPixels < 0) errors.Add(k.Id + ": shadowPixels below 0");
                if (k.ShadowCascadePixels < k.ShadowPixels) errors.Add(k.Id + ": shadowCascadePixels must not sit below shadowPixels");
                if (k.Wind && !(k.SwayPixels > 0 && k.FlutterPixels > k.SwayPixels)) errors.Add(k.Id + ": flutterPixels must sit above swayPixels above 0");
                if (!lights.Contains(k.Light ?? "")) errors.Add(k.Id + ": light " + k.Light + " is not defined");
                if (k.ImpostorPixels != 0 && (k.ImpostorPixels <= k.FadePixels || k.LodPixels == null || k.LodPixels.Length == 0 || 1.5f * k.ImpostorPixels >= k.LodPixels[k.LodPixels.Length - 1])) errors.Add(k.Id + ": impostorPixels must sit above fadePixels and 1.5x below the last lodPixels");
            }
            var styles = new HashSet<string>();
            foreach (var st in lib.Styles)
            {
                if (string.IsNullOrEmpty(st.Id)) errors.Add("style without id");
                else if (!styles.Add(st.Id)) errors.Add("duplicate style " + st.Id);
                if (Array.IndexOf(MaterialLibrary.KitNames, st.Trim ?? "") < 0) errors.Add("style " + st.Id + ": trim " + st.Trim + " is not a kit material");
            }
            var ids = new HashSet<string>();
            foreach (var d in lib.Objects)
            {
                if (string.IsNullOrEmpty(d.Id)) { errors.Add("object without id"); continue; }
                if (!ids.Add(d.Id)) errors.Add("duplicate object id " + d.Id);
                if (!kinds.Contains(d.Kind ?? "")) errors.Add(d.Id + ": kind " + d.Kind + " is not defined");
                if (d.Variants < 1 || d.Variants > 16) errors.Add(d.Id + ": variants must be 1-16");
                if (d.Parts.Count == 0) errors.Add(d.Id + ": needs at least one part");
                if (d.Kind == Interactive && (Array.IndexOf(Verbs, d.Verb ?? "") < 0 || d.ReachM <= 0 || d.ReachM > 10))
                    errors.Add(d.Id + ": interactive objects need interact verb take/cut and reachM above 0, at most 10");
                if (d.Kind != Interactive && !string.IsNullOrEmpty(d.Verb)) errors.Add(d.Id + ": only interactive objects take interact");
                if (d.Style != null && (!styles.Contains(d.Style) || d.Kind != Structure)) errors.Add(d.Id + ": style " + d.Style + " must name a defined style on a structure");
                foreach (var p in d.Parts)
                {
                    if (p.Shape < 0 || p.Shape >= ObjectPart.Shapes) errors.Add(d.Id + ": part " + p.Id + " has an unknown shape");
                    if (ObjectPart.IsFoliage(p.Shape) && !p.HasLeafColor) errors.Add(d.Id + ": part " + p.Id + " needs a leafMaterial");
                    if (p.ScaleX <= 0 || p.ScaleY <= 0 || p.ScaleZ <= 0) errors.Add(d.Id + ": part " + p.Id + " scale must be above 0");
                    if (ObjectPart.IsStructure(p.Shape) != (d.Kind == Structure)) errors.Add(d.Id + ": structure shapes belong only to structure objects");
                    if (p.Trim < 0 || p.Trim > MaterialLibrary.KitNames.Length) errors.Add(d.Id + ": part " + p.Id + " has an unknown trim");
                    if (p.Ends < 0 || p.Ends >= ObjectPart.EndNames.Length) errors.Add(d.Id + ": part " + p.Id + " has unknown ends");
                    else if (p.Ends != ObjectPart.EndsAuto && p.Shape != ObjectPart.Log && p.Shape != ObjectPart.Stump) errors.Add(d.Id + ": part " + p.Id + " ends belong only to logs and stumps");
                    else if ((p.Ends == ObjectPart.EndsRooted || p.Ends == ObjectPart.EndsWorn) && p.Shape != ObjectPart.Log) errors.Add(d.Id + ": part " + p.Id + " " + ObjectPart.EndNames[p.Ends] + " ends belong only to logs");
                    else if (p.Ends == ObjectPart.EndsSnapped && p.Shape != ObjectPart.Stump) errors.Add(d.Id + ": part " + p.Id + " snapped ends belong only to stumps");
                }
                if (d.Light != null)
                {
                    var li = d.Light;
                    if (!lights.Contains(li.Category ?? "")) errors.Add(d.Id + ": light category " + li.Category + " is not defined");
                    if (!(li.Intensity > 0) || !(li.RangeM > 0)) errors.Add(d.Id + ": light needs intensity and radiusM above 0");
                    if (li.Importance < 0) errors.Add(d.Id + ": light importance must not be below 0");
                    if (li.Flicker < 0 || li.Flicker > 1 || (li.Flicker > 0 && !(li.FlickerHz > 0))) errors.Add(d.Id + ": light flicker must be 0-1 and needs flickerHz above 0");
                    if (li.LiftM < 0 || li.LiftM > 20) errors.Add(d.Id + ": light liftM must be 0-20");
                    if (li.ConeDeg < 0 || li.ConeDeg > 150) errors.Add(d.Id + ": light coneDeg must be 0-150");
                    if (d.Kind != Structure && d.Placement.Cave == 0) errors.Add(d.Id + ": lights belong only to structure and cave objects");
                    if (li.Daylight != (d.Placement.Cave == ObjectField.CaveSkylight)) errors.Add(d.Id + ": daylight lights belong to skylight objects and only to them");
                }
                var pl = d.Placement;
                if (pl.PerHectare < 0 || pl.PerHectare > 400) errors.Add(d.Id + ": perHectare must be 0-400");
                if (pl.SlopeMax <= pl.SlopeMin || pl.SlopeMin < 0) errors.Add(d.Id + ": slopeMax must exceed slopeMin >= 0");
                if (pl.HardnessMax <= pl.HardnessMin || pl.HardnessMin < 0 || pl.HardnessMax > 1) errors.Add(d.Id + ": hardness range must lie in 0-1 and be increasing");
                if (pl.SizeMinM <= 0 || pl.SizeMaxM < pl.SizeMinM || pl.SizeMaxM > 20) errors.Add(d.Id + ": sizeMinM/sizeMaxM must be increasing, 0-20 m");
                if (pl.ClusterCount < 1 || pl.ClusterCount > 32) errors.Add(d.Id + ": clusterCount must be 1-32");
                if (pl.Oasis != null && ObjectField.OasisKind(pl.Oasis) == 0) errors.Add(d.Id + ": oasis must be palm, reeds, grass or shrub");
                if (pl.ClusterRadiusM < 0 || pl.ClusterRadiusM > 30) errors.Add(d.Id + ": clusterRadiusM must be 0-30");
                if (pl.Sink < 0 || pl.Sink > 0.9f) errors.Add(d.Id + ": sink must be 0-0.9");
                if (pl.AlignToSlope < 0 || pl.AlignToSlope > 1) errors.Add(d.Id + ": alignToSlope must be 0-1");
                if (d.Kind == Structure && (pl.AlignToSlope != 0 || pl.ClusterCount != 1 || pl.FootRiseM > 0 || pl.EditMinSizeM > 0))
                    errors.Add(d.Id + ": structures stand level, alone, with no footRiseM or editMinSizeM");
                if (pl.ChainMin < 1 || pl.ChainMax < pl.ChainMin || pl.ChainMax > 24) errors.Add(d.Id + ": chainMin/chainMax must be increasing, 1-24");
                if (pl.ChainCorner < 0 || pl.ChainCorner > 1 || pl.ChainGate < 0 || pl.ChainGate > 1) errors.Add(d.Id + ": chainCorner and chainGate must be 0-1");
                if ((pl.ChainMax > 1 || pl.ChainCorner > 0 || pl.ChainGate > 0) && !(d.Kind == Structure && d.Parts.Count == 1 && d.Parts[0].Shape == ObjectPart.Wall))
                    errors.Add(d.Id + ": chains belong only to single-part wall structures");
                if (pl.ChainHead != null && (pl.ChainMax < 2 || !lib.Objects.Exists(h => h.Id == pl.ChainHead && h.Kind == Structure && h.Placement.PerHectare <= 0 && h.Parts.Count == 1 && h.Parts[0].Shape == ObjectPart.WallHead)))
                    errors.Add(d.Id + ": chainHead must name an unplaced wall-head structure, on a chain");
                if (pl.FootRiseM < 0 || pl.FootRiseM > 100) errors.Add(d.Id + ": footRiseM must be 0-100");
                if (pl.FootReachM <= 0 || pl.FootReachM > 60) errors.Add(d.Id + ": footReachM must be above 0, at most 60");
                if (pl.FanM < 0 || pl.FanM > 30 || (pl.FanM > 0 && pl.FootRiseM <= 0)) errors.Add(d.Id + ": fanM must be 0-30 and needs footRiseM");
                if (pl.EditMinSizeM < 0 || pl.EditMinSizeM > 20) errors.Add(d.Id + ": editMinSizeM must be 0-20");
                if (pl.EditMinSizeM > 0 && (d.Parts.Count != 1 || d.Parts[0].Shape != ObjectPart.Rock || pl.ClusterCount != 1 || pl.FootRiseM > 0))
                    errors.Add(d.Id + ": editMinSizeM needs one rock part, no clusters and no footRiseM");
                if (pl.EditMinSizeM > 0 && ObjectField.BoulderSideR * d.FootprintRadius() * pl.EditMinSizeM < 0.75f * NearField.NodeStep(0))
                    errors.Add(d.Id + ": editMinSizeM is too small for boulder edits to mesh at the finest step");
                if (pl.Bank != null && (BankSites.Of(pl.Bank) < 0 || pl.PerHectare > 0 || pl.PerHundredM <= 0 || pl.PerHundredM > 800 || pl.FootRiseM > 0 || pl.EditMinSizeM > 0 || pl.ChainMax > 1 || d.Kind == Structure || pl.Biomes.Count > 0))
                    errors.Add(d.Id + ": bank placement needs a known bank site, perHundredM 0-800, no perHectare, biomes, foot, edits, chains or structures");
                if (pl.CaveIce && pl.Cave != ObjectField.CaveStalactite && pl.Cave != ObjectField.CaveStalagmite) errors.Add(d.Id + ": caveIce belongs only to stalactite and stalagmite placement");
                if (pl.Bank == null && pl.Cave == 0 && pl.PerHundredM != 0) errors.Add(d.Id + ": perHundredM belongs only to bank and cave placement");
                if (pl.Cave < 0 || (pl.Cave > 0 && (pl.PerHundredM <= 0 || pl.PerHundredM > 200 || pl.PerHectare > 0 || pl.Bank != null || pl.Discovery > 0 || pl.Biomes.Count > 0 || pl.FootRiseM > 0 || pl.EditMinSizeM > 0 || pl.ChainMax > 1 || (d.Kind == Structure) != ObjectField.IsMineKind(pl.Cave))))
                    errors.Add(d.Id + ": cave placement needs a known cave spot, perHundredM 0-200, no perHectare, bank, discovery, biomes, foot, edits or chains, and structures exactly for mine spots");
                if (pl.Discovery < 0 || (pl.Discovery > 0 && (d.Kind != Structure || pl.PerHectare > 0 || pl.Bank != null || pl.SpacingM < 200 || pl.SpacingM > 5000 || pl.Chance <= 0 || pl.Chance > 1)))
                    errors.Add(d.Id + ": discovery placement needs a known discovery, a structure, no perHectare or bank, spacingM 200-5000 and chance above 0, at most 1");
                if (pl.Liquid != null && !ObjectField.Pooled(pl.Discovery)) errors.Add(d.Id + ": liquid belongs only to spring, geyser and mud-pot discoveries");
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
            if (lib.Objects.Count > MaxDefs) errors.Add("at most " + MaxDefs + " object defs");
            lib.Terrain.Validate(errors);
            lib.Foliage.Validate(errors);
            var towns = new HashSet<string>();
            foreach (var t in lib.Towns)
            {
                if (string.IsNullOrEmpty(t.Id) || !towns.Add(t.Id)) errors.Add("town without unique id " + t.Id);
                if (!(t.ReliefM > 0 && t.WaterM > 0 && t.PlazaM > 0 && t.LaneWidthM > 0 && t.SeekM >= 0 && t.LaneM >= 0)) errors.Add("town " + t.Id + ": distances out of range");
                if (biomeIds != null)
                    foreach (var b in t.Biomes)
                        if (!Contains(biomeIds, b)) errors.Add("town " + t.Id + ": unknown biome " + b);
                int centres = 0;
                foreach (var s in t.Sites)
                {
                    var od = lib.Objects.Find(o => o.Id == s.ObjectId);
                    if (od == null) errors.Add("town " + t.Id + ": unknown object " + s.ObjectId);
                    else if (od.Kind != "structure") errors.Add("town " + t.Id + ": " + s.ObjectId + " is not a structure");
                    if (s.Place < 0) errors.Add("town " + t.Id + ": unknown place for " + s.ObjectId);
                    if (!(s.Min >= 0 && s.Max >= s.Min)) errors.Add("town " + t.Id + ": count out of range for " + s.ObjectId);
                    if (s.Place == TownSite.Centre) centres += s.Max;
                }
                if (centres > 1) errors.Add("town " + t.Id + ": at most one centre site");
            }
            var rd = lib.Roads;
            if (!string.IsNullOrEmpty(rd.BridgeObject))
            {
                var bd = lib.Objects.Find(o => o.Id == rd.BridgeObject);
                if (bd == null || bd.Kind != "structure" || bd.Placement.PerHectare > 0 || bd.Placement.SizeMinM != bd.Placement.SizeMaxM || bd.Placement.SizeMinM <= 0) errors.Add("roads: bridge " + rd.BridgeObject + " needs a structure with no perHectare and one size");
                if (!(rd.SpanM > 0 && rd.RiverM > 0 && rd.RiverM < rd.SpanM && rd.ClearM >= 0)) errors.Add("roads: bridge distances out of range");
            }
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
