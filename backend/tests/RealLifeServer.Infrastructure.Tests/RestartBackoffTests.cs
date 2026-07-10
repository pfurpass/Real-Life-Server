using RealLifeServer.Infrastructure.Streaming;
using Xunit;

namespace RealLifeServer.Infrastructure.Tests;

/// <summary>Item 10: no restart-every-2-seconds loop, growth is bounded.</summary>
public class RestartBackoffTests
{
    [Fact]
    public void ZeroFailures_NoDelay()
    {
        Assert.Equal(TimeSpan.Zero, RestartBackoff.DelayFor(0));
    }

    [Fact]
    public void NegativeFailures_TreatedAsZero()
    {
        Assert.Equal(TimeSpan.Zero, RestartBackoff.DelayFor(-1));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    public void DelayGrowsWithConsecutiveFailures(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RestartBackoff.DelayFor(failures));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void DelayNeverExceedsTheConfiguredMaximum(int failures)
    {
        Assert.Equal(RestartBackoff.MaxDelay, RestartBackoff.DelayFor(failures));
    }

    [Fact]
    public void DelaysAreStrictlyIncreasingUpToTheCap()
    {
        var previous = TimeSpan.Zero;
        for (var i = 1; i <= 5; i++)
        {
            var current = RestartBackoff.DelayFor(i);
            Assert.True(current > previous, $"Delay for {i} failures ({current}) should exceed delay for {i - 1} ({previous})");
            previous = current;
        }
    }
}
