using AutoShopper.Core.Shopping.Model;
using System.Text.Json;

namespace AutoShopper.Core.Shopping.Import;

internal static class JsonListParser
{
    // The native format nests three levels deep and an Artisan list four; anything deeper is not one of ours.
    private const int MaximumDepth = 8;

    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = MaximumDepth,
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult(ImportFormat.Json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException)
        {
            result.Fail(ImportError.MalformedJson);
            return result;
        }

        using (document)
        {
            ReadList(document.RootElement, result);
        }

        return result;
    }

    private static void ReadList(JsonElement root, ImportResult result)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        string? format = null;
        var version = 0;
        JsonElement lines = default;
        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;
            if (Is(property, NativeListFormat.FormatProperty))
            {
                format = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            else if (Is(property, NativeListFormat.VersionProperty))
            {
                version = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
            }
            else if (Is(property, NativeListFormat.NameProperty))
            {
                result.ListName = value.ValueKind == JsonValueKind.String
                    ? ShoppingLimits.CleanName(value.GetString(), ShoppingLimits.MaximumListNameLength)
                    : null;
            }
            else if (Is(property, NativeListFormat.LinesProperty))
            {
                lines = value;
            }
            else
            {
                result.IgnoredFields++;
            }
        }

        if (!string.Equals(format, NativeListFormat.FormatName, StringComparison.Ordinal))
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        if (version > NativeListFormat.Version)
        {
            result.Fail(ImportError.UnsupportedVersion);
            return;
        }

        if (version < 1 || lines.ValueKind != JsonValueKind.Array)
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        if (lines.GetArrayLength() > ShoppingLimits.MaximumImportRows)
        {
            result.Fail(ImportError.TooManyRows);
            return;
        }

        var lineNumber = 0;
        foreach (var element in lines.EnumerateArray())
        {
            lineNumber++;
            result.Rows.Add(ReadLine(lineNumber, element, result));
        }

        if (result.Rows.Count == 0)
        {
            result.Fail(ImportError.Empty);
        }
    }

    private static ImportRow ReadLine(int lineNumber, JsonElement element, ImportResult result)
    {
        var row = new ImportRow(lineNumber, TextLines.Clip(element.GetRawText()));
        if (element.ValueKind != JsonValueKind.Object)
        {
            row.Reject(ImportRowProblem.MissingItem);
            return row;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!ReadField(row, property))
            {
                result.IgnoredFields++;
            }
        }

        if (row.RequestedItemId == 0 && row.Name.Length == 0)
        {
            row.Reject(ImportRowProblem.MissingItem);
        }

        return row;
    }

    private static bool ReadField(ImportRow row, JsonProperty property)
    {
        var value = property.Value;
        long number;
        if (Is(property, NativeListFormat.LineIdProperty))
        {
            if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var lineId))
            {
                row.LineId = lineId;
            }
        }
        else if (Is(property, NativeListFormat.ItemIdProperty))
        {
            var status = NumberOf(value, out number);
            if (status == NumberStatus.Valid && number is > 0 and <= uint.MaxValue)
            {
                row.RequestedItemId = (uint)number;
            }
            else if (status != NumberStatus.Empty)
            {
                row.Reject(ImportRowProblem.MalformedNumber);
            }
        }
        else if (Is(property, NativeListFormat.ItemNameProperty))
        {
            ReadName(row, value);
        }
        else if (Is(property, NativeListFormat.QualityProperty))
        {
            if (value.ValueKind == JsonValueKind.String && ImportFields.TryParseQuality(value.GetString(), out var quality))
            {
                row.Quality = quality;
            }
            else
            {
                row.Reject(ImportRowProblem.UnknownQuality);
            }
        }
        else if (Is(property, NativeListFormat.ModeProperty))
        {
            if (value.ValueKind == JsonValueKind.String && ImportFields.TryParseMode(value.GetString(), out var mode))
            {
                row.Mode = mode;
            }
            else
            {
                row.Reject(ImportRowProblem.UnknownMode);
            }
        }
        else if (Is(property, NativeListFormat.QuantityProperty))
        {
            ImportFields.SetQuantity(row, NumberOf(value, out number), number);
        }
        else if (Is(property, NativeListFormat.MaxUnitPriceProperty))
        {
            ImportFields.SetUnitCap(row, NumberOf(value, out number), number);
        }
        else if (Is(property, NativeListFormat.ExcessAllowanceProperty))
        {
            ImportFields.SetExcess(row, NumberOf(value, out number), number);
        }
        else
        {
            return false;
        }

        return true;
    }

    private static void ReadName(ImportRow row, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var name = value.GetString() ?? string.Empty;
        if (name.Length > ShoppingLimits.MaximumItemNameLength)
        {
            row.Reject(ImportRowProblem.NameTooLong);
            return;
        }

        row.Name = ShoppingLimits.CleanName(name, ShoppingLimits.MaximumItemNameLength);
    }

    public static NumberStatus NumberOf(JsonElement value, out long number)
    {
        number = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return NumberStatus.Empty;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out number))
                {
                    return number < 0 ? NumberStatus.Overflow : NumberStatus.Valid;
                }

                return value.TryGetDecimal(out var fractional) && fractional > 0 && decimal.Truncate(fractional) == fractional
                    ? NumberStatus.Overflow
                    : NumberStatus.Malformed;
            case JsonValueKind.String:
                return ImportFields.TryParseWhole(value.GetString(), out number);
            default:
                return NumberStatus.Malformed;
        }
    }

    public static bool Is(JsonProperty property, string name) => property.NameEquals(name) || ImportFields.Is(property.Name, name);
}
