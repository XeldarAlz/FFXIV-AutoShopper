using AutoShopper.Core.Shopping.Model;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AutoShopper.Core.Shopping.Import;

// Exports carry item ids, names, quantities and limits only, so a list can be shared without naming the character.
public static class ListExporter
{
    private const string HighQualityMarker = " (HQ)";
    private const string NormalQualityMarker = " (NQ)";
    private const string QuantitySeparator = "x ";
    private const int TextCharactersPerLine = 32;

    // The clipboard is not an HTML page, so names keep their own letters instead of turning into escape sequences.
    private static readonly JsonWriterOptions jsonOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToText(ShoppingList list, IItemNames names)
    {
        var lines = list.Lines;
        var builder = new StringBuilder(lines.Count * TextCharactersPerLine);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            builder.Append(line.Quantity.ToString(CultureInfo.InvariantCulture))
                .Append(QuantitySeparator)
                .Append(NameOf(line.ItemId, names));
            if (line.Quality == ItemQuality.HighQuality)
            {
                builder.Append(HighQualityMarker);
            }
            else if (line.Quality == ItemQuality.NormalQuality)
            {
                builder.Append(NormalQualityMarker);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    public static string ToCsv(ShoppingList list, IItemNames names)
    {
        var lines = list.Lines;
        var builder = new StringBuilder(CsvListParser.Header.Length + lines.Count * TextCharactersPerLine * 2);
        builder.Append(CsvListParser.Header).Append('\n');
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            builder.Append(line.ItemId.ToString(CultureInfo.InvariantCulture)).Append(Csv.Comma);
            Csv.AppendField(builder, names.TryGetName(line.ItemId, out var name) ? name : string.Empty);
            builder.Append(Csv.Comma).Append(line.Quality.ToString())
                .Append(Csv.Comma).Append(line.Mode.ToString())
                .Append(Csv.Comma).Append(line.Quantity.ToString(CultureInfo.InvariantCulture))
                .Append(Csv.Comma).Append(line.MaxUnitPrice.ToString(CultureInfo.InvariantCulture))
                .Append(Csv.Comma).Append(line.ExcessAllowance.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    public static string ToJson(ShoppingList list, IItemNames names)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, jsonOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(NativeListFormat.FormatProperty, NativeListFormat.FormatName);
            writer.WriteNumber(NativeListFormat.VersionProperty, NativeListFormat.Version);
            writer.WriteString(NativeListFormat.NameProperty, list.Name);
            writer.WriteStartArray(NativeListFormat.LinesProperty);
            var lines = list.Lines;
            for (var index = 0; index < lines.Count; index++)
            {
                WriteLine(writer, lines[index], names);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteLine(Utf8JsonWriter writer, ShoppingLine line, IItemNames names)
    {
        writer.WriteStartObject();
        writer.WriteString(NativeListFormat.LineIdProperty, line.Id);
        writer.WriteNumber(NativeListFormat.ItemIdProperty, line.ItemId);
        if (names.TryGetName(line.ItemId, out var name))
        {
            writer.WriteString(NativeListFormat.ItemNameProperty, name);
        }

        writer.WriteString(NativeListFormat.QualityProperty, line.Quality.ToString());
        writer.WriteString(NativeListFormat.ModeProperty, line.Mode.ToString());
        writer.WriteNumber(NativeListFormat.QuantityProperty, line.Quantity);
        writer.WriteNumber(NativeListFormat.MaxUnitPriceProperty, line.MaxUnitPrice);
        writer.WriteNumber(NativeListFormat.ExcessAllowanceProperty, line.ExcessAllowance);
        writer.WriteEndObject();
    }

    private static string NameOf(uint itemId, IItemNames names)
        => names.TryGetName(itemId, out var name) && name.Length > 0 ? name : itemId.ToString(CultureInfo.InvariantCulture);
}
