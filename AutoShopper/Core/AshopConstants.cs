namespace AutoShopper.Core;

internal static class AshopConstants
{
    public const string PrimaryCommand = "/ashop";
    public const string AliasCommand = "/shopper";

    public const string LogPrefix = "[ASHOP]";

    public const int SaveThrottleMs = 500;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoShopper.Save";
    }
}
