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
    private const string SchemaProperty = "schema";
    private const int TopLevel = 1;

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

    public static bool TryRead<T>(ReadOnlySpan<byte> utf8, JsonTypeInfo<T> type, [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = null;
        try
        {
            value = JsonSerializer.Deserialize(utf8, type);
        }
        catch (JsonException)
        {
            return false;
        }

        return value is not null;
    }

    public static bool TryReadSchema(ReadOnlySpan<byte> utf8, out int schema)
    {
        schema = 0;
        var reader = new Utf8JsonReader(utf8);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != TopLevel)
                {
                    continue;
                }

                var isSchema = string.Equals(reader.GetString(), SchemaProperty, StringComparison.OrdinalIgnoreCase);
                if (!reader.Read() || !isSchema)
                {
                    continue;
                }

                schema = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) ? value : 0;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return true;
    }
}
