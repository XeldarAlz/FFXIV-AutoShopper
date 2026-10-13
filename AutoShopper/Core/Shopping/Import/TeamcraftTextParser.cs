using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Shopping.Import;

// Teamcraft's "Copy as text" writes a panel as its translated title followed by " :", then one "<amount>x <item name>"
// line per item still missing. The whole-list button repeats that for every panel, crystals first; the crystal panel's
// own button writes its title without the colon. Titles are sections, never items.
internal static class TeamcraftTextParser
{
    private const string HeaderSuffix = " :";

    // Panel titles as Teamcraft writes them in English, German, French, Japanese, Korean and Chinese.
    private static readonly string[] crystalTitles =
    [
        "Crystals", "Kristalle", "Cristaux", "クリスタル", "크리스탈", "水晶",
    ];

    private static readonly string[] preCraftTitles =
    [
        "Pre crafts", "Vorhergehende Handwerksschritte", "Crafts pré-requis", "下位素材", "밑재료",
        "半成品",
    ];

    private static readonly string[] finalItemTitles =
    [
        "Items", "Gegenstände", "Objets", "アイテム", "아이템", "物品",
    ];

    private enum CraftStage : byte
    {
        Material,
        PreCraft,
        FinalItem,
    }

    // Only Teamcraft's own headings count, so a plain list with a "Shopping:" heading stays a plain list.
    public static bool LooksLikeTeamcraft(string text)
    {
        var position = 0;
        var hasHeader = false;
        var hasItem = false;
        while (TextLines.Next(text, ref position, out var line))
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            if (IsItemLine(trimmed))
            {
                hasItem = true;
                continue;
            }

            if (!IsTeamcraftHeader(trimmed))
            {
                return false;
            }

            hasHeader = true;
        }

        return hasHeader && hasItem;
    }

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult(ImportFormat.TeamcraftText);
        var position = 0;
        var lineNumber = 0;
        string? section = null;
        while (TextLines.Next(text, ref position, out var line))
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            if (!IsItemLine(trimmed))
            {
                section = TitleOf(trimmed);
                continue;
            }

            if (result.Rows.Count >= ShoppingLimits.MaximumImportRows)
            {
                result.Fail(ImportError.TooManyRows);
                return result;
            }

            var row = TextListParser.ParseLine(lineNumber, trimmed);
            row.Section = section;
            result.Rows.Add(row);
        }

        if (result.Rows.Count == 0)
        {
            result.Fail(ImportError.Empty);
            return result;
        }

        RejectCraftedRows(result.Rows);
        return result;
    }

    // A whole-list copy holds the materials together with the pre-crafts and final items made from them. Only the
    // lowest stage present is bought, so a crafted item and the materials for it are never both on the list.
    private static void RejectCraftedRows(List<ImportRow> rows)
    {
        var lowest = CraftStage.FinalItem;
        for (var index = 0; index < rows.Count; index++)
        {
            var stage = StageOf(rows[index].Section);
            if (stage < lowest)
            {
                lowest = stage;
            }
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (StageOf(rows[index].Section) > lowest)
            {
                rows[index].Reject(ImportRowProblem.CraftedFromOtherRows);
            }
        }
    }

    private static CraftStage StageOf(string? section)
    {
        if (section is null)
        {
            return CraftStage.Material;
        }

        if (IsAny(section, preCraftTitles))
        {
            return CraftStage.PreCraft;
        }

        return IsAny(section, finalItemTitles) ? CraftStage.FinalItem : CraftStage.Material;
    }

    private static bool IsItemLine(ReadOnlySpan<char> line)
    {
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        return digits > 0
            && digits + 2 < line.Length
            && line[digits] == 'x'
            && char.IsWhiteSpace(line[digits + 1])
            && !line[(digits + 2)..].IsWhiteSpace();
    }

    private static bool IsTeamcraftHeader(ReadOnlySpan<char> line)
        => (line.Length > HeaderSuffix.Length && line.EndsWith(HeaderSuffix, StringComparison.Ordinal)) || IsAny(line, crystalTitles);

    private static string TitleOf(ReadOnlySpan<char> line)
    {
        var title = line.EndsWith(':') ? line[..^1] : line;
        return ShoppingLimits.CleanName(title.ToString(), ShoppingLimits.MaximumItemNameLength);
    }

    private static bool IsAny(ReadOnlySpan<char> text, string[] candidates)
    {
        for (var index = 0; index < candidates.Length; index++)
        {
            if (text.Equals(candidates[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
