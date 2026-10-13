using System.Globalization;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Tasks;
using AutoShopper.Core.Travel;

namespace AutoShopper.Windows.Sections;

internal readonly record struct RunLineView(string Name, uint IconId, bool HighQuality, string? Quality, string Progress, string Spent, float Fraction, bool Satisfied);

internal readonly record struct RunIssueView(string Title, string Detail, bool Blocking);

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

    public bool IsCurrent(ShopRunState run, bool withNames)
        => ReferenceEquals(Run, run)
        && Revision == run.Revision
        && TallyRevision == run.Tally.Revision
        && ReferenceEquals(Language, Loc.Current)
        && WithNames == withNames;

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
        };
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
        RunIssueReason.TravelNotYetSupported => Loc.T(L.Issues.TravelNotYetSupported),
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
        _ => Loc.T(L.Issues.NotMarketable),
    };
}
