using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal enum AuthorizationProblem : byte
{
    None = 0,
    MissingRunId = 1,
    MissingCharacter = 2,
    MissingHomeWorld = 3,
    MissingSpendCap = 4,
    NegativeReserve = 5,
    NoDemand = 6,
    InvalidDemand = 7,
    DuplicateDemand = 8,
    NoAllowedWorlds = 9,
}

internal sealed class RunAuthorization
{
    private RunAuthorization()
    {
    }

    public required Guid RunId { get; init; }

    public required ulong CharacterContentId { get; init; }

    public required uint HomeWorldId { get; init; }

    public required ImmutableArray<PlanDemand> Demands { get; init; }

    public required ImmutableArray<uint> AllowedWorldIds { get; init; }

    public required long MaxSpend { get; init; }

    public required long GilReserve { get; init; }

    public required int PolicyVersion { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    // The most stops (0 for no limit) and the world revisits the run planned with, so a resume keeps them; null in a
    // journal written before they were recorded.
    public int? MaxStops { get; init; }

    public int? WorldRevisits { get; init; }

    // A dry run is authorized in memory only, so one read back from the journal file is always a real run.
    public bool DryRun { get; init; }

    public RunCaller? Caller { get; init; }

    public static AuthorizationProblem TryCreate(
        Guid runId,
        ulong characterContentId,
        uint homeWorldId,
        ImmutableArray<PlanDemand> demands,
        ImmutableArray<uint> allowedWorldIds,
        long maxSpend,
        long gilReserve,
        int policyVersion,
        DateTime createdAtUtc,
        out RunAuthorization? authorization,
        int? maxStops = null,
        int? worldRevisits = null,
        bool dryRun = false,
        RunCaller? caller = null)
    {
        authorization = null;
        var frozenDemands = demands.IsDefault ? [] : demands.AsSpan().ToArray();
        var frozenWorlds = allowedWorldIds.IsDefault ? [] : allowedWorldIds.AsSpan().ToArray();
        Array.Sort(frozenDemands, static (left, right) => left.Key.CompareTo(right.Key));
        Array.Sort(frozenWorlds);
        var problem = Validate(runId, characterContentId, homeWorldId, frozenDemands, frozenWorlds, maxSpend, gilReserve);
        if (problem != AuthorizationProblem.None)
        {
            return problem;
        }

        authorization = new RunAuthorization
        {
            RunId = runId,
            CharacterContentId = characterContentId,
            HomeWorldId = homeWorldId,
            Demands = ImmutableCollectionsMarshal.AsImmutableArray(frozenDemands),
            AllowedWorldIds = ImmutableCollectionsMarshal.AsImmutableArray(frozenWorlds),
            MaxSpend = maxSpend,
            GilReserve = gilReserve,
            PolicyVersion = policyVersion,
            CreatedAtUtc = createdAtUtc,
            MaxStops = maxStops is { } stops ? Math.Max(0, stops) : null,
            WorldRevisits = worldRevisits is { } revisits ? Math.Max(0, revisits) : null,
            DryRun = dryRun,
            Caller = caller,
        };
        return AuthorizationProblem.None;
    }

    public static AuthorizationProblem TryRestore(RunAuthorized recorded, out RunAuthorization? authorization)
    {
        var demands = recorded.Demands.IsDefault ? [] : new PlanDemand[recorded.Demands.Length];
        for (var index = 0; index < demands.Length; index++)
        {
            demands[index] = recorded.Demands[index].ToPlanDemand();
        }

        return TryCreate(
            recorded.RunId,
            recorded.CharacterContentId,
            recorded.HomeWorldId,
            ImmutableCollectionsMarshal.AsImmutableArray(demands),
            recorded.AllowedWorldIds,
            recorded.MaxSpend,
            recorded.GilReserve,
            recorded.PolicyVersion,
            recorded.AuthorizedAtUtc,
            out authorization,
            recorded.MaxStops,
            recorded.WorldRevisits,
            recorded.DryRun,
            recorded.Caller);
    }

    public RunAuthorized ToJournalEvent(long sequence, DateTime atUtc)
    {
        var demands = new JournalDemand[Demands.Length];
        for (var index = 0; index < demands.Length; index++)
        {
            demands[index] = JournalDemand.From(Demands[index]);
        }

        return new RunAuthorized
        {
            Sequence = sequence,
            RunId = RunId,
            AtUtc = atUtc,
            CharacterContentId = CharacterContentId,
            HomeWorldId = HomeWorldId,
            Demands = ImmutableCollectionsMarshal.AsImmutableArray(demands),
            AllowedWorldIds = AllowedWorldIds,
            MaxSpend = MaxSpend,
            GilReserve = GilReserve,
            PolicyVersion = PolicyVersion,
            AuthorizedAtUtc = CreatedAtUtc,
            MaxStops = MaxStops,
            WorldRevisits = WorldRevisits,
            DryRun = DryRun,
            Caller = Caller,
        };
    }

    public bool AllowsWorld(uint worldId)
    {
        for (var index = 0; index < AllowedWorldIds.Length; index++)
        {
            if (AllowedWorldIds[index] == worldId)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetDemand(DemandKey key, out PlanDemand demand)
    {
        for (var index = 0; index < Demands.Length; index++)
        {
            if (Demands[index].Key == key)
            {
                demand = Demands[index];
                return true;
            }
        }

        demand = default;
        return false;
    }

    private static AuthorizationProblem Validate(
        Guid runId,
        ulong characterContentId,
        uint homeWorldId,
        PlanDemand[] sortedDemands,
        uint[] allowedWorldIds,
        long maxSpend,
        long gilReserve)
    {
        if (runId == Guid.Empty)
        {
            return AuthorizationProblem.MissingRunId;
        }

        if (characterContentId == 0)
        {
            return AuthorizationProblem.MissingCharacter;
        }

        if (homeWorldId == 0)
        {
            return AuthorizationProblem.MissingHomeWorld;
        }

        if (maxSpend <= 0)
        {
            return AuthorizationProblem.MissingSpendCap;
        }

        if (gilReserve < 0)
        {
            return AuthorizationProblem.NegativeReserve;
        }

        if (allowedWorldIds.Length == 0)
        {
            return AuthorizationProblem.NoAllowedWorlds;
        }

        return ValidateDemands(sortedDemands);
    }

    private static AuthorizationProblem ValidateDemands(PlanDemand[] sortedDemands)
    {
        var openDemand = false;
        for (var index = 0; index < sortedDemands.Length; index++)
        {
            var demand = sortedDemands[index];
            if (!demand.IsValid)
            {
                return AuthorizationProblem.InvalidDemand;
            }

            if (index > 0 && sortedDemands[index - 1].Key == demand.Key)
            {
                return AuthorizationProblem.DuplicateDemand;
            }

            openDemand |= demand.RequiredQuantity > 0;
        }

        return openDemand ? AuthorizationProblem.None : AuthorizationProblem.NoDemand;
    }
}
