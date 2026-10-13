using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Windows.Components;
using AutoShopper.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed class HistoryPage
{
    private const int ChartRuns = 24;
    private const int TileCount = 3;
    private const float PadX = 14f;
    private const float IconColumn = 18f;
    private const float ConfirmSlide = 12f;
    private const string NoValue = "-";
    private const float BadgeGap = 6f;
    private const float SummaryButtonGap = 6f;
    private const string SummaryMenuId = "##ashop_hist_summary_menu";

    private static readonly Dictionary<RunRecord, string> details = new(ReferenceEqualityComparer.Instance);
    private static LanguageInfo? detailLanguage;

    private bool confirmClear;
    private CachedText finishedText;
    private CachedText timeText;
    private CachedText perRunText;
    private CachedText topWorldText;
    private TransientNotice summaryNotice;
    private RunRecord? menuRecord;

    public void Draw(Plugin plugin)
    {
        var history = plugin.History;
        var totals = history.Lifetime;
        var subtitle = history.Records.Count == 0
            ? Loc.T(L.History.Empty)
            : Loc.Plural(L.History.Summary, history.Records.Count, Formatting.Elapsed(totals.Duration));
        PageHeader.Draw(Loc.T(L.History.Title), subtitle);

        DrawLifetime(totals, history.Revision);
        Styling.VSpace(14f);

        if (history.Records.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        Label(Loc.T(L.History.TimePerRun));
        DrawChart(history);
        Styling.VSpace(12f);

        DrawRecentRunsLabel();
        var records = history.Records;
        for (var index = 0; index < records.Count; index++)
        {
            if (DrawRow(plugin.Summaries, records[index], index))
            {
                menuRecord = records[index];
                ImGui.OpenPopup(SummaryMenuId);
            }

            Styling.VSpace(2f);
        }

        DrawSummaryMenu(plugin.Summaries);

        Styling.VSpace(8f);
        DrawClearControl(history);
    }

    private static void Label(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.SectionTitleSize(text);
        TextDraw.SectionTitle(text, origin, Styling.TextStrong);
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, size.Y + 8f * scale));
    }

    // The section label shares its line with what the last summary copy did.
    private void DrawRecentRunsLabel()
    {
        var label = Loc.T(L.History.RecentRuns);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var titleSize = TextDraw.SectionTitleSize(label);
        Label(label);
        summaryNotice.DrawRight(origin.X + width, origin.Y + titleSize.Y * 0.5f, width - titleSize.X - PadX * ImGuiHelpers.GlobalScale);
    }

    private void DrawSummaryMenu(RunSummaries summaries)
    {
        if (!SummaryMenu.Draw(SummaryMenuId, out var format) || menuRecord is null)
        {
            return;
        }

        SummaryMenu.Deliver(summaries.Export(menuRecord, format), ref summaryNotice);
    }

    // The captions are rebuilt only when the history changes; the revision is their cache key.
    private void DrawLifetime(RunHistory.LifetimeTotals totals, int revision)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * (TileCount - 1)) / TileCount;
        var finished = totals.Runs > 0 ? finishedText.Get(revision, static _ => Loc.T(L.History.TileFinished, Plugin.Instance.History.Lifetime.Finished)) : null;
        var time = timeText.Get(revision, static _ => Formatting.Elapsed(Plugin.Instance.History.Lifetime.Duration));
        var perRun = totals.Runs > 0 ? perRunText.Get(revision, static _ => Loc.T(L.History.TilePerRun, Formatting.Elapsed(Plugin.Instance.History.Lifetime.AverageDuration))) : null;
        var topWorld = totals.TopWorld is null ? NoValue : topWorldText.Get(revision, static _ => WhereText(Plugin.Instance.History.Lifetime.TopWorld!, Plugin.Instance.History.Lifetime.TopDataCenter ?? string.Empty));

        StatTile.Draw(Loc.T(L.History.TileRuns), totals.Runs.ToString("N0", Loc.Culture), finished, Styling.AccentGlow, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.History.TileTime), time, perRun, Styling.AccentBlue, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.History.TileTopWorld), topWorld, null, Styling.AccentNebula, tileWidth);
    }

    private static string WhereText(string world, string dataCenter)
    {
        if (world.Length == 0)
        {
            return NoValue;
        }

        return dataCenter.Length == 0 ? world : Loc.T(L.History.WorldAndDataCenter, world, dataCenter);
    }

    private static void DrawEmptyState()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 110f * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var center = new Vector2((origin.X + end.X) * 0.5f, origin.Y + size.Y * 0.42f);
        ProgressRing.CenterIcon(center, FontAwesomeIcon.History, Styling.TextMuted, 26f * scale);
        TextDraw.Center(Loc.T(L.History.NoRuns), center.X, center.Y + 22f * scale, Styling.TextMuted);
        ImGui.Dummy(size);
    }

    private static void DrawChart(RunHistory history)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var records = history.Records;
        var count = Math.Min(ChartRuns, records.Count);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.ChartHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();

        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var padX = PadX * scale;
        var plotMin = new Vector2(origin.X + padX, origin.Y + 28f * scale);
        var plotMax = new Vector2(end.X - padX, end.Y - 12f * scale);
        var plotWidth = plotMax.X - plotMin.X;
        var plotHeight = plotMax.Y - plotMin.Y;

        var peakSeconds = 1.0;
        for (var index = 0; index < count; index++)
        {
            peakSeconds = Math.Max(peakSeconds, records[index].DurationSeconds);
        }

        using (Fonts.PushCaption())
        {
            TextDraw.At(Loc.Plural(L.History.ChartRange, count), new Vector2(plotMin.X, origin.Y + 9f * scale), Styling.TextMuted);
            TextDraw.Right(Loc.T(L.History.ChartPeak, Formatting.Elapsed(TimeSpan.FromSeconds(peakSeconds))), plotMax.X, origin.Y + 9f * scale, Styling.TextMuted);
        }

        drawList.AddLine(new Vector2(plotMin.X, plotMax.Y), new Vector2(plotMax.X, plotMax.Y), Paint.Col(Styling.WithAlpha(Styling.BorderDim, 0.7f)), 1f);

        var gap = 2f * scale;
        var barWidth = MathF.Max(2f * scale, (plotWidth - gap * (count - 1)) / count);
        var stride = barWidth + gap;
        var hovered = -1;
        if (Hit.HoveringRect(plotMin, plotMax))
        {
            hovered = (int)((ImGui.GetMousePos().X - plotMin.X) / stride);
            if (hovered >= count)
            {
                hovered = -1;
            }
        }

        var rounding = MathF.Min(4f * scale, barWidth * 0.5f);
        for (var index = 0; index < count; index++)
        {
            var record = records[count - 1 - index];
            var height = MathF.Max(2f * scale, plotHeight * (float)(record.DurationSeconds / peakSeconds));
            var barMin = new Vector2(plotMin.X + stride * index, plotMax.Y - height);
            var barMax = new Vector2(barMin.X + barWidth, plotMax.Y);
            var color = index == hovered ? Styling.AccentGlowSoft : Styling.AccentGlow;
            drawList.AddRectFilled(barMin, barMax, Paint.Col(color), rounding, ImDrawFlags.RoundCornersTop);
        }

        if (hovered >= 0)
        {
            Tooltip.Show(ChartTooltip(records[count - 1 - hovered]));
        }

        ImGui.Dummy(size);
    }

    private static string ChartTooltip(RunRecord record)
        => Loc.T(L.History.ChartTooltip, Formatting.RelativeTime(record.EndedAtUtc), Formatting.Elapsed(record.Duration));

    // True when the row's summary button was clicked.
    private static bool DrawRow(RunSummaries summaries, RunRecord record, int index)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.HistoryRowHeight * scale);
        if (!ImGui.IsRectVisible(size))
        {
            ImGui.Dummy(size);
            return false;
        }

        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var buttonSize = ImGui.GetFrameHeight();
        var buttonX = end.X - PadX * scale - buttonSize;

        ImGui.PushID((nint)(index + 1));
        var hit = Hit.Area("##run", new Vector2(buttonX - SummaryButtonGap * scale - origin.X, size.Y), handCursor: false);
        var hover = Motion.Hover(Motion.Key("##run"), Hit.HoveringRect(origin, end));
        var copy = DrawSummaryButton(summaries, record, new Vector2(buttonX, origin.Y + (size.Y - buttonSize) * 0.5f), buttonSize);
        ImGui.PopID();

        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, Styling.AccentGlow, 0.02f, hover);

        var padX = PadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var textX = DrawRunIcon(origin.X + padX, midY);
        var when = Formatting.RelativeTime(record.EndedAtUtc);
        var detail = RowDetail(record);

        var whenSize = TextDraw.Measure(when);
        Vector2 detailSize;
        using (Fonts.PushCaption())
        {
            detailSize = TextDraw.Measure(detail);
        }

        var top = midY - (whenSize.Y + 3f * scale + detailSize.Y) * 0.5f;
        TextDraw.At(when, new Vector2(textX, top), Styling.TextStrong);
        var badgeX = textX + whenSize.X + BadgeGap * 2f * scale;
        badgeX += DrawOutcomeBadge(drawList, record, badgeX, top + whenSize.Y * 0.5f);
        if (record.DryRun)
        {
            Badge.DrawLeft(drawList, Loc.T(L.History.DryRun), Styling.AccentBlue, badgeX + BadgeGap * scale, top + whenSize.Y * 0.5f);
        }
        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(detail, buttonX - SummaryButtonGap * scale - textX), new Vector2(textX, top + whenSize.Y + 3f * scale), Styling.TextDim);
        }

        if (hit.Hovered)
        {
            Tooltip.Show(RunTooltip(record));
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        return copy;
    }

    // Enabled when a summary can be tried; otherwise its tooltip says why there is none.
    private static bool DrawSummaryButton(RunSummaries summaries, RunRecord record, Vector2 position, float size)
    {
        var availability = summaries.Availability(record);
        var enabled = availability == SummaryStatus.Ready;
        ImGui.SetCursorScreenPos(position);
        var clicked = IconButton.Draw(FontAwesomeIcon.FileExport, "##summary", size, Styling.TextSecondary, Loc.T(L.Exports.CopySummary), enabled);
        if (!enabled && Hit.HoveringRect(position, position + new Vector2(size, size)))
        {
            Tooltip.Show(SummaryMenu.Reason(availability));
        }

        return clicked;
    }

    private static float DrawOutcomeBadge(ImDrawListPtr drawList, RunRecord record, float x, float midY)
    {
        if (record.Outcome is not { } outcome)
        {
            return 0f;
        }

        return Badge.DrawLeft(drawList, OutcomeLabel(outcome), OutcomeColor(outcome), x, midY);
    }

    // Built once per row and language: a recorded run never changes.
    private static string RowDetail(RunRecord record)
    {
        if (!ReferenceEquals(detailLanguage, Loc.Current))
        {
            details.Clear();
            detailLanguage = Loc.Current;
        }

        if (details.TryGetValue(record, out var cached))
        {
            return cached;
        }

        var detail = Loc.T(L.History.RowDetail, WhereText(record.WorldName, record.DataCenterName), Formatting.Elapsed(record.Duration));
        if (record.Units > 0 || record.Spent > 0)
        {
            var spent = Loc.T(record.DryRun ? L.History.WouldSpend : L.History.Spent, Formatting.Gil(record.Spent));
            detail = string.Concat(detail, TextDraw.Separator, spent, TextDraw.Separator, Loc.Plural(L.History.Units, record.Units));
        }

        details[record] = detail;
        return detail;
    }

    internal static string OutcomeLabel(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Completed => Loc.T(L.History.OutcomeCompleted),
        RunOutcome.CompletedPartial => Loc.T(L.History.OutcomeCompletedPartial),
        RunOutcome.Stopped => Loc.T(L.History.OutcomeStopped),
        RunOutcome.NeedsReconciliation => Loc.T(L.History.OutcomeNeedsReconciliation),
        _ => Loc.T(L.History.OutcomeFailed),
    };

    internal static Vector4 OutcomeColor(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Completed => Styling.AccentMint,
        RunOutcome.CompletedPartial => Styling.AccentAmber,
        RunOutcome.Stopped => Styling.TextDim,
        _ => Styling.AccentRose,
    };

    // Returns where the row's text starts; the icon is centred in a fixed column so every row's text lines up.
    private static float DrawRunIcon(float x, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var column = IconColumn * scale;
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.ShoppingCart);
        TextDraw.Icon(FontAwesomeIcon.ShoppingCart, new Vector2(x + (column - iconSize.X) * 0.5f, midY - iconSize.Y * 0.5f), Styling.TextDim);
        return x + column + 10f * scale;
    }

    private static string RunTooltip(RunRecord record)
    {
        var lines = record.EndedAtUtc.ToLocalTime().ToString("g", Loc.Culture);
        if (record.WorldName.Length > 0)
        {
            lines += "\n" + Loc.T(L.History.TooltipWorld, record.WorldName, record.DataCenterName.Length == 0 ? NoValue : record.DataCenterName);
        }

        if (record.CrossedDataCenter)
        {
            lines += "\n" + Loc.T(L.History.TooltipCrossed);
        }

        if (record.ListName.Length > 0)
        {
            lines += "\n" + Loc.T(L.History.TooltipList, record.ListName);
        }

        return lines;
    }

    private void DrawClearControl(RunHistory history)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var available = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var reveal = Motion.Transition(Motion.Key("##ashop_hist_clear_state"), confirmClear);
        var slide = (1f - reveal) * ConfirmSlide * scale;
        using var alpha = Motion.PushAlpha(reveal);

        if (!confirmClear)
        {
            var label = Loc.T(L.History.ClearHistory);
            ImGui.SetCursorScreenPos(new Vector2(origin.X + available - PillButton.Width(label, FontAwesomeIcon.Trash) - slide, origin.Y));
            if (PillButton.Draw("##ashop_hist_clear", label, Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Trash))
            {
                confirmClear = true;
            }

            return;
        }

        var question = Loc.T(L.History.ClearQuestion);
        var yes = Loc.T(L.History.ClearYes);
        var no = Loc.T(L.Common.Cancel);
        var questionSize = TextDraw.Measure(question);
        var yesWidth = PillButton.Width(yes);
        var noWidth = PillButton.Width(no);
        var gap = 8f * scale;
        var buttonHeight = 28f * scale;

        var x = origin.X + available - noWidth - slide;
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_hist_clear_no", no, Styling.AccentGlow, PillButton.Emphasis.Ghost))
        {
            confirmClear = false;
        }

        x -= gap + yesWidth;
        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
        if (PillButton.Draw("##ashop_hist_clear_yes", yes, Styling.AccentRose, PillButton.Emphasis.Filled))
        {
            history.Clear();
            confirmClear = false;
        }

        TextDraw.At(question, new Vector2(x - gap * 1.5f - questionSize.X, origin.Y + (buttonHeight - questionSize.Y) * 0.5f), Styling.AccentRoseSoft);
    }
}
