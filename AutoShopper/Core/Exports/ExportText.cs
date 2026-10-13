using System.Globalization;
using System.Text;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Exports;

// Exports are pasted into chat and notes in any locale, so every number and date is written with the invariant culture.
internal static class ExportText
{
    public const char NewLine = '\n';
    public const string Indent = "  ";

    private const string GilUnit = " gil";
    private const string HighQualityMarker = " (HQ)";
    private const string NormalQualityMarker = " (NQ)";
    private const string ItemFallbackPrefix = "Item ";
    private const string WorldFallbackPrefix = "World ";
    private const string DataCenterFallbackPrefix = "Data center ";
    private const string MarkdownSpecialCharacters = "\\`*_~|[]<>#";

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    public static string ItemName(IExportNames names, uint itemId)
    {
        if (names.TryGetName(itemId, out var name) && name.Length > 0)
        {
            return name;
        }

        return string.Concat(ItemFallbackPrefix, itemId.ToString(CultureInfo.InvariantCulture));
    }

    public static string WorldName(IExportNames names, uint worldId, string knownName = "")
    {
        if (names.TryGetWorldName(worldId, out var name) && name.Length > 0)
        {
            return name;
        }

        if (knownName.Length > 0)
        {
            return knownName;
        }

        return string.Concat(WorldFallbackPrefix, worldId.ToString(CultureInfo.InvariantCulture));
    }

    public static string DataCenterName(IExportNames names, uint dataCenterId)
    {
        if (names.TryGetDataCenterName(dataCenterId, out var name) && name.Length > 0)
        {
            return name;
        }

        return string.Concat(DataCenterFallbackPrefix, dataCenterId.ToString(CultureInfo.InvariantCulture));
    }

    public static StringBuilder AppendNumber(StringBuilder builder, long value)
    {
        return builder.Append(CultureInfo.InvariantCulture, $"{value:N0}");
    }

    public static StringBuilder AppendGil(StringBuilder builder, long amount)
    {
        return AppendNumber(builder, amount).Append(GilUnit);
    }

    public static StringBuilder AppendCode(StringBuilder builder, long code)
    {
        return builder.Append(CultureInfo.InvariantCulture, $"{code}");
    }

    public static StringBuilder AppendCounted(StringBuilder builder, long count, string singular, string plural)
    {
        return AppendNumber(builder, count).Append(' ').Append(count == 1 ? singular : plural);
    }

    public static StringBuilder AppendTimestamp(StringBuilder builder, DateTime atUtc)
    {
        return builder.Append(CultureInfo.InvariantCulture, $"{atUtc:yyyy'-'MM'-'dd HH':'mm} UTC");
    }

    public static StringBuilder AppendQuality(StringBuilder builder, DemandQuality quality)
    {
        return quality switch
        {
            DemandQuality.HighQuality => builder.Append(HighQualityMarker),
            DemandQuality.NormalQuality => builder.Append(NormalQualityMarker),
            _ => builder,
        };
    }

    public static StringBuilder AppendQuality(StringBuilder builder, bool isHighQuality)
    {
        return isHighQuality ? builder.Append(HighQualityMarker) : builder;
    }

    public static StringBuilder AppendPriceAge(StringBuilder builder, DateTime oldestUtc, DateTime newestUtc, DateTime nowUtc)
    {
        if (oldestUtc == default || newestUtc == default)
        {
            return builder.Append("no price data");
        }

        var newestAge = Age(nowUtc - newestUtc);
        var oldestAge = Age(nowUtc - oldestUtc);
        builder.Append("prices ").Append(newestAge);
        if (!string.Equals(newestAge, oldestAge, StringComparison.Ordinal))
        {
            builder.Append(" to ").Append(oldestAge);
        }

        return builder.Append(" old");
    }

    public static StringBuilder AppendMarkdown(StringBuilder builder, string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '\r' or '\n')
            {
                builder.Append(' ');
                continue;
            }

            if (MarkdownSpecialCharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder;
    }

    private static string Age(TimeSpan age)
    {
        if (age < OneMinute)
        {
            return "under 1 min";
        }

        if (age.TotalHours < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{age.Minutes} min");
        }

        if (age.TotalDays < 1)
        {
            return age.Minutes == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{age.Hours} h")
                : string.Create(CultureInfo.InvariantCulture, $"{age.Hours} h {age.Minutes} min");
        }

        var days = (long)age.TotalDays;
        return age.Hours == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{days} d")
            : string.Create(CultureInfo.InvariantCulture, $"{days} d {age.Hours} h");
    }
}
