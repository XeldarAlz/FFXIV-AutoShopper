using AutoShopper.Core.Exports;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Planning;
using AutoShopper.Windows.Components;
using AutoShopper.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class PlanPage
{
    private const string ChecklistMenuId = "##ashop_plan_checklist_menu";
    private const float ChecklistNoticeGap = 12f;

    private static TransientNotice checklistNotice;

    // An empty state that points to the Lists page, where the missing list or lines are made.
    private static void DrawOpenListsCard(AppWindow window, FontAwesomeIcon icon, string text)
    {
        EmptyCard(icon, text);
        Styling.VSpace(10f);
        if (PillButton.Draw("##ashop_plan_open_lists", Loc.T(L.Shop.OpenLists), Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.ClipboardList,
                height: Layout.ActionPillHeight))
        {
            window.Show(AppWindow.Page.Lists);
        }
    }

    // The route's section title, with the checklist copy at the right of its line.
    private static void RouteTitle(PlanView current)
    {
        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(SectionGap);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var title = Loc.T(L.Plan.Route);
        var titleSize = TextDraw.SectionTitleSize(title);
        var buttonHeight = Layout.ActionPillHeight * scale;
        var rowHeight = MathF.Max(titleSize.Y, buttonHeight);
        TextDraw.SectionTitle(title, new Vector2(origin.X, origin.Y + (rowHeight - titleSize.Y) * 0.5f), Styling.TextStrong);

        var label = Loc.T(L.Exports.Checklist);
        var buttonX = origin.X + width - PillButton.Width(label, FontAwesomeIcon.ClipboardCheck);
        ImGui.SetCursorScreenPos(new Vector2(buttonX, origin.Y + (rowHeight - buttonHeight) * 0.5f));
        if (PillButton.Draw("##ashop_plan_checklist", label, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.ClipboardCheck,
                height: Layout.ActionPillHeight, tooltip: Loc.T(L.Exports.ChecklistHelp)))
        {
            ImGui.OpenPopup(ChecklistMenuId);
        }

        DrawChecklistMenu(current.Result.Selected);
        var gap = ChecklistNoticeGap * scale;
        checklistNotice.DrawRight(buttonX - gap, origin.Y + rowHeight * 0.5f, buttonX - gap * 2f - origin.X - titleSize.X);

        var height = rowHeight + 4f * scale;
        using (Fonts.PushCaption())
        {
            var note = Loc.T(L.Plan.RouteNote);
            var noteSize = TextDraw.MeasureWrapped(note, width);
            TextDraw.Wrapped(note, new Vector2(origin.X, origin.Y + height), width, Styling.TextMuted);
            height += noteSize.Y;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height + 8f * scale));
    }

    private static void DrawChecklistMenu(PurchasePlan plan)
    {
        using var menu = ContextMenu.Begin(ChecklistMenuId);
        if (!menu.Open)
        {
            return;
        }

        string? text = null;
        if (ImGui.MenuItem(Loc.T(L.Exports.ChecklistAsText)))
        {
            text = PlanChecklistExporter.ToText(plan, GameExportNames.For(Plugin.Instance.Items), DateTime.UtcNow);
        }
        else if (ImGui.MenuItem(Loc.T(L.Exports.ChecklistAsMarkdown)))
        {
            text = PlanChecklistExporter.ToMarkdown(plan, GameExportNames.For(Plugin.Instance.Items), DateTime.UtcNow);
        }

        if (text is null)
        {
            return;
        }

        ImGui.SetClipboardText(text);
        checklistNotice.Show(Loc.T(L.Exports.ChecklistCopied), Styling.AccentMintSoft);
    }
}
