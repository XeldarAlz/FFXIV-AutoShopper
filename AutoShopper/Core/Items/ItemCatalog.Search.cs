using System.Globalization;
using System.Text;

namespace AutoShopper.Core.Items;

internal sealed partial class ItemCatalog
{
    private const char HighQualityGlyph = (char)0xE03C;
    private const char LeftSingleQuote = (char)0x2018;
    private const char RightSingleQuote = (char)0x2019;

    // Fills results with catalog indices: an exact item id first, then names that start with the query, then names
    // that contain it, each group in name order. Returns how many were written.
    public int Search(string query, Span<int> results)
    {
        var current = snapshot;
        if (current is null || results.IsEmpty)
        {
            return 0;
        }

        var folded = Fold(query);
        if (folded.Length == 0)
        {
            return 0;
        }

        var count = 0;
        if (IsDigits(folded) && uint.TryParse(folded, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            && current.IndexById.TryGetValue(itemId, out var idIndex))
        {
            results[count++] = idIndex;
        }

        var names = current.FoldedNames;
        for (var index = 0; index < names.Length && count < results.Length; index++)
        {
            if (names[index].StartsWith(folded, StringComparison.Ordinal) && !results[..count].Contains(index))
            {
                results[count++] = index;
            }
        }

        for (var index = 0; index < names.Length && count < results.Length; index++)
        {
            var name = names[index];
            if (!name.StartsWith(folded, StringComparison.Ordinal) && name.Contains(folded, StringComparison.Ordinal) && !results[..count].Contains(index))
            {
                results[count++] = index;
            }
        }

        return count;
    }

    // One spelling for comparing names: lower case, typographic apostrophes as plain ones, the HQ glyph dropped and
    // runs of whitespace collapsed, so a pasted "Skybuilders’ Ingot" finds "Skybuilders' Ingot".
    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == HighQualityGlyph || char.IsControl(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character is LeftSingleQuote or RightSingleQuote ? '\'' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
