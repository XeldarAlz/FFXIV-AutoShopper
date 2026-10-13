using AutoShopper.Core;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Recipes;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using System.Text;

namespace AutoShopper.Windows.Pages;

internal sealed partial class ListsPage
{
    private const float ImportBoxHeight = 110f;
    private const float ImportRowHeight = 34f;
    private const float ImportHeaderHeight = 26f;
    private const float ImportRowsMaxHeight = 272f;
    private const float ImportIconSize = 22f;
    private const float ImportPickerWidth = 230f;
    private const float ImportSourceWidth = 230f;
    private const float ImportExpansionWidth = 260f;
    private const float ImportExpansionPanelWidth = 380f;
    private const float ImportLabelGap = 12f;
    private const float ImportNumberColumn = 34f;
    private const int ImportDebounceMs = 250;
    private const int ReplaceConfirmMs = 3000;
    // Each status count stays below 2^11 because an import holds at most 1,000 rows.
    private const int SummaryCountBits = 11;
    private const int DetectSource = 0;

    // The source picker after "Detect automatically", in the order its labels are built.
    private static readonly ImportFormat[] sourceFormats =
    [
        ImportFormat.Text, ImportFormat.Csv, ImportFormat.Json, ImportFormat.ArtisanList, ImportFormat.TeamcraftText,
    ];

    private readonly string[] sourceLabels = new string[sourceFormats.Length + 1];
    private readonly string[] expansionLabels = new string[3];
    private readonly string[] expansionDetails = new string[3];
    private LanguageInfo? importLabelsLanguage;
    private bool importOpen;
    private bool focusImportBox;
    private byte[]? importBuffer;
    private bool importDirty;
    private long importEditedAtMs;
    private int importSource;
    private ExpansionMode importExpansion = ExpansionMode.DirectIngredients;
    private ImportResult? importParsed;
    private ImportResult? importResult;
    private bool importResolved;
    private ImportPreviewEntry[] importEntries = [];
    private string?[] importRowNames = [];
    private int importHeaders;
    private string[][] candidateLabels = [];
    private int[] candidatePicks = [];
    private CachedText[] importQuantityTexts = new CachedText[16];
    private CachedText[] importLineTexts = new CachedText[16];
    private CachedText importSummaryText;
    private CachedText importIgnoredText;
    private CachedText importHelpText;
    private CachedText importFormatText;
    private int importReady;
    private int importPick;
    private int importUnknown;
    private int importInvalid;
    private int importLeftOut;
    private bool importHasCaps;
    private long replaceArmedAtMs;

    private void ToggleImport()
    {
        if (importOpen)
        {
            CloseImport();
            return;
        }

        CloseCrystals();
        importOpen = true;
        focusImportBox = true;
        importBuffer = new byte[ShoppingLimits.MaximumImportBytes + 1];
    }

    private void CloseImport()
    {
        importOpen = false;
        focusImportBox = false;
        importBuffer = null;
        importParsed = null;
        importResult = null;
        importDirty = false;
        importResolved = false;
        importSource = DetectSource;
        importEntries = [];
        importRowNames = [];
        importHeaders = 0;
        candidateLabels = [];
        candidatePicks = [];
        replaceArmedAtMs = 0;
    }

    private void DrawImportPanel(Plugin plugin, ShoppingList list)
    {
        if (importBuffer is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (importDirty && now - importEditedAtMs >= ImportDebounceMs)
        {
            importDirty = false;
            ParseImport(plugin);
        }

        if (importParsed is not null && !importResolved && plugin.Items.IsReady && (!importParsed.IsCraftingList || plugin.Recipes.IsReady))
        {
            PrepareImport(plugin);
        }

        EnsureImportLabels();
        using (SettingsGroup.Begin(string.Empty))
        {
            var width = SettingsGroup.ContentRightEdge - ImGui.GetCursorScreenPos().X;
            if (DrawImportTitle(width))
            {
                CloseImport();
                return;
            }

            var labelColumn = ImportLabelColumn();
            DrawImportSource(plugin, width, labelColumn);
            DrawImportBox(plugin, importBuffer, width);
            DrawImportExpansion(plugin, width, labelColumn);
            DrawImportStatus(plugin, width);
            DrawImportRows(plugin.Items, width);
            DrawImportNotes(width);
            DrawImportActions(plugin.Lists, list);
        }
    }

    private void EnsureImportLabels()
    {
        if (ReferenceEquals(importLabelsLanguage, Loc.Current))
        {
            return;
        }

        importLabelsLanguage = Loc.Current;
        sourceLabels[DetectSource] = Loc.T(L.Lists.SourceDetect);
        for (var index = 0; index < sourceFormats.Length; index++)
        {
            sourceLabels[index + 1] = Loc.T(SourceLabel(sourceFormats[index]));
        }

        expansionLabels[(int)ExpansionMode.FinishedItems] = Loc.T(L.Lists.ExpansionFinished);
        expansionLabels[(int)ExpansionMode.DirectIngredients] = Loc.T(L.Lists.ExpansionDirect);
        expansionLabels[(int)ExpansionMode.FullyExpanded] = Loc.T(L.Lists.ExpansionFull);
        expansionDetails[(int)ExpansionMode.FinishedItems] = Loc.T(L.Lists.ExpansionFinishedHelp);
        expansionDetails[(int)ExpansionMode.DirectIngredients] = Loc.T(L.Lists.ExpansionDirectHelp);
        expansionDetails[(int)ExpansionMode.FullyExpanded] = Loc.T(L.Lists.ExpansionFullHelp);
    }

    private static LocString SourceLabel(ImportFormat format) => format == ImportFormat.Text ? L.Lists.SourceText : FormatLabel(format);

    private bool DrawImportTitle(float width)
    {
        var help = importHelpText.Get(0, static _ => string.Concat(Loc.T(L.Lists.ImportHelp), " ", Loc.T(L.Lists.ImportCraftingHelp)));
        return DrawPanelTitle("##ashop_import_close", Loc.T(L.Lists.ImportTitle), help, width);
    }

    // Both picker rows put their fields at one x, past the wider of the two labels in the current language.
    private static float ImportLabelColumn()
    {
        using (Fonts.PushCaption())
        {
            return MathF.Max(TextDraw.Measure(Loc.T(L.Lists.ImportSource)).X, TextDraw.Measure(Loc.T(L.Lists.ImportExpansion)).X)
                + ImportLabelGap * ImGuiHelpers.GlobalScale;
        }
    }

    private static void DrawPickerLabel(string label, Vector2 origin)
    {
        using (Fonts.PushCaption())
        {
            var size = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(origin.X, origin.Y + (ImGui.GetFrameHeight() - size.Y) * 0.5f), Styling.TextDim);
        }
    }

    private void DrawImportSource(Plugin plugin, float width, float labelColumn)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawPickerLabel(Loc.T(L.Lists.ImportSource), origin);
        var pickerOrigin = new Vector2(origin.X + labelColumn, origin.Y);
        ImGui.SetCursorScreenPos(pickerOrigin);
        var pick = importSource;
        if (Dropdown.Draw("##ashop_import_source", sourceLabels, ref pick, ImportSourceWidth) && pick != importSource)
        {
            importSource = pick;
            if (importParsed is not null)
            {
                ParseImport(plugin);
            }
        }

        var pickerSize = new Vector2(ImportSourceWidth * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        if (Hit.HoveringRect(pickerOrigin, pickerOrigin + pickerSize) && !ImGui.IsAnyItemActive())
        {
            Tooltip.Show(Loc.T(L.Lists.ImportSourceHelp));
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, ImGui.GetFrameHeight()));
        Styling.VSpace(6f);
    }

    // Shown only for a crafting list: its recipes say what to craft, and this says how far down them to shop.
    private void DrawImportExpansion(Plugin plugin, float width, float labelColumn)
    {
        if (importParsed is not { IsCraftingList: true })
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(8f);
        var origin = ImGui.GetCursorScreenPos();
        DrawPickerLabel(Loc.T(L.Lists.ImportExpansion), origin);
        var pickerOrigin = new Vector2(origin.X + labelColumn, origin.Y);
        var pickerWidth = MathF.Max(1f, MathF.Min(ImportExpansionWidth, (width - labelColumn) / scale));
        ImGui.SetCursorScreenPos(pickerOrigin);
        var pick = (int)importExpansion;
        if (Dropdown.DrawDetailed("##ashop_import_expansion", expansionLabels, expansionDetails, ref pick, pickerWidth, ImportExpansionPanelWidth)
            && pick != (int)importExpansion)
        {
            importExpansion = (ExpansionMode)pick;
            PrepareImport(plugin);
        }

        var frameHeight = ImGui.GetFrameHeight();
        if (Hit.HoveringRect(pickerOrigin, pickerOrigin + new Vector2(pickerWidth * scale, frameHeight)) && !ImGui.IsAnyItemActive())
        {
            Tooltip.Show(Loc.T(L.Lists.ImportExpansionHelp));
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, frameHeight));
        using (Fonts.PushCaption())
        {
            var detail = expansionDetails[(int)importExpansion];
            var detailWidth = width - labelColumn;
            var detailSize = TextDraw.MeasureWrapped(detail, detailWidth);
            Styling.VSpace(3f);
            TextDraw.Wrapped(detail, new Vector2(origin.X + labelColumn, ImGui.GetCursorScreenPos().Y), detailWidth, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, detailSize.Y));
        }
    }

    private void DrawImportBox(Plugin plugin, byte[] buffer, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        bool edited;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Styling.WithAlpha(Styling.Surface0, 0.9f))
            .Push(ImGuiCol.FrameBgHovered, Styling.WithAlpha(Styling.Surface0, 0.95f))
            .Push(ImGuiCol.FrameBgActive, Styling.WithAlpha(Styling.Surface0, 1f)))
        {
            if (focusImportBox)
            {
                ImGui.SetKeyboardFocusHere();
                focusImportBox = false;
            }

            edited = ImGui.InputTextMultiline("##ashop_import_text", buffer, new Vector2(width, ImportBoxHeight * scale));
        }

        if (edited)
        {
            importDirty = true;
            importEditedAtMs = Environment.TickCount64;
        }

        Styling.VSpace(6f);
        var origin = ImGui.GetCursorScreenPos();
        var pasteLabel = Loc.T(L.Lists.ImportPaste);
        var clearLabel = Loc.T(L.Lists.ImportClear);
        if (PillButton.Draw("##ashop_import_paste", pasteLabel, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Paste, height: Layout.ActionPillHeight))
        {
            PasteImport(plugin);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(PillButton.Width(pasteLabel, FontAwesomeIcon.Paste) + ToolbarGap * scale, 0f));
        if (PillButton.Draw("##ashop_import_clear", clearLabel, Styling.TextSecondary, PillButton.Emphasis.Ghost, FontAwesomeIcon.Eraser, height: Layout.ActionPillHeight))
        {
            Array.Clear(buffer);
            importParsed = null;
            importResult = null;
            importEntries = [];
            importHeaders = 0;
            importDirty = false;
        }

        if (importParsed is not null)
        {
            using (Fonts.PushCaption())
            {
                var key = (long)importParsed.Format * 2 + (importSource == DetectSource ? 1 : 0);
                var format = importFormatText.Get(key, static packed => FormatCaption((ImportFormat)(packed / 2), packed % 2 == 1));
                var formatSize = TextDraw.Measure(format);
                TextDraw.At(format, new Vector2(origin.X + width - formatSize.X, origin.Y + (Layout.ActionPillHeight * scale - formatSize.Y) * 0.5f), Styling.TextDim);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, Layout.ActionPillHeight * scale));
    }

    private static string FormatCaption(ImportFormat format, bool detected)
        => Loc.T(detected ? L.Lists.ImportDetected : L.Lists.ImportReadAs, Loc.T(FormatLabel(format)));

    private static LocString FormatLabel(ImportFormat format) => format switch
    {
        ImportFormat.Csv => L.Lists.FormatCsv,
        ImportFormat.Json => L.Lists.FormatJson,
        ImportFormat.ArtisanList => L.Lists.FormatArtisan,
        ImportFormat.TeamcraftText => L.Lists.FormatTeamcraft,
        _ => L.Lists.FormatText,
    };

    private void PasteImport(Plugin plugin)
    {
        var text = ImGui.GetClipboardText() ?? string.Empty;
        Array.Clear(importBuffer!);
        importDirty = false;
        if (Encoding.UTF8.GetByteCount(text) > ShoppingLimits.MaximumImportBytes)
        {
            importParsed = ImportResult.Failed(ImportFormat.Text, ImportError.TooLarge);
            importResult = importParsed;
            importEntries = [];
            importHeaders = 0;
            importResolved = true;
            return;
        }

        Encoding.UTF8.GetBytes(text, importBuffer!);
        ParseImport(plugin);
    }

    private void ParseImport(Plugin plugin)
    {
        var buffer = importBuffer!;
        var length = Array.IndexOf(buffer, (byte)0);
        var text = Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
        importParsed = importSource == DetectSource ? ListImporter.Parse(text) : ListImporter.Parse(text, sourceFormats[importSource - 1]);
        PrepareImport(plugin);
    }

    // A crafting list holds recipes, so it becomes rows only once the recipe book and the item data are both there. The
    // parsed list is kept, so a new choice of what to buy expands it again without reading the text again.
    private void PrepareImport(Plugin plugin)
    {
        var parsed = importParsed!;
        importResolved = false;
        replaceArmedAtMs = 0;
        candidateLabels = [];
        candidatePicks = [];
        importEntries = [];
        importRowNames = [];
        importHeaders = 0;
        if (parsed.IsCraftingList)
        {
            plugin.Recipes.EnsureLoading();
            if (!plugin.Items.IsReady || !plugin.Recipes.IsReady)
            {
                importResult = null;
                return;
            }

            importResult = plugin.Recipes.Expand(parsed, importExpansion, plugin.Items);
        }
        else
        {
            importResult = parsed;
        }

        if (plugin.Items.IsReady)
        {
            ResolveImport(plugin.Items);
        }
        else
        {
            CountImport();
        }
    }

    private void ResolveImport(ItemCatalog catalog)
    {
        var result = importResult!;
        ListImporter.Resolve(result, catalog);
        importResolved = true;
        var rows = result.Rows;
        importEntries = ImportPreview.Entries(rows);
        importHeaders = importEntries.Length - rows.Count;
        importRowNames = new string?[rows.Count];
        candidateLabels = new string[rows.Count][];
        candidatePicks = new int[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var candidates = rows[index].Candidates;
            if (IsExpandedItem(rows[index]) && rows[index].Status != ImportRowStatus.Resolved)
            {
                importRowNames[index] = ExpandedItemName(catalog, rows[index].RequestedItemId);
            }

            if (candidates.Length == 0)
            {
                candidateLabels[index] = [];
                continue;
            }

            var labels = new string[candidates.Length + 1];
            labels[0] = Loc.T(L.Lists.PickItem);
            for (var candidate = 0; candidate < candidates.Length; candidate++)
            {
                var name = catalog.TryGetName(candidates[candidate], out var found) ? found : string.Empty;
                labels[candidate + 1] = Loc.T(L.Lists.Candidate, name, candidates[candidate]);
            }

            candidateLabels[index] = labels;
        }

        CountImport();
    }

    private void CountImport()
    {
        var result = importResult!;
        importReady = result.Count(ImportRowStatus.Resolved);
        importPick = result.Count(ImportRowStatus.Ambiguous);
        importUnknown = result.Count(ImportRowStatus.Unknown);
        importInvalid = 0;
        importLeftOut = 0;
        importHasCaps = false;
        for (var index = 0; index < result.Rows.Count; index++)
        {
            var row = result.Rows[index];
            importHasCaps |= row.MaxUnitPrice > 0;
            if (row.Status != ImportRowStatus.Invalid)
            {
                continue;
            }

            if (IsLeftOut(row.Problem))
            {
                importLeftOut++;
            }
            else
            {
                importInvalid++;
            }
        }

        var rows = result.Rows.Count;
        if (importQuantityTexts.Length < rows)
        {
            importQuantityTexts = new CachedText[Math.Max(rows, importQuantityTexts.Length * 2)];
            importLineTexts = new CachedText[importQuantityTexts.Length];
        }
    }

    private void DrawImportStatus(Plugin plugin, float width)
    {
        Styling.VSpace(8f);
        var catalog = plugin.Items;
        var shown = importResult ?? importParsed;
        string text;
        Vector4 color;
        if (shown is null || shown.Error == ImportError.Empty)
        {
            text = Loc.T(L.Lists.ImportErrorEmpty);
            color = Styling.TextMuted;
        }
        else if (shown.Error != ImportError.None)
        {
            text = Loc.T(ErrorLabel(shown.Error, shown.Format));
            color = Styling.AccentRoseSoft;
        }
        else if (!catalog.IsReady)
        {
            text = Loc.T(catalog.LoadFailed ? L.Lists.LoadFailed : L.Lists.Loading);
            color = Styling.TextMuted;
        }
        else if (importResult is null)
        {
            var failed = plugin.Recipes.LoadFailed;
            text = Loc.T(failed ? L.Lists.RecipesFailed : L.Lists.RecipesLoading);
            color = failed ? Styling.AccentRoseSoft : Styling.TextMuted;
        }
        else
        {
            var key = (long)importReady | ((long)importPick << SummaryCountBits) | ((long)importUnknown << (SummaryCountBits * 2))
                | ((long)importInvalid << (SummaryCountBits * 3)) | ((long)importLeftOut << (SummaryCountBits * 4));
            text = importSummaryText.Get(key, static packed => SummaryText(packed));
            color = Styling.TextSecondary;
        }

        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.MeasureWrapped(text, width);
        TextDraw.Wrapped(text, origin, width, color);
        ImGui.Dummy(new Vector2(width, size.Y));
    }

    private static string SummaryText(long packed)
    {
        const long mask = (1L << SummaryCountBits) - 1;
        var ready = packed & mask;
        var pick = (packed >> SummaryCountBits) & mask;
        var unknown = (packed >> (SummaryCountBits * 2)) & mask;
        var invalid = (packed >> (SummaryCountBits * 3)) & mask;
        var leftOut = (packed >> (SummaryCountBits * 4)) & mask;
        return leftOut == 0
            ? Loc.T(L.Lists.ImportSummary, ready, pick, unknown, invalid)
            : Loc.T(L.Lists.ImportSummaryLeftOut, ready, pick, unknown, invalid, leftOut);
    }

    private static LocString ErrorLabel(ImportError error, ImportFormat format) => error switch
    {
        ImportError.TooLarge => L.Lists.ImportErrorTooLarge,
        ImportError.TooManyRows => L.Lists.ImportErrorTooMany,
        ImportError.MalformedJson => L.Lists.ImportErrorJson,
        ImportError.WrongFormat => format == ImportFormat.ArtisanList ? L.Lists.ImportErrorCraftingFormat : L.Lists.ImportErrorFormat,
        ImportError.UnsupportedVersion => format == ImportFormat.ArtisanList ? L.Lists.ImportErrorCraftingVersion : L.Lists.ImportErrorVersion,
        ImportError.MissingColumns => L.Lists.ImportErrorColumns,
        _ => L.Lists.ImportErrorEmpty,
    };

    private void DrawImportRows(ItemCatalog catalog, float width)
    {
        if (importResult is null || !importResolved || importResult.Error != ImportError.None || importResult.Rows.Count == 0)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var rows = importResult.Rows;
        var rowHeight = ImportRowHeight * scale;
        var headerHeight = ImportHeaderHeight * scale;
        var height = MathF.Min(rows.Count * rowHeight + importHeaders * headerHeight, ImportRowsMaxHeight * scale);
        Styling.VSpace(6f);
        using var child = ImRaii.Child("##ashop_import_rows", new Vector2(width, height), false, ImGuiWindowFlags.None);
        if (!child)
        {
            return;
        }

        var rowWidth = ImGui.GetContentRegionAvail().X;
        var entries = importEntries;
        for (var position = 0; position < entries.Length; position++)
        {
            var entry = entries[position];
            var size = new Vector2(rowWidth, entry.IsHeader ? headerHeight : rowHeight);
            if (!ImGui.IsRectVisible(size))
            {
                ImGui.Dummy(size);
                continue;
            }

            if (entry.IsHeader)
            {
                DrawImportHeader(entry.Header!, size);
                continue;
            }

            DrawImportRow(catalog, rows[entry.RowIndex], entry.RowIndex, size);
        }
    }

    // The title of the panel the rows below it were copied from, as the source wrote it.
    private static void DrawImportHeader(string title, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var padX = 8f * scale;
        var bottom = origin.Y + size.Y - 4f * scale;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.LayerGroup);
        var textX = origin.X + padX + iconSize.X + 8f * scale;
        using (Fonts.PushCaption())
        {
            var shown = TextDraw.Truncate(title, size.X - (textX - origin.X) - padX);
            var textSize = TextDraw.Measure(shown);
            var midY = bottom - textSize.Y * 0.5f;
            TextDraw.Icon(FontAwesomeIcon.LayerGroup, new Vector2(origin.X + padX, midY - iconSize.Y * 0.5f), Styling.TextMuted);
            TextDraw.At(shown, new Vector2(textX, bottom - textSize.Y), Styling.TextDim);
        }

        ImGui.Dummy(size);
    }

    private void DrawImportRow(ItemCatalog catalog, ImportRow row, int index, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var midY = origin.Y + size.Y * 0.5f;
        var drawList = ImGui.GetWindowDrawList();
        if (index % 2 == 1)
        {
            Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface2, 0.35f), 6f * scale);
        }

        var (statusLabel, statusColor) = StatusOf(row);
        var badgeWidth = Badge.Draw(drawList, statusLabel, statusColor, end.X - 4f * scale, midY);
        var quantityRight = end.X - badgeWidth - 14f * scale;
        var quantity = importQuantityTexts[index].Get(row.Quantity, static value => string.Concat("x", ((int)value).ToString("N0", Loc.Culture)));
        var quantitySize = TextDraw.Measure(quantity);
        TextDraw.At(quantity, new Vector2(quantityRight - quantitySize.X, midY - quantitySize.Y * 0.5f), Styling.TextSecondary);

        var numberWidth = ImportNumberColumn * scale;
        using (Fonts.PushCaption())
        {
            var lineNumber = importLineTexts[index].Get(row.SourceLine, static value => ((int)value).ToString(Loc.Culture));
            var numberSize = TextDraw.Measure(lineNumber);
            TextDraw.At(lineNumber, new Vector2(origin.X + numberWidth - numberSize.X - 8f * scale, midY - numberSize.Y * 0.5f), Styling.TextMuted);
        }

        var contentLeft = origin.X + numberWidth;
        var targetLeft = contentLeft;
        if (!IsExpandedItem(row))
        {
            var contentWidth = quantityRight - quantitySize.X - 12f * scale - contentLeft;
            var sourceWidth = contentWidth * 0.42f;
            TextDraw.At(TextDraw.Truncate(row.SourceText, sourceWidth), new Vector2(contentLeft, midY - ImGui.GetTextLineHeight() * 0.5f), Styling.TextDim);
            if (Hit.HoveringRect(new Vector2(contentLeft, origin.Y), new Vector2(contentLeft + sourceWidth, end.Y)) && TextDraw.Measure(row.SourceText).X > sourceWidth)
            {
                Tooltip.Show(row.SourceText);
            }

            targetLeft = contentLeft + sourceWidth + 12f * scale;
            var arrowSize = TextDraw.IconSize(FontAwesomeIcon.LongArrowAltRight);
            TextDraw.Icon(FontAwesomeIcon.LongArrowAltRight, new Vector2(targetLeft, midY - arrowSize.Y * 0.5f), Styling.TextMuted);
            targetLeft += arrowSize.X + 10f * scale;
        }

        DrawImportTarget(catalog, row, index, new Vector2(targetLeft, origin.Y), MathF.Max(0f, quantityRight - quantitySize.X - 12f * scale - targetLeft), size.Y);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    // A row a crafting list's recipes produced has only an item id for its source text, which says nothing its name does not.
    private bool IsExpandedItem(ImportRow row) => importParsed is { IsCraftingList: true } && row.RequestedItemId != 0;

    private void DrawImportTarget(ItemCatalog catalog, ImportRow row, int index, Vector2 origin, float width, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var midY = origin.Y + height * 0.5f;
        if (row.Candidates.Length > 0 && (uint)index < (uint)candidateLabels.Length && candidateLabels[index].Length > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, midY - ImGui.GetFrameHeight() * 0.5f));
            ImGui.PushID(index);
            var pick = candidatePicks[index];
            var pickerWidth = MathF.Min(ImportPickerWidth, width / scale);
            if (Dropdown.Draw("##candidate", candidateLabels[index], ref pick, pickerWidth, ImportPickerWidth) && pick > 0)
            {
                candidatePicks[index] = pick;
                row.Choose(row.Candidates[pick - 1]);
                CountImport();
            }

            ImGui.PopID();
            return;
        }

        if (row.Status == ImportRowStatus.Resolved && catalog.TryGet(row.ItemId, out var item))
        {
            var iconSize = ImportIconSize * scale;
            var drawList = ImGui.GetWindowDrawList();
            ItemIcon.Draw(drawList, item.IconId, new Vector2(origin.X, midY - iconSize * 0.5f), iconSize, row.Quality == ItemQuality.HighQuality && item.CanBeHighQuality);
            var nameX = origin.X + iconSize + 8f * scale;
            var right = origin.X + width;
            if (row.Quality != ItemQuality.Any)
            {
                var marker = Loc.T(row.Quality == ItemQuality.HighQuality ? L.Lists.QualityHigh : L.Lists.QualityNormal);
                right -= Badge.Draw(drawList, marker, Styling.AccentAmber, right, midY) + 6f * scale;
            }

            var nameSize = TextDraw.Measure(item.Name);
            TextDraw.At(TextDraw.Truncate(item.Name, right - nameX), new Vector2(nameX, midY - nameSize.Y * 0.5f), Styling.TextStrong);
            return;
        }

        DrawImportProblem(row, index, origin, width, midY);
    }

    // The catalog holds only marketable items, so an untradable ingredient is named from the game data once, here.
    private static string ExpandedItemName(ItemCatalog catalog, uint itemId)
    {
        if (catalog.TryGetName(itemId, out var name) || ItemCatalog.TryGetGameName(itemId, out name))
        {
            return name;
        }

        return Loc.T(L.Lists.ItemFallback, itemId);
    }

    // An expanded item that cannot be bought still shows its name, so the reason reads against the item it is about.
    private void DrawImportProblem(ImportRow row, int index, Vector2 origin, float width, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var problem = row.Status == ImportRowStatus.Invalid ? Loc.T(ProblemLabel(row.Problem, importResult?.Format)) : Loc.T(L.Lists.ImportProblemUnknown);
        var color = row.Status == ImportRowStatus.Invalid && IsLeftOut(row.Problem) ? Styling.TextDim : Styling.AccentRoseSoft;
        var x = origin.X;
        var right = origin.X + width;
        if ((uint)index < (uint)importRowNames.Length && importRowNames[index] is { } name)
        {
            var nameSize = TextDraw.Measure(name);
            var nameWidth = MathF.Min(nameSize.X, width * 0.5f);
            TextDraw.At(TextDraw.Truncate(name, nameWidth), new Vector2(x, midY - nameSize.Y * 0.5f), Styling.TextSecondary);
            x += nameWidth + 10f * scale;
        }

        using (Fonts.PushCaption())
        {
            var size = TextDraw.Measure(problem);
            TextDraw.At(TextDraw.Truncate(problem, right - x), new Vector2(x, midY - size.Y * 0.5f), color);
        }
    }

    private static (string Label, Vector4 Color) StatusOf(ImportRow row) => row.Status switch
    {
        ImportRowStatus.Resolved => (Loc.T(L.Lists.StatusReady), Styling.AccentMint),
        ImportRowStatus.Ambiguous => (Loc.T(L.Lists.StatusPick), Styling.AccentAmber),
        ImportRowStatus.Invalid when IsLeftOut(row.Problem) => (Loc.T(L.Lists.StatusLeftOut), Styling.TextDim),
        ImportRowStatus.Invalid => (Loc.T(L.Lists.StatusInvalid), Styling.AccentRose),
        _ => (Loc.T(L.Lists.StatusUnknown), Styling.AccentRose),
    };

    // Rows the source itself says not to buy, or that nobody can buy, rather than rows that were written wrong.
    private static bool IsLeftOut(ImportRowProblem problem)
        => problem is ImportRowProblem.CraftedFromOtherRows or ImportRowProblem.SkippedCraft or ImportRowProblem.NotMarketable;

    private static LocString ProblemLabel(ImportRowProblem problem, ImportFormat? format) => problem switch
    {
        ImportRowProblem.QuantityOutOfRange => L.Lists.ImportProblemQuantity,
        ImportRowProblem.UnitCapOutOfRange => L.Lists.ImportProblemCap,
        ImportRowProblem.ExcessOutOfRange => L.Lists.ImportProblemExcess,
        ImportRowProblem.MalformedNumber => L.Lists.ImportProblemNumber,
        ImportRowProblem.AmbiguousNumber => L.Lists.ImportProblemAmbiguous,
        ImportRowProblem.UnknownQuality => L.Lists.ImportProblemQuality,
        ImportRowProblem.UnknownMode => L.Lists.ImportProblemMode,
        ImportRowProblem.NameTooLong => L.Lists.ImportProblemName,
        ImportRowProblem.UnknownRecipe => L.Lists.ImportProblemRecipe,
        ImportRowProblem.SkippedCraft => L.Lists.ImportProblemSkipped,
        ImportRowProblem.NotMarketable => L.Lists.ImportProblemUntradable,
        ImportRowProblem.CraftedFromOtherRows => L.Lists.ImportProblemCrafted,
        _ => format == ImportFormat.ArtisanList ? L.Lists.ImportProblemNoRecipe : L.Lists.ImportProblemMissing,
    };

    private void DrawImportNotes(float width)
    {
        if (importResult is null || importResult.Error != ImportError.None)
        {
            return;
        }

        using (Fonts.PushCaption())
        {
            if (importResult.IgnoredFields > 0)
            {
                Styling.VSpace(4f);
                var ignored = importIgnoredText.Get(importResult.IgnoredFields, static count => Loc.Plural(L.Lists.ImportIgnored, (int)count));
                TextDraw.At(TextDraw.Truncate(ignored, width), ImGui.GetCursorScreenPos(), Styling.AccentAmberSoft);
                ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
            }

            if (importHasCaps)
            {
                Styling.VSpace(4f);
                var note = Loc.T(L.Lists.ImportCapNote);
                var noteSize = TextDraw.MeasureWrapped(note, width);
                TextDraw.Wrapped(note, ImGui.GetCursorScreenPos(), width, Styling.AccentAmberSoft);
                ImGui.Dummy(new Vector2(width, noteSize.Y));
            }
        }
    }

    private void DrawImportActions(ShoppingListStore store, ShoppingList list)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = ToolbarGap * scale;
        var enabled = importResult is not null && importResult.Error == ImportError.None && importReady > 0;
        var now = Environment.TickCount64;
        var replaceArmed = now - replaceArmedAtMs < ReplaceConfirmMs;
        Styling.VSpace(10f);
        var origin = ImGui.GetCursorScreenPos();
        var x = origin.X;

        var addLabel = Loc.T(L.Lists.ImportAdd);
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_import_add", addLabel, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.Plus, enabled, Layout.ActionPillHeight))
        {
            ApplyImport(store, list, replace: false);
            return;
        }

        x += PillButton.Width(addLabel, FontAwesomeIcon.Plus) + gap;
        var replaceLabel = Loc.T(replaceArmed ? L.Lists.ImportReplaceConfirm : L.Lists.ImportReplace);
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_import_replace", replaceLabel, Styling.AccentRose, replaceArmed ? PillButton.Emphasis.Filled : PillButton.Emphasis.Tinted,
                FontAwesomeIcon.ExchangeAlt, enabled, Layout.ActionPillHeight))
        {
            if (replaceArmed)
            {
                ApplyImport(store, list, replace: true);
                return;
            }

            replaceArmedAtMs = now;
        }

        x += PillButton.Width(replaceLabel, FontAwesomeIcon.ExchangeAlt) + gap;
        var newLabel = Loc.T(L.Lists.ImportNewList);
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_import_new", newLabel, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.FolderPlus, enabled, Layout.ActionPillHeight))
        {
            var rows = importResult!.Rows;
            var created = store.CreateFromImport(importResult.ListName, rows, out var added);
            ShowNotice(Loc.T(L.Lists.ImportDone, added, rows.Count), Styling.AccentMintSoft);
            CloseImport();
            RunLog.Info($"Imported {added} of {rows.Count} rows as the new list '{created.Name}'.");
            return;
        }

        x += PillButton.Width(newLabel, FontAwesomeIcon.FolderPlus) + gap;
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_import_cancel", Loc.T(L.Common.Cancel), Styling.TextSecondary, PillButton.Emphasis.Ghost, height: Layout.ActionPillHeight))
        {
            CloseImport();
            return;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(1f, Layout.ActionPillHeight * scale));
    }

    private void ApplyImport(ShoppingListStore store, ShoppingList list, bool replace)
    {
        var rows = importResult!.Rows;
        var added = store.ApplyImport(list, rows, replace);
        ShowNotice(Loc.T(L.Lists.ImportDone, added, rows.Count), Styling.AccentMintSoft);
        RunLog.Info($"Imported {added} of {rows.Count} rows into '{list.Name}'{(replace ? ", replacing its lines" : string.Empty)}.");
        CloseImport();
    }
}
