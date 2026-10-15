using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class InputOwnershipTests
{
    [Fact]
    public void Starts_unavailable()
    {
        var route = new InputOwnershipManager();
        Assert.Equal(InputRouteStatus.Unavailable, route.Status);
    }

    [Fact]
    public void Active_only_after_a_masked_axis()
    {
        var route = new InputOwnershipManager();
        route.Begin();
        Assert.Equal(InputRouteStatus.Initializing, route.Status);
        route.NoteMaskedAxis();
        Assert.Equal(InputRouteStatus.Active, route.Status);
    }

    [Fact]
    public void Bypass_and_fault_are_explicit()
    {
        var route = new InputOwnershipManager();
        route.Bypass("Master off");
        Assert.Equal(InputRouteStatus.Bypassed, route.Status);
        route.Fault("write failed");
        Assert.Equal(InputRouteStatus.Fault, route.Status);
    }
}
