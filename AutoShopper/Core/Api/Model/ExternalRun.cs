using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Api.Model;

internal sealed class ExternalRun
{
    private ExternalRun()
    {
    }

    public required ShoppingList List { get; init; }

    public required long MaxSpend { get; init; }

    public required bool ReturnHome { get; init; }

    public required string Caller { get; init; }

    public required string CallerDisplayName { get; init; }

    public required string? CallerRunId { get; init; }

    public static ExternalRun From(RunRequest request, DateTime nowUtc)
    {
        var caller = request.Caller!;
        var displayName = ApiRequests.DisplayName(caller, request.CallerDisplayName);
        var label = ShoppingLimits.CleanName(request.Label, ShoppingLimits.MaximumListNameLength);
        var list = ShoppingList.Create(label.Length > 0 ? label : displayName, nowUtc);
        var lines = request.Lines!;
        list.Lines.Capacity = lines.Length;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var shoppingLine = ShoppingLine.Create(line.ItemId, line.Quantity, line.Quality, QuantityMode.Buy);
            shoppingLine.MaxUnitPrice = line.MaxUnitPrice;
            shoppingLine.ExcessAllowance = line.ExcessAllowance;
            list.Lines.Add(shoppingLine);
        }

        return new ExternalRun
        {
            List = list,
            MaxSpend = request.MaxSpend,
            ReturnHome = request.ReturnHome,
            Caller = caller,
            CallerDisplayName = displayName,
            CallerRunId = request.CallerRunId,
        };
    }

    public RunCaller ToRunCaller() => new(Caller, CallerDisplayName, ReturnHome);

    public RunStatusLine[] StatusLines(RunTally tally)
    {
        var lines = List.Lines;
        var status = new RunStatusLine[lines.Count];
        for (var index = 0; index < status.Length; index++)
        {
            var line = lines[index];
            var acquired = tally.Acquired(new Planning.DemandKey(line.ItemId, DemandBuilder.ToDemandQuality(line.Quality)));
            status[index] = new RunStatusLine(line.ItemId, line.Quality, line.Quantity, acquired, Math.Max(0, line.Quantity - acquired));
        }

        return status;
    }

    public static RunStatusIssue[] StatusIssues(IReadOnlyList<RunIssue> issues)
    {
        var status = new RunStatusIssue[issues.Count];
        for (var index = 0; index < status.Length; index++)
        {
            status[index] = new RunStatusIssue(issues[index].Reason, issues[index].ItemId);
        }

        return status;
    }
}
