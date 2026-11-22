using System.Diagnostics;
using System.Globalization;
using Stabby.Content;
using Stabby.Lint;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class RuntimeParityTests(ITestOutputHelper log)
{
    static readonly string[] Assemblies = ["Stabby.World.dll", "Stabby.Content.dll"];
    static readonly string[] Sources = ["src/Stabby.World", "src/Stabby.Content"];

    const string Driver = """
        using System;
        using Stabby.Content;
        using Stabby.World;
        static class Driver
        {
            static ulong H(float[] a) { ulong h = 14695981039346656037UL; foreach (var v in a) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; } return h; }
            static void P(string s, ulong h) { Console.WriteLine(s + " " + h.ToString("x16")); }
            static void Main(string[] args)
            {
                var c = new WorldConfig { Seed = ulong.Parse(args[2]), Size = int.Parse(args[1]), CellMeters = WorldConfig.CellMetersFor(int.Parse(args[1])) };
                var biomes = ContentLoader.LoadBiomes(args[0]);
                var materials = ContentLoader.LoadMaterials(args[0]);
                var sk = WorldSketch.Generate(c);
                P("sketch-uplift", H(sk.Uplift)); P("sketch-valley", H(sk.Valley)); P("sketch-cliff", H(sk.Cliff)); P("sketch-coast", H(sk.Coast));
                var h = Erosion.InitialHeight(c, sk);
                P("initial-height", H(h));
                var w = WorldGenerator.Generate(c, biomes, materials);
                P("output", w.OutputFingerprint()); P("full", w.Fingerprint());
            }
        }
        """;

    static string H(float[] a)
    {
        ulong h = 14695981039346656037UL;
        foreach (var v in a) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    static List<string> CoreClr(string content, int size, ulong seed)
    {
        var c = new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) };
        var sk = WorldSketch.Generate(c);
        var w = WorldGenerator.Generate(c, ContentLoader.LoadBiomes(content), ContentLoader.LoadMaterials(content));
        return
        [
            "sketch-uplift " + H(sk.Uplift), "sketch-valley " + H(sk.Valley), "sketch-cliff " + H(sk.Cliff), "sketch-coast " + H(sk.Coast),
            "initial-height " + H(Erosion.InitialHeight(c, sk)),
            "output " + w.OutputFingerprint().ToString("x16", CultureInfo.InvariantCulture),
            "full " + w.Fingerprint().ToString("x16", CultureInfo.InvariantCulture),
        ];
    }

    static string? UnityMono(string repo)
    {
        var env = Environment.GetEnvironmentVariable("STABBY_UNITY_MONO");
        if (!string.IsNullOrEmpty(env)) return Directory.Exists(env) ? env : null;
        var version = File.ReadLines(Path.Combine(repo, "unity/ProjectSettings/ProjectVersion.txt"))
            .First(l => l.StartsWith("m_EditorVersion:", StringComparison.Ordinal)).Split(':')[1].Trim();
        var dir = Path.Combine("C:/Unity", version, "Editor/Data/MonoBleedingEdge");
        return Directory.Exists(dir) ? dir : null;
    }

    static string Run(string exe, string args, string cwd)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, exe + " " + args + "\n" + output + err.Result);
        return output;
    }

    [Fact]
    public void UnityBuildGeneratesTheCliWorld()
    {
        var repo = Linter.FindRepo(AppContext.BaseDirectory);
        var mono = UnityMono(repo);
        var built = Path.Combine(repo, "unity/Library/ScriptAssemblies");
        var dlls = Assemblies.Select(d => Path.Combine(built, d)).ToArray();
        if (mono == null || !dlls.All(File.Exists))
        {
            log.WriteLine("skipped: no Unity Mono or no Unity-built Stabby.World in " + built);
            return;
        }
        var compiled = dlls.Min(File.GetLastWriteTimeUtc);
        var newest = Sources
            .SelectMany(d => Directory.GetFiles(Path.Combine(repo, d), "*.cs", SearchOption.AllDirectories))
            .Append(Path.Combine(repo, "unity/Assets/CodeGen/FloatRounding.cs"))
            .Max(File.GetLastWriteTimeUtc);
        if (newest > compiled)
        {
            log.WriteLine("skipped: Unity's Stabby.World build predates the source; open the project to recompile");
            return;
        }
        var work = Path.Combine(Path.GetTempPath(), "stabby-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            foreach (var d in dlls) File.Copy(d, Path.Combine(work, Path.GetFileName(d)));
            File.WriteAllText(Path.Combine(work, "Driver.cs"), Driver);
            var monoExe = Path.Combine(mono, "bin/mono.exe");
            var lib = Path.Combine(mono, "lib/mono/4.5");
            Run(monoExe, $"\"{Path.Combine(lib, "csc.exe")}\" -nologo -out:driver.exe -r:Stabby.World.dll -r:Stabby.Content.dll -r:\"{Path.Combine(lib, "Facades/netstandard.dll")}\" Driver.cs", work);
            var content = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
            var unity = Run(monoExe, $"driver.exe \"{content}\" 128 3", work).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var cli = CoreClr(content, 128, 3);
            for (int i = 0; i < cli.Count; i++) log.WriteLine("cli " + cli[i] + " | unity " + (i < unity.Count ? unity[i] : "-"));
            Assert.Equal(cli, unity);
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }
}
