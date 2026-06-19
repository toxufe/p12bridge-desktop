using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNowReturnsCurrentUtcTime()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var now = new SystemClock().UtcNow;
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(now, before, after);
    }
}
