namespace AutoShopper.Core.Api.Model;

internal static class ApiContract
{
    public const int Schema = 1;
    public const int Version = 1;

    public const int MaximumEntries = 100;
    public const int MaximumQuoteQuantity = 9_999;
    public const long MaximumSpend = 999_999_999;
    public const int MaximumNameLength = 64;

    public const int QuoteCapacity = 16;
    public const int RunResultCapacity = 8;

    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(10);
}
