using System.Text.Json.Serialization;

namespace AutoShopper.Core.Pricing.Universalis;

// One class reads both answer shapes: a multi-item answer fills ItemIds, Items and UnresolvedItems and each nested
// item fills the rest, while a single-item answer fills the rest at the top. Fields this plugin does not ask for
// are skipped. Numbers are read as long and range-checked afterwards, because the server writes them as 32-bit.
internal sealed class UniversalisView
{
    [JsonPropertyName("itemIDs")]
    public long[]? ItemIds { get; init; }

    [JsonPropertyName("items")]
    public Dictionary<string, UniversalisView>? Items { get; init; }

    [JsonPropertyName("unresolvedItems")]
    public long[]? UnresolvedItems { get; init; }

    [JsonPropertyName("itemID")]
    public long? ItemId { get; init; }

    [JsonPropertyName("worldID")]
    public long? WorldId { get; init; }

    [JsonPropertyName("lastUploadTime")]
    public long? LastUploadTime { get; init; }

    [JsonPropertyName("worldUploadTimes")]
    public Dictionary<string, long>? WorldUploadTimes { get; init; }

    [JsonPropertyName("hasData")]
    public bool? HasData { get; init; }

    [JsonPropertyName("listings")]
    public UniversalisListing[]? Listings { get; init; }
}

internal sealed class UniversalisListing
{
    [JsonPropertyName("listingID")]
    public string? ListingId { get; init; }

    [JsonPropertyName("pricePerUnit")]
    public long PricePerUnit { get; init; }

    [JsonPropertyName("quantity")]
    public long Quantity { get; init; }

    [JsonPropertyName("hq")]
    public bool IsHq { get; init; }

    [JsonPropertyName("worldID")]
    public long? WorldId { get; init; }

    [JsonPropertyName("onMannequin")]
    public bool OnMannequin { get; init; }

    [JsonPropertyName("lastReviewTime")]
    public long LastReviewTime { get; init; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UniversalisView))]
internal sealed partial class UniversalisJsonContext : JsonSerializerContext
{
}
