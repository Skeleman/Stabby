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

        public static List<BiomeDef> Biomes() => ContentLoader.ParseBiomes(File.ReadAllText(BiomesPath), BiomesPath);

        public static MaterialLibrary Materials(string path) => ContentLoader.ParseMaterials(File.ReadAllText(path), path);

        public static ObjectLibrary Objects(IReadOnlyList<BiomeDef> defs)
        {
            var ids = new List<string>(defs.Count);
            foreach (var d in defs) ids.Add(d.Id);
            return ContentLoader.ParseObjects(File.ReadAllText(ObjectsPath), ids, ObjectsPath);
        }
    }
}
