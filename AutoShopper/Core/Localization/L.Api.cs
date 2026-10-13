namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Api
    {
        public static readonly LocString QuoteRefused = new("api.quoteRefused", "Cannot price these items: {0}.");
        public static readonly LocString ReasonSchema = new("api.reason.schema", "the request uses a Buying API schema this version of Auto Shopper does not know");
        public static readonly LocString ReasonInvalid = new("api.reason.invalid", "the request is not valid ({0})");
        public static readonly LocString ReasonConsent = new("api.reason.consent", "allow {0} on Auto Shopper's Shop page first");
        public static readonly LocString ReasonDenied = new("api.reason.denied", "{0} is denied; allow it in Auto Shopper's Settings, General");
    }
}
