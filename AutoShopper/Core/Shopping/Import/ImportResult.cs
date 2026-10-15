namespace AutoShopper.Core.Shopping.Import;

public sealed class ImportResult
{
    public ImportResult(ImportFormat format)
    {
        Format = format;
    }

    public ImportFormat Format { get; }

    public ImportError Error { get; private set; }

    public List<ImportRow> Rows { get; } = [];

    // A crafting list holds recipes rather than items; it fills Rows only once it is expanded.
    public List<CraftingImport> Crafts { get; } = [];

    public bool IsCraftingList => Crafts.Count > 0;

    public List<DyeImport> Dyes { get; } = [];

    public string? ListName { get; internal set; }

    // CSV columns and JSON properties this version does not read.
    public int IgnoredFields { get; internal set; }

    public static ImportResult Failed(ImportFormat format, ImportError error)
    {
        var result = new ImportResult(format);
        result.Fail(error);
        return result;
    }

    public int Count(ImportRowStatus status)
    {
        var count = 0;
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].Status == status)
            {
                count++;
            }
        }

        return count;
    }

    internal void Fail(ImportError error)
    {
        Error = error;
        Rows.Clear();
        Crafts.Clear();
        Dyes.Clear();
    }
}
