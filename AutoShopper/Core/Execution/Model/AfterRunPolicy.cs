namespace AutoShopper.Core.Execution.Model;

internal readonly record struct AfterRunFacts
{
    public required bool CompletedByStopCondition { get; init; }

    public required bool EndedWithFault { get; init; }

    public required bool LoggedIn { get; init; }

    public required bool DidWork { get; init; }

    public required bool ReturnHome { get; init; }

    // The home world is known and the character stands on another one.
    public required bool AwayFromHome { get; init; }

    // False when the way home needs a route the settings keep off.
    public required bool WayHomeAllowed { get; init; }

    public required AfterRunAction Action { get; init; }

    // The player listed chat commands for after a run, and the run is theirs rather than an API caller's.
    public required bool HasChatCommands { get; init; }
}

// Action is StayLoggedIn when the after-run step only sends the player's chat commands.
internal readonly record struct AfterRunChoice(bool WayHome, AfterRunAction? Action, bool ChatCommands)
{
    public static readonly AfterRunChoice Nothing = new(false, null, false);

    public bool HasWork => WayHome || Action is not null;
}

// What follows a run that ended on its own: the way home, then the after-run step, the player's chat commands ahead of
// the after-run action. A Stop, a fault or a logout runs none of them.
internal static class AfterRunPolicy
{
    public static AfterRunChoice Decide(in AfterRunFacts facts)
    {
        if (!facts.CompletedByStopCondition || facts.EndedWithFault || !facts.LoggedIn)
        {
            return AfterRunChoice.Nothing;
        }

        var wayHome = facts.ReturnHome && facts.AwayFromHome && facts.WayHomeAllowed;
        if (!facts.DidWork || (facts.Action == AfterRunAction.StayLoggedIn && !facts.HasChatCommands))
        {
            return new AfterRunChoice(wayHome, null, false);
        }

        return new AfterRunChoice(wayHome, facts.Action, facts.HasChatCommands);
    }
}
