using AutoShopper.Core.Shopping.Import;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoShopper.Core.Items;

internal sealed class DyeChart(ItemCatalog catalog) : IDyeChart
{
    private const int MaximumSquaredDistance = 192;
    private const int Channels = 3;
    private const int ChannelBits = 8;
    private const uint ChannelMask = 0xFF;

    private Entry[]? entries;

    public bool TryFind(uint color, out DyeMatch match)
    {
        match = default;
        if (!catalog.IsReady)
        {
            return false;
        }

        var chart = entries ??= Build();
        var nearest = -1;
        var nearestDistance = MaximumSquaredDistance + 1;
        for (var index = 0; index < chart.Length; index++)
        {
            var entry = chart[index];
            if (entry.Color == color)
            {
                match = new DyeMatch(entry.ItemId, entry.Name);
                return true;
            }

            if (!entry.ForHousing)
            {
                continue;
            }

            var distance = SquaredDistance(entry.Color, color);
            if (distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }

        if (nearest < 0)
        {
            return false;
        }

        match = new DyeMatch(chart[nearest].ItemId, chart[nearest].Name);
        return true;
    }

    private Entry[] Build()
    {
        var sheet = Svc.Data.GetExcelSheet<Stain>();
        var built = new List<Entry>(sheet.Count);
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            var itemId = DyeItemOf(row);
            if (row.Color == 0 || itemId == 0)
            {
                continue;
            }

            built.Add(new Entry(row.Color, itemId, row.Name.ExtractText().Trim(), row.IsHousingApplicable));
        }

        return [.. built];
    }

    private uint DyeItemOf(Stain row)
    {
        uint fallback = 0;
        for (var index = 0; index < row.Item.Count; index++)
        {
            var itemId = row.Item[index].RowId;
            if (itemId == 0)
            {
                continue;
            }

            if (catalog.IsMarketable(itemId))
            {
                return itemId;
            }

            if (fallback == 0)
            {
                fallback = itemId;
            }
        }

        return fallback;
    }

    private static int SquaredDistance(uint left, uint right)
    {
        var distance = 0;
        for (var shift = 0; shift < ChannelBits * Channels; shift += ChannelBits)
        {
            var difference = (int)((left >> shift) & ChannelMask) - (int)((right >> shift) & ChannelMask);
            distance += difference * difference;
        }

        return distance;
    }

    private readonly record struct Entry(uint Color, uint ItemId, string Name, bool ForHousing);
}
