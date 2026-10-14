using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Windows.Components;
using AutoShopper.Windows.Pages;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

// The run that ended since the plugin loaded, with its summary one click away, until the player closes the card.
internal static class FinishedRunCard
{
    private const float CardHeight = 72f;
    private const float PadX = 18f;
    private const float IconBox = 36f;
    private const float ButtonGap = 8f;
    private const float CardGap = 14f;
    private const string MenuId = "##ashop_finished_summary_menu";
    // The detail's relative time is rebuilt at most once a minute.
    private const long DetailRefreshMs = 60_000;

    private static Guid dismissedRunId;
    private static TransientNotice notice;
    private static CachedText detailText;

    public static void Draw(Plugin plugin)
    {
        var records = plugin.History.Records;
        if (records.Count == 0)
        {
            return;
        }

        var record = records[0];
        if (record.RunId is not { } runId || runId == dismissedRunId || record.EndedAtUtc < plugin.Summaries.LoadedAtUtc)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, CardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var outcome = record.Outcome ?? RunOutcome.Stopped;
        var accent = HistoryPage.OutcomeColor(outcome);
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, 0.04f);

        var padX = PadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var iconBox = IconBox * scale;
        var iconCenter = new Vector2(origin.X + padX + iconBox * 0.5f, midY);
        drawList.AddCircleFilled(iconCenter, iconBox * 0.5f, Paint.Col(Styling.WithAlpha(accent, 0.14f)));
        TextDraw.IconCentered(FontAwesomeIcon.FlagCheckered, iconCenter, Styling.Lighten(accent, 0.3f));

        var closeSize = ImGui.GetFrameHeight();
        var closeX = end.X - padX - closeSize;
        ImGui.SetCursorScreenPos(new Vector2(closeX, midY - closeSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.Times, "##ashop_finished_close", closeSize, Styling.TextDim, Loc.T(L.Common.Close)))
        {
            dismissedRunId = runId;
        }

        var copyLabel = Loc.T(L.Exports.CopyRunSummary);
        var copyWidth = PillButton.Width(copyLabel, FontAwesomeIcon.FileExport);
        var copyX = closeX - ButtonGap * scale - copyWidth;
        DrawCopyButton(plugin.Summaries, record, copyLabel, new Vector2(copyX, midY - Layout.ActionPillHeight * scale * 0.5f));

        var contentX = iconCenter.X + iconBox * 0.5f + 14f * scale;
        var contentWidth = MathF.Max(0f, copyX - 14f * scale - contentX);
        DrawText(record, outcome, accent, contentX, contentWidth, midY);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        Styling.VSpace(CardGap);
    }

    private static void DrawCopyButton(RunSummaries summaries, RunRecord record, string label, Vector2 position)
    {
        var availability = summaries.Availability(record);
        var enabled = availability == SummaryStatus.Ready;
        ImGui.SetCursorScreenPos(position);
        var tooltip = enabled ? Loc.T(L.Exports.SummaryHelp) : SummaryMenu.Reason(availability);
        if (PillButton.Draw("##ashop_finished_copy", label, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.FileExport, enabled, Layout.ActionPillHeight, tooltip))
        {
            ImGui.OpenPopup(MenuId);
        }

        if (!enabled && Hit.HoveringRect(position, position + new Vector2(PillButton.Width(label, FontAwesomeIcon.FileExport), Layout.ActionPillHeight * ImGuiHelpers.GlobalScale)))
        {
            Tooltip.Show(tooltip);
        }

        if (SummaryMenu.Draw(MenuId, out var format))
        {
            SummaryMenu.Deliver(summaries.Export(record, format), ref notice);
        }
    }

    // The second line reports a copy for a moment, then goes back to when the run ended and what it spent.
    private static void DrawText(RunRecord record, RunOutcome outcome, Vector4 accent, float x, float width, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        var top = midY - (lineHeight * 2f + 6f * scale) * 0.5f;
        var title = Loc.T(L.Exports.FinishedTitle);
        var titleShown = TextDraw.Truncate(title, width);
        TextDraw.At(titleShown, new Vector2(x, top), Styling.TextStrong);
        var badgeX = x + TextDraw.Measure(titleShown).X + ButtonGap * scale;
        if (badgeX < x + width)
        {
            Badge.DrawLeft(ImGui.GetWindowDrawList(), HistoryPage.OutcomeLabel(outcome), accent, badgeX, top + lineHeight * 0.5f);
        }

        var detailY = top + lineHeight + 6f * scale;
        if (notice.TryGet(out var message, out var tint))
        {
            TextDraw.At(TextDraw.Truncate(message, width), new Vector2(x, detailY), tint);
            return;
        }

        var detail = detailText.Get(HashCode.Combine(record, Environment.TickCount64 / DetailRefreshMs), static _ => Detail(Plugin.Instance.History.Records[0]));
        TextDraw.At(TextDraw.Truncate(detail, width), new Vector2(x, detailY), Styling.TextDim);
    }

    private static string Detail(RunRecord record)
    {
        var detail = Formatting.RelativeTime(record.EndedAtUtc);
        if (record.Units == 0 && record.Spent == 0)
        {
            return detail;
        }

        var spent = Loc.T(L.History.Spent, Formatting.Gil(record.Spent));
        return string.Concat(detail, TextDraw.Separator, spent, TextDraw.Separator, Loc.Plural(L.History.Units, record.Units));
    }
}
