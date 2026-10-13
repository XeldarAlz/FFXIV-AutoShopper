using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class ListsPage
{
    private const float ColumnGap = 18f;
    private const float SectionGap = 10f;
    private const float ActionGap = 4f;
    private const float SubtitleGap = 5f;
    private const float BadgeGap = 8f;
    private const float ConfirmSlide = 12f;
    private const float EmptyCardHeight = 150f;
    private const float EmptyCardPadX = 24f;
    private const long RevisionStride = 2;

    private Guid shownListId;
    private bool renaming;
    private bool focusRename;
    private string renameText = string.Empty;
    private bool confirmDelete;
    private CachedText subtitleText;
    private CachedText errorsText;
    private CachedText warningsText;
    private TransientNotice notice;

    public void Draw(Plugin plugin)
    {
        plugin.Items.EnsureLoading();
        var scale = ImGuiHelpers.GlobalScale;
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
        {
            using (var column = ImRaii.Child("##ashop_lists_column", new Vector2(ListColumnWidth * scale, -1f), false, ImGuiWindowFlags.None))
            {
                if (column)
                {
                    DrawListColumn(plugin.Lists);
                }
            }

            ImGui.SameLine(0f, ColumnGap * scale);

            using (var editor = ImRaii.Child("##ashop_lists_editor", new Vector2(-1f, -1f), false, ImGuiWindowFlags.None))
            {
                if (editor)
                {
                    DrawEditor(plugin);
                }
            }
        }
    }

    private void DrawEditor(Plugin plugin)
    {
        var store = plugin.Lists;
        var list = store.Selected;
        if (list is null)
        {
            DrawEmptyState(store);
            return;
        }

        if (list.Id != shownListId)
        {
            OnListChanged(list);
        }

        var analysis = store.Analyze(list);
        using var reveal = Motion.PushSwitch("##ashop_lists_shown", list.Id.GetHashCode());
        using var group = ImRaii.Group();
        ImGui.Dummy(new Vector2(0f, 2f * ImGuiHelpers.GlobalScale));
        DrawHeader(store, list, analysis);
        if (DrawDeleteConfirm(store, list))
        {
            return;
        }

        Paint.Divider(8f);

        DrawToolbar(plugin, list);
        DrawNotice();
        DrawSearchResults(plugin, list);
        if (importOpen)
        {
            Styling.VSpace(SectionGap);
            DrawImportPanel(plugin, list);
        }

        if (crystalsOpen)
        {
            Styling.VSpace(SectionGap);
            DrawCrystalPanel(plugin, list);
        }

        Styling.VSpace(SectionGap);
        DrawLines(plugin, list, analysis);
    }

    private void OnListChanged(ShoppingList list)
    {
        shownListId = list.Id;
        renaming = false;
        confirmDelete = false;
        capTexts.Clear();
        excessTexts.Clear();
        CloseImport();
        CloseCrystals();
    }

    private void DrawHeader(ShoppingListStore store, ShoppingList list, ListAnalysis analysis)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var buttonSize = ImGui.GetFrameHeight();
        var actionsWidth = buttonSize * 4f + ActionGap * scale * 3f;
        var titleWidth = MathF.Max(1f, width - actionsWidth - ColumnGap * scale);

        float titleHeight;
        using (Fonts.PushTitle())
        {
            titleHeight = ImGui.GetFrameHeight();
            if (renaming)
            {
                DrawRenameField(store, list, origin, titleWidth);
            }
            else
            {
                var title = TextDraw.Truncate(list.Name, titleWidth);
                var titleSize = TextDraw.Measure(title);
                TextDraw.At(title, new Vector2(origin.X, origin.Y + (titleHeight - titleSize.Y) * 0.5f), Styling.TextStrong);
                if (Hit.HoveringRect(origin, origin + new Vector2(titleSize.X, titleHeight)) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    BeginRename(list);
                }
            }
        }

        DrawHeaderActions(store, list, new Vector2(origin.X + width - actionsWidth, origin.Y + (titleHeight - buttonSize) * 0.5f), buttonSize);

        var subtitleY = origin.Y + titleHeight + SubtitleGap * scale;
        DrawSubtitle(store, list, analysis, new Vector2(origin.X, subtitleY), width);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, titleHeight + SubtitleGap * scale + ImGui.GetTextLineHeight()));
    }

    private void DrawRenameField(ShoppingListStore store, ShoppingList list, Vector2 origin, float width)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.SetNextItemWidth(width);
        if (focusRename)
        {
            ImGui.SetKeyboardFocusHere();
            focusRename = false;
        }

        bool submitted;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Styling.WithAlpha(Styling.Surface0, 0.9f))
            .Push(ImGuiCol.FrameBgHovered, Styling.WithAlpha(Styling.Surface1, 0.9f))
            .Push(ImGuiCol.FrameBgActive, Styling.WithAlpha(Styling.Surface1, 0.9f)))
        {
            submitted = ImGui.InputTextWithHint("##ashop_list_rename", Loc.T(L.Lists.NameHint), ref renameText, ShoppingLimits.MaximumListNameLength,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            renaming = false;
            return;
        }

        if (submitted || ImGui.IsItemDeactivated())
        {
            store.Rename(list, renameText);
            renaming = false;
        }
    }

    private void BeginRename(ShoppingList list)
    {
        renaming = true;
        focusRename = true;
        renameText = list.Name;
        confirmDelete = false;
    }

    private void DrawHeaderActions(ShoppingListStore store, ShoppingList list, Vector2 origin, float buttonSize)
    {
        var step = buttonSize + ActionGap * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorScreenPos(origin);
        if (IconButton.Draw(FontAwesomeIcon.Pen, "##ashop_list_rename_button", buttonSize, Styling.TextSecondary, Loc.T(L.Lists.Rename)))
        {
            BeginRename(list);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(step, 0f));
        if (IconButton.Draw(FontAwesomeIcon.Copy, "##ashop_list_duplicate", buttonSize, Styling.TextSecondary, Loc.T(L.Lists.Duplicate)))
        {
            store.Duplicate(list);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(step * 2f, 0f));
        var archiveIcon = list.Archived ? FontAwesomeIcon.BoxOpen : FontAwesomeIcon.Archive;
        var archiveTooltip = Loc.T(list.Archived ? L.Lists.Unarchive : L.Lists.Archive);
        if (IconButton.Draw(archiveIcon, "##ashop_list_archive", buttonSize, Styling.TextSecondary, archiveTooltip))
        {
            store.SetArchived(list, !list.Archived);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(step * 3f, 0f));
        if (IconButton.Draw(FontAwesomeIcon.TrashAlt, "##ashop_list_delete", buttonSize, Styling.AccentRose, Loc.T(L.Lists.Delete)))
        {
            confirmDelete = !confirmDelete;
            renaming = false;
        }
    }

    // The captions are rebuilt only when the list changes; the store revision, and whether item data was there to
    // check against, is their cache key.
    private void DrawSubtitle(ShoppingListStore store, ShoppingList list, ListAnalysis analysis, Vector2 origin, float width)
    {
        var key = store.Revision * RevisionStride + (analysis.Validation.ItemsChecked ? 1 : 0);
        var subtitle = subtitleText.Get(key, static _ => Summary(Plugin.Instance.Lists.Selected));
        var x = origin.X;
        var midY = origin.Y + ImGui.GetTextLineHeight() * 0.5f;
        TextDraw.At(TextDraw.Truncate(subtitle, width), origin, Styling.TextDim);
        x += TextDraw.Measure(subtitle).X + BadgeGap * 2f * ImGuiHelpers.GlobalScale;

        var drawList = ImGui.GetWindowDrawList();
        if (list.Archived)
        {
            x += Badge.DrawLeft(drawList, Loc.T(L.Lists.Archived), Styling.TextDim, x, midY) + BadgeGap * ImGuiHelpers.GlobalScale;
        }

        var validation = analysis.Validation;
        if (validation.ErrorLines > 0)
        {
            var errors = errorsText.Get(validation.ErrorLines, static count => Loc.Plural(L.Lists.NeedsFix, (int)count));
            x += Badge.DrawLeft(drawList, errors, Styling.AccentRose, x, midY) + BadgeGap * ImGuiHelpers.GlobalScale;
        }

        if (validation.WarningLines > 0)
        {
            var warnings = warningsText.Get(validation.WarningLines, static count => Loc.Plural(L.Lists.Warnings, (int)count));
            var badgeWidth = Badge.DrawLeft(drawList, warnings, Styling.AccentAmber, x, midY);
            if (Hit.HoveringRect(new Vector2(x, origin.Y), new Vector2(x + badgeWidth, origin.Y + ImGui.GetTextLineHeight())))
            {
                Tooltip.Show(Loc.T(L.Lists.MergeNote));
            }
        }
    }

    internal static string Summary(ShoppingList? list)
    {
        if (list is null)
        {
            return string.Empty;
        }

        var units = list.TotalUnits();
        return string.Concat(
            Loc.Plural(L.Lists.LineCount, list.Lines.Count),
            TextDraw.Separator,
            Loc.Plural(L.Lists.UnitCount, (int)Math.Min(units, int.MaxValue)));
    }

    // True when the list was deleted, so the rest of the editor skips the frame instead of drawing a list that is gone.
    private bool DrawDeleteConfirm(ShoppingListStore store, ShoppingList list)
    {
        var reveal = Motion.Transition(Motion.Key("##ashop_list_delete_state"), confirmDelete);
        if (!confirmDelete)
        {
            return false;
        }

        var scale = ImGuiHelpers.GlobalScale;
        using var alpha = Motion.PushAlpha(reveal);
        Styling.VSpace(6f);
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var slide = (1f - reveal) * ConfirmSlide * scale;
        var question = Loc.T(L.Lists.DeleteQuestion, list.Name);
        var yes = Loc.T(L.Lists.DeleteYes);
        var no = Loc.T(L.Common.Cancel);
        var gap = 8f * scale;
        var buttonHeight = Layout.ActionPillHeight * scale;

        var x = origin.X + available - PillButton.Width(no) - slide;
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_list_delete_no", no, Styling.AccentGlow, PillButton.Emphasis.Ghost, height: Layout.ActionPillHeight))
        {
            confirmDelete = false;
        }

        x -= gap + PillButton.Width(yes);
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_list_delete_yes", yes, Styling.AccentRose, PillButton.Emphasis.Filled, height: Layout.ActionPillHeight))
        {
            confirmDelete = false;
            store.Delete(list);
            return true;
        }

        var questionWidth = x - gap * 1.5f - origin.X;
        var shown = TextDraw.Truncate(question, questionWidth);
        var questionSize = TextDraw.Measure(shown);
        TextDraw.At(shown, new Vector2(x - gap * 1.5f - questionSize.X, origin.Y + (buttonHeight - questionSize.Y) * 0.5f), Styling.AccentRoseSoft);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, buttonHeight));
        return false;
    }

    private static void DrawEmptyState(ShoppingListStore store)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, EmptyCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var padX = EmptyCardPadX * scale;
        var wrapWidth = size.X - padX * 2f;
        var hasLists = store.Lists.Count > 0;
        var title = Loc.T(hasLists ? L.Lists.NoSelectionTitle : L.Lists.EmptyTitle);
        var body = Loc.T(hasLists ? L.Lists.NoSelectionBody : L.Lists.EmptyBody);
        var bodySize = TextDraw.MeasureWrapped(body, wrapWidth);
        var titleSize = TextDraw.SectionTitleSize(title);
        var label = Loc.T(L.Lists.NewList);
        var buttonWidth = PillButton.Width(label, FontAwesomeIcon.Plus);
        var buttonHeight = Layout.ActionPillHeight * scale;
        var gap = 10f * scale;
        var top = origin.Y + (size.Y - titleSize.Y - gap - bodySize.Y - gap * 1.5f - buttonHeight) * 0.5f;
        var centerX = (origin.X + end.X) * 0.5f;

        TextDraw.SectionTitle(title, new Vector2(centerX - titleSize.X * 0.5f, top), Styling.TextStrong);
        top += titleSize.Y + gap;
        TextDraw.Wrapped(body, new Vector2(centerX - MathF.Min(bodySize.X, wrapWidth) * 0.5f, top), wrapWidth, Styling.TextMuted);
        top += bodySize.Y + gap * 1.5f;

        ImGui.SetCursorScreenPos(new Vector2(centerX - buttonWidth * 0.5f, top));
        if (PillButton.Draw("##ashop_lists_empty_new", label, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.Plus, height: Layout.ActionPillHeight))
        {
            store.CreateList();
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    // The title row, close button and help text of an inline panel; true when the close button was clicked.
    private static bool DrawPanelTitle(string closeId, string title, string help, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var titleSize = TextDraw.SectionTitleSize(title);
        var closeSize = ImGui.GetFrameHeight();
        TextDraw.SectionTitle(title, new Vector2(origin.X, origin.Y + (closeSize - titleSize.Y) * 0.5f), Styling.TextStrong);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - closeSize, origin.Y));
        var closed = IconButton.Draw(FontAwesomeIcon.Times, closeId, closeSize, Styling.TextDim, Loc.T(L.Common.Close));
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, closeSize));

        using (Fonts.PushCaption())
        {
            var helpOrigin = ImGui.GetCursorScreenPos();
            var helpSize = TextDraw.MeasureWrapped(help, width);
            TextDraw.Wrapped(help, helpOrigin, width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, helpSize.Y + 6f * scale));
        }

        return closed;
    }

    private void ShowNotice(string text, Vector4 color) => notice.Show(text, color);

    private void DrawNotice() => notice.DrawLine(ImGui.GetContentRegionAvail().X);
}
