namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Lists
    {
        public static readonly LocString Title = new("lists.title", "Lists");
        public static readonly LocString NewList = new("lists.new", "New list");
        public static readonly LocString DefaultName = new("lists.defaultName", "Shopping list");
        public static readonly LocString DefaultNameNumbered = new("lists.defaultNameNumbered", "Shopping list {0}");
        public static readonly LocString CopyName = new("lists.copyName", "{0} (copy)");
        public static readonly LocString NoLists = new("lists.noLists", "No lists yet.");
        public static readonly LocString Archived = new("lists.archived", "Archived");
        public static readonly LocString ShowArchived = new("lists.showArchived", "Archived ({0})");
        public static readonly LocPlural LineCount = new("lists.lineCount", "{0} line", "{0} lines");
        public static readonly LocPlural UnitCount = new("lists.unitCount", "{0} unit", "{0} units");
        public static readonly LocPlural NeedsFix = new("lists.needsFix", "{0} line needs a fix", "{0} lines need a fix");
        public static readonly LocPlural Warnings = new("lists.warnings", "{0} line counts together with another", "{0} lines count together with others");
        public static readonly LocString EmptyTitle = new("lists.emptyTitle", "Start a shopping list");
        public static readonly LocString EmptyBody = new("lists.emptyBody", "Write down what you want from the market board. Nothing is bought from this page.");
        public static readonly LocString EmptyList = new("lists.emptyList", "This list is empty. Search above to add an item, or import a list.");
        public static readonly LocString Rename = new("lists.rename", "Rename");
        public static readonly LocString NameHint = new("lists.nameHint", "List name");
        public static readonly LocString Duplicate = new("lists.duplicate", "Duplicate");
        public static readonly LocString Archive = new("lists.archive", "Archive");
        public static readonly LocString Unarchive = new("lists.unarchive", "Restore from the archive");
        public static readonly LocString Delete = new("lists.delete", "Delete");
        public static readonly LocString DeleteQuestion = new("lists.deleteQuestion", "Delete “{0}” for good?");
        public static readonly LocString DeleteYes = new("lists.deleteYes", "Yes, delete");
        public static readonly LocString AddHint = new("lists.addHint", "Add an item: type its name or item id");
        public static readonly LocString Loading = new("lists.loading", "Loading item data…");
        public static readonly LocString LoadFailed = new("lists.loadFailed", "Item data could not be loaded. The console has the details.");
        public static readonly LocString ListFull = new("lists.listFull", "A list holds at most {0} lines.");
        public static readonly LocString Added = new("lists.added", "Added {0}");
        public static readonly LocString Merged = new("lists.merged", "{0} is now {1}");
        public static readonly LocString ItemFallback = new("lists.itemFallback", "Item #{0}");

        public static readonly LocString ColumnItem = new("lists.column.item", "Item");
        public static readonly LocString ColumnQuality = new("lists.column.quality", "Quality");
        public static readonly LocString ColumnMode = new("lists.column.mode", "Mode");
        public static readonly LocString ColumnQuantity = new("lists.column.quantity", "Quantity");
        public static readonly LocString ColumnOwned = new("lists.column.owned", "Owned");
        public static readonly LocString ColumnCap = new("lists.column.cap", "Max/unit incl. fee");
        public static readonly LocString ColumnExcess = new("lists.column.excess", "Extra");
        public static readonly LocString SortHint = new("lists.sortHint", "Click to sort. Sorting changes only this view, never the list.");

        public static readonly LocString QualityAny = new("lists.quality.any", "Any");
        public static readonly LocString QualityHigh = new("lists.quality.high", "HQ");
        public static readonly LocString QualityNormal = new("lists.quality.normal", "NQ");
        public static readonly LocString QualityHelp = new("lists.qualityHelp", "Any takes HQ or NQ. HQ and NQ take only that quality.");
        public static readonly LocString NoHighQuality = new("lists.noHighQuality", "This item has no HQ version.");
        public static readonly LocString ModeBuy = new("lists.mode.buy", "Buy this many");
        public static readonly LocString ModeBuyHelp = new("lists.mode.buyHelp", "Buy the quantity during the run, whatever you already own.");
        public static readonly LocString ModeReach = new("lists.mode.reach", "Top up to this many");
        public static readonly LocString ModeReachHelp = new("lists.mode.reachHelp", "Buy only what is missing for you to own the quantity.");
        public static readonly LocString CapHint = new("lists.capHint", "no cap");
        public static readonly LocString CapHelp = new("lists.capHelp", "The most this line may pay for one unit, including the 5% market fee. Leave it empty for no cap of its own; the run's spending limit still applies.");
        public static readonly LocString ExcessHelp = new("lists.excessHelp", "How many units over the quantity the run may buy when only bigger stacks are listed. 0 never buys more than asked.");
        public static readonly LocString OwnedHelp = new("lists.ownedHelp", "Counted in your bags, crystals, armory chest and equipped gear. Saddlebags and retainers are not counted.");
        public static readonly LocString OwnedUnknown = new("lists.ownedUnknown", "Log in to see what you own.");
        public static readonly LocString Need = new("lists.need", "need {0}");
        public static readonly LocString Done = new("lists.done", "done");
        public static readonly LocString InventoryNote = new("lists.inventoryNote", "Owned counts cover your bags, crystals, armory chest and equipped gear. Saddlebags and retainers are not counted.");
        public static readonly LocString RemoveLine = new("lists.removeLine", "Remove this line");
        public static readonly LocString MergeNote = new("lists.mergeNote", "Lines with the same item, quality and mode count together: Buy lines add up, Top up lines use the largest target, and the strictest limits win.");

        public static readonly LocString ProblemNotMarketable = new("lists.problem.notMarketable", "Not sold on the market board");
        public static readonly LocString ProblemQuantity = new("lists.problem.quantity", "Quantity must be 1 to 999,999");
        public static readonly LocString ProblemCap = new("lists.problem.cap", "Max per unit must be 0 to 999,999,999");
        public static readonly LocString ProblemExcess = new("lists.problem.excess", "Extra must be 0 to 999,999");
        public static readonly LocString ProblemQuality = new("lists.problem.quality", "No HQ version exists");
        public static readonly LocString ProblemDuplicate = new("lists.problem.duplicate", "Counts together with another line for this item");

        public static readonly LocString Import = new("lists.import", "Import");
        public static readonly LocString ImportTitle = new("lists.import.title", "Import a list");
        public static readonly LocString ImportHelp = new("lists.import.help", "Paste plain text such as 3x Darksteel Ore, a CSV with the header ItemId,Name,Quality,Mode,Quantity,MaxUnitPrice,ExcessAllowance, or an Auto Shopper JSON export. Nothing changes until you choose what to do with the rows.");
        public static readonly LocString ImportPaste = new("lists.import.paste", "Paste from clipboard");
        public static readonly LocString ImportClear = new("lists.import.clear", "Clear");
        public static readonly LocString ImportReadAs = new("lists.import.readAs", "Read as {0}");
        public static readonly LocString FormatText = new("lists.format.text", "plain text");
        public static readonly LocString FormatCsv = new("lists.format.csv", "CSV");
        public static readonly LocString FormatJson = new("lists.format.json", "Auto Shopper JSON");
        public static readonly LocString ImportSummary = new("lists.import.summary", "Ready {0}  ·  Pick one {1}  ·  Unknown {2}  ·  Invalid {3}");
        public static readonly LocString StatusReady = new("lists.import.status.ready", "Ready");
        public static readonly LocString StatusPick = new("lists.import.status.pick", "Pick one");
        public static readonly LocString StatusUnknown = new("lists.import.status.unknown", "Unknown item");
        public static readonly LocString StatusInvalid = new("lists.import.status.invalid", "Invalid");
        public static readonly LocString PickItem = new("lists.import.pickItem", "Pick the item");
        public static readonly LocString Candidate = new("lists.import.candidate", "{0} (#{1})");
        public static readonly LocString ImportProblemQuantity = new("lists.import.problem.quantity", "Quantity out of range");
        public static readonly LocString ImportProblemCap = new("lists.import.problem.cap", "Max per unit out of range");
        public static readonly LocString ImportProblemExcess = new("lists.import.problem.excess", "Extra out of range");
        public static readonly LocString ImportProblemNumber = new("lists.import.problem.number", "Not a whole number");
        public static readonly LocString ImportProblemQuality = new("lists.import.problem.quality", "Unknown quality");
        public static readonly LocString ImportProblemMode = new("lists.import.problem.mode", "Unknown mode");
        public static readonly LocString ImportProblemName = new("lists.import.problem.name", "Name too long");
        public static readonly LocString ImportProblemMissing = new("lists.import.problem.missing", "No item name or id");
        public static readonly LocString ImportProblemUnknown = new("lists.import.problem.unknown", "No marketable item has this name or id");
        public static readonly LocString ImportErrorEmpty = new("lists.import.error.empty", "Paste a list above to see what it holds.");
        public static readonly LocString ImportErrorTooLarge = new("lists.import.error.tooLarge", "The text is over the 256 KB limit.");
        public static readonly LocString ImportErrorTooMany = new("lists.import.error.tooMany", "More than 1,000 rows. Split the list and import it in parts.");
        public static readonly LocString ImportErrorJson = new("lists.import.error.json", "This is not valid JSON.");
        public static readonly LocString ImportErrorFormat = new("lists.import.error.format", "This JSON is not an Auto Shopper list.");
        public static readonly LocString ImportErrorVersion = new("lists.import.error.version", "This list comes from a newer Auto Shopper. Update the plugin to import it.");
        public static readonly LocString ImportErrorColumns = new("lists.import.error.columns", "The CSV header needs an ItemId or a Name column.");
        public static readonly LocPlural ImportIgnored = new("lists.import.ignored", "{0} unknown field was ignored.", "{0} unknown fields were ignored.");
        public static readonly LocString ImportCapNote = new("lists.import.capNote", "Price caps from an import only fill in the editor. Check them before a run.");
        public static readonly LocString ImportAdd = new("lists.import.add", "Add to this list");
        public static readonly LocString ImportReplace = new("lists.import.replace", "Replace this list");
        public static readonly LocString ImportReplaceConfirm = new("lists.import.replaceConfirm", "Click again to replace");
        public static readonly LocString ImportNewList = new("lists.import.newList", "As a new list");
        public static readonly LocString ImportDone = new("lists.import.done", "Added {0} of {1} rows.");

        public static readonly LocString Export = new("lists.export", "Export");
        public static readonly LocString ExportText = new("lists.export.text", "Copy as text");
        public static readonly LocString ExportCsv = new("lists.export.csv", "Copy as CSV");
        public static readonly LocString ExportJson = new("lists.export.json", "Copy as JSON");
        public static readonly LocString ExportCopied = new("lists.export.copied", "Copied to the clipboard");
        public static readonly LocString ExportHelp = new("lists.export.help", "Exports hold item ids, names, quantities and limits, nothing about your character.");

        public static readonly LocString CommandUsage = new("lists.command.usage", "Usage: /ashop add <item name or id> [quantity]");
        public static readonly LocString CommandUnknown = new("lists.command.unknown", "No marketable item matches “{0}”.");
        public static readonly LocString CommandAmbiguous = new("lists.command.ambiguous", "“{0}” matches several items: {1}. Add it by item id instead.");
        public static readonly LocString CommandQuantity = new("lists.command.quantity", "The quantity must be between 1 and 999,999.");
        public static readonly LocString CommandAdded = new("lists.command.added", "Added {0}x {1} to “{2}”.");
        public static readonly LocString CommandMerged = new("lists.command.merged", "{0} on “{1}” is now {2}.");
        public static readonly LocString CommandFull = new("lists.command.full", "“{0}” already holds the most lines a list can have ({1}).");
        public static readonly LocString CommandFailed = new("lists.command.failed", "Item data could not be loaded. See /ashop log.");
    }
}
