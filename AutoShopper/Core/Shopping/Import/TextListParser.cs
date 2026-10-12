using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Shopping.Import;

// Reads the shapes lists take on the clipboard: "3x Darksteel Ore", "3 x Darksteel Ore", "Darksteel Ore x3",
// "3 Darksteel Ore", "Darksteel Ore", two spreadsheet cells separated by a tab, and an HQ marker as "(HQ)", "[HQ]",
// a trailing " HQ" or the game's HQ glyph. Headings ending in ":" and lines starting with "#" or "//" are skipped.
internal static class TextListParser
{
    private const char MultiplicationSign = (char)0x00D7;
    private const char Bullet = (char)0x2022;
    private const char MiddleDot = (char)0x00B7;
    private const int StackBufferLength = 256;

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult(ImportFormat.Text);
        var position = 0;
        var lineNumber = 0;
        while (TextLines.Next(text, ref position, out var line))
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.IsEmpty || IsSkipped(trimmed))
            {
                continue;
            }

            if (result.Rows.Count >= ShoppingLimits.MaximumImportRows)
            {
                result.Fail(ImportError.TooManyRows);
                return result;
            }

            result.Rows.Add(ParseLine(lineNumber, trimmed));
        }

        if (result.Rows.Count == 0)
        {
            result.Fail(ImportError.Empty);
        }

        return result;
    }

    public static ImportRow ParseLine(int lineNumber, ReadOnlySpan<char> line)
    {
        var row = new ImportRow(lineNumber, TextLines.Clip(line));
        Span<char> buffer = line.Length <= StackBufferLength ? stackalloc char[line.Length] : new char[line.Length];
        var quality = ItemQuality.Any;
        var text = WithoutGlyph(line, buffer, ref quality);
        text = WithoutBullet(text);

        scoped ReadOnlySpan<char> name;
        if (text.Contains('\t'))
        {
            name = ReadCells(row, text);
        }
        else if (TryLeadingQuantity(text, out var status, out var quantity, out var rest, out var bare))
        {
            ImportFields.SetQuantity(row, status, quantity);
            name = rest;
            if (bare)
            {
                row.WholeName = CleanName(StripQualityMarker(text, ref quality));
            }
        }
        else if (TryTrailingQuantity(text, out status, out quantity, out rest))
        {
            ImportFields.SetQuantity(row, status, quantity);
            name = rest;
        }
        else
        {
            name = text;
        }

        name = StripQualityMarker(name, ref quality);
        row.Quality = quality;
        if (name.Trim().Length > ShoppingLimits.MaximumItemNameLength)
        {
            row.Reject(ImportRowProblem.NameTooLong);
            return row;
        }

        row.Name = CleanName(name);
        if (row.Name.Length == 0)
        {
            row.Reject(ImportRowProblem.MissingItem);
        }

        return row;
    }

    private static bool IsSkipped(ReadOnlySpan<char> line)
        => line[0] == '#' || line.StartsWith("//") || line[^1] == ':';

    private static string CleanName(ReadOnlySpan<char> name)
        => ShoppingLimits.CleanName(name.ToString(), ShoppingLimits.MaximumItemNameLength);

    private static ReadOnlySpan<char> WithoutGlyph(ReadOnlySpan<char> line, Span<char> buffer, scoped ref ItemQuality quality)
    {
        if (!line.Contains(ImportFields.HighQualityGlyph))
        {
            return line;
        }

        quality = ItemQuality.HighQuality;
        var length = 0;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != ImportFields.HighQualityGlyph)
            {
                buffer[length++] = line[index];
            }
        }

        return buffer[..length].Trim();
    }

    private static ReadOnlySpan<char> WithoutBullet(ReadOnlySpan<char> text)
    {
        if (text.Length < 2 || !char.IsWhiteSpace(text[1]))
        {
            return text;
        }

        return text[0] is '-' or '*' or '+' or Bullet or MiddleDot ? text[1..].TrimStart() : text;
    }

    // A tab means cells pasted from a spreadsheet: the first cell is the item, and the first whole number after it is
    // the quantity.
    private static ReadOnlySpan<char> ReadCells(ImportRow row, ReadOnlySpan<char> text)
    {
        var tab = text.IndexOf('\t');
        var name = text[..tab].Trim();
        var rest = text[(tab + 1)..];
        while (!rest.IsEmpty)
        {
            var next = rest.IndexOf('\t');
            var cell = next < 0 ? rest : rest[..next];
            var status = ImportFields.TryParseWhole(cell, out var quantity);
            if (status != NumberStatus.Empty && status != NumberStatus.Malformed)
            {
                ImportFields.SetQuantity(row, status, quantity);
                break;
            }

            rest = next < 0 ? ReadOnlySpan<char>.Empty : rest[(next + 1)..];
        }

        return name;
    }

    private static bool TryLeadingQuantity(ReadOnlySpan<char> text, out NumberStatus status, out long quantity, out ReadOnlySpan<char> rest, out bool bare)
    {
        status = NumberStatus.Empty;
        quantity = 0;
        rest = default;
        bare = false;

        var length = NumberLength(text);
        if (length == 0 || length == text.Length)
        {
            return false;
        }

        var after = text[length..];
        var afterTrimmed = after.TrimStart();
        if (afterTrimmed.Length > 1 && IsTimes(afterTrimmed[0]) && char.IsWhiteSpace(afterTrimmed[1]))
        {
            rest = afterTrimmed[1..].TrimStart();
        }
        else if (char.IsWhiteSpace(after[0]) && !afterTrimmed.IsEmpty)
        {
            rest = afterTrimmed;
            bare = true;
        }
        else
        {
            return false;
        }

        status = ImportFields.TryParseWhole(text[..length], out quantity);
        return !rest.IsEmpty;
    }

    private static bool TryTrailingQuantity(ReadOnlySpan<char> text, out NumberStatus status, out long quantity, out ReadOnlySpan<char> rest)
    {
        status = NumberStatus.Empty;
        quantity = 0;
        rest = default;

        var start = text.Length;
        while (start > 0 && (char.IsAsciiDigit(text[start - 1]) || text[start - 1] == ','))
        {
            start--;
        }

        if (start == text.Length || !char.IsAsciiDigit(text[start]))
        {
            return false;
        }

        var before = text[..start].TrimEnd();
        if (before.Length < 2 || !IsTimes(before[^1]) || !char.IsWhiteSpace(before[^2]))
        {
            return false;
        }

        rest = before[..^1].TrimEnd();
        status = ImportFields.TryParseWhole(text[start..], out quantity);
        return !rest.IsEmpty;
    }

    private static int NumberLength(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && (char.IsAsciiDigit(text[length]) || (text[length] == ',' && length > 0 && length + 1 < text.Length && char.IsAsciiDigit(text[length + 1]))))
        {
            length++;
        }

        return length;
    }

    private static bool IsTimes(char character) => character is 'x' or 'X' or MultiplicationSign;

    private static ReadOnlySpan<char> StripQualityMarker(ReadOnlySpan<char> name, scoped ref ItemQuality quality)
    {
        name = name.TrimEnd();
        if (EndsWithMarker(name, "(HQ)", "[HQ]", " HQ", out var length))
        {
            quality = ItemQuality.HighQuality;
            return name[..^length].TrimEnd();
        }

        if (EndsWithMarker(name, "(NQ)", "[NQ]", " NQ", out length))
        {
            quality = ItemQuality.NormalQuality;
            return name[..^length].TrimEnd();
        }

        return name;
    }

    // The bracketed markers ignore case; the bare suffix must be upper case so a name that happens to end in "hq"
    // keeps its last word.
    private static bool EndsWithMarker(ReadOnlySpan<char> name, string parenthesised, string bracketed, string suffix, out int length)
    {
        length = parenthesised.Length;
        if (name.EndsWith(parenthesised, StringComparison.OrdinalIgnoreCase) || name.EndsWith(bracketed, StringComparison.OrdinalIgnoreCase))
        {
            return name.Length > length;
        }

        length = suffix.Length;
        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > length;
    }
}
