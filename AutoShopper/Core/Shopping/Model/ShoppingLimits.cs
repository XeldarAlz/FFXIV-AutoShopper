namespace AutoShopper.Core.Shopping.Model;

public static class ShoppingLimits
{
    public const int MinimumQuantity = 1;
    public const int MaximumQuantity = 999_999;
    public const long MaximumUnitPrice = 999_999_999;
    public const int MaximumExcessAllowance = 999_999;

    public const int MaximumListNameLength = 64;
    public const int MaximumItemNameLength = 128;
    public const int MaximumLinesPerList = 1_000;

    public const int MaximumImportBytes = 256 * 1024;
    public const int MaximumImportRows = 1_000;

    private const int StackBufferLength = 256;

    public static bool IsValidQuantity(long quantity) => quantity is >= MinimumQuantity and <= MaximumQuantity;

    public static bool IsValidUnitPrice(long unitPrice) => unitPrice is >= 0 and <= MaximumUnitPrice;

    public static bool IsValidExcess(long excess) => excess is >= 0 and <= MaximumExcessAllowance;

    public static int ClampQuantity(long quantity) => (int)Math.Clamp(quantity, MinimumQuantity, MaximumQuantity);

    public static long ClampUnitPrice(long unitPrice) => Math.Clamp(unitPrice, 0, MaximumUnitPrice);

    public static int ClampExcess(long excess) => (int)Math.Clamp(excess, 0, MaximumExcessAllowance);

    // Names come from the clipboard and from hand-edited files, so control characters and runs of whitespace are
    // dropped before a name reaches the configuration or the window.
    public static string CleanName(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var span = text.AsSpan().Trim();
        Span<char> buffer = span.Length <= StackBufferLength ? stackalloc char[span.Length] : new char[span.Length];
        var length = 0;
        var previousWasSpace = false;
        for (var index = 0; index < span.Length && length < maximumLength; index++)
        {
            var character = span[index];
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    buffer[length++] = ' ';
                }

                previousWasSpace = true;
                continue;
            }

            if (char.IsControl(character))
            {
                continue;
            }

            buffer[length++] = character;
            previousWasSpace = false;
        }

        return new string(buffer[..length].TrimEnd());
    }
}
