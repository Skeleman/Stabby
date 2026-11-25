using System.Reflection;
using Stabby.Content;

namespace Stabby.World.Tests;

public class TestImpactTests
{
    static readonly string Root = Path.GetDirectoryName(ContentLoader.FindContentRoot(AppContext.BaseDirectory))!;

    static List<(string[] Globs, string[] Terms)> Map()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "tools", "scripts", "test-impact.txt")).Where(l => l.Trim().Length > 0).ToList();
        foreach (var l in lines) Assert.True(l.Contains(" -> "), $"test-impact line without ' -> ': {l}");
        return lines.Select(l =>
        {
            var i = l.IndexOf(" -> ", StringComparison.Ordinal);
            return (l[..i].Split(' ', StringSplitOptions.RemoveEmptyEntries), l[(i + 4)..].Split('|', StringSplitOptions.RemoveEmptyEntries));
        }).ToList();
    }

    [Fact]
    public void EveryTestClassIsReachableFromTheMap()
    {
        var terms = Map().SelectMany(e => e.Terms).ToList();
        var missing = typeof(TestImpactTests).Assembly.GetTypes()
            .Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<FactAttribute>() != null))
            .Where(t => !t.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() != null).Any(m => terms.Any(x => $"{t.FullName}.{m.Name}".Contains(x, StringComparison.Ordinal))))
            .Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, "no test-impact line reaches: " + string.Join(", ", missing));
    }

    [Fact]
    public void MapNamesRealFilesAndHasAFallback()
    {
        var map = Map();
        Assert.Contains(map, e => e.Globs.Length == 1 && e.Globs[0] == "fallback");
        var gone = map.SelectMany(e => e.Globs).Where(g => g != "fallback" && !Matches(g)).ToList();
        Assert.True(gone.Count == 0, "test-impact names missing files: " + string.Join(", ", gone));
    }

    static bool Matches(string glob)
    {
        var dir = Path.Combine(Root, Path.GetDirectoryName(glob) ?? "");
        return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir, Path.GetFileName(glob)).Any();
    }
}
