namespace AutoShopper.Core.Execution.Model;

// Why a trip to a stop's world did not end on that world.
internal enum TravelFailure : byte
{
    None = 0,
    // Lifestream cannot take the character there, or it is not loaded.
    Refused = 1,
    // Lifestream stayed busy with work this run did not start.
    LifestreamBusy = 2,
    // A data center transfer never started, which is what Lifestream does while its data center travel is off.
    DataCenterTravelOff = 3,
    Congested = 4,
    TimedOut = 5,
    // Lifestream went idle on another world, or a hop never started.
    Abandoned = 6,
    // The character reached the world but it never settled: still loading, or Lifestream kept working.
    ArrivalUnconfirmed = 7,
    // A party, a duty, a trade or an open window kept the transfer from starting.
    Blocked = 8,
}
