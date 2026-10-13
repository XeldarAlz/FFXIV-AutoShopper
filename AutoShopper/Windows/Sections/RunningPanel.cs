using AutoShopper.Core;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using AutoShopper.Core.Travel;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

internal static class RunningPanel
{
    private const float PadX = 18f;
    private const float CardPadY = 16f;
    private const float RingInset = 18f;
    private const float RingGap = 20f;
    private const float ChipPadX = 9f;
    private const float ChipPadY = 3f;
    private const float ChipGap = 10f;
    private const float StatusGap = 12f;
    private const float BarHeight = 8f;
    private const float HeaderFooterGap = 16f;
    private const int TileCount = 2;
    private const int ShopTileCount = 4;
    private const float SectionGap = 18f;
    private const float RowHeight = 34f;
    private const float RowIconSize = 24f;
    private const float RowPadX = 14f;
    private const float ProgressColumnWidth = 110f;
    private const float SpentColumnWidth = 120f;
    private const float RowBarHeight = 3f;
    private const float IssueRowHeight = 42f;
    private const float RouteChipHeight = 26f;
    private const float RouteChipPadX = 10f;
    private const float RouteChipIconGap = 6f;
    private const float RouteChipGap = 6f;
    private const float RouteNoteGap = 6f;
    // Packs the run's revision, the elapsed seconds, the phase and the login state into one cache key.
    private const int RevisionKeyShift = 28;
    private const int ElapsedKeyShift = 4;
    private const int PhaseKeyShift = 1;
    private const string NoValue = "-";

    private static uint cachedTerritoryId = uint.MaxValue;
    private static string cachedZoneName = string.Empty;
    private static RunView? view;
    private static CachedText travelText;

    public static void Draw(AutoShopController controller)
    {
        var paused = controller.Paused;
        var (accent, accentSoft, label) = PhasePalette(controller);
        var run = controller.ShopRun;

        DrawHeaderStrip(CurrentZoneName(), accent, accentSoft, paused, run is { DryRun: true });
        Styling.VSpace(6f);
        DrawHeroCard(controller, accent, accentSoft, label);

        Styling.VSpace(10f);
        if (run is null)
        {
            DrawStatTiles(controller);
            return;
        }

        var current = EnsureView(run);
        DrawShopTiles(controller, current);
        DrawRoute(current, run);
        DrawLines(current);
        DrawIssues(current);
        DrawSummaryAction(run);
    }

    // The stops behind and ahead, the trip under way with its time, why stops were skipped, and the way home.
    private static void DrawRoute(RunView current, ShopRunState run)
    {
        if (current.Route.Length == 0 && run.TravelPhase == TravelPhase.None && current.ReturnHome is null)
        {
            return;
        }

        GroupLabel.Draw(Loc.T(L.Route.Title), SectionGap, 6f);
        DrawRouteChips(current.Route);
        using (Fonts.PushCaption())
        {
            if (run.TravelPhase != TravelPhase.None)
            {
                DrawRouteNote(TravelLine(run), Styling.AccentBlueSoft);
            }

            for (var index = 0; index < current.SkippedNotes.Length; index++)
            {
                DrawRouteNote(current.SkippedNotes[index], Styling.TextDim);
            }

            if (current.ReturnHome is { } home)
            {
                DrawRouteNote(home, Styling.TextSecondary);
            }
        }
    }

    private static void DrawRouteChips(RouteChipView[] chips)
    {
        if (chips.Length == 0)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var right = origin.X + ImGui.GetContentRegionAvail().X;
        var height = RouteChipHeight * scale;
        var gap = RouteChipGap * scale;
        var padX = RouteChipPadX * scale;
        var x = origin.X;
        var y = origin.Y;
        for (var index = 0; index < chips.Length; index++)
        {
            var chip = chips[index];
            var icon = ChipIcon(chip.State);
            var iconSize = TextDraw.IconSize(icon);
            var textSize = TextDraw.Measure(chip.Label);
            var width = padX * 2f + iconSize.X + RouteChipIconGap * scale + textSize.X;
            if (x + width > right && x > origin.X)
            {
                x = origin.X;
                y += height + gap;
            }

            var min = new Vector2(x, y);
            var max = min + new Vector2(width, height);
            var accent = ChipAccent(chip.State);
            var current = chip.State == RouteChipState.Current;
            var border = current ? Styling.PulseColor(Styling.WithAlpha(accent, 0.45f), accent, Styling.PulseMedium) : Styling.WithAlpha(accent, 0.45f);
            Paint.Pill(drawList, min, max, Styling.WithAlpha(accent, current ? 0.22f : 0.10f), border);
            var midY = y + height * 0.5f;
            TextDraw.Icon(icon, new Vector2(x + padX, midY - iconSize.Y * 0.5f), accent);
            TextDraw.At(chip.Label, new Vector2(x + padX + iconSize.X + RouteChipIconGap * scale, midY - textSize.Y * 0.5f), chip.State == RouteChipState.Skipped ? Styling.TextMuted : Styling.TextStrong);
            if (Hit.HoveringRect(min, max))
            {
                Tooltip.Show(chip.Tooltip);
            }

            x += width + gap;
        }

        ImGui.Dummy(new Vector2(right - origin.X, y + height - origin.Y));
    }

    private static void DrawRouteNote(string text, Vector4 color)
    {
        Styling.VSpace(RouteNoteGap);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        TextDraw.At(TextDraw.Truncate(text, width), origin, color);
        ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
    }

    // Rebuilt once a second while the trip goes, and whenever its phase or target changes.
    private static string TravelLine(ShopRunState run)
    {
        var elapsedSeconds = Math.Max(0L, (Environment.TickCount64 - run.TravelStartedAtMs) / TimeUnits.MillisecondsPerSecond);
        var loggedIn = Svc.ClientState.IsLoggedIn ? 1L : 0L;
        var key = ((long)run.Revision << RevisionKeyShift) | (elapsedSeconds << ElapsedKeyShift) | ((long)run.TravelPhase << PhaseKeyShift) | loggedIn;
        return travelText.Get(key, static _ => BuildTravelLine());
    }

    private static string BuildTravelLine()
    {
        if (Plugin.Instance.Controller.ShopRun is not { } run)
        {
            return string.Empty;
        }

        var target = run.TravelTarget;
        var phase = run.TravelPhase switch
        {
            TravelPhase.ChangingWorld => Loc.T(L.Route.PhaseChangingWorld, target),
            TravelPhase.TransferringDataCenter when !Svc.ClientState.IsLoggedIn => Loc.T(L.Route.PhaseLoggedOut, target),
            TravelPhase.TransferringDataCenter => Loc.T(L.Route.PhaseTransfer, target),
            TravelPhase.Settling => Loc.T(L.Route.PhaseSettling, target),
            TravelPhase.GoingToBoard => Loc.T(L.Route.PhaseBoard, target),
            TravelPhase.WaitingToRetry => Loc.T(L.Route.PhaseRetry, target),
            _ => Loc.T(L.Route.PhaseHome, target),
        };
        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0L, Environment.TickCount64 - run.TravelStartedAtMs));
        return Loc.T(L.Route.PhaseElapsed, phase, Formatting.Elapsed(elapsed));
    }

    private static FontAwesomeIcon ChipIcon(RouteChipState state) => state switch
    {
        RouteChipState.Done => FontAwesomeIcon.Check,
        RouteChipState.Current => FontAwesomeIcon.MapMarkerAlt,
        RouteChipState.Next => FontAwesomeIcon.ArrowRight,
        RouteChipState.Skipped => FontAwesomeIcon.Ban,
        _ => FontAwesomeIcon.EllipsisH,
    };

    private static Vector4 ChipAccent(RouteChipState state) => state switch
    {
        RouteChipState.Done => Styling.AccentMint,
        RouteChipState.Current => Styling.AccentBlue,
        RouteChipState.Next => Styling.AccentGlow,
        RouteChipState.Skipped => Styling.AccentRose,
        _ => Styling.TextDim,
    };

    private static RunView EnsureView(ShopRunState run)
    {
        var withNames = Plugin.Instance.Items.IsReady;
        if (view is null || !view.IsCurrent(run, withNames))
        {
            view = RunView.Build(run, withNames);
        }

        return view;
    }

    private static void DrawHeaderStrip(string footer, Vector4 accent, Vector4 accentSoft, bool paused, bool dryRun)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var midY = origin.Y + lineHeight * 0.5f;

        var dot = paused ? accent : Styling.PulseColor(accent, accentSoft, Styling.PulseMedium);
        var radius = 4f * scale;
        Paint.Dot(drawList, new Vector2(origin.X + radius + 3f * scale, midY), radius, dot);

        var status = paused ? Loc.T(L.Shell.StatusPaused) : Loc.T(L.Shell.StatusRunning);
        var statusSize = TextDraw.SmallCapsSize(status);
        var statusX = origin.X + radius * 2f + 12f * scale;
        TextDraw.SmallCaps(status, new Vector2(statusX, midY - statusSize.Y * 0.5f), Styling.TextSecondary);
        var statusEnd = statusX + statusSize.X;
        if (dryRun)
        {
            statusEnd += HeaderFooterGap * scale * 0.5f;
            statusEnd += Badge.DrawLeft(drawList, Loc.T(L.Market.DryRunBadge), Styling.AccentBlue, statusEnd, midY);
        }

        using (Fonts.PushCaption())
        {
            var footerLeft = statusEnd + HeaderFooterGap * scale;
            var footerText = TextDraw.Truncate(footer, origin.X + available - footerLeft);
            var footerSize = TextDraw.Measure(footerText);
            TextDraw.At(footerText, new Vector2(origin.X + available - footerSize.X, midY - footerSize.Y * 0.5f), Styling.TextMuted);
        }

        ImGui.Dummy(new Vector2(available, lineHeight));
    }

    // The card is as tall as its lines at the current font sizes, and every line is cut to the column, so nothing
    // spills past its border whatever size the fonts are set to.
    private static void DrawHeroCard(AutoShopController controller, Vector4 accent, Vector4 accentSoft, string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, HeroCardHeight());
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var active = controller.Running && !controller.Paused;
        var rounding = Styling.PanelRounding * scale;

        Paint.Glass(drawList, origin, end, rounding, accent, active ? 0.10f : 0.03f, 0f, elevated: true);
        if (active)
        {
            Paint.Stroke(drawList, origin, end, Styling.PulseColor(Styling.WithAlpha(accent, 0.5f), accentSoft, Styling.PulseMedium), rounding, 1.6f);
        }

        var padX = PadX * scale;
        var ringRadius = size.Y * 0.5f - RingInset * scale;
        var ringCenter = new Vector2(origin.X + padX + ringRadius, origin.Y + size.Y * 0.5f);
        DrawRunRing(ringCenter, ringRadius, accent, active);

        var columnX = ringCenter.X + ringRadius + RingGap * scale;
        var columnWidth = end.X - padX - columnX;
        var y = origin.Y + CardPadY * scale;

        y += DrawPhaseChip(columnX, y, columnWidth, label, accent, accentSoft) + ChipGap * scale;
        y = DrawStatus(controller.Status, columnX, columnWidth, y);

        var barHeight = BarHeight * scale;
        var barOrigin = new Vector2(columnX, y);
        if (active)
        {
            Paint.IndeterminateBar(drawList, barOrigin, columnWidth, barHeight, accent);
        }
        else
        {
            Paint.Bar(drawList, barOrigin, columnWidth, barHeight, 0f, accent);
        }

        ImGui.Dummy(size);
    }

    // Adds up the same lines and gaps the card draws, so the two stay in step.
    private static float HeroCardHeight()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var bodyLine = ImGui.GetTextLineHeight();
        float captionLine;
        using (Fonts.PushCaption())
        {
            captionLine = ImGui.GetTextLineHeight();
        }

        var chip = captionLine + ChipPadY * 2f * scale;
        var gaps = (CardPadY * 2f + ChipGap + StatusGap + BarHeight) * scale;
        return gaps + chip + bodyLine;
    }

    private static float DrawStatus(string status, float x, float width, float y)
    {
        var text = TextDraw.Truncate(string.IsNullOrWhiteSpace(status) ? Loc.T(L.Common.Working) : status, width);
        TextDraw.At(text, new Vector2(x, y), Styling.TextSecondary);
        return y + ImGui.GetTextLineHeight() + StatusGap * ImGuiHelpers.GlobalScale;
    }

    // A run has no fixed amount of work to fill a ring with yet, so the ring only shows that it is moving.
    private static void DrawRunRing(Vector2 center, float radius, Vector4 accent, bool active)
    {
        var thickness = 6f * ImGuiHelpers.GlobalScale;
        ProgressRing.Track(center, radius, thickness, Styling.WithAlpha(Styling.BorderDim, 0.7f));
        if (active)
        {
            ProgressRing.Sweep(center, radius, thickness, accent, Styling.PulseOrbit, MathF.PI * 0.6f, 1f);
        }

        ProgressRing.CenterIcon(center, FontAwesomeIcon.ShoppingCart, Styling.TextDim, radius * 0.55f);
    }

    private static float DrawPhaseChip(float x, float y, float maxWidth, string text, Vector4 accent, Vector4 accentSoft)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var padX = ChipPadX * scale;
        var padY = ChipPadY * scale;

        using (Fonts.PushCaption())
        {
            var label = TextDraw.Truncate(TextDraw.Upper(text), maxWidth - padX * 2f);
            var textSize = new Vector2(TextDraw.Measure(label).X, ImGui.GetTextLineHeight());
            var chipMin = new Vector2(x, y);
            var chipMax = chipMin + new Vector2(padX * 2f + textSize.X, textSize.Y + padY * 2f);
            Paint.Pill(drawList, chipMin, chipMax, Styling.WithAlpha(accent, 0.28f), Styling.WithAlpha(accent, 0.65f));
            TextDraw.At(label, new Vector2(x + padX, y + padY), accentSoft);
            return chipMax.Y - chipMin.Y;
        }
    }

    private static (Vector4 Accent, Vector4 AccentSoft, string Label) PhasePalette(AutoShopController controller)
    {
        if (!controller.Running)
        {
            return (Styling.TextDim, Styling.TextSecondary, Loc.T(L.Run.PhaseReady));
        }

        if (controller.Stopping)
        {
            return (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhaseStopping));
        }

        if (controller.Paused)
        {
            return controller.PauseReason == PauseReason.InContent
                ? (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePausedInContent))
                : (Styling.AccentAmber, Styling.AccentAmberSoft, Loc.T(L.Run.PhasePaused));
        }

        return controller.Phase switch
        {
            RunPhase.Finishing => (Styling.AccentMint, Styling.AccentMintSoft, Loc.T(L.Run.PhaseFinishing)),
            RunPhase.Idle      => (Styling.TextDim,    Styling.TextSecondary,  Loc.T(L.Run.PhaseStandingBy)),
            _                  => (Styling.AccentBlue, Styling.AccentBlueSoft, ReadyState.PhaseLabel(controller.Phase)),
        };
    }

    private static void DrawStatTiles(AutoShopController controller)
    {
        var session = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * (TileCount - 1)) / TileCount;
        var known = Worlds.TryCurrent(out var current);

        StatTile.Draw(Loc.T(L.Run.TileElapsed), Formatting.Elapsed(session?.Elapsed ?? TimeSpan.Zero), null, Styling.AccentBlue, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileWorld), known ? current.Name : NoValue, known ? current.DataCenterName : null, Styling.AccentNebula, tileWidth);
    }

    private static void DrawShopTiles(AutoShopController controller, RunView current)
    {
        var session = controller.SessionSnapshot;
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * (ShopTileCount - 1)) / ShopTileCount;
        var known = Worlds.TryCurrent(out var world);

        StatTile.Draw(Loc.T(L.Run.TileElapsed), Formatting.Elapsed(session?.Elapsed ?? TimeSpan.Zero), null, Styling.AccentBlue, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileWorld), known ? world.Name : NoValue, known ? world.DataCenterName : null, Styling.AccentNebula, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(current.SpentLabel, current.SpentValue, current.SpentSub, Styling.AccentMint, tileWidth);
        ImGui.SameLine(0, gap);
        StatTile.Draw(Loc.T(L.Run.TileBudgetLeft), current.BudgetValue, current.BudgetSub, Styling.AccentAmber, tileWidth);
    }

    private static void DrawLines(RunView current)
    {
        if (current.Lines.Length == 0)
        {
            return;
        }

        GroupLabel.Draw(Loc.T(L.Run.Lines), SectionGap, 6f);
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var height = current.Lines.Length * RowHeight * scale;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, origin + new Vector2(width, height), Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface1, 0.45f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);
        var rowY = origin.Y;
        for (var index = 0; index < current.Lines.Length; index++)
        {
            DrawLine(current.Lines[index], origin.X, rowY, width, drawList);
            rowY += RowHeight * scale;
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawLine(in RunLineView line, float left, float top, float width, ImDrawListPtr drawList)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = RowHeight * scale;
        if (!ImGui.IsRectVisible(new Vector2(left, top), new Vector2(left + width, top + height)))
        {
            return;
        }

        var padX = RowPadX * scale;
        var midY = top + height * 0.5f;
        var iconSize = RowIconSize * scale;
        ItemIcon.Draw(drawList, line.IconId, new Vector2(left + padX, midY - iconSize * 0.5f), iconSize, line.HighQuality);

        var right = left + width - padX;
        var progressRight = right - SpentColumnWidth * scale;
        var nameX = left + padX + iconSize + 10f * scale;
        var nameRight = progressRight - ProgressColumnWidth * scale;
        var textY = midY - ImGui.GetTextLineHeight() * 0.5f;
        var badgeWidth = 0f;
        if (line.Quality is { } quality)
        {
            using (Fonts.PushCaption())
            {
                badgeWidth = TextDraw.Measure(quality).X + 24f * scale;
            }
        }

        var name = TextDraw.Truncate(line.Name, MathF.Max(0f, nameRight - nameX - badgeWidth));
        TextDraw.At(name, new Vector2(nameX, textY), line.Satisfied ? Styling.AccentMintSoft : Styling.TextStrong);
        if (line.Quality is { } label)
        {
            Badge.DrawLeft(drawList, label, line.HighQuality ? Styling.AccentAmber : Styling.TextDim, nameX + TextDraw.Measure(name).X + 8f * scale, midY);
        }

        TextDraw.Right(line.Progress, progressRight, textY, line.Satisfied ? Styling.AccentMintSoft : Styling.TextSecondary);
        TextDraw.Right(line.Spent, right, textY, Styling.TextDim);
        var barWidth = ProgressColumnWidth * scale - 12f * scale;
        Paint.Bar(drawList, new Vector2(progressRight - barWidth, top + height - RowBarHeight * scale - 4f * scale), barWidth, RowBarHeight * scale, line.Fraction, line.Satisfied ? Styling.AccentMint : Styling.AccentBlue);
    }

    private static void DrawIssues(RunView current)
    {
        GroupLabel.Draw(Loc.T(L.Run.Issues), SectionGap, 6f);
        if (current.Issues.Length == 0)
        {
            using (Fonts.PushCaption())
            {
                var origin = ImGui.GetCursorScreenPos();
                TextDraw.At(Loc.T(L.Run.NoIssues), new Vector2(origin.X + 2f * ImGuiHelpers.GlobalScale, origin.Y), Styling.TextMuted);
                ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight()));
            }

            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        for (var index = 0; index < current.Issues.Length; index++)
        {
            var issue = current.Issues[index];
            var origin = ImGui.GetCursorScreenPos();
            var height = IssueRowHeight * scale;
            var accent = issue.Blocking ? Styling.AccentRose : Styling.AccentAmber;
            Paint.Glass(drawList, origin, origin + new Vector2(width, height), Styling.CardRounding * scale, accent, 0.04f);
            var padX = RowPadX * scale;
            var lineHeight = ImGui.GetTextLineHeight();
            float captionHeight;
            using (Fonts.PushCaption())
            {
                captionHeight = ImGui.GetTextLineHeight();
            }

            var top = origin.Y + (height - lineHeight - 2f * scale - captionHeight) * 0.5f;
            Paint.Dot(drawList, new Vector2(origin.X + padX, origin.Y + height * 0.5f), 3.5f * scale, accent);
            var textX = origin.X + padX + 14f * scale;
            var textWidth = origin.X + width - padX - textX;
            TextDraw.At(TextDraw.Truncate(issue.Title, textWidth), new Vector2(textX, top), issue.Blocking ? Styling.AccentRoseSoft : Styling.TextStrong);
            using (Fonts.PushCaption())
            {
                TextDraw.At(TextDraw.Truncate(issue.Detail, textWidth), new Vector2(textX, top + lineHeight + 2f * scale), Styling.TextDim);
            }

            ImGui.Dummy(new Vector2(width, height));
            Styling.VSpace(4f);
        }
    }

    private static string CurrentZoneName()
    {
        uint territoryId = Svc.ClientState.TerritoryType;
        if (territoryId == cachedTerritoryId)
        {
            return cachedZoneName;
        }

        cachedTerritoryId = territoryId;
        cachedZoneName = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
        if (cachedZoneName.Length == 0)
        {
            cachedZoneName = Loc.T(L.Run.SomewhereElse);
        }

        return cachedZoneName;
    }

    private const string SummaryMenuId = "##ashop_run_summary_menu";
    private const float SummaryNoticeGap = 12f;

    private static TransientNotice summaryNotice;

    // Copies what the run did so far, or all of it once it ended, as text or JSON.
    private static void DrawSummaryAction(ShopRunState run)
    {
        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(SectionGap);
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.ActionPillHeight * scale;
        var label = Loc.T(L.Exports.CopyRunSummary);
        var buttonWidth = PillButton.Width(label, FontAwesomeIcon.FileExport);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, origin.Y));
        if (PillButton.Draw("##ashop_run_copy_summary", label, Styling.AccentGlow, PillButton.Emphasis.Ghost, FontAwesomeIcon.FileExport,
                height: Layout.ActionPillHeight, tooltip: Loc.T(L.Exports.SummaryHelp)))
        {
            ImGui.OpenPopup(SummaryMenuId);
        }

        if (SummaryMenu.Draw(SummaryMenuId, out var format))
        {
            SummaryMenu.Deliver(Plugin.Instance.Summaries.Export(run, format), ref summaryNotice);
        }

        var noticeRight = origin.X + width - buttonWidth - SummaryNoticeGap * scale;
        summaryNotice.DrawRight(noticeRight, origin.Y + height * 0.5f, noticeRight - origin.X);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }
}
