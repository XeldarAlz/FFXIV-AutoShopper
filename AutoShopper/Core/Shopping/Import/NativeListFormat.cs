namespace AutoShopper.Core.Shopping.Import;

// {"format":"AutoShopper.List","version":1,"name":"...","lines":[{"id":"...","itemId":5057,"name":"...",
// "quality":"Any","mode":"Buy","quantity":3,"maxUnitPrice":0,"excessAllowance":0}]}
internal static class NativeListFormat
{
    public const string FormatName = "AutoShopper.List";
    public const int Version = 1;

    public const string FormatProperty = "format";
    public const string VersionProperty = "version";
    public const string NameProperty = "name";
    public const string LinesProperty = "lines";

    public const string LineIdProperty = "id";
    public const string ItemIdProperty = "itemId";
    public const string ItemNameProperty = "name";
    public const string QualityProperty = "quality";
    public const string ModeProperty = "mode";
    public const string QuantityProperty = "quantity";
    public const string MaxUnitPriceProperty = "maxUnitPrice";
    public const string ExcessAllowanceProperty = "excessAllowance";
}
