using System;
using System.Collections.Generic;
using System.IO;
using Stabby.World;
using UnityEngine;

namespace Stabby.Viewer
{
    public static class MaterialFile
    {
        [Serializable] class Row { public string id, name, color; public float thicknessM; }
        [Serializable] class Dto { public int schemaVersion; public float offsetAmplitudeM, offsetWavelengthM, tiltPerKm; public Row[] strata; }

        public static string DefaultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content", "materials.json"));

        public static MaterialLibrary Load(string path)
        {
            var dto = JsonUtility.FromJson<Dto>(File.ReadAllText(path));
            if (dto.schemaVersion != 1) throw new InvalidDataException($"materials.json schemaVersion {dto.schemaVersion}, expected 1");
            var lib = new MaterialLibrary { OffsetAmplitudeM = dto.offsetAmplitudeM, OffsetWavelengthM = dto.offsetWavelengthM, TiltPerKm = dto.tiltPerKm };
            foreach (var r in dto.strata)
                lib.Strata.Add(new StratumDef { Id = r.id, Name = r.name, ThicknessM = r.thicknessM, Color = Convert.ToUInt32(r.color.TrimStart('#'), 16) });
            var errors = MaterialLibrary.Validate(lib);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            return lib;
        }
    }
}
