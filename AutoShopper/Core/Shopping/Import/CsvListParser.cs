using AutoShopper.Core.Shopping.Model;
using System.Text;

namespace AutoShopper.Core.Shopping.Import;

// The documented header is ItemId,Name,Quality,Mode,Quantity,MaxUnitPrice,ExcessAllowance. Columns may come in any
// order and only ItemId or Name is required; when a row carries both, the item id decides and the name is ignored.
internal static class CsvListParser
{
    public const string Header = "ItemId,Name,Quality,Mode,Quantity,MaxUnitPrice,ExcessAllowance";

    private enum Column : byte
    {
        Ignored,
        ItemId,
        Name,
        Quality,
        Mode,
        Quantity,
        MaxUnitPrice,
        ExcessAllowance,
    }

    public static bool LooksLikeHeader(ReadOnlySpan<char> line)
    {
        var fields = new List<string>();
        Csv.Split(line, Csv.DetectDelimiter(line), fields, new StringBuilder());
        if (fields.Count < 2)
        {
            return false;
        }

        for (var index = 0; index < fields.Count; index++)
        {
            if (ColumnOf(fields[index]) is Column.ItemId or Column.Name)
            {
                return true;
            }
        }

        return false;
    }

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult(ImportFormat.Csv);
        var position = 0;
        var lineNumber = 0;
        var fields = new List<string>();
        var scratch = new StringBuilder();
        Column[]? columns = null;
        var delimiter = Csv.Comma;

        while (TextLines.Next(text, ref position, out var line))
        {
            lineNumber++;
            if (line.IsWhiteSpace())
            {
                continue;
            }

            if (columns is null)
            {
                delimiter = Csv.DetectDelimiter(line);
                Csv.Split(line, delimiter, fields, scratch);
                columns = MapColumns(fields, result);
                if (columns is null)
                {
                    result.Fail(ImportError.MissingColumns);
                    return result;
                }

                continue;
            }

            if (result.Rows.Count >= ShoppingLimits.MaximumImportRows)
            {
                result.Fail(ImportError.TooManyRows);
                return result;
            }

            Csv.Split(line, delimiter, fields, scratch);
            result.Rows.Add(ReadRow(lineNumber, line, columns, fields));
        }

        if (result.Rows.Count == 0)
        {
            result.Fail(ImportError.Empty);
        }

        return result;
    }

    private static Column[]? MapColumns(List<string> fields, ImportResult result)
    {
        var columns = new Column[fields.Count];
        var hasItem = false;
        for (var index = 0; index < fields.Count; index++)
        {
            columns[index] = ColumnOf(fields[index]);
            if (columns[index] == Column.Ignored)
            {
                result.IgnoredFields++;
            }

            hasItem |= columns[index] is Column.ItemId or Column.Name;
        }

        return hasItem ? columns : null;
    }

    private static Column ColumnOf(string field)
    {
        Span<char> buffer = stackalloc char[Math.Min(field.Length, 32)];
        var length = 0;
        for (var index = 0; index < field.Length && length < buffer.Length; index++)
        {
            var character = field[index];
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        var key = buffer[..length];
        if (ImportFields.Is(key, "itemid") || ImportFields.Is(key, "id"))
        {
            return Column.ItemId;
        }

        if (ImportFields.Is(key, "name") || ImportFields.Is(key, "item") || ImportFields.Is(key, "itemname"))
        {
            return Column.Name;
        }

        if (ImportFields.Is(key, "quality"))
        {
            return Column.Quality;
        }

        if (ImportFields.Is(key, "mode"))
        {
            return Column.Mode;
        }

        if (ImportFields.Is(key, "quantity") || ImportFields.Is(key, "qty") || ImportFields.Is(key, "amount") || ImportFields.Is(key, "count"))
        {
            return Column.Quantity;
        }

        if (ImportFields.Is(key, "maxunitprice") || ImportFields.Is(key, "unitcap") || ImportFields.Is(key, "maxprice"))
        {
            return Column.MaxUnitPrice;
        }

        return ImportFields.Is(key, "excessallowance") || ImportFields.Is(key, "excess") ? Column.ExcessAllowance : Column.Ignored;
    }

    private static ImportRow ReadRow(int lineNumber, ReadOnlySpan<char> line, Column[] columns, List<string> fields)
    {
        var row = new ImportRow(lineNumber, TextLines.Clip(line));
        var count = Math.Min(columns.Length, fields.Count);
        for (var index = 0; index < count; index++)
        {
            ReadField(row, columns[index], fields[index]);
        }

        if (row.RequestedItemId == 0 && row.Name.Length == 0)
        {
            row.Reject(ImportRowProblem.MissingItem);
        }

        return row;
    }

    private static void ReadField(ImportRow row, Column column, string value)
    {
        long number;
        switch (column)
        {
            case Column.ItemId:
                var status = ImportFields.TryParseWhole(value, out number);
                if (status == NumberStatus.Valid && number is > 0 and <= uint.MaxValue)
                {
                    row.RequestedItemId = (uint)number;
                }
                else if (status != NumberStatus.Empty)
                {
                    row.Reject(ImportRowProblem.MalformedNumber);
                }

                break;
            case Column.Name:
                if (value.Length > ShoppingLimits.MaximumItemNameLength)
                {
                    row.Reject(ImportRowProblem.NameTooLong);
                }
                else
                {
                    row.Name = ShoppingLimits.CleanName(value, ShoppingLimits.MaximumItemNameLength);
                }

                break;
            case Column.Quality:
                if (ImportFields.TryParseQuality(value, out var quality))
                {
                    row.Quality = quality;
                }
                else
                {
                    row.Reject(ImportRowProblem.UnknownQuality);
                }

                break;
            case Column.Mode:
                if (ImportFields.TryParseMode(value, out var mode))
                {
                    row.Mode = mode;
                }
                else
                {
                    row.Reject(ImportRowProblem.UnknownMode);
                }

                break;
            case Column.Quantity:
                ImportFields.SetQuantity(row, ImportFields.TryParseWhole(value, out number), number);
                break;
            case Column.MaxUnitPrice:
                ImportFields.SetUnitCap(row, ImportFields.TryParseWhole(value, out number), number);
                break;
            case Column.ExcessAllowance:
                ImportFields.SetExcess(row, ImportFields.TryParseWhole(value, out number), number);
                break;
        }
    }
}
