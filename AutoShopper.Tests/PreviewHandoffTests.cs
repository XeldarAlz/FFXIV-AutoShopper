using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;

namespace AutoShopper.Tests;

public sealed class PreviewHandoffTests
{
    [Fact]
    public void AnOlderWorkerFinishingLateNeitherPublishesNorEndsTheNewerPlanning()
    {
        var handoff = new PreviewHandoff();

        handoff.Complete(Result(1));

        Assert.Equal(PlanningProgress.Running, handoff.Take(2, out var result));
        Assert.Null(result);
        Assert.Equal(PlanningProgress.Running, handoff.Take(2, out _));
    }

    [Fact]
    public void TheNewestGenerationsResultIsAdoptedOnce()
    {
        var handoff = new PreviewHandoff();
        var newest = Result(2);

        handoff.Complete(newest);

        Assert.Equal(PlanningProgress.Planned, handoff.Take(2, out var result));
        Assert.Same(newest, result);
        Assert.Equal(PlanningProgress.Running, handoff.Take(2, out _));
    }

    [Fact]
    public void AnOlderResultNeverReplacesANewerOneWaiting()
    {
        var handoff = new PreviewHandoff();
        var newest = Result(3);

        handoff.Complete(newest);
        handoff.Complete(Result(2));

        Assert.Equal(PlanningProgress.Planned, handoff.Take(3, out var result));
        Assert.Same(newest, result);
    }

    [Fact]
    public void OnlyTheNewestGenerationsFailureEndsPlanning()
    {
        var handoff = new PreviewHandoff();

        handoff.Fail(1);
        Assert.Equal(PlanningProgress.Running, handoff.Take(2, out _));

        handoff.Fail(2);
        handoff.Fail(1);
        Assert.Equal(PlanningProgress.Failed, handoff.Take(2, out var result));
        Assert.Null(result);
    }

    [Fact]
    public async Task WorkersRacingKeepTheNewestResult()
    {
        var handoff = new PreviewHandoff();
        var results = new PreviewResult[64];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = Result(index + 1);
        }

        using var start = new ManualResetEventSlim();
        var workers = new Task[results.Length];
        for (var index = 0; index < workers.Length; index++)
        {
            var result = results[results.Length - 1 - index];
            workers[index] = Task.Run(() =>
            {
                start.Wait();
                handoff.Complete(result);
            });
        }

        start.Set();
        await Task.WhenAll(workers);

        Assert.Equal(PlanningProgress.Planned, handoff.Take(results.Length, out var adopted));
        Assert.Same(results[^1], adopted);
    }

    private static PreviewResult Result(long generation)
    {
        return new PreviewResult
        {
            Input = new PreviewInput
            {
                Generation = generation,
                ListId = Guid.Empty,
                Demands = DemandSet.Empty,
                Prices = null,
                Worlds = ImmutableArray<PlanWorld>.Empty,
                Settings = new PlanSettings { CurrentWorld = default, HomeWorld = default },
            },
            Plans = ImmutableArray<PurchasePlan>.Empty,
            PlannedAtUtc = default,
        };
    }
}
