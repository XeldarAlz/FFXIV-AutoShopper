using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using AutoShopper.Windows.Sections;
using AutoShopper.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed class ShopPage
{
    private const float SwitchRevealMs = 320f;
    private const float EmptyCardHeight = 118f;
    private const float EmptyCardPadX = 24f;
    private const float ListCardHeight = 92f;
    private const float ListCardPadX = 18f;
    private const float ListIconBox = 40f;
    private const float PickerWidth = 260f;
    private const float BadgeGap = 8f;
    private const long RevisionStride = 2;

    private readonly List<ShoppingList> pickerLists = [];
    private string[] pickerLabels = [];
    private int pickerRevision = -1;
    private CachedText summaryText;
    private CachedText issuesText;

    public void Draw(Plugin plugin, AppWindow window)
    {
        var controller = plugin.Controller;
        var running = controller.Running;

        using var reveal = Motion.PushSwitch("##ashop_shop_state", running, SwitchRevealMs);
        if (running)
        {
            RunningPanel.Draw(controller);
            return;
        }

        DrawIdle(plugin, window);
    }

    private void DrawIdle(Plugin plugin, AppWindow window)
    {
        plugin.Items.EnsureLoading();
        if (Headline.Draw(plugin.Controller, plugin.History))
        {
            window.Show(AppWindow.Page.Plugins);
        }

        Styling.VSpace(20f);
        SectionTitle(Loc.T(L.Shop.Lists));
        DrawListCard(plugin.Lists, window);
        Styling.VSpace(12f);
    }

    private void DrawListCard(ShoppingListStore store, AppWindow window)
    {
        var list = store.Selected;
        if (list is null)
        {
            EmptyCard(FontAwesomeIcon.ClipboardList, Loc.T(L.Shop.ListsEmpty));
            Styling.VSpace(10f);
            if (PillButton.Draw("##ashop_shop_open_lists", Loc.T(L.Shop.OpenLists), Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.ClipboardList,
                    height: Layout.ActionPillHeight))
            {
                window.Show(AppWindow.Page.Lists);
            }

            return;
        }

        RefreshPicker(store, list);
        var analysis = store.Analyze(list);
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ListCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, Styling.AccentGlow, 0.04f);

        var padX = ListCardPadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var iconBox = ListIconBox * scale;
        var iconCenter = new Vector2(origin.X + padX + iconBox * 0.5f, midY);
        drawList.AddCircleFilled(iconCenter, iconBox * 0.5f, Paint.Col(Styling.WithAlpha(Styling.AccentGlow, 0.14f)));
        TextDraw.IconCentered(FontAwesomeIcon.ClipboardList, iconCenter, Styling.AccentGlowSoft);

        var editLabel = Loc.T(L.Shop.EditList);
        var editWidth = PillButton.Width(editLabel, FontAwesomeIcon.Pen);
        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - editWidth, midY - Layout.ActionPillHeight * scale * 0.5f));
        if (PillButton.Draw("##ashop_shop_edit_list", editLabel, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Pen, height: Layout.ActionPillHeight))
        {
            window.Show(AppWindow.Page.Lists);
        }

        var contentX = iconCenter.X + iconBox * 0.5f + 14f * scale;
        var contentRight = end.X - padX - editWidth - 14f * scale;
        var frameHeight = ImGui.GetFrameHeight();
        var captionHeight = ImGui.GetTextLineHeight();
        var top = midY - (frameHeight + 8f * scale + captionHeight) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(contentX, top));
        var selected = pickerLists.IndexOf(list);
        var pickerWidth = MathF.Max(1f, MathF.Min(PickerWidth, (contentRight - contentX) / scale));
        if (Dropdown.Draw("##ashop_shop_list_picker", pickerLabels, ref selected, pickerWidth) && (uint)selected < (uint)pickerLists.Count)
        {
            store.Select(pickerLists[selected]);
        }

        DrawSummary(store, list, analysis, new Vector2(contentX, top + frameHeight + 8f * scale), contentRight);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    // The picker shows the lists in use, plus the selected one even when it is archived, so the field never reads empty.
    private void RefreshPicker(ShoppingListStore store, ShoppingList selected)
    {
        if (pickerRevision == store.Revision)
        {
            return;
        }

        pickerRevision = store.Revision;
        pickerLists.Clear();
        var lists = store.Lists;
        for (var index = 0; index < lists.Count; index++)
        {
            if (!lists[index].Archived || ReferenceEquals(lists[index], selected))
            {
                pickerLists.Add(lists[index]);
            }
        }

        pickerLabels = new string[pickerLists.Count];
        for (var index = 0; index < pickerLists.Count; index++)
        {
            pickerLabels[index] = pickerLists[index].Name;
        }
    }

    private void DrawSummary(ShoppingListStore store, ShoppingList list, ListAnalysis analysis, Vector2 origin, float right)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var key = store.Revision * RevisionStride + (analysis.Validation.ItemsChecked ? 1 : 0);
        var summary = summaryText.Get(key, static _ => ListsPage.Summary(Plugin.Instance.Lists.Selected));
        var shown = TextDraw.Truncate(summary, MathF.Max(0f, right - origin.X));
        TextDraw.At(shown, origin, Styling.TextDim);

        var drawList = ImGui.GetWindowDrawList();
        var x = origin.X + TextDraw.Measure(shown).X + BadgeGap * 2f * scale;
        var midY = origin.Y + ImGui.GetTextLineHeight() * 0.5f;
        if (list.Archived)
        {
            x += Badge.DrawLeft(drawList, Loc.T(L.Lists.Archived), Styling.TextDim, x, midY) + BadgeGap * scale;
        }

        if (analysis.Validation.ErrorLines > 0)
        {
            var issues = issuesText.Get(analysis.Validation.ErrorLines, static count => Loc.Plural(L.Lists.NeedsFix, (int)count));
            Badge.DrawLeft(drawList, issues, Styling.AccentRose, x, midY);
        }
    }

    private static void SectionTitle(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.SectionTitleSize(text);
        TextDraw.SectionTitle(text, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, size.Y + 8f * scale));
    }

    private static void EmptyCard(FontAwesomeIcon icon, string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, EmptyCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var padX = EmptyCardPadX * scale;
        var wrapWidth = size.X - padX * 2f;
        var textSize = TextDraw.MeasureWrapped(text, wrapWidth);
        var iconHeight = 26f * scale;
        var gap = 12f * scale;
        var top = origin.Y + (size.Y - iconHeight - gap - textSize.Y) * 0.5f;
        var center = new Vector2((origin.X + end.X) * 0.5f, top + iconHeight * 0.5f);
        ProgressRing.CenterIcon(center, icon, Styling.TextMuted, iconHeight);
        TextDraw.Wrapped(text, new Vector2(center.X - textSize.X * 0.5f, top + iconHeight + gap), wrapWidth, Styling.TextMuted);
        ImGui.Dummy(size);
    }
}
