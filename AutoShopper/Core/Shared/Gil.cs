namespace AutoShopper.Core.Shared;

internal static class Gil
{
    private const int PercentDenominator = 100;
    private const int DigitGroupLength = 3;
    private const int DecimalBase = 10;

    public static bool TryAdd(long left, long right, out long sum)
    {
        var result = unchecked(left + right);
        if (((left ^ result) & (right ^ result)) < 0)
        {
            sum = 0;
            return false;
        }

        sum = result;
        return true;
    }

    public static bool TrySubtract(long left, long right, out long difference)
    {
        var result = unchecked(left - right);
        if (((left ^ right) & (left ^ result)) < 0)
        {
            difference = 0;
            return false;
        }

        difference = result;
        return true;
    }

    public static bool TryMultiply(long left, long right, out long product)
    {
        var wide = (Int128)left * right;
        if (wide > long.MaxValue || wide < long.MinValue)
        {
            product = 0;
            return false;
        }

        product = (long)wide;
        return true;
    }

    public static bool TryPercentFloor(long amount, int percent, out long portion)
    {
        if (amount < 0 || percent < 0)
        {
            portion = 0;
            return false;
        }

        var wide = (Int128)amount * percent / PercentDenominator;
        if (wide > long.MaxValue)
        {
            portion = 0;
            return false;
        }

        portion = (long)wide;
        return true;
    }

    public static bool TryStackPayable(long unitPrice, int quantity, long fee, out long payable)
    {
        if (unitPrice < 0 || quantity <= 0 || fee < 0 || !TryMultiply(unitPrice, quantity, out var baseAmount))
        {
            payable = 0;
            return false;
        }

        return TryAdd(baseAmount, fee, out payable);
    }

    public static long SaturatingAdd(long left, long right)
    {
        if (TryAdd(left, right, out var sum))
        {
            return sum;
        }

        return right > 0 ? long.MaxValue : long.MinValue;
    }

    public static long SaturatingMultiply(long left, long right)
    {
        if (TryMultiply(left, right, out var product))
        {
            return product;
        }

        return (left < 0) == (right < 0) ? long.MaxValue : long.MinValue;
    }

    public static bool TryParse(ReadOnlySpan<char> text, out long amount)
    {
        amount = 0;
        var trimmed = text.Trim();
        if (trimmed.IsEmpty)
        {
            return false;
        }

        long value = 0;
        var groupLength = 0;
        var separatorSeen = false;
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (char.IsAsciiDigit(character))
            {
                if (!TryMultiply(value, DecimalBase, out var shifted) || !TryAdd(shifted, character - '0', out value))
                {
                    return false;
                }

                groupLength++;
                continue;
            }

            if (!IsGroupSeparator(character) || !IsCompleteGroup(groupLength, separatorSeen))
            {
                return false;
            }

            separatorSeen = true;
            groupLength = 0;
        }

        if (groupLength == 0 || (separatorSeen && groupLength != DigitGroupLength))
        {
            return false;
        }

        amount = value;
        return true;
    }

    private static bool IsCompleteGroup(int groupLength, bool separatorSeen)
    {
        if (separatorSeen)
        {
            return groupLength == DigitGroupLength;
        }

        return groupLength is > 0 and <= DigitGroupLength;
    }

    private static bool IsGroupSeparator(char character)
    {
        return character is ',' or '.' or '_' or ' ' or '\'' or ' ' or ' ';
    }
}
