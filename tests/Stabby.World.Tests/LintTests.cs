using Stabby.Lint;

namespace Stabby.World.Tests;

public class LintTests
{
    [Fact]
    public void RepoPassesLint() => Assert.Empty(Linter.Check(Linter.FindRepo(AppContext.BaseDirectory)));

    const string GameFile = "unity/Assets/Game/Sample.cs";

    static List<string> Metres(string body, Dictionary<string, string>? allow = null) =>
        Linter.MetreProblems(GameFile, "class Grass { float nearM = 5f, reach; int defs; void Draw(float3 a, float3 b, float d, float dist) { " + body + " } }",
            allow ?? new Dictionary<string, string>());

    [Theory]
    [InlineData("if (math.distance(a, b) < 80f) return;")]
    [InlineData("bool near = dist <= nearM;")]
    [InlineData("bool far = 25 < Vector3.Distance(a, b);")]
    [InlineData("bool on = (a - b).magnitude > 2.5f;")]
    public void FlagsMetreDistanceThresholds(string body) => Assert.Single(Metres(body));

    [Theory]
    [InlineData("if (math.distancesq(a, b) < 0.0001f) return;")]
    [InlineData("for (int i = 0; i < defs; i++) { }")]
    [InlineData("bool far = d > 80f;")]
    [InlineData("bool on = math.distance(a, b) < reach * 1.2f;")]
    [InlineData("bool on = Detail.ScreenSize(1f, math.distance(a, b), 935f) > 86.4f;")]
    public void IgnoresOtherComparisons(string body) => Assert.Empty(Metres(body));

    [Fact]
    public void AllowListSilencesAMember() =>
        Assert.Empty(Metres("if (math.distance(a, b) < 80f) return;", new Dictionary<string, string> { ["Grass.Draw"] = "reason" }));

    [Fact]
    public void OnlyDetailCodeIsChecked()
    {
        Assert.True(Linter.IsDetailCode(GameFile));
        Assert.True(Linter.IsDetailCode("src/Stabby.World/Detail.cs"));
        Assert.False(Linter.IsDetailCode("src/Stabby.World/Rivers.cs"));
        Assert.False(Linter.IsDetailCode("unity/Assets/Game/Editor/GameCapture.cs"));
    }
}
