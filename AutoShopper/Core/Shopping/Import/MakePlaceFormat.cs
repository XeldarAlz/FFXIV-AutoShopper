namespace AutoShopper.Core.Shopping.Import;

// MakePlace and the ReMakePlace plugin save a house as {"houseSize":"Small","interiorFixture":[{"level":"Basement",
// "type":"Wall","name":"...","itemId":20716,"color":""}],"interiorFurniture":[{"itemId":6569,"name":"...",
// "transform":{...},"properties":{"color":"B9A48900","material":"..."},"attachments":[...]}],"exteriorFixture":[...],
// "exteriorFurniture":[...],...}. Every placed piece is one unit of its item, items set on a table sit in its
// "attachments", "color" is the RGBA of the stain it is dyed with, and "material" is the wallpaper applied to it: an
// object with an item id from the plugin, or an asset path from the app. A fixture with item id 0 is the house's default.
internal static class MakePlaceFormat
{
    public const int MaximumDepth = 32;
    public const int ColorDigits = 6;

    public const string InteriorFurnitureProperty = "interiorFurniture";
    public const string ExteriorFurnitureProperty = "exteriorFurniture";
    public const string InteriorFixtureProperty = "interiorFixture";
    public const string ExteriorFixtureProperty = "exteriorFixture";

    public const string ItemIdProperty = "itemId";
    public const string NameProperty = "name";
    public const string PropertiesProperty = "properties";
    public const string AttachmentsProperty = "attachments";
    public const string ColorProperty = "color";
    public const string MaterialProperty = "material";

    public static readonly string[] LayoutSettingProperties =
    [
        "houseSize", "lightLevel", "interiorScale", "exteriorScale", "metaData", "properties", "playerTransform",
    ];
}
