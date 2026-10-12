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
    private const string NoValue = "-";

    private static uint cachedTerritoryId = uint.MaxValue;
    private static string cachedZoneName = string.Empty;

    public static void Draw(AutoShopController controller)
    {
        var paused = controller.Paused;
        var (accent, accentSoft, label) = PhasePalette(controller);

        DrawHeaderStrip(CurrentZoneName(), accent, accentSoft, paused);
        Styling.VSpace(6f);
        DrawHeroCard(controller, accent, accentSoft, label);

        Styling.VSpace(10f);
        DrawStatTiles(controller);
    }

    private static void DrawHeaderStrip(string footer, Vector4 accent, Vector4 accentSoft, bool paused)
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

        using (Fonts.PushCaption())
        {
            var footerLeft = statusX + statusSize.X + HeaderFooterGap * scale;
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
}
