using AutoShopper.Core.Shopping.Model;
using System.Globalization;
using System.Text;

namespace AutoShopper.Core.Shopping.Import;

// Imported text is data: it only ever becomes rows for the preview, and nothing in it is run or authorised. Price caps
// it carries fill the editor and are reviewed there like any other edit.
public static class ListImporter
{
    // Spreadsheets save UTF-8 files with a byte order mark, which would hide the JSON brace or the CSV header.
    private const char ByteOrderMark = (char)0xFEFF;

    public static ImportFormat Detect(string text)
    {
        var start = text.AsSpan().TrimStart();
        if (!start.IsEmpty && start[0] == '{')
        {
            return ImportFormat.Json;
        }

        var position = 0;
        while (TextLines.Next(text, ref position, out var line))
        {
            if (line.IsWhiteSpace())
            {
                continue;
            }

            return CsvListParser.LooksLikeHeader(line.Trim()) ? ImportFormat.Csv : ImportFormat.Text;
        }

        return ImportFormat.Text;
    }

    public static ImportResult Parse(string text)
    {
        text = text.TrimStart(ByteOrderMark);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImportResult.Failed(ImportFormat.Text, ImportError.Empty);
        }

        var format = Detect(text);
        if (Encoding.UTF8.GetByteCount(text) > ShoppingLimits.MaximumImportBytes)
        {
            return ImportResult.Failed(format, ImportError.TooLarge);
        }

        return format switch
        {
            ImportFormat.Json => JsonListParser.Parse(text),
            ImportFormat.Csv => CsvListParser.Parse(text),
            _ => TextListParser.Parse(text),
        };
    }

    public static void Resolve(ImportResult result, IItemNameResolver resolver)
    {
        var rows = result.Rows;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Status == ImportRowStatus.Invalid)
            {
                continue;
            }

            if (row.RequestedItemId != 0)
            {
                row.Apply(resolver.Resolve(row.RequestedItemId.ToString(CultureInfo.InvariantCulture)));
                continue;
            }

            var resolution = resolver.Resolve(row.Name);
            if (resolution.Status == ResolutionStatus.Unknown && !string.IsNullOrEmpty(row.WholeName))
            {
                var whole = resolver.Resolve(row.WholeName);
                if (whole.Status != ResolutionStatus.Unknown)
                {
                    row.UseWholeName();
                    resolution = whole;
                }
            }

            row.Apply(resolution);
        }
    }
}
