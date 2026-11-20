using System;
using System.Collections.Generic;
using System.IO;
using Stabby.World;
using UnityEngine;

namespace Stabby.Viewer
{
    public static class BiomeFile
    {
        [Serializable] class Row { public string id, name, color; public float tempMinC, tempMaxC, precipMinMm, precipMaxMm; }
        [Serializable] class Dto { public int schemaVersion; public Row[] biomes; }

        public static string DefaultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content", "biomes.json"));

        public static List<BiomeDef> Load(string path)
        {
            var dto = JsonUtility.FromJson<Dto>(File.ReadAllText(path));
            if (dto.schemaVersion != 1) throw new InvalidDataException($"biomes.json schemaVersion {dto.schemaVersion}, expected 1");
            var defs = new List<BiomeDef>();
            foreach (var r in dto.biomes)
                defs.Add(new BiomeDef
                {
                    Id = r.id, Name = r.name,
                    TempMinC = r.tempMinC, TempMaxC = r.tempMaxC,
                    PrecipMinMm = r.precipMinMm, PrecipMaxMm = r.precipMaxMm,
                    Color = Convert.ToUInt32(r.color.TrimStart('#'), 16),
                });
            var errors = Biomes.Validate(defs);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            return defs;
        }
    }
}
