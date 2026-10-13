using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Tests;

public sealed class ExternalRunTests
{
    private const uint Ore = 5_057;
    private const uint Cloth = 5_058;

    private static readonly DateTime Now = new(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryLineIsBoughtAsAFixedQuantityWithItsLimits()
    {
        var external = ExternalRun.From(Request(), Now);

        var lines = external.List.Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal(Ore, lines[0].ItemId);
        Assert.Equal(ItemQuality.NormalQuality, lines[0].Quality);
        Assert.Equal(QuantityMode.Buy, lines[0].Mode);
        Assert.Equal(3, lines[0].Quantity);
        Assert.Equal(700, lines[0].MaxUnitPrice);
        Assert.Equal(3, lines[0].ExcessAllowance);
        Assert.Equal(ItemQuality.HighQuality, lines[1].Quality);
        Assert.NotEqual(lines[0].Id, lines[1].Id);
        Assert.Equal(150_000, external.MaxSpend);
        Assert.True(external.ReturnHome);
        Assert.Equal("0d7f", external.CallerRunId);
    }

    [Fact]
    public void TheListIsNamedByTheLabelOrElseByTheCaller()
    {
        var labelled = ExternalRun.From(Request(), Now);
        var unlabelled = ExternalRun.From(Request() with { Label = "  " }, Now);
        var unnamed = ExternalRun.From(Request() with { Label = null, CallerDisplayName = null }, Now);

        Assert.Equal("GC supply 2026-10-13", labelled.List.Name);
        Assert.Equal("Auto GC Supply", unlabelled.List.Name);
        Assert.Equal("AutoGcSupply", unnamed.List.Name);
        Assert.Equal("AutoGcSupply", unnamed.CallerDisplayName);
        Assert.Equal("AutoGcSupply", unnamed.Caller);
    }

    [Fact]
    public void TheRunRemembersWhoStartedItAndItsWayHome()
    {
        var external = ExternalRun.From(Request(), Now);

        Assert.Equal(new RunCaller("AutoGcSupply", "Auto GC Supply", ReturnHome: true), external.ToRunCaller());
        Assert.False(ExternalRun.From(Request() with { ReturnHome = false }, Now).ToRunCaller().ReturnHome);
    }

    [Fact]
    public void TheListIsNeverOneOfThePlayersLists()
    {
        var external = ExternalRun.From(Request(), Now);

        Assert.NotEqual(Guid.Empty, external.List.Id);
        Assert.False(external.List.Archived);
        Assert.Equal(Now, external.List.CreatedAtUtc);
    }

    [Fact]
    public void StatusLinesFollowTheRequestBeforeAnythingIsBought()
    {
        var external = ExternalRun.From(Request(), Now);

        var lines = external.StatusLines(new RunTally([]));

        Assert.Equal(
            [new RunStatusLine(Ore, ItemQuality.NormalQuality, 3, 0, 3), new RunStatusLine(Cloth, ItemQuality.HighQuality, 2, 0, 2)],
            lines);
    }

    [Fact]
    public void StatusLinesCountConfirmedAndWouldBuyUnits()
    {
        var external = ExternalRun.From(Request(), Now);
        var ore = PlanDemand.ForPurchase(Ore, DemandQuality.NormalQuality, 3, 3, 700);
        var cloth = PlanDemand.ForPurchase(Cloth, DemandQuality.HighQuality, 2);
        var tally = new RunTally([ore, cloth]);
        tally.RecordConfirmed(ore.Key, "game:1", 5, 3_000);
        tally.RecordWouldBuy(cloth.Key, "game:2", 1, 900);

        var lines = external.StatusLines(tally);

        Assert.Equal(new RunStatusLine(Ore, ItemQuality.NormalQuality, 3, 5, 0), lines[0]);
        Assert.Equal(new RunStatusLine(Cloth, ItemQuality.HighQuality, 2, 1, 1), lines[1]);
    }

    [Fact]
    public void StatusIssuesKeepTheReasonAndTheItem()
    {
        var issues = new List<RunIssue>
        {
            new(RunIssueReason.NoStock, Cloth, 40, 2, Now),
            new(RunIssueReason.UncertainPurchase, Ore, 41, 3, Now),
        };

        Assert.Equal(
            [new RunStatusIssue(RunIssueReason.NoStock, Cloth), new RunStatusIssue(RunIssueReason.UncertainPurchase, Ore)],
            ExternalRun.StatusIssues(issues));
    }

    private static RunRequest Request() => new()
    {
        Schema = ApiContract.Schema,
        Caller = "AutoGcSupply",
        CallerDisplayName = "Auto GC Supply",
        CallerRunId = "0d7f",
        Label = "GC supply 2026-10-13",
        MaxSpend = 150_000,
        ReturnHome = true,
        Lines =
        [
            new RunRequestLine(Ore, ItemQuality.NormalQuality, 3, 700, 3),
            new RunRequestLine(Cloth, ItemQuality.HighQuality, 2, 0, 0),
        ],
    };
}
