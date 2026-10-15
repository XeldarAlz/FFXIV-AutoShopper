using AutoShopper.Core.Shopping.Model;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AutoShopper.Core.Shopping.Import;

internal static class MakePlaceParser
{
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        MaxDepth = MakePlaceFormat.MaximumDepth,
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonReaderOptions readerOptions = new()
    {
        MaxDepth = MakePlaceFormat.MaximumDepth,
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static bool LooksLikeMakePlace(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), readerOptions);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (IsPlacementList(reader.GetString()))
                {
                    return true;
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult(ImportFormat.MakePlace);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, documentOptions);
        }
        catch (JsonException)
        {
            result.Fail(ImportError.MalformedJson);
            return result;
        }

        using (document)
        {
            ReadLayout(document.RootElement, result);
        }

        return result;
    }

    public static string WallpaperName(ReadOnlySpan<char> assetPath)
    {
        var asset = assetPath[(assetPath.LastIndexOfAny('.', '/') + 1)..].Trim();
        var builder = new StringBuilder(asset.Length * 2);
        for (var index = 0; index < asset.Length; index++)
        {
            var character = asset[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(asset[index - 1]) && !char.IsWhiteSpace(asset[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return ShoppingLimits.CleanName(builder.ToString(), ShoppingLimits.MaximumItemNameLength);
    }

    private static void ReadLayout(JsonElement root, ImportResult result)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        var tally = new LayoutTally();
        var hasPlacements = false;
        foreach (var property in root.EnumerateObject())
        {
            if (IsPlacementList(property.Name))
            {
                hasPlacements = true;
                ReadPieces(property.Value, tally);
            }
            else if (!IsAny(property, MakePlaceFormat.LayoutSettingProperties))
            {
                result.IgnoredFields++;
            }
        }

        if (!hasPlacements)
        {
            result.Fail(ImportError.WrongFormat);
            return;
        }

        tally.WriteTo(result);
        if (result.Rows.Count + result.Dyes.Count > ShoppingLimits.MaximumImportRows)
        {
            result.Fail(ImportError.TooManyRows);
        }
        else if (result.Rows.Count == 0 && result.Dyes.Count == 0)
        {
            result.Fail(ImportError.Empty);
        }
    }

    private static void ReadPieces(JsonElement pieces, LayoutTally tally)
    {
        if (pieces.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var piece in pieces.EnumerateArray())
        {
            ReadPiece(piece, tally);
        }
    }

    private static void ReadPiece(JsonElement piece, LayoutTally tally)
    {
        if (piece.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        uint itemId = 0;
        string? name = null;
        JsonElement attachments = default;
        foreach (var property in piece.EnumerateObject())
        {
            var value = property.Value;
            if (JsonListParser.Is(property, MakePlaceFormat.ItemIdProperty))
            {
                itemId = ReadItemId(value);
            }
            else if (JsonListParser.Is(property, MakePlaceFormat.NameProperty))
            {
                name = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            else if (JsonListParser.Is(property, MakePlaceFormat.ColorProperty))
            {
                tally.AddColor(value);
            }
            else if (JsonListParser.Is(property, MakePlaceFormat.PropertiesProperty))
            {
                ReadProperties(value, tally);
            }
            else if (JsonListParser.Is(property, MakePlaceFormat.AttachmentsProperty))
            {
                attachments = value;
            }
        }

        if (itemId != 0)
        {
            tally.AddItem(itemId, name);
        }

        ReadPieces(attachments, tally);
    }

    private static void ReadProperties(JsonElement properties, LayoutTally tally)
    {
        if (properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (JsonListParser.Is(property, MakePlaceFormat.ColorProperty))
            {
                tally.AddColor(property.Value);
            }
            else if (JsonListParser.Is(property, MakePlaceFormat.MaterialProperty))
            {
                ReadMaterial(property.Value, tally);
            }
        }
    }

    private static void ReadMaterial(JsonElement material, LayoutTally tally)
    {
        if (material.ValueKind == JsonValueKind.String)
        {
            tally.AddNamed(WallpaperName(material.GetString()));
            return;
        }

        if (material.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        uint itemId = 0;
        string? name = null;
        foreach (var property in material.EnumerateObject())
        {
            if (JsonListParser.Is(property, MakePlaceFormat.ItemIdProperty))
            {
                itemId = ReadItemId(property.Value);
            }
            else if (JsonListParser.Is(property, MakePlaceFormat.NameProperty) && property.Value.ValueKind == JsonValueKind.String)
            {
                name = property.Value.GetString();
            }
        }

        if (itemId != 0)
        {
            tally.AddItem(itemId, name);
            return;
        }

        tally.AddNamed(ShoppingLimits.CleanName(name, ShoppingLimits.MaximumItemNameLength));
    }

    private static uint ReadItemId(JsonElement value)
        => JsonListParser.NumberOf(value, out var number) == NumberStatus.Valid && number is > 0 and <= uint.MaxValue ? (uint)number : 0;

    private static bool TryReadColor(JsonElement value, out uint color)
    {
        color = 0;
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString().AsSpan().Trim().TrimStart('#');
        return text.Length >= MakePlaceFormat.ColorDigits
            && uint.TryParse(text[..MakePlaceFormat.ColorDigits], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color);
    }

    private static bool IsPlacementList(string? name)
        => ImportFields.Is(name, MakePlaceFormat.InteriorFurnitureProperty)
            || ImportFields.Is(name, MakePlaceFormat.ExteriorFurnitureProperty)
            || ImportFields.Is(name, MakePlaceFormat.InteriorFixtureProperty)
            || ImportFields.Is(name, MakePlaceFormat.ExteriorFixtureProperty);

    private static bool IsAny(JsonProperty property, string[] names)
    {
        for (var index = 0; index < names.Length; index++)
        {
            if (JsonListParser.Is(property, names[index]))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Placement(uint itemId, string name)
    {
        public readonly uint ItemId = itemId;
        public readonly string Name = name;
        public int Count;
    }

    private sealed class LayoutTally
    {
        private readonly List<Placement> placements = [];
        private readonly Dictionary<uint, int> indexByItem = [];
        private readonly Dictionary<string, int> indexByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DyeImport> dyes = [];
        private readonly Dictionary<uint, int> indexByColor = [];

        public void AddItem(uint itemId, string? name)
        {
            if (!indexByItem.TryGetValue(itemId, out var index))
            {
                index = placements.Count;
                indexByItem.Add(itemId, index);
                placements.Add(new Placement(itemId, ShoppingLimits.CleanName(name, ShoppingLimits.MaximumItemNameLength)));
            }

            placements[index].Count++;
        }

        public void AddNamed(string name)
        {
            if (name.Length == 0)
            {
                return;
            }

            if (!indexByName.TryGetValue(name, out var index))
            {
                index = placements.Count;
                indexByName.Add(name, index);
                placements.Add(new Placement(0, name));
            }

            placements[index].Count++;
        }

        public void AddColor(JsonElement value)
        {
            if (!TryReadColor(value, out var color))
            {
                return;
            }

            if (indexByColor.TryGetValue(color, out var index))
            {
                dyes[index] = dyes[index] with { Count = dyes[index].Count + 1 };
                return;
            }

            indexByColor.Add(color, dyes.Count);
            dyes.Add(new DyeImport(color, 1));
        }

        public void WriteTo(ImportResult result)
        {
            for (var index = 0; index < placements.Count; index++)
            {
                var placement = placements[index];
                var label = placement.Name.Length > 0 ? placement.Name : placement.ItemId.ToString(CultureInfo.InvariantCulture);
                var row = new ImportRow(index + 1, TextLines.Clip(label));
                if (placement.ItemId != 0)
                {
                    row.RequestedItemId = placement.ItemId;
                }
                else
                {
                    row.Name = placement.Name;
                }

                ImportFields.SetQuantity(row, NumberStatus.Valid, placement.Count);
                result.Rows.Add(row);
            }

            result.Dyes.AddRange(dyes);
        }
    }
}
