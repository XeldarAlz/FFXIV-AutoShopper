using AutoShopper.Core.Travel;

namespace AutoShopper.Tests;

public sealed class JourneyPlanTests
{
    private const string HomeWorld = "Cerberus";
    private const int LimsaGateway = 8;

    [Fact]
    public void TheWayHomeChangesWorldsThroughItsGateway()
    {
        var plan = JourneyPlan.Home(HomeWorld, LimsaGateway);

        Assert.True(plan.ReturnTrip);
        Assert.Equal(HomeWorld, plan.World);
        Assert.Equal(LimsaGateway, plan.Gateway);
        Assert.Equal(0u, plan.AetheryteId);
        Assert.Equal(0, plan.Instance);
    }
}
