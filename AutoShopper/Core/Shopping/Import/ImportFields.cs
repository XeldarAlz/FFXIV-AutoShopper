using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Shopping.Import;

internal enum NumberStatus : byte
{
    Empty,
    Valid,
    Malformed,
    Overflow,
}

internal static class ImportFields
{
    public const char HighQualityGlyph = (char)0xE03C;
    public const char NoBreakSpace = (char)0x00A0;
    public const char NarrowNoBreakSpace = (char)0x202F;

    // Every limit is far below this many digits, so a longer number is out of range without risking overflow.
    private const int MaximumDigits = 12;

    public static bool TryParseQuality(ReadOnlySpan<char> token, out ItemQuality quality)
    {
        token = token.Trim();
        if (token.IsEmpty || Is(token, "any") || Is(token, "all"))
        {
            quality = ItemQuality.Any;
            return true;
        }

        if (Is(token, "hq") || Is(token, "high") || Is(token, "highquality") || Is(token, "high quality") || Is(token, "high-quality"))
        {
            quality = ItemQuality.HighQuality;
            return true;
        }

        if (Is(token, "nq") || Is(token, "normal") || Is(token, "normalquality") || Is(token, "normal quality") || Is(token, "normal-quality"))
        {
            quality = ItemQuality.NormalQuality;
            return true;
        }

        quality = ItemQuality.Any;
        return false;
    }

    public static bool TryParseMode(ReadOnlySpan<char> token, out QuantityMode mode)
    {
        token = token.Trim();
        if (token.IsEmpty || Is(token, "buy"))
        {
            mode = QuantityMode.Buy;
            return true;
        }

        if (Is(token, "reachinventory") || Is(token, "reach inventory") || Is(token, "reach") || Is(token, "topup") || Is(token, "top up") || Is(token, "top-up"))
        {
            mode = QuantityMode.ReachInventory;
            return true;
        }

        mode = QuantityMode.Buy;
        return false;
    }

    // Group separators (spaces, commas, underscores, apostrophes) are skipped. A period is refused: it is the decimal
    // point in some locales and the group separator in others, so "1.500" has no single reading.
    public static NumberStatus TryParseWhole(ReadOnlySpan<char> text, out long value)
    {
        value = 0;
        text = text.Trim();
        if (text.IsEmpty)
        {
            return NumberStatus.Empty;
        }

        var digits = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsAsciiDigit(character))
            {
                if (digits == 0 && character == '0')
                {
                    continue;
                }

                digits++;
                if (digits > MaximumDigits)
                {
                    return NumberStatus.Overflow;
                }

                value = value * 10 + (character - '0');
                continue;
            }

            if (index == 0 || !IsGroupSeparator(character))
            {
                return NumberStatus.Malformed;
            }
        }

        return NumberStatus.Valid;
    }

    public static void SetQuantity(ImportRow row, NumberStatus status, long value)
    {
        if (Accepts(row, status, ImportRowProblem.QuantityOutOfRange) && ShoppingLimits.IsValidQuantity(value))
        {
            row.Quantity = (int)value;
        }
        else if (status == NumberStatus.Valid)
        {
            row.Reject(ImportRowProblem.QuantityOutOfRange);
        }
    }

    public static void SetUnitCap(ImportRow row, NumberStatus status, long value)
    {
        if (Accepts(row, status, ImportRowProblem.UnitCapOutOfRange) && ShoppingLimits.IsValidUnitPrice(value))
        {
            row.MaxUnitPrice = value;
        }
        else if (status == NumberStatus.Valid)
        {
            row.Reject(ImportRowProblem.UnitCapOutOfRange);
        }
    }

    public static void SetExcess(ImportRow row, NumberStatus status, long value)
    {
        if (Accepts(row, status, ImportRowProblem.ExcessOutOfRange) && ShoppingLimits.IsValidExcess(value))
        {
            row.ExcessAllowance = (int)value;
        }
        else if (status == NumberStatus.Valid)
        {
            row.Reject(ImportRowProblem.ExcessOutOfRange);
        }
    }

    // An empty field keeps the default; a field that is not a whole number, or too long to be one in range, rejects
    // the row.
    private static bool Accepts(ImportRow row, NumberStatus status, ImportRowProblem outOfRange)
    {
        switch (status)
        {
            case NumberStatus.Valid:
                return true;
            case NumberStatus.Malformed:
                row.Reject(ImportRowProblem.MalformedNumber);
                return false;
            case NumberStatus.Overflow:
                row.Reject(outOfRange);
                return false;
            default:
                return false;
        }
    }

    public static bool IsGroupSeparator(char character)
        => character is ',' or '_' or '\'' or ' ' or NoBreakSpace or NarrowNoBreakSpace;

    public static bool Is(ReadOnlySpan<char> token, string expected)
        => token.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
