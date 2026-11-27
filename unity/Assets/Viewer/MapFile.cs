using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Stabby.World;

namespace Stabby.Viewer
{
    public sealed class MapFileException : Exception
    {
        public MapFileException(string message) : base(message) { }
    }

    public static class MapFile
    {
        public static string PathFor(ulong seed, int size) =>
            Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "out", "maps", $"seed{seed}-{size}.stabbymap"));

        public static ulong ContentHash() => WorldFile.ContentHash(File.ReadAllBytes(ContentFiles.BiomesPath), File.ReadAllBytes(ContentFiles.MaterialsPath));

        public static string BakeCommand(WorldConfig c, string path) =>
            $"dotnet run -c Release --project tools/Stabby.Cli -- bake --seed {c.Seed} --size {c.Size} --out \"{path}\"";

        public static (GeneratedWorld, string) LoadOrGenerate(string path, WorldConfig c, IReadOnlyList<BiomeDef> defs, MaterialLibrary materials, bool save)
        {
            var sw = Stopwatch.StartNew();
            var times = new LoadTimes();
            string why = "no map file";
            ulong hash = ContentHash();
            times.Mark("content hash");
            if (File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    times.Mark("file read");
                    int v = WorldFile.GeneratorVersionOf(bytes);
                    if (v != WorldConfig.GeneratorVersion) why = $"map file from generator {v}, this build runs {WorldConfig.GeneratorVersion}";
                    else if (WorldFile.ContentHashOf(bytes) != hash) why = "map file baked from other biomes.json or materials.json";
                    else
                    {
                        times.Mark("version and hash checks");
                        var w = WorldFile.Read(bytes, materials, times);
                        if (w.Config.Seed == c.Seed && w.Config.Size == c.Size)
                        {
                            w.SketchInBackground();
                            return (w, $"loaded map file in {sw.ElapsedMilliseconds} ms ({times} ms)");
                        }
                        why = "map file has another seed or size";
                    }
                }
                catch (Exception e) when (e is FormatException || e is IOException) { why = $"map file unreadable: {e.Message}"; }
            }
            if (!UnityEngine.Application.isEditor) throw new MapFileException($"{why}: {path}. Bake it with: {BakeCommand(c, path)}");
            var g = WorldGenerator.Generate(c, defs, materials);
            string note = $"generated in {sw.ElapsedMilliseconds} ms ({why})";
            if (!save) return (g, note);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path + ".tmp", WorldFile.Write(g, hash));
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".tmp", path);
            return (g, note + ", saved map file");
        }
    }
}
