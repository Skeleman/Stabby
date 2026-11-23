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

    static List<string> Colours(string body) =>
        Linter.ColourProblems(Linter.KitShader, "float3 Other() { return float3(0.1, 0.2, 0.3); }\nfloat3 Built(float3 a)\n{\n" + body + "\n}\nfloat3 After() { return float3(0.4, 0.5, 0.6); }");

    [Theory]
    [InlineData("return ToLinear(float3(0.42, 0.37, 0.30));")]
    [InlineData("float3 c = a.x < 0.5 ? float3(0.5, 0.4, 0.3) : a; return c;")]
    [InlineData("return lerp(a, float3(0.2f, 0.17f, 0.14f), 0.5);")]
    public void FlagsBuiltInColours(string body) => Assert.Single(Colours(body));

    [Theory]
    [InlineData("return ToLinear(_StabbyKitSrgb[3].rgb) * float3(0.95, 1.05, 0.9);")]
    [InlineData("return lerp(a, dot(a, float3(0.2126, 0.7152, 0.0722)), 0.2);")]
    [InlineData("if (a.x > 0) { a = a * float3(0.5, 0.47, 0.43); } return a;")]
    public void IgnoresKitColoursAndWeights(string body) => Assert.Empty(Colours(body));

    [Fact]
    public void MissingKitFunctionIsAProblem() => Assert.Single(Linter.ColourProblems(Linter.KitShader, "float3 Other() { return 0; }"));
}
