using HeavyPro.Core.Physics;
using Xunit;

namespace HeavyPro.Core.Tests;

public class ControlLimitsTests
{
    [Fact]
    public void Locked_by_default()
    {
        Assert.False(ControlLimits.IsUnlockedAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Unlocked_only_until_the_given_lease()
    {
        var now = DateTimeOffset.UtcNow;
        ControlLimits.Unlock(now.AddMinutes(10));
        Assert.True(ControlLimits.IsUnlockedAt(now));
        Assert.True(ControlLimits.IsUnlockedAt(now.AddMinutes(9)));
        Assert.False(ControlLimits.IsUnlockedAt(now.AddMinutes(10)));
        Assert.False(ControlLimits.IsUnlockedAt(now.AddMinutes(11)));
    }

    [Fact]
    public void Lock_turns_it_off_immediately_not_only_at_expiry()
    {
        ControlLimits.Unlock(DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(ControlLimits.IsUnlockedAt(DateTimeOffset.UtcNow));
        ControlLimits.Lock();
        Assert.False(ControlLimits.IsUnlockedAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_fresh_unlock_replaces_an_older_lease()
    {
        var now = DateTimeOffset.UtcNow;
        ControlLimits.Unlock(now.AddMinutes(5));
        ControlLimits.Unlock(now.AddHours(1));
        Assert.True(ControlLimits.IsUnlockedAt(now.AddMinutes(30)));
    }
}
