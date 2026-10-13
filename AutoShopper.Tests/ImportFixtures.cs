using System.Globalization;
using System.IO;
using AutoShopper.Core.Recipes;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Tests;

// The Artisan files are written by hand from the NewCraftingList schema that PunishXIV/Artisan 247c2df serialises with
// Newtonsoft's defaults; the Teamcraft files follow the "Copy as text" templates in ffxiv-teamcraft 5340582. No export was
// captured from either tool. The recipe ids and item ids below are this workshop's own, not the game's.
internal static class ImportFixtures
{
    public const uint FireShard = 2;
    public const uint WindShard = 4;
    public const uint Sword = 40001;
    public const uint Ingot = 40002;
    public const uint Ore = 40003;
    public const uint Grip = 40004;
    public const uint Rivets = 40005;
    public const uint Leather = 40006;
    public const uint Shield = 40007;

    public const uint SwordRecipe = 31001;
    public const uint IngotRecipe = 31002;
    public const uint RivetsRecipe = 31003;
    public const uint GripRecipe = 31004;
    public const uint ShieldRecipe = 31005;
    public const uint IngotRecipeOfAnotherClass = 31010;

    public const string ArtisanList = "artisan-list.json";
    public const string ArtisanLegacyList = "artisan-legacy-list.json";
    public const string TeamcraftWholeList = "teamcraft-whole-list.txt";
    public const string TeamcraftCrystalsPanel = "teamcraft-crystals-panel.txt";

    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ImportFixtures", name));

    // A sword needs ingots and an untradable grip; rivets come three to a craft; a shield needs rivets and an ingot.
    public static FakeRecipeBook Workshop() => new(
        [
            Recipe(SwordRecipe, Sword, 1, (Ingot, 2), (Grip, 1), (FireShard, 3)),
            Recipe(IngotRecipe, Ingot, 1, (Ore, 3), (FireShard, 1)),
            Recipe(RivetsRecipe, Rivets, 3, (Ingot, 1), (FireShard, 1)),
            Recipe(GripRecipe, Grip, 1, (Leather, 1), (WindShard, 1)),
            Recipe(ShieldRecipe, Shield, 1, (Rivets, 7), (Ingot, 1), (WindShard, 2)),
            Recipe(IngotRecipeOfAnotherClass, Ingot, 1, (Ore, 4)),
        ],
        [Grip]);

    public static CraftingRecipe Recipe(uint recipeId, uint resultItemId, int yield, params (uint ItemId, int Amount)[] ingredients)
    {
        var parts = new RecipeIngredient[ingredients.Length];
        for (var index = 0; index < ingredients.Length; index++)
        {
            parts[index] = new RecipeIngredient(ingredients[index].ItemId, ingredients[index].Amount);
        }

        return new CraftingRecipe(recipeId, resultItemId, yield, parts);
    }

    public static Dictionary<uint, long> Quantities(ExpansionResult result)
    {
        var quantities = new Dictionary<uint, long>();
        for (var index = 0; index < result.Demands.Length; index++)
        {
            quantities.Add(result.Demands[index].ItemId, result.Demands[index].Quantity);
        }

        return quantities;
    }

    public static Dictionary<uint, int> ReadyQuantities(ImportResult result)
    {
        var quantities = new Dictionary<uint, int>();
        for (var index = 0; index < result.Rows.Count; index++)
        {
            var row = result.Rows[index];
            if (row.RequestedItemId != 0 && row.Status != ImportRowStatus.Invalid)
            {
                quantities.Add(row.RequestedItemId, row.Quantity);
            }
        }

        return quantities;
    }

    public static ImportRow? RowFor(ImportResult result, uint itemId)
    {
        for (var index = 0; index < result.Rows.Count; index++)
        {
            if (result.Rows[index].RequestedItemId == itemId)
            {
                return result.Rows[index];
            }
        }

        return null;
    }

    public static string ArtisanJson(int entryCount, uint firstRecipeId, int crafts)
    {
        var builder = new System.Text.StringBuilder("{\"ID\":1,\"Name\":\"Generated\",\"Recipes\":[");
        for (var index = 0; index < entryCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(CultureInfo.InvariantCulture, $"{{\"ID\":{firstRecipeId + (uint)index},\"Quantity\":{crafts},\"ListItemOptions\":{{\"NQOnly\":false,\"Skipping\":false}}}}");
        }

        return builder.Append("]}").ToString();
    }
}

internal sealed class FakeRecipeBook : IRecipeBook, IItemTraits
{
    private readonly Dictionary<uint, CraftingRecipe> byId = [];
    private readonly Dictionary<uint, CraftingRecipe> byItem = [];
    private readonly HashSet<uint> unmarketable;

    public FakeRecipeBook(CraftingRecipe[] recipes, uint[] unmarketableItems)
    {
        for (var index = 0; index < recipes.Length; index++)
        {
            var recipe = recipes[index];
            byId.Add(recipe.RecipeId, recipe);
            if (!byItem.TryGetValue(recipe.ResultItemId, out var known) || recipe.RecipeId < known.RecipeId)
            {
                byItem[recipe.ResultItemId] = recipe;
            }
        }

        unmarketable = [.. unmarketableItems];
    }

    public bool TryGetRecipe(uint recipeId, out CraftingRecipe recipe) => byId.TryGetValue(recipeId, out recipe);

    public bool TryGetRecipeForItem(uint itemId, out CraftingRecipe recipe) => byItem.TryGetValue(itemId, out recipe);

    public bool IsMarketable(uint itemId) => !unmarketable.Contains(itemId);

    public bool CanBeHighQuality(uint itemId) => false;
}

internal sealed class FakeItemResolver(params uint[] marketableItems) : IItemNameResolver
{
    private readonly HashSet<uint> known = [.. marketableItems];

    public NameResolution Resolve(string nameOrId)
        => uint.TryParse(nameOrId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId) && known.Contains(itemId)
            ? NameResolution.Unique(itemId)
            : NameResolution.None;
}
