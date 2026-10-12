using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class ListsPage
{
    private const float ListColumnWidth = 208f;
    private const float ListRowHeight = 48f;
    private const float ListRowPadX = 12f;
    private const float ListRowGap = 3f;
    private const float ArchiveToggleHeight = 30f;

    private bool showArchived;
    private CachedText[] listCaptions = new CachedText[8];
    private CachedText archivedToggleText;

    private void DrawListColumn(ShoppingListStore store)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var title = Loc.T(L.Lists.Title);
        using (Fonts.PushTitle())
        {
            TextDraw.At(title, new Vector2(origin.X + 6f * scale, origin.Y), Styling.TextStrong);
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, TextDraw.Measure(title).Y + 10f * scale));
        }

        var newLabel = Loc.T(L.Lists.NewList);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4f * scale);
        if (PillButton.Draw("##ashop_lists_new", newLabel, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Plus, height: Layout.ActionPillHeight))
        {
            store.CreateList();
        }

        Styling.VSpace(10f);
        var lists = store.Lists;
        if (lists.Count == 0)
        {
            using (Fonts.PushCaption())
            {
                TextDraw.Hint(Loc.T(L.Lists.NoLists));
            }

            return;
        }

        EnsureCaptionCapacity(lists.Count);
        var selected = store.Selected;
        for (var index = 0; index < lists.Count; index++)
        {
            if (!lists[index].Archived)
            {
                DrawListRow(store, lists[index], index, ReferenceEquals(lists[index], selected));
            }
        }

        var archived = store.CountArchived();
        if (archived == 0)
        {
            return;
        }

        Styling.VSpace(6f);
        DrawArchiveToggle(archived);
        if (!showArchived)
        {
            return;
        }

        for (var index = 0; index < lists.Count; index++)
        {
            if (lists[index].Archived)
            {
                DrawListRow(store, lists[index], index, ReferenceEquals(lists[index], selected));
            }
        }
    }

    private void EnsureCaptionCapacity(int count)
    {
        if (listCaptions.Length >= count)
        {
            return;
        }

        var grown = new CachedText[Math.Max(count, listCaptions.Length * 2)];
        Array.Copy(listCaptions, grown, listCaptions.Length);
        listCaptions = grown;
    }

    private void DrawListRow(ShoppingListStore store, ShoppingList list, int index, bool selected)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ListRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID(list.Id.GetHashCode());
        var hit = Hit.Area("##list_row", size);
        var hover = Motion.Hover(Motion.Key("##list_row"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##list_row", 1), selected ? 1f : 0f, 16f);
        ImGui.PopID();

        var rounding = 9f * scale;
        var backgroundAlpha = MathF.Max(hover * 0.7f, active) * 0.9f;
        if (backgroundAlpha > 0.01f)
        {
            Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Tint(Styling.Surface2, Styling.AccentGlow, active * 0.18f), backgroundAlpha), rounding);
        }

        if (active > 0.01f)
        {
            var barMin = new Vector2(origin.X, origin.Y + size.Y * (0.5f - 0.25f * active));
            var barMax = new Vector2(origin.X + 3f * scale, origin.Y + size.Y * (0.5f + 0.25f * active));
            Paint.Fill(drawList, barMin, barMax, Styling.WithAlpha(Styling.AccentGlow, active), 2f * scale);
        }

        var padX = ListRowPadX * scale;
        var textWidth = size.X - padX * 2f;
        var caption = listCaptions[index].Get(list.Lines.Count, static count => Loc.Plural(L.Lists.LineCount, (int)count));
        var nameHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var top = origin.Y + (size.Y - nameHeight - captionHeight - 2f * scale) * 0.5f;
        var nameColor = list.Archived ? Styling.TextDim : Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, MathF.Max(hover, active));
        TextDraw.At(TextDraw.Truncate(list.Name, textWidth), new Vector2(origin.X + padX, top), nameColor);
        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(caption, textWidth), new Vector2(origin.X + padX, top + nameHeight + 2f * scale), Styling.TextMuted);
        }

        if (hit.Hovered && TextDraw.Measure(list.Name).X > textWidth)
        {
            Tooltip.Show(list.Name);
        }

        if (hit.Clicked)
        {
            store.Select(list);
        }

        Styling.VSpace(ListRowGap);
    }

    private void DrawArchiveToggle(int archived)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ArchiveToggleHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var hit = Hit.Area("##ashop_lists_archived", size);
        var hover = Motion.Hover(Motion.Key("##ashop_lists_archived"), hit.Hovered);
        if (hit.Clicked)
        {
            showArchived = !showArchived;
        }

        var color = Vector4.Lerp(Styling.TextMuted, Styling.TextSecondary, hover);
        var midY = origin.Y + size.Y * 0.5f;
        var padX = ListRowPadX * scale;
        var icon = showArchived ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
        var iconSize = TextDraw.IconSize(icon);
        TextDraw.Icon(icon, new Vector2(origin.X + padX, midY - iconSize.Y * 0.5f), color);

        var label = archivedToggleText.Get(archived, static count => Loc.T(L.Lists.ShowArchived, (int)count));
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.SmallCapsSize(label);
            TextDraw.SmallCaps(label, new Vector2(origin.X + padX + iconSize.X + 8f * scale, midY - labelSize.Y * 0.5f), color);
        }
    }
}
