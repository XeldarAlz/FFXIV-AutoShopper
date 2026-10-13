using System.Globalization;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Tasks;
using AutoShopper.Core.Travel;

namespace AutoShopper.Windows.Sections;

internal readonly record struct RunLineView(string Name, uint IconId, bool HighQuality, string? Quality, string Progress, string Spent, float Fraction, bool Satisfied);

internal readonly record struct RunIssueView(string Title, string Detail, bool Blocking);

internal enum RouteChipState : byte
{
    Done = 0,
    Current = 1,
    Next = 2,
    Later = 3,
    Skipped = 4,
}

internal readonly record struct RouteChipView(string Label, RouteChipState State, string Tooltip);

// The running panel's text for one state of the run, rebuilt only when the run, its tally or the language changes,
// so the panel draws every frame without building strings.
internal sealed class RunView
{
    private RunView()
    {
    }

    public required ShopRunState Run { get; init; }

    public required int Revision { get; init; }

    public required int TallyRevision { get; init; }

    public required LanguageInfo Language { get; init; }

    public required bool WithNames { get; init; }

    public required string SpentLabel { get; init; }

    public required string SpentValue { get; init; }

    public required string? SpentSub { get; init; }

    public required string BudgetValue { get; init; }

    public required string? BudgetSub { get; init; }

    public required RunLineView[] Lines { get; init; }

    public required RunIssueView[] Issues { get; init; }

    public required RouteChipView[] Route { get; init; }

    // One line per skipped stop: the world and why.
    public required string[] SkippedNotes { get; init; }

    // Null once a finished run had no way home to report.
    public required string? ReturnHome { get; init; }

    public required bool ReturnHomeWanted { get; init; }

    public bool IsCurrent(ShopRunState run, bool withNames)
        => ReferenceEquals(Run, run)
        && Revision == run.Revision
        && TallyRevision == run.Tally.Revision
        && ReferenceEquals(Language, Loc.Current)
        && WithNames == withNames
        && ReturnHomeWanted == run.ReturnsHome(Plugin.Instance.Configuration);

    public static RunView Build(ShopRunState run, bool withNames)
    {
        var spend = run.Spend;
        var spent = run.DryRun ? run.Tally.WouldBuySpend : run.Tally.ConfirmedSpend;
        return new RunView
        {
            Run = run,
            Revision = run.Revision,
            TallyRevision = run.Tally.Revision,
            Language = Loc.Current,
            WithNames = withNames,
            SpentLabel = Loc.T(run.DryRun ? L.Run.TileWouldSpend : L.Run.TileSpent),
            SpentValue = Loc.Gil(spent),
            SpentSub = spend.Reserved > 0 ? Loc.T(L.Run.TileReserved, Loc.Gil(spend.Reserved)) : null,
            BudgetValue = run.IsAuthorized ? Loc.Gil(spend.Remaining) : "-",
            BudgetSub = run.LastGil >= 0 ? Loc.T(L.Run.TilePurse, Loc.Gil(run.LastGil)) : null,
            Lines = BuildLines(run),
            Issues = BuildIssues(run),
            Route = BuildRoute(run),
            SkippedNotes = BuildSkippedNotes(run),
            ReturnHome = ReturnHomeText(run, run.ReturnsHome(Plugin.Instance.Configuration)),
            ReturnHomeWanted = run.ReturnsHome(Plugin.Instance.Configuration),
        };
    }

    private static RouteChipView[] BuildRoute(ShopRunState run)
    {
        if (run.Route is not { } route)
        {
            return [];
        }

        var next = route.NextIndex();
        var following = route.FollowingIndex();
        var chips = new RouteChipView[route.Count];
        for (var index = 0; index < chips.Length; index++)
        {
            var entry = route[index];
            var state = entry.Status switch
            {
                RouteStatus.Done => RouteChipState.Done,
                RouteStatus.Skipped => RouteChipState.Skipped,
                _ when index == next => RouteChipState.Current,
                _ when index == following => RouteChipState.Next,
                _ => RouteChipState.Later,
            };
            chips[index] = new RouteChipView(entry.World.Name, state, ChipTooltip(entry, state));
        }

        return chips;
    }

    private static string ChipTooltip(in RouteEntry entry, RouteChipState state) => state switch
    {
        RouteChipState.Done => Loc.T(L.Route.StatusDone),
        RouteChipState.Current => Loc.T(L.Route.StatusCurrent),
        RouteChipState.Next => Loc.T(L.Route.StatusNext),
        RouteChipState.Later => Loc.T(L.Route.StatusLater),
        _ => Loc.T(L.Route.LabelValue, Loc.T(L.Route.StatusSkipped), SkipReason(entry)),
    };

    private static string[] BuildSkippedNotes(ShopRunState run)
    {
        if (run.Route is not { } route)
        {
            return [];
        }

        var notes = new List<string>();
        for (var index = 0; index < route.Count; index++)
        {
            var entry = route[index];
            if (entry.Status == RouteStatus.Skipped)
            {
                notes.Add(Loc.T(L.Route.LabelValue, entry.World.Name, SkipReason(entry)));
            }
        }

        return [.. notes];
    }

    public static string SkipReason(in RouteEntry entry) => entry.Skip switch
    {
        RouteSkip.NotNeeded => Loc.T(L.Route.SkipNotNeeded),
        RouteSkip.StopLimit => Loc.T(L.Route.SkipStopLimit),
        RouteSkip.TravelHalted => Loc.T(L.Route.SkipHalted),
        _ => FailureLabel(entry.Failure),
    };

    public static string FailureLabel(TravelFailure failure) => failure switch
    {
        TravelFailure.Refused => Loc.T(L.Route.FailRefused),
        TravelFailure.LifestreamBusy => Loc.T(L.Route.FailBusy),
        TravelFailure.DataCenterTravelOff => Loc.T(L.Route.FailDataCenterOff),
        TravelFailure.Congested => Loc.T(L.Route.FailCongested),
        TravelFailure.TimedOut => Loc.T(L.Route.FailTimedOut),
        TravelFailure.ArrivalUnconfirmed => Loc.T(L.Route.FailUnconfirmed),
        TravelFailure.Blocked => Loc.T(L.Route.FailBlocked),
        _ => Loc.T(L.Route.FailAbandoned),
    };

    private static string? ReturnHomeText(ShopRunState run, bool wanted)
    {
        var label = run.ReturnHome switch
        {
            ReturnHomeResult.AlreadyHome => Loc.T(L.Route.HomeAlready),
            ReturnHomeResult.Underway => Loc.T(L.Route.HomeUnderway),
            ReturnHomeResult.Arrived => Loc.T(L.Route.HomeArrived),
            ReturnHomeResult.Failed => Loc.T(L.Route.HomeFailed),
            ReturnHomeResult.Skipped => Loc.T(L.Route.HomeSkipped),
            _ when run.FinalState is not null => null,
            _ => Loc.T(wanted ? L.Route.HomePending : L.Route.HomeOff),
        };
        return label is null ? null : Loc.T(L.Route.LabelValue, Loc.T(L.Route.ReturnHome), label);
    }

    private static RunLineView[] BuildLines(ShopRunState run)
    {
        var tally = run.Tally;
        var items = Plugin.Instance.Items;
        var lines = new RunLineView[tally.Count];
        for (var index = 0; index < lines.Length; index++)
        {
            var line = tally.LineAt(index);
            var demand = line.Demand;
            var found = items.TryGet(demand.ItemId, out var item);
            var name = found ? item.Name : demand.ItemId.ToString(CultureInfo.InvariantCulture);
            var required = Math.Max(1, demand.RequiredQuantity);
            lines[index] = new RunLineView(
                name,
                found ? item.IconId : 0,
                demand.Quality == DemandQuality.HighQuality,
                QualityLabel(demand.Quality),
                Loc.T(L.Run.LineProgress, line.Acquired.ToString("N0", Loc.Culture), demand.RequiredQuantity.ToString("N0", Loc.Culture)),
                Loc.Gil(line.Spent),
                Math.Clamp(line.Acquired / (float)required, 0f, 1f),
                line.Satisfied);
        }

        return lines;
    }

    private static string? QualityLabel(DemandQuality quality) => quality switch
    {
        DemandQuality.HighQuality => Loc.T(L.Lists.QualityHigh),
        DemandQuality.NormalQuality => Loc.T(L.Lists.QualityNormal),
        _ => null,
    };

    private static RunIssueView[] BuildIssues(ShopRunState run)
    {
        var issues = run.Issues;
        var views = new RunIssueView[issues.Count];
        var items = Plugin.Instance.Items;
        for (var index = 0; index < views.Length; index++)
        {
            var issue = issues[index];
            var title = ReasonLabel(issue.Reason);
            var parts = new List<string>(3);
            if (issue.ItemId != 0)
            {
                parts.Add(items.TryGetName(issue.ItemId, out var name) ? name : issue.ItemId.ToString(CultureInfo.InvariantCulture));
            }

            if (issue.WorldId != 0 && Worlds.TryFindById(issue.WorldId, out var world))
            {
                parts.Add(Loc.T(L.Issues.OnWorld, world.Name));
            }

            if (issue.Quantity > 0)
            {
                parts.Add(Loc.Plural(L.Issues.Missing, issue.Quantity));
            }

            views[index] = new RunIssueView(title, string.Join(TextDraw.Separator, parts), IsBlocking(issue.Reason));
        }

        return views;
    }

    private static bool IsBlocking(RunIssueReason reason)
        => reason is RunIssueReason.UncertainPurchase or RunIssueReason.JournalWriteFailed or RunIssueReason.UnsupportedGameVersion
            or RunIssueReason.InventoryFull or RunIssueReason.InsufficientGil or RunIssueReason.CharacterChanged or RunIssueReason.BoardUnreachable;

    public static string ReasonLabel(RunIssueReason reason) => reason switch
    {
        RunIssueReason.PriceCapExceeded => Loc.T(L.Issues.PriceCapExceeded),
        RunIssueReason.NoStock => Loc.T(L.Issues.NoStock),
        RunIssueReason.InventoryFull => Loc.T(L.Issues.InventoryFull),
        RunIssueReason.InsufficientGil => Loc.T(L.Issues.InsufficientGil),
        RunIssueReason.TravelFailed => Loc.T(L.Issues.TravelFailed),
        RunIssueReason.DependencyBusy => Loc.T(L.Issues.DependencyBusy),
        RunIssueReason.UncertainPurchase => Loc.T(L.Issues.UncertainPurchase),
        RunIssueReason.UnsupportedGameVersion => Loc.T(L.Issues.UnsupportedGameVersion),
        RunIssueReason.BudgetReached => Loc.T(L.Issues.BudgetReached),
        RunIssueReason.SearchFailed => Loc.T(L.Issues.SearchFailed),
        RunIssueReason.RetriesExhausted => Loc.T(L.Issues.RetriesExhausted),
        RunIssueReason.ListingRejected => Loc.T(L.Issues.ListingRejected),
        RunIssueReason.UniqueOwned => Loc.T(L.Issues.UniqueOwned),
        RunIssueReason.BoardUnreachable => Loc.T(L.Issues.BoardUnreachable),
        RunIssueReason.JournalWriteFailed => Loc.T(L.Issues.JournalWriteFailed),
        RunIssueReason.CharacterChanged => Loc.T(L.Issues.CharacterChanged),
        RunIssueReason.CrystalLimit => Loc.T(L.Issues.CrystalLimit),
        _ => Loc.T(L.Issues.NotMarketable),
    };
}
