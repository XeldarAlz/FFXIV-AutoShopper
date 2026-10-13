namespace AutoShopper.Core.Execution.Model;

internal readonly record struct AfterRunFacts
{
    public required bool CompletedByStopCondition { get; init; }

    public required bool EndedWithFault { get; init; }

    public required bool LoggedIn { get; init; }

    public required bool DidWork { get; init; }

    public required bool DryRun { get; init; }

    public required bool ReturnHome { get; init; }

    // The home world is known and the character stands on another one.
    public required bool AwayFromHome { get; init; }

    // False when the way home needs a route the settings keep off.
    public required bool WayHomeAllowed { get; init; }

    public required AfterRunAction Action { get; init; }
}

internal readonly record struct AfterRunChoice(bool WayHome, AfterRunAction? Action, bool ActionSkippedForDryRun)
{
    public static readonly AfterRunChoice Nothing = new(false, null, false);

    public bool HasWork => WayHome || Action is not null;
}

// What follows a run that ended on its own: the way home, then the after-run action. A Stop, a fault or a logout runs
// neither. A dry run still goes home when asked, but never logs out or closes the game, since nothing was bought.
internal static class AfterRunPolicy
{
    public static AfterRunChoice Decide(in AfterRunFacts facts)
    {
        if (!facts.CompletedByStopCondition || facts.EndedWithFault || !facts.LoggedIn)
        {
            return AfterRunChoice.Nothing;
        }

        var wayHome = facts.ReturnHome && facts.AwayFromHome && facts.WayHomeAllowed;
        if (facts.Action == AfterRunAction.StayLoggedIn || !facts.DidWork)
        {
            return new AfterRunChoice(wayHome, null, false);
        }

        if (facts.DryRun && facts.Action is AfterRunAction.Logout or AfterRunAction.CloseGame)
        {
            return new AfterRunChoice(wayHome, null, true);
        }

        return new AfterRunChoice(wayHome, facts.Action, false);
    }
}
