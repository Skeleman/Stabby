using System;
using System.Collections.Generic;
using System.IO;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game
{
    public static class ObjectFile
    {
        [Serializable] class Kind { public string id; public int lods = 3; public float[] lodPixels; public float fadePixels = 6.48f, shadowPixels = 32.4f; public bool shadows = true, wind, animated, solid = true; }
        [Serializable] class Part { public string id, shape, solid = "", shadows = "", color = ""; public float[] offset, scale; }
        [Serializable] class Place
        {
            public string[] biomes; public float perHectare, slopeMin, slopeMax = 10f, hardnessMin, hardnessMax = 1f, sizeMinM = 1f, sizeMaxM = 1f;
            public int clusterCount = 1; public float clusterRadiusM, sink, alignToSlope, footRiseM, footReachM = 12f, editMinSizeM;
            public Density[] biomeDensity;
        }
        [Serializable] class Density { public string biome; public float scale = 1f; }
        [Serializable] class Obj { public string id, name, kind, color = "#ff00ff"; public int variants = 1; public Part[] parts; public Place placement; }
        [Serializable] class Wind { public float directionDeg = 60f, strength = 0.35f, gustStrength = 0.5f, gustScaleM = 24f, gustSpeed = 6f, swayHz = 0.9f; }
        [Serializable] class Grass
        {
            public float perM2 = 160f, nearPixels = 112.24f, reachPixels = 7.015f, thinPow = 1.2f, widen = 0.9f, widthMaxM = 0.25f, lod1Pixels = 56.12f, lod2Pixels = 22.45f, heightMinM = 0.25f, heightMaxM = 0.6f, widthM = 0.035f;
            public float bend = 0.35f, tipLight = 0.45f, translucency = 0.35f, coatMin = 0.35f, slopeMax = 0.7f;
            public int maxBlades = 131072;
        }
        [Serializable] class TerrainView { public float quadPixels = 10.8f; }
        [Serializable] class Foliage { public Wind wind = new Wind(); public Grass grass = new Grass(); }
        [Serializable] class Dto { public int schemaVersion; public Kind[] kinds; public Obj[] objects; public TerrainView terrain = new TerrainView(); public Foliage foliage = new Foliage(); }

        const int SchemaVersion = 2;

        public static string DefaultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content", "objects.json"));

        static uint Hex(string s) => Convert.ToUInt32(s.TrimStart('#'), 16);

        public static ObjectLibrary ForBiomes(IReadOnlyList<BiomeDef> defs)
        {
            var ids = new List<string>();
            foreach (var d in defs) ids.Add(d.Id);
            return Load(DefaultPath, ids);
        }

        public static ObjectLibrary Load(string path, IReadOnlyList<string> biomeIds)
        {
            var dto = JsonUtility.FromJson<Dto>(File.ReadAllText(path));
            if (dto.schemaVersion != SchemaVersion) throw new InvalidDataException($"objects.json schemaVersion {dto.schemaVersion}, expected {SchemaVersion}");
            var lib = new ObjectLibrary();
            var errors = new List<string>();
            foreach (var k in dto.kinds ?? Array.Empty<Kind>())
                lib.Kinds.Add(new ObjectKind
                {
                    Id = k.id, Lods = k.lods, LodPixels = k.lodPixels ?? Array.Empty<float>(), FadePixels = k.fadePixels,
                    Shadows = k.shadows, ShadowPixels = k.shadowPixels, Wind = k.wind, Animated = k.animated, Solid = k.solid,
                });
            foreach (var o in dto.objects ?? Array.Empty<Obj>())
            {
                var def = new ObjectDef { Id = o.id, Name = o.name, Kind = o.kind, Color = Hex(o.color), Variants = o.variants };
                foreach (var p in o.parts ?? Array.Empty<Part>())
                {
                    bool colored = !string.IsNullOrEmpty(p.color);
                    var part = new ObjectPart
                    {
                        Id = p.id, Shape = ObjectLibrary.ShapeOf(p.shape),
                        Solid = ObjectLibrary.Tri(p.solid), Shadows = ObjectLibrary.Tri(p.shadows),
                        HasColor = colored, Color = colored ? Hex(p.color) : def.Color,
                    };
                    if (part.Solid < -1 || part.Shadows < -1) errors.Add(o.id + ": part " + p.id + " solid/shadows must be \"\", \"on\" or \"off\"");
                    if ((p.offset != null && p.offset.Length != 0 && p.offset.Length != 3) || (p.scale != null && p.scale.Length != 0 && p.scale.Length != 3))
                        errors.Add(o.id + ": part " + p.id + " offset/scale need 3 numbers");
                    if (p.offset != null && p.offset.Length == 3) { part.OffsetX = p.offset[0]; part.OffsetY = p.offset[1]; part.OffsetZ = p.offset[2]; }
                    if (p.scale != null && p.scale.Length == 3) { part.ScaleX = p.scale[0]; part.ScaleY = p.scale[1]; part.ScaleZ = p.scale[2]; }
                    def.Parts.Add(part);
                }
                var pl = o.placement ?? new Place();
                def.Placement = new ObjectPlacement
                {
                    Biomes = new List<string>(pl.biomes ?? Array.Empty<string>()), PerHectare = pl.perHectare, SlopeMin = pl.slopeMin, SlopeMax = pl.slopeMax,
                    HardnessMin = pl.hardnessMin, HardnessMax = pl.hardnessMax, SizeMinM = pl.sizeMinM, SizeMaxM = pl.sizeMaxM,
                    ClusterCount = pl.clusterCount, ClusterRadiusM = pl.clusterRadiusM, Sink = pl.sink, AlignToSlope = pl.alignToSlope,
                    FootRiseM = pl.footRiseM, FootReachM = pl.footReachM, EditMinSizeM = pl.editMinSizeM,
                };
                foreach (var bd in pl.biomeDensity ?? Array.Empty<Density>()) def.Placement.BiomeDensity.Add(new BiomeDensity { Biome = bd.biome, Scale = bd.scale });
                lib.Objects.Add(def);
            }
            lib.Terrain = new TerrainViewDef { QuadPixels = (dto.terrain ?? new TerrainView()).quadPixels };
            var w = dto.foliage?.wind ?? new Wind();
            var g = dto.foliage?.grass ?? new Grass();
            lib.Foliage = new FoliageDef
            {
                Wind = new WindDef { DirectionDeg = w.directionDeg, Strength = w.strength, GustStrength = w.gustStrength, GustScaleM = w.gustScaleM, GustSpeed = w.gustSpeed, SwayHz = w.swayHz },
                Grass = new GrassDef
                {
                    PerM2 = g.perM2, NearPixels = g.nearPixels, ReachPixels = g.reachPixels, ThinPow = g.thinPow, Widen = g.widen, WidthMaxM = g.widthMaxM,
                    Lod1Pixels = g.lod1Pixels, Lod2Pixels = g.lod2Pixels, HeightMinM = g.heightMinM, HeightMaxM = g.heightMaxM, WidthM = g.widthM,
                    Bend = g.bend, TipLight = g.tipLight, Translucency = g.translucency, CoatMin = g.coatMin, SlopeMax = g.slopeMax, MaxBlades = g.maxBlades,
                },
            };
            errors.AddRange(ObjectLibrary.Validate(lib, biomeIds));
            if (errors.Count > 0) throw new InvalidDataException("objects.json: " + string.Join("; ", errors));
            return lib;
        }
    }
}
