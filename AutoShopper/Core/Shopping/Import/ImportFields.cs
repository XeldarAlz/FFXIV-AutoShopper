using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Shopping.Import;

internal enum NumberStatus : byte
{
    Empty,
    Valid,
    Malformed,
    Overflow,
    // Digits whose separator may be a decimal mark, such as "10,5", "2500,00" or "1.500": refused, never read as a
    // larger whole number.
    Ambiguous,
}

internal static class ImportFields
{
    public const char HighQualityGlyph = (char)0xE03C;
    public const char NoBreakSpace = (char)0x00A0;
    public const char NarrowNoBreakSpace = (char)0x202F;

    // Every limit is far below this many digits, so a longer number is out of range without risking overflow.
    private const int MaximumDigits = 12;
    private const int DigitGroupLength = 3;
    private const char DecimalPoint = '.';

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

    // Digit groups may be split by spaces, commas, underscores or apostrophes when every group after the first has
    // exactly three digits, so "1,000" and "12 500" read as written. A period, a shorter or longer group ("10,5",
    // "2500,00") or, where the comma is the decimal mark, any comma could be a decimal part and is Ambiguous.
    public static NumberStatus TryParseWhole(ReadOnlySpan<char> text, out long value, bool decimalComma = false)
    {
        value = 0;
        text = text.Trim();
        if (text.IsEmpty)
        {
            return NumberStatus.Empty;
        }

        if (!IsNumberShaped(text))
        {
            return NumberStatus.Malformed;
        }

        var status = ReadDigits(text, decimalComma, out var parsed);
        if (status == NumberStatus.Valid)
        {
            value = parsed;
        }

        return status;
    }

    private static NumberStatus ReadDigits(ReadOnlySpan<char> text, bool decimalComma, out long value)
    {
        value = 0;
        var digits = 0;
        var groupLength = 0;
        var grouped = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsAsciiDigit(character))
            {
                groupLength++;
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

            if (character == DecimalPoint || (decimalComma && character == ','))
            {
                return NumberStatus.Ambiguous;
            }

            if (groupLength == 0)
            {
                return NumberStatus.Malformed;
            }

            if (grouped ? groupLength != DigitGroupLength : groupLength > DigitGroupLength)
            {
                return NumberStatus.Ambiguous;
            }

            grouped = true;
            groupLength = 0;
        }

        if (!grouped)
        {
            return NumberStatus.Valid;
        }

        if (groupLength == 0)
        {
            return NumberStatus.Malformed;
        }

        return groupLength == DigitGroupLength ? NumberStatus.Valid : NumberStatus.Ambiguous;
    }

    // Only digits, group separators and periods, with at least one digit; anything else is not a number at all.
    private static bool IsNumberShaped(ReadOnlySpan<char> text)
    {
        var hasDigit = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsAsciiDigit(character))
            {
                hasDigit = true;
                continue;
            }

            if (character != DecimalPoint && !IsGroupSeparator(character))
            {
                return false;
            }
        }

        return hasDigit;
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

    // An empty field keeps the default; a field that is not a whole number, may be a decimal, or is too long to be one
    // in range rejects the row.
    private static bool Accepts(ImportRow row, NumberStatus status, ImportRowProblem outOfRange)
    {
        if (status == NumberStatus.Valid)
        {
            return true;
        }

        var problem = ProblemOf(status, outOfRange);
        if (problem != ImportRowProblem.None)
        {
            row.Reject(problem);
        }

        return false;
    }

    // The problem a number field gives its row; None for a usable or empty field.
    public static ImportRowProblem ProblemOf(NumberStatus status, ImportRowProblem outOfRange) => status switch
    {
        NumberStatus.Malformed => ImportRowProblem.MalformedNumber,
        NumberStatus.Ambiguous => ImportRowProblem.AmbiguousNumber,
        NumberStatus.Overflow => outOfRange,
        _ => ImportRowProblem.None,
    };

    public static bool IsGroupSeparator(char character)
        => character is ',' or '_' or '\'' or ' ' or NoBreakSpace or NarrowNoBreakSpace;

    public static bool Is(ReadOnlySpan<char> token, string expected)
        => token.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
