using AutoShopper.Core;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
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
    private const float ImportRowsMaxHeight = 272f;
    private const float ImportIconSize = 22f;
    private const float ImportPickerWidth = 230f;
    private const float ImportNumberColumn = 34f;
    private const int ImportDebounceMs = 250;
    private const int ReplaceConfirmMs = 3000;
    // Each status count stays below 2^11 because an import holds at most 1,000 rows.
    private const int SummaryCountBits = 11;

    private bool importOpen;
    private byte[]? importBuffer;
    private bool importDirty;
    private long importEditedAtMs;
    private ImportResult? importResult;
    private bool importResolved;
    private string[][] candidateLabels = [];
    private int[] candidatePicks = [];
    private CachedText[] importQuantityTexts = new CachedText[16];
    private CachedText[] importLineTexts = new CachedText[16];
    private CachedText importSummaryText;
    private CachedText importIgnoredText;
    private int importReady;
    private int importPick;
    private int importUnknown;
    private int importInvalid;
    private bool importHasCaps;
    private long replaceArmedAtMs;

    private void ToggleImport()
    {
        if (importOpen)
        {
            CloseImport();
            return;
        }

        importOpen = true;
        importBuffer = new byte[ShoppingLimits.MaximumImportBytes + 1];
    }

    private void CloseImport()
    {
        importOpen = false;
        importBuffer = null;
        importResult = null;
        importDirty = false;
        importResolved = false;
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
            ParseImport(plugin.Items);
        }

        if (importResult is not null && !importResolved && plugin.Items.IsReady)
        {
            ResolveImport(plugin.Items);
        }

        using (SettingsGroup.Begin(string.Empty))
        {
            var width = SettingsGroup.ContentRightEdge - ImGui.GetCursorScreenPos().X;
            if (DrawImportTitle(width))
            {
                CloseImport();
                return;
            }

            DrawImportBox(plugin.Items, importBuffer, width);
            DrawImportStatus(plugin.Items, width);
            DrawImportRows(plugin.Items, width);
            DrawImportNotes(width);
            DrawImportActions(plugin.Lists, list);
        }
    }

    private static bool DrawImportTitle(float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var title = Loc.T(L.Lists.ImportTitle);
        var titleSize = TextDraw.SectionTitleSize(title);
        var closeSize = ImGui.GetFrameHeight();
        TextDraw.SectionTitle(title, new Vector2(origin.X, origin.Y + (closeSize - titleSize.Y) * 0.5f), Styling.TextStrong);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - closeSize, origin.Y));
        var closed = IconButton.Draw(FontAwesomeIcon.Times, "##ashop_import_close", closeSize, Styling.TextDim, Loc.T(L.Common.Close));
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, closeSize));

        var help = Loc.T(L.Lists.ImportHelp);
        using (Fonts.PushCaption())
        {
            var helpOrigin = ImGui.GetCursorScreenPos();
            var helpSize = TextDraw.MeasureWrapped(help, width);
            TextDraw.Wrapped(help, helpOrigin, width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, helpSize.Y + 6f * scale));
        }

        return closed;
    }

    private void DrawImportBox(ItemCatalog catalog, byte[] buffer, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        bool edited;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Styling.WithAlpha(Styling.Surface0, 0.9f))
            .Push(ImGuiCol.FrameBgHovered, Styling.WithAlpha(Styling.Surface0, 0.95f))
            .Push(ImGuiCol.FrameBgActive, Styling.WithAlpha(Styling.Surface0, 1f)))
        {
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
            PasteImport(catalog);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(PillButton.Width(pasteLabel, FontAwesomeIcon.Paste) + ToolbarGap * scale, 0f));
        if (PillButton.Draw("##ashop_import_clear", clearLabel, Styling.TextSecondary, PillButton.Emphasis.Ghost, FontAwesomeIcon.Eraser, height: Layout.ActionPillHeight))
        {
            Array.Clear(buffer);
            importResult = null;
            importDirty = false;
        }

        if (importResult is not null)
        {
            using (Fonts.PushCaption())
            {
                var format = Loc.T(L.Lists.ImportReadAs, Loc.T(FormatLabel(importResult.Format)));
                var formatSize = TextDraw.Measure(format);
                TextDraw.At(format, new Vector2(origin.X + width - formatSize.X, origin.Y + (Layout.ActionPillHeight * scale - formatSize.Y) * 0.5f), Styling.TextDim);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, Layout.ActionPillHeight * scale));
    }

    private static LocString FormatLabel(ImportFormat format) => format switch
    {
        ImportFormat.Csv => L.Lists.FormatCsv,
        ImportFormat.Json => L.Lists.FormatJson,
        _ => L.Lists.FormatText,
    };

    private void PasteImport(ItemCatalog catalog)
    {
        var text = ImGui.GetClipboardText() ?? string.Empty;
        Array.Clear(importBuffer!);
        importDirty = false;
        if (Encoding.UTF8.GetByteCount(text) > ShoppingLimits.MaximumImportBytes)
        {
            importResult = ImportResult.Failed(ImportFormat.Text, ImportError.TooLarge);
            importResolved = true;
            return;
        }

        Encoding.UTF8.GetBytes(text, importBuffer!);
        ParseImport(catalog);
    }

    private void ParseImport(ItemCatalog catalog)
    {
        var buffer = importBuffer!;
        var length = Array.IndexOf(buffer, (byte)0);
        var text = Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
        importResult = ListImporter.Parse(text);
        importResolved = false;
        replaceArmedAtMs = 0;
        candidateLabels = [];
        candidatePicks = [];
        if (catalog.IsReady)
        {
            ResolveImport(catalog);
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
        candidateLabels = new string[rows.Count][];
        candidatePicks = new int[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var candidates = rows[index].Candidates;
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
        importInvalid = result.Count(ImportRowStatus.Invalid);
        importHasCaps = false;
        for (var index = 0; index < result.Rows.Count; index++)
        {
            importHasCaps |= result.Rows[index].MaxUnitPrice > 0;
        }

        var rows = result.Rows.Count;
        if (importQuantityTexts.Length < rows)
        {
            importQuantityTexts = new CachedText[Math.Max(rows, importQuantityTexts.Length * 2)];
            importLineTexts = new CachedText[importQuantityTexts.Length];
        }
    }

    private void DrawImportStatus(ItemCatalog catalog, float width)
    {
        Styling.VSpace(8f);
        string text;
        Vector4 color;
        if (importResult is null || importResult.Error == ImportError.Empty)
        {
            text = Loc.T(L.Lists.ImportErrorEmpty);
            color = Styling.TextMuted;
        }
        else if (importResult.Error != ImportError.None)
        {
            text = Loc.T(ErrorLabel(importResult.Error));
            color = Styling.AccentRoseSoft;
        }
        else if (!catalog.IsReady)
        {
            text = Loc.T(catalog.LoadFailed ? L.Lists.LoadFailed : L.Lists.Loading);
            color = Styling.TextMuted;
        }
        else
        {
            var key = (long)importReady | ((long)importPick << SummaryCountBits) | ((long)importUnknown << (SummaryCountBits * 2)) | ((long)importInvalid << (SummaryCountBits * 3));
            text = importSummaryText.Get(key, static packed =>
            {
                const long mask = (1L << SummaryCountBits) - 1;
                return Loc.T(L.Lists.ImportSummary, packed & mask, (packed >> SummaryCountBits) & mask, (packed >> (SummaryCountBits * 2)) & mask, (packed >> (SummaryCountBits * 3)) & mask);
            });
            color = Styling.TextSecondary;
        }

        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.MeasureWrapped(text, width);
        TextDraw.Wrapped(text, origin, width, color);
        ImGui.Dummy(new Vector2(width, size.Y));
    }

    private static LocString ErrorLabel(ImportError error) => error switch
    {
        ImportError.TooLarge => L.Lists.ImportErrorTooLarge,
        ImportError.TooManyRows => L.Lists.ImportErrorTooMany,
        ImportError.MalformedJson => L.Lists.ImportErrorJson,
        ImportError.WrongFormat => L.Lists.ImportErrorFormat,
        ImportError.UnsupportedVersion => L.Lists.ImportErrorVersion,
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
        var height = MathF.Min(rows.Count * rowHeight, ImportRowsMaxHeight * scale);
        Styling.VSpace(6f);
        using var child = ImRaii.Child("##ashop_import_rows", new Vector2(width, height), false, ImGuiWindowFlags.None);
        if (!child)
        {
            return;
        }

        var rowWidth = ImGui.GetContentRegionAvail().X;
        for (var index = 0; index < rows.Count; index++)
        {
            var size = new Vector2(rowWidth, rowHeight);
            if (!ImGui.IsRectVisible(size))
            {
                ImGui.Dummy(size);
                continue;
            }

            DrawImportRow(catalog, rows[index], index, size);
        }
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

        var (statusLabel, statusColor) = StatusOf(row.Status);
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
        var contentWidth = quantityRight - quantitySize.X - 12f * scale - contentLeft;
        var sourceWidth = contentWidth * 0.42f;
        TextDraw.At(TextDraw.Truncate(row.SourceText, sourceWidth), new Vector2(contentLeft, midY - ImGui.GetTextLineHeight() * 0.5f), Styling.TextDim);
        if (Hit.HoveringRect(new Vector2(contentLeft, origin.Y), new Vector2(contentLeft + sourceWidth, end.Y)) && TextDraw.Measure(row.SourceText).X > sourceWidth)
        {
            Tooltip.Show(row.SourceText);
        }

        var targetLeft = contentLeft + sourceWidth + 12f * scale;
        var arrowSize = TextDraw.IconSize(FontAwesomeIcon.LongArrowAltRight);
        TextDraw.Icon(FontAwesomeIcon.LongArrowAltRight, new Vector2(targetLeft, midY - arrowSize.Y * 0.5f), Styling.TextMuted);
        targetLeft += arrowSize.X + 10f * scale;
        DrawImportTarget(catalog, row, index, new Vector2(targetLeft, origin.Y), MathF.Max(0f, quantityRight - quantitySize.X - 12f * scale - targetLeft), size.Y);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

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

        var problem = row.Status == ImportRowStatus.Invalid ? Loc.T(ProblemLabel(row.Problem)) : Loc.T(L.Lists.ImportProblemUnknown);
        using (Fonts.PushCaption())
        {
            var size = TextDraw.Measure(problem);
            TextDraw.At(TextDraw.Truncate(problem, width), new Vector2(origin.X, midY - size.Y * 0.5f), Styling.AccentRoseSoft);
        }
    }

    private static (string Label, Vector4 Color) StatusOf(ImportRowStatus status) => status switch
    {
        ImportRowStatus.Resolved => (Loc.T(L.Lists.StatusReady), Styling.AccentMint),
        ImportRowStatus.Ambiguous => (Loc.T(L.Lists.StatusPick), Styling.AccentAmber),
        ImportRowStatus.Invalid => (Loc.T(L.Lists.StatusInvalid), Styling.AccentRose),
        _ => (Loc.T(L.Lists.StatusUnknown), Styling.AccentRose),
    };

    private static LocString ProblemLabel(ImportRowProblem problem) => problem switch
    {
        ImportRowProblem.QuantityOutOfRange => L.Lists.ImportProblemQuantity,
        ImportRowProblem.UnitCapOutOfRange => L.Lists.ImportProblemCap,
        ImportRowProblem.ExcessOutOfRange => L.Lists.ImportProblemExcess,
        ImportRowProblem.MalformedNumber => L.Lists.ImportProblemNumber,
        ImportRowProblem.UnknownQuality => L.Lists.ImportProblemQuality,
        ImportRowProblem.UnknownMode => L.Lists.ImportProblemMode,
        ImportRowProblem.NameTooLong => L.Lists.ImportProblemName,
        _ => L.Lists.ImportProblemMissing,
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
