using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using ECommons.DalamudServices;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace AutoShopper.Core.Shopping;

// /ashop add <item name or id> [quantity] adds a Buy line at Any quality to the selected list, and makes a list first
// when there is none. The item data loads off the framework thread on first use, so the add completes once it is ready.
internal static class AddItemCommand
{
    private const int CandidatesShown = 5;
    private const string CandidateSeparator = ", ";

    public static void Handle(string arguments, ItemCatalog catalog, ShoppingListStore store)
    {
        if (arguments.Length == 0)
        {
            PrintError(Loc.T(L.Lists.CommandUsage));
            return;
        }

        _ = AddWhenReady(arguments, catalog, store);
    }

    private static async Task AddWhenReady(string arguments, ItemCatalog catalog, ShoppingListStore store)
    {
        try
        {
            await catalog.WhenReady();
            await Svc.Framework.RunOnFrameworkThread(() => Add(arguments, catalog, store));
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Adding an item from the command failed");
        }
    }

    private static void Add(string arguments, ItemCatalog catalog, ShoppingListStore store)
    {
        if (!catalog.IsReady)
        {
            PrintError(Loc.T(L.Lists.CommandFailed));
            return;
        }

        var name = SplitQuantity(arguments, out var quantityStatus, out var quantity);
        var resolution = catalog.Resolve(name);
        if (resolution.Status == ResolutionStatus.Unknown && name.Length != arguments.Length)
        {
            var whole = catalog.Resolve(arguments);
            if (whole.Status != ResolutionStatus.Unknown)
            {
                resolution = whole;
                name = arguments;
                quantityStatus = NumberStatus.Valid;
                quantity = ShoppingLimits.MinimumQuantity;
            }
        }

        if (resolution.Status == ResolutionStatus.Unknown)
        {
            PrintError(Loc.T(L.Lists.CommandUnknown, name));
            return;
        }

        if (resolution.Status == ResolutionStatus.Ambiguous)
        {
            PrintError(Loc.T(L.Lists.CommandAmbiguous, name, CandidateList(resolution.Candidates, catalog)));
            return;
        }

        if (quantityStatus != NumberStatus.Valid || !ShoppingLimits.IsValidQuantity(quantity))
        {
            PrintError(Loc.T(L.Lists.CommandQuantity));
            return;
        }

        Apply(store.Selected ?? store.CreateList(), resolution.ItemId, (int)quantity, catalog, store);
    }

    private static void Apply(ShoppingList list, uint itemId, int quantity, ItemCatalog catalog, ShoppingListStore store)
    {
        var itemName = catalog.TryGetName(itemId, out var found) ? found : itemId.ToString(CultureInfo.InvariantCulture);
        var result = store.AddItem(list, itemId, quantity, out var line);
        RunLog.Info($"Command add: {itemName} ({itemId}) x{quantity} on '{list.Name}': {result}.");
        switch (result)
        {
            case AddLineResult.Added:
                Print(Loc.T(L.Lists.CommandAdded, quantity.ToString("N0", Loc.Culture), itemName, list.Name));
                break;
            case AddLineResult.Merged:
                Print(Loc.T(L.Lists.CommandMerged, itemName, list.Name, line!.Quantity.ToString("N0", Loc.Culture)));
                break;
            default:
                PrintError(Loc.T(L.Lists.CommandFull, list.Name, ShoppingLimits.MaximumLinesPerList.ToString("N0", Loc.Culture)));
                break;
        }
    }

    // A last word that is a whole number is the quantity; everything before it is the item.
    private static string SplitQuantity(string arguments, out NumberStatus status, out long quantity)
    {
        status = NumberStatus.Valid;
        quantity = ShoppingLimits.MinimumQuantity;
        var lastSpace = arguments.LastIndexOf(' ');
        if (lastSpace <= 0)
        {
            return arguments;
        }

        var token = arguments.AsSpan(lastSpace + 1);
        if (token.IsEmpty || !char.IsAsciiDigit(token[0]))
        {
            return arguments;
        }

        var parsed = ImportFields.TryParseWhole(token, out var value);
        if (parsed == NumberStatus.Malformed)
        {
            return arguments;
        }

        status = parsed;
        quantity = value;
        return arguments[..lastSpace].TrimEnd();
    }

    private static string CandidateList(uint[] candidates, ItemCatalog catalog)
    {
        var builder = new StringBuilder();
        var shown = Math.Min(candidates.Length, CandidatesShown);
        for (var index = 0; index < shown; index++)
        {
            if (index > 0)
            {
                builder.Append(CandidateSeparator);
            }

            var name = catalog.TryGetName(candidates[index], out var found) ? found : string.Empty;
            builder.Append(Loc.T(L.Lists.Candidate, name, candidates[index]));
        }

        return builder.ToString();
    }

    private static void Print(string message) => Svc.Chat.Print($"{AshopConstants.LogPrefix} {message}");

    private static void PrintError(string message) => Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {message}");
}
