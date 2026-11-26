using Stabby.Game;

namespace Stabby.World.Tests;

public class PoolSizeTests
{
    [Fact]
    public void EmptyPoolsTrimAtOnceAndShrunkPoolsWait()
    {
        Assert.True(PoolSize.TrimDue(150000, 0, 0, PoolSize.TrimSeconds));
        Assert.Equal(64, PoolSize.Trimmed(0));
        Assert.False(PoolSize.TrimDue(150000, 2000, 0.1f, PoolSize.TrimSeconds));
        Assert.True(PoolSize.TrimDue(150000, 2000, PoolSize.TrimSeconds, PoolSize.TrimSeconds));
        Assert.Equal(3000, PoolSize.Trimmed(2000));
    }

    [Fact]
    public void TrimmedPoolsDoNotTrimAgainUntilTheyShrinkFurther()
    {
        for (int need = 0; need < 100000; need += 997)
        {
            int held = PoolSize.Trimmed(need);
            Assert.True(held >= need);
            Assert.False(PoolSize.Oversized(held, need));
            Assert.False(PoolSize.Oversized(held, held / 2));
        }
    }
}
