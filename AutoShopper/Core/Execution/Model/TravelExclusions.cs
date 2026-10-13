using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal enum ExclusionStep : byte
{
    Retry = 0,
    Excluded = 1,
    TravelHalted = 2,
}

internal readonly record struct WorldTravelRecord(uint WorldId, int Failures, TravelFailure LastFailure, bool Excluded);

// The worlds a run gave up travelling to. A world is left out after AttemptsPerWorld failed trips, or after the first
// one when trying again cannot help: Lifestream refused the route, or the game reported the world congested. A data
// center transfer that never starts means data center travel is off in Lifestream, so every world outside the data
// center the character stands on is left out with it. Lifestream busy with someone else's work blocks every world
// alike, so after BusyLimit such trips in a row the run stops travelling instead of trying world after world.
internal sealed class TravelExclusions
{
    public const int AttemptsPerWorld = 2;
    public const int BusyLimit = 2;

    private const uint NoDataCenter = 0;

    private readonly List<WorldTravelRecord> records = [];
    private ImmutableArray<uint> excludedSnapshot = ImmutableArray<uint>.Empty;
    private uint confinedToDataCenterId = NoDataCenter;
    private int busyInARow;

    public int Revision { get; private set; }

    public bool TravelHalted => busyInARow >= BusyLimit;

    // Zero unless data center travel turned out to be off; then the one data center the run can still shop in.
    public uint ConfinedToDataCenterId => confinedToDataCenterId;

    public ImmutableArray<uint> ExcludedWorldIds => excludedSnapshot;

    public static bool ExcludesAtOnce(TravelFailure failure)
        => failure is TravelFailure.Refused or TravelFailure.Congested or TravelFailure.DataCenterTravelOff;

    public ExclusionStep Record(in PlanWorld target, TravelFailure failure, uint currentDataCenterId)
    {
        if (failure == TravelFailure.None)
        {
            return ExclusionStep.Retry;
        }

        Revision++;
        if (failure == TravelFailure.LifestreamBusy)
        {
            busyInARow++;
            return TravelHalted ? ExclusionStep.TravelHalted : ExclusionStep.Retry;
        }

        busyInARow = 0;
        var index = IndexOf(target.WorldId);
        var failures = index < 0 ? 1 : records[index].Failures + 1;
        if (failure == TravelFailure.DataCenterTravelOff && currentDataCenterId != NoDataCenter)
        {
            confinedToDataCenterId = currentDataCenterId;
        }

        var excluded = ExcludesAtOnce(failure) || failures >= AttemptsPerWorld;
        var record = new WorldTravelRecord(target.WorldId, failures, failure, excluded || (index >= 0 && records[index].Excluded));
        if (index < 0)
        {
            records.Add(record);
        }
        else
        {
            records[index] = record;
        }

        if (record.Excluded)
        {
            RefreshSnapshot();
            return ExclusionStep.Excluded;
        }

        return ExclusionStep.Retry;
    }

    // A trip that arrived shows Lifestream free again, so earlier busy trips no longer count toward the halt.
    public void RecordArrival()
    {
        if (busyInARow == 0)
        {
            return;
        }

        busyInARow = 0;
        Revision++;
    }

    public bool IsExcluded(in PlanWorld world)
    {
        if (confinedToDataCenterId != NoDataCenter && world.DataCenterId != confinedToDataCenterId)
        {
            return true;
        }

        var index = IndexOf(world.WorldId);
        return index >= 0 && records[index].Excluded;
    }

    public int Failures(uint worldId)
    {
        var index = IndexOf(worldId);
        return index < 0 ? 0 : records[index].Failures;
    }

    public TravelFailure LastFailure(uint worldId)
    {
        var index = IndexOf(worldId);
        return index < 0 ? TravelFailure.None : records[index].LastFailure;
    }

    // Worlds a journal checkpoint carried over from before a restart; why they were left out is not kept.
    public void Restore(ImmutableArray<uint> worldIds)
    {
        if (worldIds.IsDefaultOrEmpty)
        {
            return;
        }

        for (var index = 0; index < worldIds.Length; index++)
        {
            var worldId = worldIds[index];
            var existing = IndexOf(worldId);
            if (existing >= 0)
            {
                records[existing] = records[existing] with { Excluded = true };
                continue;
            }

            records.Add(new WorldTravelRecord(worldId, AttemptsPerWorld, TravelFailure.None, Excluded: true));
        }

        Revision++;
        RefreshSnapshot();
    }

    private int IndexOf(uint worldId)
    {
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].WorldId == worldId)
            {
                return index;
            }
        }

        return -1;
    }

    private void RefreshSnapshot()
    {
        var count = 0;
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].Excluded)
            {
                count++;
            }
        }

        var worldIds = new uint[count];
        var write = 0;
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].Excluded)
            {
                worldIds[write] = records[index].WorldId;
                write++;
            }
        }

        Array.Sort(worldIds);
        excludedSnapshot = [.. worldIds];
    }
}
