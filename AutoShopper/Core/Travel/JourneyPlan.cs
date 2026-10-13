namespace AutoShopper.Core.Travel;

// Where a journey ends: a world, and on it an optional aetheryte (0 for none) and instance (0 for any). It is written
// to the configuration before a data center transfer, because Lifestream logs the character out to make one, with
// the character it belongs to (0 when not known). Gateway is the Lifestream city the world change happens at, 0 to
// leave the choice to Lifestream.
public readonly record struct JourneyPlan(string World, uint AetheryteId, uint TerritoryId, int Instance, bool ReturnTrip, DateTime StartedAtUtc, int Gateway = 0, ulong CharacterContentId = 0)
{
    public static JourneyPlan ToWorld(string world, uint aetheryteId, uint territoryId, int instance)
        => new(world, aetheryteId, territoryId, instance, ReturnTrip: false, DateTime.UtcNow);

    public static JourneyPlan ToWorldThrough(string world, int gateway)
        => new(world, 0, 0, 0, ReturnTrip: false, DateTime.UtcNow, gateway);

    public static JourneyPlan Home(string world)
        => new(world, 0, 0, 0, ReturnTrip: true, DateTime.UtcNow);
}

public enum JourneyOutcome : byte
{
    Arrived,
    Cancelled,
    Refused,
    WorldUnreached,
    AetheryteUnreached,
    InstanceUnreached,
    // The data center transfer did not get there before the journey's deadline.
    Overdue,
    // Lifestream stayed busy with work the journey did not start, so no hop was asked for.
    LifestreamBusy,
    // Lifestream took the request but never started the hop.
    NotStarted,
    // The game reported the target world congested.
    Congested,
    // The hop did not reach the world within its budget.
    TimedOut,
    // A party, a duty, a trade or an open window kept a data center transfer from starting.
    Blocked,
}
