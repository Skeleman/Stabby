using System;
using System.Collections.Generic;
using System.IO;
using Stabby.World;
using UnityEngine;

namespace Stabby.Viewer
{
    public static class MaterialFile
    {
        public const int SchemaVersion = 2;

        [Serializable] class Row { public string id, name, color; public float thicknessM, hardness; }
        [Serializable] class Rock { public float grainAmp, grainWavelengthM, bumpAmp, bumpWavelengthM, bedSpacingM, bedDepthM, jointSpacingM, jointDepthM, parallax; }
        [Serializable] class Coat { public string[] biomes; public float bumpAmp, bumpWavelengthM, grainAmp, grainWavelengthM, featureAmp, featureWavelengthM, parallax, albedoVar; }
        [Serializable] class Coats { public Coat grass, soil, sand, snow; }
        [Serializable] class Surface { public float fadePixels, parallaxFootprintM, windDeg, strataWobbleM, strataWobbleWavelengthM, coatPatchAmp, coatPatchWavelengthM; public Rock rock; public Coats coats; }
        [Serializable] class Dto { public int schemaVersion; public float offsetAmplitudeM, offsetWavelengthM, tiltPerKm; public Surface surface; public Row[] strata; }

        public static string DefaultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content", "materials.json"));

        public static MaterialLibrary Load(string path)
        {
            var dto = JsonUtility.FromJson<Dto>(File.ReadAllText(path));
            if (dto.schemaVersion != SchemaVersion) throw new InvalidDataException($"materials.json schemaVersion {dto.schemaVersion}, expected {SchemaVersion}");
            var lib = new MaterialLibrary { OffsetAmplitudeM = dto.offsetAmplitudeM, OffsetWavelengthM = dto.offsetWavelengthM, TiltPerKm = dto.tiltPerKm };
            var s = dto.surface;
            lib.Surface = s == null ? null : new SurfaceDetail
            {
                FadePixels = s.fadePixels, ParallaxFootprintM = s.parallaxFootprintM, WindDeg = s.windDeg,
                StrataWobbleM = s.strataWobbleM, StrataWobbleWavelengthM = s.strataWobbleWavelengthM,
                CoatPatchAmp = s.coatPatchAmp, CoatPatchWavelengthM = s.coatPatchWavelengthM,
                Rock = s.rock == null ? null : new RockSurface
                {
                    GrainAmp = s.rock.grainAmp, GrainWavelengthM = s.rock.grainWavelengthM,
                    BumpAmp = s.rock.bumpAmp, BumpWavelengthM = s.rock.bumpWavelengthM,
                    BedSpacingM = s.rock.bedSpacingM, BedDepthM = s.rock.bedDepthM,
                    JointSpacingM = s.rock.jointSpacingM, JointDepthM = s.rock.jointDepthM,
                    Parallax = s.rock.parallax,
                },
            };
            if (lib.Surface != null && s.coats != null)
            {
                var coats = new[] { s.coats.grass, s.coats.soil, s.coats.sand, s.coats.snow };
                for (int k = 0; k < coats.Length; k++)
                {
                    var c = coats[k];
                    lib.Surface.Coats[k] = c == null ? null : new CoatDef
                    {
                        Id = SurfaceDetail.CoatKinds[k], Biomes = new List<string>(c.biomes ?? new string[0]),
                        BumpAmp = c.bumpAmp, BumpWavelengthM = c.bumpWavelengthM,
                        GrainAmp = c.grainAmp, GrainWavelengthM = c.grainWavelengthM,
                        FeatureAmp = c.featureAmp, FeatureWavelengthM = c.featureWavelengthM,
                        Parallax = c.parallax, AlbedoVar = c.albedoVar,
                    };
                }
            }
            foreach (var r in dto.strata)
                lib.Strata.Add(new StratumDef { Id = r.id, Name = r.name, ThicknessM = r.thicknessM, Hardness = r.hardness, Color = Convert.ToUInt32(r.color.TrimStart('#'), 16) });
            var errors = MaterialLibrary.Validate(lib);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            return lib;
        }
    }
}
