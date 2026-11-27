using System.Collections.Generic;
using System.IO;
using Stabby.Content;
using Stabby.World;
using UnityEngine;

namespace Stabby.Viewer
{
    public static class ContentFiles
    {
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content"));
        public static string BiomesPath => Path.Combine(Root, "biomes.json");
        public static string MaterialsPath => Path.Combine(Root, "materials.json");
        public static string ObjectsPath => Path.Combine(Root, "objects.json");
        public static string CreaturesPath => Path.Combine(Root, "creatures.json");
        public static string SkyPath => Path.Combine(Root, "sky.json");
        public static string CavesPath => Path.Combine(Root, "caves.json");

        public static List<BiomeDef> Biomes() => ContentLoader.ParseBiomes(File.ReadAllText(BiomesPath), BiomesPath);

        public static SkyDef Sky() => ContentLoader.ParseSky(File.ReadAllText(SkyPath), SkyPath);

        public static CaveLibrary Caves() => ContentLoader.ParseCaves(File.ReadAllText(CavesPath), CavesPath);

        public static MaterialLibrary Materials(string path) => ContentLoader.ParseMaterials(File.ReadAllText(path), path);

        public static ObjectLibrary Objects(IReadOnlyList<BiomeDef> defs)
        {
            var ids = new List<string>(defs.Count);
            foreach (var d in defs) ids.Add(d.Id);
            var lib = ContentLoader.ParseObjects(File.ReadAllText(ObjectsPath), ids, ObjectsPath, Materials(MaterialsPath));
            if (ObjectDensity != 1f)
                foreach (var d in lib.Objects)
                    if (d.Placement.EditMinSizeM <= 0 && d.Kind != ObjectLibrary.Structure) d.Placement.PerHectare *= ObjectDensity;
            return lib;
        }

        public static CreatureLibrary Creatures(IReadOnlyList<BiomeDef> defs)
        {
            var ids = new List<string>(defs.Count);
            foreach (var d in defs) ids.Add(d.Id);
            return ContentLoader.ParseCreatures(File.ReadAllText(CreaturesPath), ids, CreaturesPath, Materials(MaterialsPath));
        }

        static ulong? bakeKey;

        public static ulong BakeKey() => bakeKey ??= GroundBake.KeyOf(new[] { File.ReadAllBytes(BiomesPath), File.ReadAllBytes(MaterialsPath), File.ReadAllBytes(ObjectsPath), File.ReadAllBytes(CavesPath) }, ObjectDensity);

        public static readonly float ObjectDensity = float.TryParse(System.Environment.GetEnvironmentVariable("STABBY_OBJECT_DENSITY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var k) && k > 0 ? k : 1f;
    }
}
