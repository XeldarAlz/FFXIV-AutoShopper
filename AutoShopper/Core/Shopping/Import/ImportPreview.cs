namespace AutoShopper.Core.Shopping.Import;

// One line of the import preview: a row, or the title of the section the rows under it were copied from.
public readonly record struct ImportPreviewEntry(int RowIndex, string? Header)
{
    public bool IsHeader => Header is not null;
}

public static class ImportPreview
{
    // A title goes above the first row of each run of rows from one section; rows outside any section get none.
    public static ImportPreviewEntry[] Entries(IReadOnlyList<ImportRow> rows)
    {
        var headers = 0;
        string? previous = null;
        for (var index = 0; index < rows.Count; index++)
        {
            var section = rows[index].Section;
            if (StartsSection(section, previous))
            {
                headers++;
            }

            previous = section;
        }

        var entries = new ImportPreviewEntry[rows.Count + headers];
        var position = 0;
        previous = null;
        for (var index = 0; index < rows.Count; index++)
        {
            var section = rows[index].Section;
            if (StartsSection(section, previous))
            {
                entries[position++] = new ImportPreviewEntry(index, section);
            }

            entries[position++] = new ImportPreviewEntry(index, null);
            previous = section;
        }

        return entries;
    }

    private static bool StartsSection(string? section, string? previous)
        => !string.IsNullOrEmpty(section) && !string.Equals(section, previous, StringComparison.Ordinal);
}
