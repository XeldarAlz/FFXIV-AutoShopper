using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal readonly record struct GuardInput
{
    public required RunAuthorization Authorization { get; init; }

    public required Guid ActiveRunId { get; init; }

    public required ulong CharacterContentId { get; init; }

    public required uint CurrentWorldId { get; init; }

    public required int SearchGeneration { get; init; }

    public required bool ExecutionAvailable { get; init; }

    public required DemandKey Demand { get; init; }

    public required ObservedListing Selected { get; init; }

    public required ObservedListing Current { get; init; }

    public required int ConfirmedQuantity { get; init; }

    public required SpendState Spend { get; init; }

    public required long CurrentGil { get; init; }

    public long TravelCommitment { get; init; }

    public required int FreeInventorySlots { get; init; }

    public required int ItemStackSize { get; init; }

    public bool UniqueItemAlreadyOwned { get; init; }

    public ImmutableArray<ulong> OwnRetainerIds { get; init; }
}
