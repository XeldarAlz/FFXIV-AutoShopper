using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AutoShopper.Core.Api.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuoteRequest))]
[JsonSerializable(typeof(QuoteStartResponse))]
[JsonSerializable(typeof(QuoteResultResponse))]
[JsonSerializable(typeof(RunRequest))]
[JsonSerializable(typeof(StartRunResponse))]
[JsonSerializable(typeof(RunStatusResponse))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

internal static class ApiJson
{
    public static string Write<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.Serialize(value, type);

    public static bool TryRead<T>(string? json, JsonTypeInfo<T> type, [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return false;
        }

        return value is not null;
    }
}
