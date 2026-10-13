using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;

namespace AutoShopper.Core.Recipes;

// The game's crafting recipes, read once off the draw thread the first time an import needs them, like the item
// catalog. The snapshot never changes afterwards, so any thread may read it.
internal sealed class RecipeExpander : IRecipeBook
{
    // Lumina's recipe sheet holds a little over twelve thousand rows.
    private const int ExpectedRecipes = 14_000;
    // Six item slots and two crystal slots.
    private const int IngredientSlots = 8;

    private readonly object gate = new();
    private Task? loading;
    private volatile Snapshot? snapshot;

    public bool IsReady => snapshot is not null;

    public bool LoadFailed { get; private set; }

    public Task WhenReady()
    {
        lock (gate)
        {
            loading ??= Task.Run(Load);
            return loading;
        }
    }

    public void EnsureLoading()
    {
        if (snapshot is null && loading is null)
        {
            WhenReady();
        }
    }

    public bool TryGetRecipe(uint recipeId, out CraftingRecipe recipe)
    {
        var current = snapshot;
        if (current is not null && current.ById.TryGetValue(recipeId, out recipe))
        {
            return true;
        }

        recipe = default;
        return false;
    }

    public bool TryGetRecipeForItem(uint itemId, out CraftingRecipe recipe)
    {
        var current = snapshot;
        if (current is not null && current.RecipeIdByItem.TryGetValue(itemId, out var recipeId))
        {
            return current.ById.TryGetValue(recipeId, out recipe);
        }

        recipe = default;
        return false;
    }

    // Call only once IsReady is true: before that every recipe reads as unknown.
    public ImportResult Expand(ImportResult crafting, ExpansionMode mode, IItemTraits traits)
        => ListImporter.Expand(crafting, mode, this, traits);

    private void Load()
    {
        try
        {
            var built = Build();
            snapshot = built;
            RunLog.Info($"Recipe book ready: {built.ById.Count} recipes.");
        }
        catch (Exception exception)
        {
            LoadFailed = true;
            RunLog.Error(exception, "The recipe book failed to load");
        }
    }

    private static Snapshot Build()
    {
        var sheet = Svc.Data.GetExcelSheet<Recipe>();
        var byId = new Dictionary<uint, CraftingRecipe>(ExpectedRecipes);
        var recipeIdByItem = new Dictionary<uint, uint>(ExpectedRecipes);
        var ingredients = new List<RecipeIngredient>(IngredientSlots);
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            var resultItemId = row.ItemResult.RowId;
            if (resultItemId == 0 || row.AmountResult == 0)
            {
                continue;
            }

            ingredients.Clear();
            for (var slot = 0; slot < row.Ingredient.Count && slot < row.AmountIngredient.Count; slot++)
            {
                var ingredientId = row.Ingredient[slot].RowId;
                var amount = row.AmountIngredient[slot];
                if (ingredientId != 0 && amount != 0)
                {
                    ingredients.Add(new RecipeIngredient(ingredientId, amount));
                }
            }

            if (ingredients.Count == 0)
            {
                continue;
            }

            byId[row.RowId] = new CraftingRecipe(row.RowId, resultItemId, row.AmountResult, ingredients.ToArray());
            if (!recipeIdByItem.TryGetValue(resultItemId, out var known) || row.RowId < known)
            {
                recipeIdByItem[resultItemId] = row.RowId;
            }
        }

        return new Snapshot(byId, recipeIdByItem);
    }

    private sealed class Snapshot(Dictionary<uint, CraftingRecipe> byId, Dictionary<uint, uint> recipeIdByItem)
    {
        public readonly Dictionary<uint, CraftingRecipe> ById = byId;
        public readonly Dictionary<uint, uint> RecipeIdByItem = recipeIdByItem;
    }
}
