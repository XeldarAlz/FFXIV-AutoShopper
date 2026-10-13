namespace AutoShopper.Core.Api.Model;

internal enum QuoteState : byte
{
    Pending,
    Ready,
    Failed,
    Unknown,
}

internal readonly record struct QuoteRequestItem(uint ItemId, int Quantity);

internal sealed record QuoteRequest
{
    public int Schema { get; init; }

    public string? Caller { get; init; }

    public string? CallerDisplayName { get; init; }

    public QuoteRequestItem[]? Items { get; init; }
}

internal sealed record QuoteStartResponse
{
    public int Schema { get; init; } = ApiContract.Schema;

    public bool Accepted { get; init; }

    public string? QuoteId { get; init; }

    public ApiRefusal? Refusal { get; init; }

    public string? Message { get; init; }

    public static QuoteStartResponse Accept(Guid quoteId) => new() { Accepted = true, QuoteId = ApiIds.Format(quoteId) };

    public static QuoteStartResponse Refuse(ApiRefusal refusal, string message) => new() { Refusal = refusal, Message = message };
}

internal readonly record struct QuoteCover(bool Available, int Units, long Total, long UnitPrice, double OldestListingHours, int Worlds);

internal readonly record struct QuoteItemResult(uint ItemId, int Quantity, QuoteCover Normal, QuoteCover High);

internal sealed record QuoteResultResponse
{
    public int Schema { get; init; } = ApiContract.Schema;

    public string? QuoteId { get; init; }

    public QuoteState State { get; init; }

    public ApiRefusal? Refusal { get; init; }

    public DateTime? FetchedAtUtc { get; init; }

    public QuoteItemResult[] Items { get; init; } = [];

    public static QuoteResultResponse Unknown(string? quoteId) => new() { QuoteId = quoteId, State = QuoteState.Unknown };
}
