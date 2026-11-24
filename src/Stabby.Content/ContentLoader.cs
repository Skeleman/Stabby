using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Stabby.World;

namespace Stabby.Content
{
    public static class ContentLoader
    {
        public const int BiomeSchemaVersion = 1;
        public const int MaterialSchemaVersion = 5;
        public const int ObjectSchemaVersion = 6;
        public const int CreatureSchemaVersion = 2;
        public const int SkySchemaVersion = 1;
        public const int CaveSchemaVersion = 1;

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
            return ParseBiomes(File.ReadAllText(path), path);
        }

        public static MaterialLibrary LoadMaterials(string contentRoot)
        {
            var path = Path.Combine(contentRoot, "materials.json");
            return ParseMaterials(File.ReadAllText(path), path);
        }

        public static ObjectLibrary LoadObjects(string contentRoot, IReadOnlyList<string> biomeIds = null)
        {
            var path = Path.Combine(contentRoot, "objects.json");
            return ParseObjects(File.ReadAllText(path), biomeIds, path, LoadMaterials(contentRoot));
        }

        public static CreatureLibrary LoadCreatures(string contentRoot, IReadOnlyList<string> biomeIds = null)
        {
            var path = Path.Combine(contentRoot, "creatures.json");
            return ParseCreatures(File.ReadAllText(path), biomeIds, path, LoadMaterials(contentRoot));
        }

        public static SkyDef LoadSky(string contentRoot)
        {
            var path = Path.Combine(contentRoot, "sky.json");
            return ParseSky(File.ReadAllText(path), path);
        }

        public static CaveLibrary LoadCaves(string contentRoot)
        {
            var path = Path.Combine(contentRoot, "caves.json");
            return ParseCaves(File.ReadAllText(path), path);
        }

        public static CaveLibrary ParseCaves(string json, string source)
        {
            var root = Root(json, source, CaveSchemaVersion);
            var l = new CaveLibrary { Chance = root.Float("chance", 0) };
            foreach (var k in root.Objects("classes", true))
            {
                var nodes = Pair(k, "nodes", null, source);
                var radius = Pair(k, "radius", null, source);
                var edge = Pair(k, "edge", null, source);
                var chamber = Pair(k, "chamber", DefaultChamber, source);
                var hall = Pair(k, "hall", DefaultHall, source);
                var bridge = Pair(k, "bridge", DefaultBridge, source);
                l.Classes.Add(new CaveClass
                {
                    Id = k.String("id", ""), Fallback = k.String("fallback", ""), Weight = k.Float("weight", 0),
                    MinNodes = (int)nodes[0], MaxNodes = (int)nodes[1], Loops = k.Int("loops", 0), Entrances = k.Int("entrances", 1), Bridges = k.Int("bridges", 0),
                    RadiusMin = radius[0], RadiusMax = radius[1], EdgeMin = edge[0], EdgeMax = edge[1], Turn = k.Float("turn", 0),
                    ChamberChance = k.Float("chamberChance", 0), ChamberMin = chamber[0], ChamberMax = chamber[1],
                    HallMin = hall[0], HallMax = hall[1], BridgeMin = bridge[0], BridgeMax = bridge[1],
                    Warp = k.Float("warp", 0), Lumps = k.Float("lumps", 0), RoomLumps = k.Float("roomLumps", 0),
                });
                k.End();
            }
            root.End();
            Check(CaveLibrary.Validate(l), source);
            return l;
        }

        static readonly float[] DefaultChamber = { 39f, 60f }, DefaultHall = { 0f, 0f }, DefaultBridge = { 9f, 12f };

        static float[] Pair(Json j, string key, float[] fallback, string source)
        {
            var f = j.Floats(key, fallback);
            if (f == null || f.Length != 2) throw new InvalidDataException(source + ": " + key + " must be [min, max]");
            return f;
        }

        public static SkyDef ParseSky(string json, string source)
        {
            var root = Root(json, source, SkySchemaVersion);
            var d = new SkyDef
            {
                DayLengthS = root.Float("dayLengthS", 0), StartHour = root.Float("startHour", 0),
                Sun = Orbit(root.Object("sun")), Moon = Orbit(root.Object("moon")),
                MoonColor = Color(root, "moonColor", source), MoonIntensity = root.Float("moonIntensity", 0),
                MoonSwitchHeight = root.Float("moonSwitchHeight", 0), MoonFadeHeight = root.Float("moonFadeHeight", 0),
                MoonCycleDays = root.Float("moonCycleDays", 0), MoonStartPhase = root.Float("moonStartPhase", 0),
                NewMoonLight = root.Float("newMoonLight", 0), NewMoonAmbient = root.Float("newMoonAmbient", 0),
                MoonGlow = root.Float("moonGlow", 0), StarBrightness = root.Float("starBrightness", 0),
            };
            foreach (var s in root.Objects("stops", true))
            {
                d.Stops.Add(new SkyStop
                {
                    SunHeight = s.Float("sunHeight", 0), Sky = Color(s, "sky", source), Zenith = Color(s, "zenith", source), Glow = Color(s, "glow", source), AmbientSky = Color(s, "ambientSky", source),
                    AmbientGround = Color(s, "ambientGround", source), Sun = Color(s, "sun", source), SunIntensity = s.Float("sunIntensity", 0),
                });
                s.End();
            }
            root.End();
            Check(SkyDef.Validate(d), source);
            return d;
        }

        static Orbit Orbit(Json o)
        {
            if (o == null) return null;
            var r = new Orbit { NoonElevationDeg = o.Float("noonElevationDeg", 0), NoonAzimuthDeg = o.Float("noonAzimuthDeg", 0), HourOffset = o.Float("hourOffset", 0) };
            o.End();
            return r;
        }

        static Rgb Color(Json j, string key, string source)
        {
            var f = j.Floats(key, null);
            if (f == null || f.Length != 3) throw new InvalidDataException(source + ": " + key + " must be [r, g, b]");
            return new Rgb(f[0], f[1], f[2]);
        }

        public static WorldSketch LoadSketch(string contentRoot, string relativePath, WorldConfig c) =>
            SketchFile.Read(File.ReadAllText(Path.Combine(contentRoot, relativePath)), c);

        static Json Root(string json, string source, int schemaVersion)
        {
            var root = Json.Parse(json, source);
            if (root.IsNull) throw new InvalidDataException(source + " is empty");
            int v = root.Int("schemaVersion", 0);
            if (v != schemaVersion)
                throw new InvalidDataException(source + ": schemaVersion " + v + ", expected " + schemaVersion);
            return root;
        }

        static void Check(List<string> errors, string source)
        {
            if (errors.Count > 0) throw new InvalidDataException(source + ": " + string.Join("; ", errors));
        }

        static float Nan(Json j, string key) => j.FloatOrNull(key) ?? float.NaN;

        static uint Hex(string s) => uint.Parse(s.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static uint Hex(string s, string path, string field) =>
            s.Length == 7 && s[0] == '#' && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
                ? v : throw new InvalidDataException(path + ": " + field + " '" + s + "' is not #rrggbb");

        public static List<BiomeDef> ParseBiomes(string json, string source)
        {
            var root = Root(json, source, BiomeSchemaVersion);
            var defs = new List<BiomeDef>();
            foreach (var b in root.Objects("biomes"))
            {
                defs.Add(new BiomeDef
                {
                    Id = b.String("id", ""), Name = b.String("name", ""),
                    TempMinC = b.Float("tempMinC", 0), TempMaxC = b.Float("tempMaxC", 0),
                    PrecipMinMm = b.Float("precipMinMm", 0), PrecipMaxMm = b.Float("precipMaxMm", 0),
                    Color = Hex(b.String("color", "#ff00ff")),
                    HeightMinM = Nan(b, "heightMinM"), HeightMaxM = Nan(b, "heightMaxM"),
                    WaterWithinM = Nan(b, "waterWithinM"), CoastWithinM = Nan(b, "coastWithinM"),
                });
                b.End();
            }
            root.End();
            Check(Biomes.Validate(defs), source);
            return defs;
        }

        public static MaterialLibrary ParseMaterials(string json, string source)
        {
            var root = Root(json, source, MaterialSchemaVersion);
            var lib = new MaterialLibrary
            {
                OffsetAmplitudeM = root.Float("offsetAmplitudeM", 0),
                OffsetWavelengthM = root.Float("offsetWavelengthM", 0),
                TiltPerKm = root.Float("tiltPerKm", 0),
            };
            var surface = root.Object("surface");
            lib.Surface = surface == null ? null : Surface(surface);
            foreach (var l in root.Objects("liquids")) lib.Liquids.Add(Liquid(l, source));
            var look = root.Object("look");
            lib.Look = look == null ? null : new LookDef
            {
                NoiseContrast = look.Float("noiseContrast", 0), AlbedoVar = look.Float("albedoVar", 0),
                DetailWavelengthM = look.Float("detailWavelengthM", 0), DetailLacunarity = look.Float("detailLacunarity", 0),
                DetailOctaves = look.Int("detailOctaves", 0), DetailGain = look.Float("detailGain", 0), Saturation = look.Float("saturation", 0),
                Roughness = look.Float("roughness", 0), Specular = look.Float("specular", 0), RoughnessVar = look.Float("roughnessVar", 0),
            };
            look?.End();
            var sinter = root.Object("sinter");
            lib.Sinter = sinter == null ? null : new SinterDef
            {
                White = Hex(sinter.String("white", ""), source, "sinter.white"), Ochre = Hex(sinter.String("ochre", ""), source, "sinter.ochre"),
                Orange = Hex(sinter.String("orange", ""), source, "sinter.orange"), Roughness = sinter.Float("roughness", 0),
            };
            sinter?.End();
            var mud = root.Object("mud");
            lib.Mud = mud == null ? null : new MudDef
            {
                Surface = Hex(mud.String("surface", ""), source, "mud.surface"),
                Clay = Hex(mud.String("clay", ""), source, "mud.clay"), Grey = Hex(mud.String("grey", ""), source, "mud.grey"), Roughness = mud.Float("roughness", 0),
            };
            mud?.End();
            var peat = root.Object("peat");
            lib.Peat = peat == null ? null : new PeatDef
            {
                Wet = Hex(peat.String("wet", ""), source, "peat.wet"), Dark = Hex(peat.String("dark", ""), source, "peat.dark"),
                Fibre = Hex(peat.String("fibre", ""), source, "peat.fibre"), Roughness = peat.Float("roughness", 0),
            };
            peat?.End();
            var strata = new List<StratumDef>();
            foreach (var s in root.Objects("strata"))
            {
                strata.Add(new StratumDef
                {
                    Id = s.String("id", ""), Name = s.String("name", ""),
                    Color = Hex(s.String("color", "#ff00ff")),
                    ThicknessM = s.Float("thicknessM", 0), Hardness = s.Float("hardness", 0),
                });
                s.End();
            }
            lib.Strata = strata;
            foreach (var m in root.Objects("materials"))
            {
                lib.Materials.Add(new LibraryMaterial { Id = m.String("id", ""), Name = m.String("name", ""), Color = Hex(m.String("color", "#ff00ff")) });
                m.End();
            }
            var kit = root.Object("kit");
            if (kit != null)
            {
                foreach (var name in MaterialLibrary.KitNames)
                    if (kit.Has(name)) lib.Kit[name] = kit.Strings(name, new List<string>());
                kit.End();
            }
            root.End();
            lib.FillCoats();
            Check(MaterialLibrary.Validate(lib), source);
            return lib;
        }

        static LiquidDef Liquid(Json e, string path)
        {
            string id = e.String("id", "");
            var w = new LiquidDef
            {
                Id = id,
                DeepColor = Hex(e.String("deepColor", ""), path, id + ".deepColor"),
                ShallowColor = Hex(e.String("shallowColor", ""), path, id + ".shallowColor"),
                FilmColor = Hex(e.String("filmColor", ""), path, id + ".filmColor"),
                EmissionColor = Hex(e.String("emissionColor", ""), path, id + ".emissionColor"),
                ClarityM = e.Float("clarityM", 0), ShoreM = e.Float("shoreM", 0), FoamWidthM = e.Float("foamWidthM", 0),
                WaveScaleM = e.Float("waveScaleM", 0), WaveSpeed = e.Float("waveSpeed", 0), WaveStrength = e.Float("waveStrength", 0),
                SwellScaleM = e.Float("swellScaleM", 0), SwellStrength = e.Float("swellStrength", 0), LakeSwell = e.Float("lakeSwell", 0),
                FlowSpeed = e.Float("flowSpeed", 0), RapidsSlope = e.Float("rapidsSlope", 0), BedFieldM = e.Float("bedFieldM", 0),
                Silt = e.Float("silt", 0), Opacity = e.Float("opacity", 0), Viscosity = e.Float("viscosity", 0), Bubbles = e.Float("bubbles", 0),
                Film = e.Float("film", 0), Emission = e.Float("emission", 0), Roughness = e.Float("roughness", 0),
                Biomes = e.Strings("biomes", new List<string>()),
            };
            var f = e.Object("fall");
            if (f != null)
            {
                w.Spray = Array.IndexOf(LiquidDef.SprayNames, f.String("spray", ""));
                w.FallSpeed = f.Float("speed", 0);
                w.SheetOpacity = f.Float("sheetOpacity", 0);
                w.SprayColor = Hex(f.String("sprayColor", ""), path, id + ".fall.sprayColor");
                w.SprayAmount = f.Float("sprayAmount", 0);
                w.Froth = f.Float("froth", 0);
                f.End();
            }
            else w.Spray = -1;
            e.End();
            return w;
        }

        static SurfaceDetail Surface(Json e)
        {
            var d = new SurfaceDetail
            {
                FadePixels = e.Float("fadePixels", 0), ParallaxFootprintM = e.Float("parallaxFootprintM", 0), WindDeg = e.Float("windDeg", 0),
                StrataWobbleM = e.Float("strataWobbleM", 0), StrataWobbleWavelengthM = e.Float("strataWobbleWavelengthM", 0),
                CoatPatchAmp = e.Float("coatPatchAmp", 0), CoatPatchWavelengthM = e.Float("coatPatchWavelengthM", 0),
            };
            var r = e.Object("rock");
            if (r != null)
            {
                d.Rock = new RockSurface
                {
                    GrainAmp = r.Float("grainAmp", 0),
                    BumpAmp = r.Float("bumpAmp", 0), BumpWavelengthM = r.Float("bumpWavelengthM", 0),
                    BedSpacingM = r.Float("bedSpacingM", 0), BedDepthM = r.Float("bedDepthM", 0),
                    JointSpacingM = r.Float("jointSpacingM", 0), JointDepthM = r.Float("jointDepthM", 0),
                    Parallax = r.Float("parallax", 0),
                };
                r.End();
            }
            else d.Rock = null;
            var coats = e.Object("coats");
            for (int k = 0; k < SurfaceDetail.CoatKinds.Length; k++)
            {
                var c = coats?.Object(SurfaceDetail.CoatKinds[k]);
                if (c == null) { d.Coats[k] = null; continue; }
                d.Coats[k] = new CoatDef
                {
                    Id = SurfaceDetail.CoatKinds[k], Biomes = c.Strings("biomes", new List<string>()),
                    BumpAmp = c.Float("bumpAmp", 0), BumpWavelengthM = c.Float("bumpWavelengthM", 0),
                    GrainAmp = Nan(c, "grainAmp"),
                    FeatureAmp = c.Float("featureAmp", 0), FeatureWavelengthM = c.Float("featureWavelengthM", 0),
                    Parallax = c.Float("parallax", 0), AlbedoVar = Nan(c, "albedoVar"), Roughness = Nan(c, "roughness"),
                };
                c.End();
            }
            coats?.End();
            e.End();
            return d;
        }

        public static ObjectLibrary ParseObjects(string json, IReadOnlyList<string> biomeIds, string source, MaterialLibrary materials)
        {
            var root = Root(json, source, ObjectSchemaVersion);
            var errors = new List<string>();
            var lib = new ObjectLibrary();
            var budget = root.Object("lightBudget");
            if (budget != null)
            {
                lib.LightCaps = new LightBudget.Caps(budget.Int("shadowed", -1), budget.Int("unshadowed", -1), budget.Int("glow", -1));
                budget.End();
            }
            foreach (var l in root.Objects("lights"))
            {
                lib.Lights.Add(new LightKind
                {
                    Id = l.String("id", ""), ShadowedPixels = l.Float("shadowedPixels", 0), UnshadowedPixels = l.Float("unshadowedPixels", 0), GlowPixels = l.Float("glowPixels", 0),
                    Importance = l.Float("importance", 0),
                });
                l.End();
            }
            foreach (var k in root.Objects("kinds"))
            {
                lib.Kinds.Add(new ObjectKind
                {
                    Id = k.String("id", ""), Lods = k.Int("lods", 0), LodPixels = k.Floats("lodPixels", Array.Empty<float>()),
                    FadePixels = k.Float("fadePixels", 0), Shadows = k.Bool("shadows", false), ShadowPixels = k.Float("shadowPixels", 0),
                    ShadowCascadePixels = k.Float("shadowCascadePixels", 0), Light = k.String("light", ""),
                    Wind = k.Bool("wind", false), SwayPixels = k.Float("swayPixels", 16.2f), FlutterPixels = k.Float("flutterPixels", 54f),
                    ImpostorPixels = k.Float("impostorPixels", 0), Animated = k.Bool("animated", false), Solid = k.Bool("solid", false),
                });
                k.End();
            }
            foreach (var o in root.Objects("objects"))
            {
                lib.Objects.Add(Object(o, errors, materials));
                o.End();
            }
            var terrain = root.Object("terrain");
            if (terrain != null)
            {
                var t0 = new TerrainViewDef();
                lib.Terrain = new TerrainViewDef
                {
                    QuadPixels = terrain.Float("quadPixels", t0.QuadPixels), ContactPixels = terrain.Float("contactPixels", t0.ContactPixels),
                    ContactTexelPixels = terrain.Float("contactTexelPixels", t0.ContactTexelPixels),
                    MistFadePixels = terrain.Float("mistFadePixels", t0.MistFadePixels), MistFullPixels = terrain.Float("mistFullPixels", t0.MistFullPixels),
                    SteamFadePixels = terrain.Float("steamFadePixels", t0.SteamFadePixels), SteamFullPixels = terrain.Float("steamFullPixels", t0.SteamFullPixels),
                    ColumnFadePixels = terrain.Float("columnFadePixels", t0.ColumnFadePixels), ColumnFullPixels = terrain.Float("columnFullPixels", t0.ColumnFullPixels),
                    ShadowTexels = terrain.Float("shadowTexels", t0.ShadowTexels),
                };
                terrain.End();
            }
            var quality = root.Object("quality");
            if (quality != null)
            {
                var q = new QualityDef { Default = quality.String("default", ""), Levels = new List<QualityLevel>() };
                foreach (var l in quality.Objects("levels"))
                {
                    q.Levels.Add(new QualityLevel { Id = l.String("id", ""), Scale = l.Float("scale", 0) });
                    l.End();
                }
                lib.Quality = q;
                quality.End();
            }
            var foliage = root.Object("foliage");
            if (foliage != null)
            {
                lib.Foliage = new FoliageDef { Wind = Wind(foliage.Object("wind")), Grass = Grass(foliage.Object("grass")) };
                foliage.End();
            }
            root.End();
            errors.AddRange(ObjectLibrary.Validate(lib, biomeIds));
            Check(errors, source);
            return lib;
        }

        public static CreatureLibrary ParseCreatures(string json, IReadOnlyList<string> biomeIds, string source, MaterialLibrary materials)
        {
            var root = Root(json, source, CreatureSchemaVersion);
            var errors = new List<string>();
            var lib = new CreatureLibrary();
            var zero = new float[3];
            foreach (var p in root.Objects("parts", true))
            {
                var part = new CreaturePartDef { Id = p.String("id", "") };
                string role = p.String("role", "");
                part.Role = Array.IndexOf(CreaturePartDef.RoleNames, role);
                foreach (var s in p.Objects("segments", true))
                {
                    var radius = s.Floats("radius", null);
                    if (radius == null || radius.Length != 2) { errors.Add(part.Id + ": radius needs 2 numbers"); radius = new float[] { 0, 0 }; }
                    string tone = s.String("tone", "base");
                    var idle = s.Floats("idle", zero);
                    var reach = s.Floats("reach", idle);
                    var push = s.Floats("push", idle);
                    var lift = s.Floats("lift", idle);
                    part.Segments.Add(new CreatureSegmentDef
                    {
                        Length = s.Float("length", 0), RadiusStart = radius[0], RadiusEnd = radius[1], Flatten = s.Float("flatten", 1),
                        Tone = Array.IndexOf(CreatureSegmentDef.ToneNames, tone),
                        Belly = s.Float("belly", 0), Rump = s.Float("rump", 0), Tip = s.Float("tip", 0),
                        Idle = idle, Reach = reach, Push = push, Lift = lift,
                        RunReach = s.Floats("runReach", reach), RunPush = s.Floats("runPush", push), RunLift = s.Floats("runLift", lift),
                        Alert = s.Floats("alert", idle), Graze = s.Floats("graze", idle),
                    });
                    s.End();
                }
                lib.Parts.Add(part);
                p.End();
            }
            foreach (var b in root.Objects("bodies", true))
            {
                string id = b.String("id", "");
                var def = new CreatureDef
                {
                    Id = id, Name = b.String("name", ""), Biomes = b.Strings("biomes", new List<string>()),
                    Length = b.Float("length", 0), Radius = b.Float("radius", 0), Taper = b.Float("taper", 1), Flatten = b.Float("flatten", 1),
                    Haunch = b.Float("haunch", 0), Waist = b.Float("waist", 0), Hump = b.Float("hump", 0),
                    WalkSpeed = b.Float("walkSpeed", 0), MaxSlope = b.Float("maxSlope", 0), Jitter = b.Float("jitter", 0),
                };
                var coat = b.Object("materials");
                if (coat == null) errors.Add(id + ": materials missing");
                else
                {
                    def.Base = Coat(coat, "base", id, materials, errors);
                    def.Accent = Coat(coat, "accent", id, materials, errors);
                    def.Dark = Coat(coat, "dark", id, materials, errors);
                    coat.End();
                }
                var size = b.Floats("size", null);
                if (size == null || size.Length != 2) errors.Add(id + ": size needs 2 numbers");
                else { def.SizeMin = size[0]; def.SizeMax = size[1]; }
                var herd = b.Floats("herd", null);
                if (herd == null || herd.Length != 2) errors.Add(id + ": herd needs 2 numbers");
                else { def.HerdMin = (int)herd[0]; def.HerdMax = (int)herd[1]; }
                def.HerdChance = b.Float("herdChance", -1);
                var detail = b.Object("detail");
                if (detail == null) errors.Add(id + ": detail missing");
                else
                {
                    def.SpawnPixels = detail.Float("spawnPixels", def.SpawnPixels);
                    def.FadePixels = detail.Float("fadePixels", def.FadePixels);
                    def.ShadowPixels = detail.Float("shadowPixels", def.ShadowPixels);
                    def.ShadowCascadePixels = detail.Float("shadowCascadePixels", def.ShadowCascadePixels);
                    def.AnimPixels = detail.Float("animPixels", def.AnimPixels);
                    def.ThinkPixels = detail.Float("thinkPixels", def.ThinkPixels);
                    def.IkPixels = detail.Float("ikPixels", def.IkPixels);
                    def.LowPixels = detail.Float("lowPixels", def.LowPixels);
                    detail.End();
                }
                var wary = b.Object("wary");
                if (wary == null) errors.Add(id + ": wary missing");
                else
                {
                    def.AlertPixels = wary.Float("alertPixels", def.AlertPixels);
                    def.FleePixels = wary.Float("fleePixels", def.FleePixels);
                    def.CalmPixels = wary.Float("calmPixels", def.CalmPixels);
                    def.RunSpeed = wary.Float("runSpeed", def.RunSpeed);
                    def.RunStride = wary.Float("runStride", def.RunStride);
                    def.Dart = wary.Float("dart", def.Dart);
                    wary.End();
                }
                var drives = b.Object("drives");
                if (drives != null)
                {
                    def.Drives.Hunger = Drive(drives.Object("hunger"), def.Drives.Hunger);
                    def.Drives.Thirst = Drive(drives.Object("thirst"), def.Drives.Thirst);
                    def.Drives.Rest = Drive(drives.Object("rest"), def.Drives.Rest);
                    drives.End();
                }
                foreach (var s in b.Objects("slots", true))
                {
                    float phase = s.Float("phase", 0), mirrorPhase = s.Float("mirrorPhase", 0);
                    def.Slots.Add(new CreatureSlot
                    {
                        Part = s.String("part", ""), Parent = s.String("parent", null),
                        Socket = s.Floats("socket", Array.Empty<float>()), Direction = s.Floats("direction", zero),
                        Mirror = s.Bool("mirror", false), Phase = phase, MirrorPhase = mirrorPhase,
                        RunPhase = s.Float("runPhase", phase), RunMirrorPhase = s.Float("runMirrorPhase", mirrorPhase), Scale = s.Float("scale", 1),
                    });
                    s.End();
                }
                lib.Bodies.Add(def);
                b.End();
            }
            root.End();
            errors.AddRange(CreatureLibrary.Validate(lib, biomeIds));
            Check(errors, source);
            return lib;
        }

        static DriveDef Drive(Json d, DriveDef fallback)
        {
            if (d == null) return fallback;
            var r = DriveDef.Of(d.Float("fillSeconds", fallback.Fill), d.Float("threshold", fallback.Threshold), d.Float("sateSeconds", fallback.Sate));
            d.End();
            return r;
        }

        static uint Coat(Json coat, string tone, string id, MaterialLibrary materials, List<string> errors)
        {
            string name = coat.String(tone, "");
            var m = materials?.Find(name);
            if (m == null) { errors.Add(id + ".materials." + tone + ": unknown material \"" + name + "\""); return 0; }
            return m.Color;
        }

        static uint Material(Json j, string field, string where, MaterialLibrary materials, List<string> errors)
        {
            string name = j.String(field, "");
            var m = materials?.Find(name);
            if (m == null) { errors.Add(where + "." + field + ": unknown material \"" + name + "\""); return 0; }
            return m.Color;
        }

        static void InlineColor(Json j, string field, string where, List<string> errors)
        {
            if (j.String(field, null) != null) errors.Add(where + ": inline \"" + field + "\"; name a materials.json material instead");
        }

        static ObjectDef Object(Json o, List<string> errors, MaterialLibrary materials)
        {
            string id = o.String("id", "");
            InlineColor(o, "color", id, errors);
            var def = new ObjectDef
            {
                Id = id, Name = o.String("name", ""), Kind = o.String("kind", ""),
                Color = Material(o, "material", id, materials, errors), Variants = o.Int("variants", 1),
            };
            var interact = o.Object("interact");
            if (interact != null)
            {
                def.Verb = interact.String("verb", "");
                def.ReachM = interact.Float("reachM", 0);
                interact.End();
            }
            var light = o.Object("light");
            if (light != null)
            {
                def.Light = new ObjectLight
                {
                    Category = light.String("category", ""), Color = Material(light, "material", id + ".light", materials, errors),
                    Intensity = light.Float("intensity", 0), RangeM = light.Float("radiusM", 0), Importance = light.Float("importance", 0),
                    Flicker = light.Float("flicker", 0), FlickerHz = light.Float("flickerHz", 0),
                };
                light.End();
            }
            foreach (var p in o.Objects("parts"))
            {
                string pid = p.String("id", "");
                var offset = p.Floats("offset", null);
                var scale = p.Floats("scale", null);
                if ((offset != null && offset.Length != 3) || (scale != null && scale.Length != 3))
                    errors.Add(id + ": part " + pid + " offset/scale need 3 numbers");
                InlineColor(p, "color", id + ": part " + pid, errors);
                InlineColor(p, "leafColor", id + ": part " + pid, errors);
                bool color = p.String("material", null) != null, leaf = p.String("leafMaterial", null) != null;
                var part = new ObjectPart
                {
                    Id = pid, Shape = ObjectLibrary.ShapeOf(p.String("shape", "")),
                    Solid = ObjectLibrary.Tri(p.String("solid", "")), Shadows = ObjectLibrary.Tri(p.String("shadows", "")),
                    HasColor = color, Color = color ? Material(p, "material", id + ".parts." + pid, materials, errors) : def.Color,
                    HasLeafColor = leaf, LeafColor = leaf ? Material(p, "leafMaterial", id + ".parts." + pid, materials, errors) : 0,
                };
                string ends = p.String("ends", "auto");
                part.Ends = Array.IndexOf(ObjectPart.EndNames, ends);
                if (part.Ends < 0) errors.Add(id + ": part " + pid + " has unknown ends \"" + ends + "\"");
                if (part.Solid < -1 || part.Shadows < -1) errors.Add(id + ": part " + pid + " solid/shadows must be \"\", \"on\" or \"off\"");
                if (offset != null && offset.Length == 3) { part.OffsetX = offset[0]; part.OffsetY = offset[1]; part.OffsetZ = offset[2]; }
                if (scale != null && scale.Length == 3) { part.ScaleX = scale[0]; part.ScaleY = scale[1]; part.ScaleZ = scale[2]; }
                def.Parts.Add(part);
                p.End();
            }
            var pl = o.Object("placement");
            if (pl != null)
            {
                def.Placement = new ObjectPlacement
                {
                    Biomes = pl.Strings("biomes", new List<string>()), PerHectare = pl.Float("perHectare", 0),
                    SlopeMin = pl.Float("slopeMin", 0), SlopeMax = pl.Float("slopeMax", 10f),
                    HardnessMin = pl.Float("hardnessMin", 0), HardnessMax = pl.Float("hardnessMax", 1f),
                    SizeMinM = pl.Float("sizeMinM", 1f), SizeMaxM = pl.Float("sizeMaxM", 1f),
                    ClusterCount = pl.Int("clusterCount", 1), ClusterRadiusM = pl.Float("clusterRadiusM", 0),
                    Sink = pl.Float("sink", 0), AlignToSlope = pl.Float("alignToSlope", 0),
                    FootRiseM = pl.Float("footRiseM", 0), FootReachM = pl.Float("footReachM", 12f), FanM = pl.Float("fanM", 0), EditMinSizeM = pl.Float("editMinSizeM", 0),
                    ChainMin = pl.Int("chainMin", 1), ChainMax = pl.Int("chainMax", 1),
                    ChainCorner = pl.Float("chainCorner", 0), ChainGate = pl.Float("chainGate", 0),
                    Bank = pl.String("bank", null), PerHundredM = pl.Float("perHundredM", 0),
                    Discovery = Array.IndexOf(ObjectField.DiscoveryNames, pl.String("discovery", "")), Liquid = pl.String("liquid", null), SpacingM = pl.Float("spacingM", 0), Chance = pl.Float("chance", 1f),
                };
                foreach (var b in pl.Objects("biomeDensity"))
                {
                    def.Placement.BiomeDensity.Add(new BiomeDensity { Biome = b.String("biome", ""), Scale = b.Float("scale", 1f) });
                    b.End();
                }
                pl.End();
            }
            return def;
        }

        static WindDef Wind(Json w)
        {
            var d = new WindDef();
            if (w == null) return d;
            var r = new WindDef
            {
                DirectionDeg = w.Float("directionDeg", d.DirectionDeg), Strength = w.Float("strength", d.Strength),
                GustStrength = w.Float("gustStrength", d.GustStrength), GustScaleM = w.Float("gustScaleM", d.GustScaleM),
                GustSpeed = w.Float("gustSpeed", d.GustSpeed), SwayHz = w.Float("swayHz", d.SwayHz),
            };
            w.End();
            return r;
        }

        static GrassDef Grass(Json g)
        {
            var d = new GrassDef();
            if (g == null) return d;
            var r = new GrassDef
            {
                PerM2 = g.Float("perM2", d.PerM2), NearPixels = g.Float("nearPixels", d.NearPixels), ReachPixels = g.Float("reachPixels", d.ReachPixels),
                ThinPow = g.Float("thinPow", d.ThinPow), Widen = g.Float("widen", d.Widen), WidthMaxM = g.Float("widthMaxM", d.WidthMaxM),
                Lod1Pixels = g.Float("lod1Pixels", d.Lod1Pixels), Lod2Pixels = g.Float("lod2Pixels", d.Lod2Pixels),
                HeightMinM = g.Float("heightMinM", d.HeightMinM), HeightMaxM = g.Float("heightMaxM", d.HeightMaxM),
                WidthM = g.Float("widthM", d.WidthM), Bend = g.Float("bend", d.Bend), TipLight = g.Float("tipLight", d.TipLight),
                Translucency = g.Float("translucency", d.Translucency), CoatMin = g.Float("coatMin", d.CoatMin),
                SlopeMax = g.Float("slopeMax", d.SlopeMax), MaxBlades = g.Int("maxBlades", d.MaxBlades),
            };
            g.End();
            return r;
        }
    }
}
