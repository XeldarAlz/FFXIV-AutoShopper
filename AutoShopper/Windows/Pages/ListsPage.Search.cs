using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Globalization;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class ListsPage
{
    private const int MaximumResults = 10;
    private const int QueryMaxLength = 64;
    private const float ToolbarGap = 8f;
    private const float ResultRowHeight = 36f;
    private const float ResultIconSize = 26f;
    private const float ResultPadX = 10f;
    private const float ResultPanelPad = 6f;
    private const string ExportMenuId = "##ashop_lists_export_menu";

    private readonly int[] results = new int[MaximumResults];
    private readonly string[] resultIds = new string[MaximumResults];
    private int resultCount;
    private int highlight;
    private string query = string.Empty;
    private string searchedQuery = string.Empty;
    private string noMatchesText = string.Empty;
    private bool searchedWithItems;
    private bool searchFocused;
    private bool focusSearch;

    private void DrawToolbar(Plugin plugin, ShoppingList list)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var gap = ToolbarGap * scale;
        var crystalsLabel = Loc.T(L.Lists.Crystals);
        var importLabel = Loc.T(L.Lists.Import);
        var exportLabel = Loc.T(L.Lists.Export);
        var crystalsWidth = PillButton.Width(crystalsLabel, FontAwesomeIcon.Gem);
        var importWidth = PillButton.Width(importLabel, FontAwesomeIcon.FileImport);
        var exportWidth = PillButton.Width(exportLabel, FontAwesomeIcon.FileExport);
        var searchWidth = MathF.Max(1f, width - crystalsWidth - importWidth - exportWidth - gap * 3f);

        var wasFocused = searchFocused;
        ImGui.SetCursorScreenPos(origin);
        SearchField.Draw("##ashop_lists_search", Loc.T(L.Lists.AddHint), ref query, ref searchFocused, searchWidth, Layout.ActionPillHeight, focusSearch, QueryMaxLength);
        focusSearch = false;
        RefreshResults(plugin.Items);
        HandleSearchKeys(plugin, list, wasFocused || searchFocused);

        ImGui.SetCursorScreenPos(origin + new Vector2(searchWidth + gap, 0f));
        DrawCrystalsButton(crystalsLabel);

        ImGui.SetCursorScreenPos(origin + new Vector2(searchWidth + crystalsWidth + gap * 2f, 0f));
        if (PillButton.Draw("##ashop_lists_import", importLabel, Styling.AccentGlow, importOpen ? PillButton.Emphasis.Filled : PillButton.Emphasis.Tinted,
                FontAwesomeIcon.FileImport, height: Layout.ActionPillHeight))
        {
            ToggleImport();
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(searchWidth + crystalsWidth + importWidth + gap * 3f, 0f));
        if (PillButton.Draw("##ashop_lists_export", exportLabel, Styling.TextSecondary, PillButton.Emphasis.Ghost, FontAwesomeIcon.FileExport,
                enabled: list.Lines.Count > 0, height: Layout.ActionPillHeight, tooltip: Loc.T(L.Lists.ExportHelp)))
        {
            ImGui.OpenPopup(ExportMenuId);
        }

        DrawExportMenu(plugin.Items, list);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, Layout.ActionPillHeight * scale));
    }

    private void DrawCrystalsButton(string label)
    {
        if (PillButton.Draw("##ashop_lists_crystals", label, Styling.AccentBlue, crystalsOpen ? PillButton.Emphasis.Filled : PillButton.Emphasis.Tinted,
                FontAwesomeIcon.Gem, height: Layout.ActionPillHeight, tooltip: Loc.T(L.Lists.CrystalsHelp)))
        {
            ToggleCrystals();
        }
    }

    private void DrawExportMenu(ItemCatalog catalog, ShoppingList list)
    {
        using var menu = ContextMenu.Begin(ExportMenuId);
        if (!menu.Open)
        {
            return;
        }

        if (ImGui.MenuItem(Loc.T(L.Lists.ExportText)))
        {
            CopyExport(ListExporter.ToText(list, catalog));
        }

        if (ImGui.MenuItem(Loc.T(L.Lists.ExportCsv)))
        {
            CopyExport(ListExporter.ToCsv(list, catalog));
        }

        if (ImGui.MenuItem(Loc.T(L.Lists.ExportJson)))
        {
            CopyExport(ListExporter.ToJson(list, catalog));
        }
    }

    private void CopyExport(string text)
    {
        ImGui.SetClipboardText(text);
        ShowNotice(Loc.T(L.Lists.ExportCopied), Styling.AccentMintSoft);
    }

    private void RefreshResults(ItemCatalog catalog)
    {
        var withItems = catalog.IsReady;
        if (string.Equals(query, searchedQuery, StringComparison.Ordinal) && searchedWithItems == withItems)
        {
            return;
        }

        searchedQuery = query;
        searchedWithItems = withItems;
        highlight = 0;
        resultCount = catalog.Search(query, results);
        for (var index = 0; index < resultCount; index++)
        {
            resultIds[index] = string.Concat("#", catalog.At(results[index]).Id.ToString(CultureInfo.InvariantCulture));
        }

        noMatchesText = resultCount == 0 && withItems ? Loc.T(L.Common.NoMatches, query.Trim()) : string.Empty;
    }

    private void HandleSearchKeys(Plugin plugin, ShoppingList list, bool focused)
    {
        if (!focused || resultCount == 0)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
        {
            highlight = (highlight + 1) % resultCount;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
        {
            highlight = (highlight + resultCount - 1) % resultCount;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
        {
            AddResult(plugin, list, results[highlight]);
        }
    }

    private void DrawSearchResults(Plugin plugin, ShoppingList list)
    {
        if (query.AsSpan().Trim().IsEmpty)
        {
            return;
        }

        var catalog = plugin.Items;
        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(6f);
        if (!catalog.IsReady)
        {
            using (Fonts.PushCaption())
            {
                TextDraw.Hint(Loc.T(catalog.LoadFailed ? L.Lists.LoadFailed : L.Lists.Loading));
            }

            return;
        }

        var pad = ResultPanelPad * scale;
        var rowHeight = ResultRowHeight * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var rows = Math.Max(1, resultCount);
        var size = new Vector2(width, pad * 2f + rowHeight * rows);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Styling.CardRounding * scale;
        Paint.Gradient(drawList, origin, origin + size, Styling.Surface2 with { W = 0.97f }, Styling.Surface0 with { W = 0.97f }, rounding);
        Paint.Stroke(drawList, origin, origin + size, Styling.WithAlpha(Styling.AccentGlow, 0.35f), rounding);

        if (resultCount == 0)
        {
            using (Fonts.PushCaption())
            {
                var textSize = TextDraw.Measure(noMatchesText);
                TextDraw.At(TextDraw.Truncate(noMatchesText, width - pad * 4f), new Vector2(origin.X + pad * 2f, origin.Y + (size.Y - textSize.Y) * 0.5f), Styling.TextMuted);
            }

            ImGui.Dummy(size);
            return;
        }

        for (var index = 0; index < resultCount; index++)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, origin.Y + pad + rowHeight * index));
            if (DrawResultRow(catalog, index, new Vector2(width - pad * 2f, rowHeight)))
            {
                AddResult(plugin, list, results[index]);
                break;
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private bool DrawResultRow(ItemCatalog catalog, int index, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var item = catalog.At(results[index]);

        ImGui.PushID(index);
        var hit = Hit.Area("##result", size);
        var hover = Motion.Hover(Motion.Key("##result"), hit.Hovered || index == highlight);
        ImGui.PopID();
        if (hit.Hovered)
        {
            highlight = index;
        }

        var drawList = ImGui.GetWindowDrawList();
        if (hover > 0.01f)
        {
            Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface3, 0.75f * hover), 8f * scale);
        }

        var padX = ResultPadX * scale;
        var iconSize = ResultIconSize * scale;
        var midY = origin.Y + size.Y * 0.5f;
        ItemIcon.Draw(drawList, item.IconId, new Vector2(origin.X + padX, midY - iconSize * 0.5f), iconSize);

        var textX = origin.X + padX * 2f + iconSize;
        var right = end.X - padX;
        if (item.CanBeHighQuality)
        {
            right -= Badge.Draw(drawList, Loc.T(L.Lists.QualityHigh), Styling.AccentAmber, right, midY) + padX;
        }

        Vector2 idSize;
        using (Fonts.PushCaption())
        {
            idSize = TextDraw.Measure(resultIds[index]);
            TextDraw.At(resultIds[index], new Vector2(right - idSize.X, midY - idSize.Y * 0.5f), Styling.TextMuted);
        }

        var nameSize = TextDraw.Measure(item.Name);
        var nameColor = Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, hover);
        TextDraw.At(TextDraw.Truncate(item.Name, right - idSize.X - padX - textX), new Vector2(textX, midY - nameSize.Y * 0.5f), nameColor);
        return hit.Clicked;
    }

    private void AddResult(Plugin plugin, ShoppingList list, int catalogIndex)
    {
        var item = plugin.Items.At(catalogIndex);
        var result = plugin.Lists.AddItem(list, item.Id, ShoppingLimits.MinimumQuantity, out var line);
        switch (result)
        {
            case AddLineResult.Added:
                ShowNotice(Loc.T(L.Lists.Added, item.Name), Styling.AccentMintSoft);
                break;
            case AddLineResult.Merged:
                ShowNotice(Loc.T(L.Lists.Merged, item.Name, line!.Quantity.ToString("N0", Loc.Culture)), Styling.AccentMintSoft);
                break;
            default:
                ShowNotice(Loc.T(L.Lists.ListFull, ShoppingLimits.MaximumLinesPerList.ToString("N0", Loc.Culture)), Styling.AccentRoseSoft);
                break;
        }

        query = string.Empty;
        focusSearch = true;
    }
}
