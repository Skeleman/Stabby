using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Stabby.World;

namespace Stabby.Viewer
{
    public static class MapFile
    {
        public static string PathFor(ulong seed, int size) =>
            Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "out", "maps", $"seed{seed}-{size}.stabbymap"));

        public static (GeneratedWorld, string) LoadOrGenerate(string path, WorldConfig c, IReadOnlyList<BiomeDef> defs, MaterialLibrary materials, bool save)
        {
            var sw = Stopwatch.StartNew();
            string why = "no map file";
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(ContentFiles.BiomesPath)) why = "map file older than biomes.json";
            else if (File.Exists(path) && File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(ContentFiles.MaterialsPath)) why = "map file older than materials.json";
            else if (File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    int v = WorldFile.GeneratorVersionOf(bytes);
                    if (v != WorldConfig.GeneratorVersion) why = $"map file from generator {v}";
                    else
                    {
                        var w = WorldFile.Read(bytes, materials);
                        if (w.Config.Seed == c.Seed && w.Config.Size == c.Size) return (w, $"loaded map file in {sw.ElapsedMilliseconds} ms");
                        why = "map file has another seed or size";
                    }
                }
                catch (Exception e) when (e is FormatException || e is IOException) { why = $"map file unreadable: {e.Message}"; }
            }
            var g = WorldGenerator.Generate(c, defs, materials);
            string note = $"generated in {sw.ElapsedMilliseconds} ms ({why})";
            if (!save) return (g, note);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path + ".tmp", WorldFile.Write(g));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
            return (g, note + ", saved map file");
        }
    }
}
