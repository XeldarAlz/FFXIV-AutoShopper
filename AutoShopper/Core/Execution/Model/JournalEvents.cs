using System.Collections.Immutable;
using System.Text.Json.Serialization;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal readonly record struct JournalDemand(
    uint ItemId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<DemandQuality>))] DemandQuality Quality,
    int RequiredQuantity,
    int ExcessAllowance,
    long UnitCapIncludingFee)
{
    public static JournalDemand From(PlanDemand demand)
    {
        return new JournalDemand(demand.ItemId, demand.Quality, demand.RequiredQuantity, demand.ExcessAllowance, demand.UnitCapIncludingFee);
    }

    public PlanDemand ToPlanDemand()
    {
        return new PlanDemand(new DemandKey(ItemId, Quality), RequiredQuantity, ExcessAllowance, UnitCapIncludingFee);
    }
}

internal sealed record RunAuthorized : JournalEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.RunAuthorized;

    public required ulong CharacterContentId { get; init; }

    public required uint HomeWorldId { get; init; }

    public required ImmutableArray<JournalDemand> Demands { get; init; }

    public required ImmutableArray<uint> AllowedWorldIds { get; init; }

    public required long MaxSpend { get; init; }

    public required long GilReserve { get; init; }

    public required int PolicyVersion { get; init; }

    public required DateTime AuthorizedAtUtc { get; init; }

    public override bool IsWellFormed()
    {
        return base.IsWellFormed() && CharacterContentId != 0 && MaxSpend > 0 && !Demands.IsDefault && !AllowedWorldIds.IsDefault;
    }
}

internal sealed record IntentRecorded : AttemptEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.IntentRecorded;

    public required uint ItemId { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<DemandQuality>))]
    public required DemandQuality Quality { get; init; }

    public required ulong ListingId { get; init; }

    public required ulong RetainerId { get; init; }

    public required uint WorldId { get; init; }

    public required bool IsHq { get; init; }

    public required int Quantity { get; init; }

    public required long UnitPrice { get; init; }

    public required long TotalTax { get; init; }

    public required long Payable { get; init; }

    public int SearchGeneration { get; init; }

    public int PlanVersion { get; init; }

    // What the purse and the bags held right before the purchase, -1 when unknown; evidence for a reconciliation.
    public long GilBefore { get; init; } = -1;

    public int OwnedBefore { get; init; } = -1;

    [JsonIgnore]
    public DemandKey Demand => new(ItemId, Quality);

    public override bool IsWellFormed()
    {
        return base.IsWellFormed() && ItemId != 0 && Quantity > 0 && UnitPrice >= 0 && TotalTax >= 0 && Payable >= 0;
    }
}

internal sealed record PurchaseSubmitted : AttemptEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.PurchaseSubmitted;

    public required bool Sent { get; init; }
}

internal sealed record PurchaseConfirmed : AttemptEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.PurchaseConfirmed;

    public required int Quantity { get; init; }

    public required long Payable { get; init; }

    public string Evidence { get; init; } = string.Empty;

    public override bool IsWellFormed()
    {
        return base.IsWellFormed() && Quantity > 0 && Payable >= 0;
    }
}

internal sealed record PurchaseRejected : AttemptEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.PurchaseRejected;

    public int ReasonCode { get; init; }

    public string Reason { get; init; } = string.Empty;
}

internal sealed record PurchaseUnknown : AttemptEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.PurchaseUnknown;

    public string Reason { get; init; } = string.Empty;
}

internal sealed record Reconciled : AttemptEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.Reconciled;

    public required bool Purchased { get; init; }

    public int Quantity { get; init; }

    public long Payable { get; init; }

    public string Note { get; init; } = string.Empty;

    public override bool IsWellFormed()
    {
        return base.IsWellFormed() && (!Purchased || (Quantity > 0 && Payable >= 0));
    }
}

internal sealed record Checkpoint : JournalEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.Checkpoint;

    [JsonConverter(typeof(JsonStringEnumConverter<RunState>))]
    public required RunState State { get; init; }

    public int PlanVersion { get; init; }

    public ImmutableArray<uint> CompletedWorldIds { get; init; } = ImmutableArray<uint>.Empty;

    public ImmutableArray<string> ExcludedListingKeys { get; init; } = ImmutableArray<string>.Empty;

    // Worlds travel gave up on; a checkpoint written before route travel existed has none.
    public ImmutableArray<uint> ExcludedWorldIds { get; init; } = ImmutableArray<uint>.Empty;

    public int AttemptCount { get; init; }

    public int FailedAttemptCount { get; init; }

    public override bool IsWellFormed()
    {
        return base.IsWellFormed() && !CompletedWorldIds.IsDefault && !ExcludedListingKeys.IsDefault && !ExcludedWorldIds.IsDefault && AttemptCount >= 0 && FailedAttemptCount >= 0;
    }
}

internal sealed record RunEnded : JournalEvent
{
    [JsonIgnore]
    public override string Kind => JournalEventKind.RunEnded;

    [JsonConverter(typeof(JsonStringEnumConverter<RunState>))]
    public required RunState FinalState { get; init; }

    public string Reason { get; init; } = string.Empty;
}
