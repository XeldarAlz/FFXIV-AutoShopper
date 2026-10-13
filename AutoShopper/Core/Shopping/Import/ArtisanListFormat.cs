namespace AutoShopper.Core.Shopping.Import;

// Artisan's "Export List" button copies the list as JSON with Newtonsoft's default names:
// {"ID":1,"Name":"...","Recipes":[{"ID":35596,"Quantity":3,"ListItemOptions":{"NQOnly":false,"Skipping":false}}],
// "ExpandedList":[...],"SkipIfEnough":false,...}. Each recipe ID is a row of the game's Recipe sheet and Quantity counts
// crafts, not items. The export carries no version number, so the schema is told apart by shape: lists saved before
// "Recipes" existed kept one recipe id per craft in "Items" with options keyed by recipe, and are refused.
internal static class ArtisanListFormat
{
    public const string NameProperty = "Name";
    public const string RecipesProperty = "Recipes";
    public const string ExpandedListProperty = "ExpandedList";
    public const string LegacyItemsProperty = "Items";
    public const string OptionsProperty = "ListItemOptions";

    public const string RecipeIdProperty = "ID";
    public const string QuantityProperty = "Quantity";
    public const string SkippingProperty = "Skipping";
    public const string NormalQualityOnlyProperty = "NQOnly";

    // Artisan reads these while it crafts; none of them changes what has to be bought.
    public static readonly string[] SettingsProperties =
    [
        "ID", "ExpandedList", "SkipIfEnough", "SkipLiteral", "Materia", "Repair", "RepairPercent", "AddAsQuickSynth",
        "TidyAfter", "OnlyRestockNonCrafted", "IsPremade",
    ];

    // Artisan has never written a version; a list that carries one comes from a schema this parser has not seen.
    public static readonly string[] VersionProperties = ["Version", "SchemaVersion", "FormatVersion"];
}
