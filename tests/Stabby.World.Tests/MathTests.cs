using System.Text.RegularExpressions;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class MathTests
{
    const double Tol = 1e-12;

    static double Rel(double got, double want) =>
        want == 0 ? Math.Abs(got) : Math.Abs(got - want) / Math.Abs(want);

    static IEnumerable<double> Uniform(int seed, int count, double lo, double hi)
    {
        var r = new Random(seed);
        for (int i = 0; i < count; i++) yield return lo + (hi - lo) * r.NextDouble();
    }

    [Fact]
    public void ExpMatchesMath()
    {
        double worst = 0;
        foreach (double x in Uniform(1, 200000, -50, 50).Concat(Uniform(2, 20000, -700, 700)).Concat(Uniform(3, 20000, -1e-6, 1e-6)))
            worst = Math.Max(worst, Rel(FMath.Exp(x), Math.Exp(x)));
        Assert.True(worst < Tol, $"worst relative error {worst:E3}");
    }

    [Fact]
    public void LogMatchesMath()
    {
        double worst = 0;
        var xs = Uniform(4, 200000, 1e-9, 1e7)
            .Concat(Uniform(5, 50000, 0.5, 2))
            .Concat(Uniform(6, 50000, -300, 300).Select(e => Math.Pow(10, e)))
            .Concat(new[] { double.Epsilon, 1e-310, double.MaxValue });
        foreach (double x in xs)
        {
            double want = Math.Log(x);
            double err = Math.Abs(want) < 1 ? Math.Abs(FMath.Log(x) - want) : Rel(FMath.Log(x), want);
            worst = Math.Max(worst, err);
        }
        Assert.True(worst < Tol, $"worst error {worst:E3}");
    }

    [Fact]
    public void PowMatchesMath()
    {
        double worst = 0;
        var r = new Random(7);
        for (int i = 0; i < 300000; i++)
        {
            double x = i % 3 == 0 ? 1e7 * r.NextDouble() : Math.Pow(10, -6 + 13 * r.NextDouble());
            double y = 3 * r.NextDouble();
            worst = Math.Max(worst, Rel(FMath.Pow(x, y), Math.Pow(x, y)));
        }
        Assert.True(worst < Tol, $"worst relative error {worst:E3}");
    }

    [Fact]
    public void EdgeCases()
    {
        Assert.Equal(1.0, FMath.Exp(0));
        Assert.Equal(0.0, FMath.Exp(-1000));
        Assert.Equal(double.PositiveInfinity, FMath.Exp(1000));
        Assert.True(double.IsNaN(FMath.Exp(double.NaN)));
        Assert.Equal(0.0, FMath.Log(1));
        Assert.Equal(double.NegativeInfinity, FMath.Log(0));
        Assert.True(double.IsNaN(FMath.Log(-1)));
        Assert.Equal(1.0, FMath.Pow(0, 0));
        Assert.Equal(1.0, FMath.Pow(123.4, 0));
        Assert.Equal(0.0, FMath.Pow(0, 0.5));
        Assert.Equal(1.0, FMath.Pow(1, 2.7));
        Assert.Equal(5.5, FMath.Pow(5.5, 1));
        Assert.True(double.IsNaN(FMath.Pow(-2, 0.5)));
        Assert.True(Rel(FMath.Pow(2, 10), 1024) < 1e-15);
        Assert.True(Rel(FMath.Exp(1), Math.E) < 1e-15);
    }

    [Theory]
    [InlineData("exp", -2.5, 0, 4590579268170609701L)]
    [InlineData("exp", 0.1, 0, 4607656066507473108L)]
    [InlineData("exp", 37.25, 0, 4848893437757513620L)]
    [InlineData("log", 3.0, 0, 4607626529066517259L)]
    [InlineData("log", 1e-7, 0, -4598141978574673293L)]
    [InlineData("pow", 1234.5, 0.5, 4630141692601755491L)]
    [InlineData("pow", 0.37, 2.2, 4592750230999131572L)]
    [InlineData("pow", 9e6, 1.7, 4783759246101277291L)]
    public void GoldenBits(string fn, double x, double y, long bits)
    {
        double v = fn == "exp" ? FMath.Exp(x) : fn == "log" ? FMath.Log(x) : FMath.Pow(x, y);
        Assert.Equal(bits, BitConverter.DoubleToInt64Bits(v));
    }

    [Fact]
    public void WorldSourceUsesOnlyFMathTranscendentals()
    {
        var root = Path.GetDirectoryName(ContentLoader.FindContentRoot(AppContext.BaseDirectory))!;
        var dir = Path.Combine(root, "src", "Stabby.World");
        var files = Directory.GetFiles(dir, "*.cs");
        Assert.NotEmpty(files);
        var banned = new Regex(@"\bMathF?\.(Pow|Exp|Log)");
        foreach (var f in files)
        {
            if (Path.GetFileName(f) == "FMath.cs") continue;
            var m = banned.Match(File.ReadAllText(f));
            Assert.False(m.Success, $"{Path.GetFileName(f)} uses {m.Value}; use FMath");
        }
    }
}
