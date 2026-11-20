using Stabby.Lint;

namespace Stabby.World.Tests;

public class LintTests
{
    [Fact]
    public void RepoPassesLint() => Assert.Empty(Linter.Check(Linter.FindRepo(AppContext.BaseDirectory)));
}
