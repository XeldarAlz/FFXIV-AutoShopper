namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Api
    {
        public static readonly LocString ConsentTitle = new("api.consent.title", "{0} wants to buy through Auto Shopper.");
        public static readonly LocString ConsentBody = new("api.consent.body", "It sends its own shopping list and a spend cap for each run. Your worlds, gil reserve and purchase checks still apply, and you can revoke this in Settings, General.");
        public static readonly LocString ConsentFacts = new("api.consent.facts", "Internal name: {0}. This run asks to spend up to {1}. A plugin's runs keep to the cap they send, in place of your run spend cap.");
        public static readonly LocString Allow = new("api.allow", "Allow");
        public static readonly LocString Deny = new("api.deny", "Deny");
        public static readonly LocString Revoke = new("api.revoke", "Revoke");
        public static readonly LocString Allowed = new("api.allowed", "Allowed");
        public static readonly LocString Denied = new("api.denied", "Denied");
        public static readonly LocString Group = new("api.group", "Other plugins");
        public static readonly LocString GroupNone = new("api.groupNone", "No plugin has asked to buy through Auto Shopper yet.");
        public static readonly LocString AllowedHelp = new("api.allowedHelp", "It can price items and start runs through Auto Shopper. Revoke stops a run it has under way and makes it ask again.");
        public static readonly LocString DeniedHelp = new("api.deniedHelp", "Its runs are refused until you allow it.");
        public static readonly LocString QuoteRefused = new("api.quoteRefused", "Cannot price these items: {0}.");
        public static readonly LocString ReasonSchema = new("api.reason.schema", "the request uses a Buying API schema this version of Auto Shopper does not know");
        public static readonly LocString ReasonInvalid = new("api.reason.invalid", "the request is not valid ({0})");
        public static readonly LocString ReasonConsent = new("api.reason.consent", "allow {0} on Auto Shopper's Shop page first");
        public static readonly LocString ReasonDenied = new("api.reason.denied", "{0} is denied; allow it in Auto Shopper's Settings, General");
        public static readonly LocString ReasonUnloading = new("api.reason.unloading", "Auto Shopper is unloading");
        public static readonly LocString ReasonQuotesWaiting = new("api.reason.quotesWaiting", "this plugin already has {0} quotes waiting for their turn at the price fetch");
    }
}
