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
        public const int MaterialSchemaVersion = 2;
        public const int ObjectSchemaVersion = 2;
        public const int CreatureSchemaVersion = 1;

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
            return ParseObjects(File.ReadAllText(path), biomeIds, path);
        }

        public static CreatureLibrary LoadCreatures(string contentRoot, IReadOnlyList<string> biomeIds = null)
        {
            var path = Path.Combine(contentRoot, "creatures.json");
            return ParseCreatures(File.ReadAllText(path), biomeIds, path);
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
            var water = root.Object("water");
            lib.Water = water == null ? null : Water(water, source);
            var look = root.Object("look");
            lib.Look = look == null ? null : new LookDef
            {
                NoiseContrast = look.Float("noiseContrast", 0), AlbedoVar = look.Float("albedoVar", 0),
                DetailWavelengthM = look.Float("detailWavelengthM", 0), Saturation = look.Float("saturation", 0),
                Roughness = look.Float("roughness", 0), Specular = look.Float("specular", 0), RoughnessVar = look.Float("roughnessVar", 0),
            };
            look?.End();
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
            root.End();
            lib.FillCoats();
            Check(MaterialLibrary.Validate(lib), source);
            return lib;
        }

        static WaterDef Water(Json e, string path)
        {
            var w = new WaterDef
            {
                DeepColor = Hex(e.String("deepColor", ""), path, "water.deepColor"),
                ShallowColor = Hex(e.String("shallowColor", ""), path, "water.shallowColor"),
                ClarityM = e.Float("clarityM", 0), ShoreM = e.Float("shoreM", 0), FoamWidthM = e.Float("foamWidthM", 0),
                WaveScaleM = e.Float("waveScaleM", 0), WaveSpeed = e.Float("waveSpeed", 0), WaveStrength = e.Float("waveStrength", 0),
                SwellScaleM = e.Float("swellScaleM", 0), SwellStrength = e.Float("swellStrength", 0), LakeSwell = e.Float("lakeSwell", 0),
                FlowSpeed = e.Float("flowSpeed", 0), RapidsSlope = e.Float("rapidsSlope", 0), BedFieldM = e.Float("bedFieldM", 0),
            };
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
                    GrainAmp = r.Float("grainAmp", 0), GrainWavelengthM = r.Float("grainWavelengthM", 0),
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
                    GrainAmp = Nan(c, "grainAmp"), GrainWavelengthM = Nan(c, "grainWavelengthM"),
                    FeatureAmp = c.Float("featureAmp", 0), FeatureWavelengthM = c.Float("featureWavelengthM", 0),
                    Parallax = c.Float("parallax", 0), AlbedoVar = Nan(c, "albedoVar"), Roughness = Nan(c, "roughness"),
                };
                c.End();
            }
            coats?.End();
            e.End();
            return d;
        }

        public static ObjectLibrary ParseObjects(string json, IReadOnlyList<string> biomeIds, string source)
        {
            var root = Root(json, source, ObjectSchemaVersion);
            var errors = new List<string>();
            var lib = new ObjectLibrary();
            foreach (var k in root.Objects("kinds"))
            {
                lib.Kinds.Add(new ObjectKind
                {
                    Id = k.String("id", ""), Lods = k.Int("lods", 0), LodPixels = k.Floats("lodPixels", Array.Empty<float>()),
                    FadePixels = k.Float("fadePixels", 0), Shadows = k.Bool("shadows", false), ShadowPixels = k.Float("shadowPixels", 0),
                    Wind = k.Bool("wind", false), SwayPixels = k.Float("swayPixels", 16.2f), FlutterPixels = k.Float("flutterPixels", 54f),
                    ImpostorPixels = k.Float("impostorPixels", 0), Animated = k.Bool("animated", false), Solid = k.Bool("solid", false),
                });
                k.End();
            }
            foreach (var o in root.Objects("objects"))
            {
                lib.Objects.Add(Object(o, errors));
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
                };
                terrain.End();
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

        public static CreatureLibrary ParseCreatures(string json, IReadOnlyList<string> biomeIds, string source)
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
                    part.Segments.Add(new CreatureSegmentDef
                    {
                        Length = s.Float("length", 0), RadiusStart = radius[0], RadiusEnd = radius[1], Flatten = s.Float("flatten", 1),
                        Tone = Array.IndexOf(CreatureSegmentDef.ToneNames, tone),
                        Belly = s.Float("belly", 0), Rump = s.Float("rump", 0), Tip = s.Float("tip", 0),
                        Idle = idle, Reach = s.Floats("reach", idle), Push = s.Floats("push", idle), Lift = s.Floats("lift", idle),
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
                    WalkSpeed = b.Float("walkSpeed", 0), Jitter = b.Float("jitter", 0),
                };
                var colors = b.Object("colors");
                if (colors == null) errors.Add(id + ": colors missing");
                else
                {
                    string path = id + ".colors";
                    def.Base = Hex(colors.String("base", ""), path, "base");
                    def.Accent = Hex(colors.String("accent", ""), path, "accent");
                    def.Dark = Hex(colors.String("dark", ""), path, "dark");
                    colors.End();
                }
                var size = b.Floats("size", null);
                if (size == null || size.Length != 2) errors.Add(id + ": size needs 2 numbers");
                else { def.SizeMin = size[0]; def.SizeMax = size[1]; }
                var herd = b.Floats("herd", null);
                if (herd == null || herd.Length != 2) errors.Add(id + ": herd needs 2 numbers");
                else { def.HerdMin = (int)herd[0]; def.HerdMax = (int)herd[1]; }
                var detail = b.Object("detail");
                if (detail == null) errors.Add(id + ": detail missing");
                else
                {
                    def.FadePixels = detail.Float("fadePixels", def.FadePixels);
                    def.ShadowPixels = detail.Float("shadowPixels", def.ShadowPixels);
                    def.AnimPixels = detail.Float("animPixels", def.AnimPixels);
                    def.IkPixels = detail.Float("ikPixels", def.IkPixels);
                    detail.End();
                }
                foreach (var s in b.Objects("slots", true))
                {
                    def.Slots.Add(new CreatureSlot
                    {
                        Part = s.String("part", ""), Parent = s.String("parent", null),
                        Socket = s.Floats("socket", Array.Empty<float>()), Direction = s.Floats("direction", zero),
                        Mirror = s.Bool("mirror", false), Phase = s.Float("phase", 0), MirrorPhase = s.Float("mirrorPhase", 0), Scale = s.Float("scale", 1),
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

        static ObjectDef Object(Json o, List<string> errors)
        {
            string id = o.String("id", "");
            var def = new ObjectDef
            {
                Id = id, Name = o.String("name", ""), Kind = o.String("kind", ""),
                Color = Hex(o.String("color", "#ff00ff")), Variants = o.Int("variants", 1),
            };
            var interact = o.Object("interact");
            if (interact != null)
            {
                def.Verb = interact.String("verb", "");
                def.ReachM = interact.Float("reachM", 0);
                interact.End();
            }
            foreach (var p in o.Objects("parts"))
            {
                string pid = p.String("id", "");
                var offset = p.Floats("offset", null);
                var scale = p.Floats("scale", null);
                if ((offset != null && offset.Length != 3) || (scale != null && scale.Length != 3))
                    errors.Add(id + ": part " + pid + " offset/scale need 3 numbers");
                var color = p.String("color", null);
                var leaf = p.String("leafColor", null);
                var part = new ObjectPart
                {
                    Id = pid, Shape = ObjectLibrary.ShapeOf(p.String("shape", "")),
                    Solid = ObjectLibrary.Tri(p.String("solid", "")), Shadows = ObjectLibrary.Tri(p.String("shadows", "")),
                    HasColor = color != null, Color = color != null ? Hex(color) : def.Color,
                    HasLeafColor = leaf != null, LeafColor = leaf != null ? Hex(leaf) : 0,
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
