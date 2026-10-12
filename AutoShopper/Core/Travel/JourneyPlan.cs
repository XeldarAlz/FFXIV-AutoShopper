namespace AutoShopper.Core.Travel;

// Where a journey ends: a world, and on it an optional aetheryte (0 for none) and instance (0 for any). It is written
// to the configuration before a data center transfer, because Lifestream logs the character out to make one.
public readonly record struct JourneyPlan(string World, uint AetheryteId, uint TerritoryId, int Instance, bool ReturnTrip, DateTime StartedAtUtc)
{
    public static JourneyPlan ToWorld(string world, uint aetheryteId, uint territoryId, int instance)
        => new(world, aetheryteId, territoryId, instance, ReturnTrip: false, DateTime.UtcNow);

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
}
