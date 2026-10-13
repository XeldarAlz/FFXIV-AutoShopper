using AutoShopper.Core.Localization;
using AutoShopper.Core.Preview;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class PlanPage
{
    private const int TileCount = 4;
    private const float TileGap = 8f;
    private const float CardPadX = 16f;
    private const float CardPadY = 12f;
    private const float WarningLineGap = 6f;
    private const float BudgetBarHeight = 8f;
    private const float ModeRowHeight = 40f;
    private const float ModeHeaderHeight = 22f;
    private const float TotalColumnWidth = 150f;
    private const float StopsColumnWidth = 64f;
    private const float DataCenterColumnWidth = 96f;
    private const float CoverageColumnWidth = 120f;
    private const float ColumnGap = 10f;
    private const int MaximumWarnings = 10;

    private readonly string?[] warningTexts = new string?[MaximumWarnings];
    private readonly bool[] warningSevere = new bool[MaximumWarnings];
    private CachedText budgetText;
    private CachedText budgetPercentText;
    private CachedText budgetOverText;
    private CachedText gilText;

    private void DrawWarnings(Plugin plugin, PlanView current)
    {
        var count = CollectWarnings(plugin, current);
        if (count == 0)
        {
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = CardPadX * scale;
        var padY = CardPadY * scale;
        var iconWidth = TextDraw.IconSize(FontAwesomeIcon.ExclamationTriangle).X + 10f * scale;
        var wrapWidth = MathF.Max(1f, width - padX * 2f - iconWidth);
        var severe = false;
        var height = padY * 2f;
        for (var index = 0; index < count; index++)
        {
            severe |= warningSevere[index];
            height += TextDraw.MeasureWrapped(warningTexts[index]!, wrapWidth).Y + (index > 0 ? WarningLineGap * scale : 0f);
        }

        var accent = severe ? Styling.AccentRose : Styling.AccentAmber;
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(width, height);
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.Tint(Styling.WithAlpha(Styling.Surface1, 0.6f), accent, 0.10f), Styling.WithAlpha(accent, 0.45f), topLight: false);
        TextDraw.Icon(FontAwesomeIcon.ExclamationTriangle, new Vector2(origin.X + padX, origin.Y + padY), accent);
        var y = origin.Y + padY;
        for (var index = 0; index < count; index++)
        {
            var text = warningTexts[index]!;
            TextDraw.Wrapped(text, new Vector2(origin.X + padX + iconWidth, y), wrapWidth, warningSevere[index] ? Styling.AccentRoseSoft : Styling.AccentAmberSoft);
            y += TextDraw.MeasureWrapped(text, wrapWidth).Y + WarningLineGap * scale;
        }

        ImGui.Dummy(new Vector2(width, height));
        Styling.VSpace(12f);
    }

    private int CollectWarnings(Plugin plugin, PlanView current)
    {
        var count = 0;
        var flags = current.Warnings;
        var configuration = plugin.Configuration;
        var total = current.Result.Selected.Totals.PurchaseEstimate;
        if (configuration.MaxSpend <= 0)
        {
            AddWarning(ref count, Loc.T(L.Plan.WarnNoCap), false);
        }
        else if (total > configuration.MaxSpend)
        {
            AddWarning(ref count, Loc.T(L.Plan.WarnOverBudget), true);
        }

        var inventory = plugin.Inventory;
        if (inventory.HasData && inventory.Gil - total < configuration.GilReserve)
        {
            AddWarning(ref count, Loc.T(L.Plan.WarnBelowReserve), true);
        }

        AddWarning(ref count, flags, PreviewWarnings.StaleData, current.StaleWarning);
        AddWarning(ref count, flags, PreviewWarnings.MissingPrices, Loc.T(L.Plan.WarnMissingPrices));
        AddWarning(ref count, flags, PreviewWarnings.ScopeChanged, Loc.T(L.Plan.WarnScopeChanged));
        AddWarning(ref count, flags, PreviewWarnings.PartialCoverage, Loc.T(L.Plan.WarnPartial));
        AddWarning(ref count, flags, PreviewWarnings.SearchLimit, Loc.T(L.Plan.WarnSearchLimit));
        AddWarning(ref count, flags, PreviewWarnings.UnknownOwned, Loc.T(L.Plan.WarnUnknownOwned));
        return count;
    }

    private void AddWarning(ref int count, PreviewWarnings flags, PreviewWarnings flag, string text)
    {
        if ((flags & flag) != PreviewWarnings.None)
        {
            AddWarning(ref count, text, false);
        }
    }

    private void AddWarning(ref int count, string text, bool severe)
    {
        if (count >= MaximumWarnings)
        {
            return;
        }

        warningTexts[count] = text;
        warningSevere[count] = severe;
        count++;
    }

    private static void DrawTotals(PlanView current)
    {
        var scale = ImGuiHelpers.GlobalScale;
        GroupLabel.Draw(Loc.T(L.Plan.Estimates), 0f, 2f);
        using (Fonts.PushCaption())
        {
            var width = ImGui.GetContentRegionAvail().X;
            var note = Loc.T(L.Plan.EstimatesNote);
            var origin = ImGui.GetCursorScreenPos();
            var size = TextDraw.MeasureWrapped(note, width);
            TextDraw.Wrapped(note, origin, width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, size.Y + 8f * scale));
        }

        var gap = TileGap * scale;
        var tileWidth = (ImGui.GetContentRegionAvail().X - gap * (TileCount - 1)) / TileCount;
        var accent = current.Partial is null ? Styling.AccentGlow : Styling.AccentRose;
        StatTile.Draw(Loc.T(L.Plan.TileTotal), current.Total, Loc.T(L.Plan.TileTotalSub), accent, tileWidth);
        ImGui.SameLine(0f, gap);
        StatTile.Draw(Loc.T(L.Plan.TileFee), current.Fee, Loc.T(L.Plan.TileFeeSub), Styling.AccentNebula, tileWidth);
        ImGui.SameLine(0f, gap);
        StatTile.Draw(Loc.T(L.Plan.TileStops), current.Stops, current.StopsDetail, Styling.AccentBlue, tileWidth);
        ImGui.SameLine(0f, gap);
        StatTile.Draw(Loc.T(L.Plan.TileDataCenters), current.DataCenters, null, Styling.AccentMint, tileWidth);

        if (current.Partial is not { } partial)
        {
            return;
        }

        Styling.VSpace(6f);
        var position = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var partialSize = TextDraw.MeasureWrapped(partial, available);
        TextDraw.Wrapped(partial, position, available, Styling.AccentRoseSoft);
        ImGui.Dummy(new Vector2(available, partialSize.Y));
    }

    private void DrawBudget(Plugin plugin, PlanView current)
    {
        var configuration = plugin.Configuration;
        var inventory = plugin.Inventory;
        var cap = configuration.MaxSpend;
        if (cap <= 0 && !inventory.HasData)
        {
            return;
        }

        SectionTitle(Loc.T(L.Plan.Budget), null);
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = CardPadX * scale;
        var padY = CardPadY * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var total = current.Result.Selected.Totals.PurchaseEstimate;
        var barBlock = lineHeight + 6f * scale + BudgetBarHeight * scale;
        var height = padY * 2f + (cap > 0 ? barBlock : 0f) + (cap > 0 && inventory.HasData ? 8f * scale : 0f) + (inventory.HasData ? captionHeight : 0f);
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, origin + new Vector2(width, height), Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface1, 0.45f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var x = origin.X + padX;
        var y = origin.Y + padY;
        var innerWidth = width - padX * 2f;
        if (cap > 0)
        {
            var over = total > cap;
            var key = HashCode.Combine(total, cap);
            var line = budgetText.Get(key, static _ => BudgetLine());
            var right = over
                ? budgetOverText.Get(total - cap, static amount => Loc.T(L.Plan.BudgetOver, Formatting.Gil(amount)))
                : budgetPercentText.Get(key, static _ => BudgetPercent());
            var rightWidth = TextDraw.Measure(right).X;
            TextDraw.At(TextDraw.Truncate(line, MathF.Max(0f, innerWidth - rightWidth - 16f * scale)), new Vector2(x, y), over ? Styling.AccentRoseSoft : Styling.TextSecondary);
            TextDraw.Right(right, x + innerWidth, y, over ? Styling.AccentRoseSoft : Styling.TextDim);
            y += lineHeight + 6f * scale;
            Paint.Bar(drawList, new Vector2(x, y), innerWidth, BudgetBarHeight * scale, (float)((double)total / cap), over ? Styling.AccentRose : Styling.AccentMint);
            y += BudgetBarHeight * scale + 8f * scale;
        }

        if (inventory.HasData)
        {
            var gil = gilText.Get(HashCode.Combine(inventory.Gil, configuration.GilReserve), static _ => GilLine());
            var belowReserve = inventory.Gil - configuration.GilReserve < total;
            using (Fonts.PushCaption())
            {
                TextDraw.At(TextDraw.Truncate(gil, innerWidth), new Vector2(x, y), belowReserve ? Styling.AccentRoseSoft : Styling.TextDim);
            }
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static string BudgetLine()
    {
        var plugin = Plugin.Instance;
        var total = plugin.Preview.LatestFor(plugin.Lists.Selected)?.Selected.Totals.PurchaseEstimate ?? 0;
        return Loc.T(L.Plan.BudgetLine, Formatting.Gil(total), Formatting.Gil(plugin.Configuration.MaxSpend));
    }

    private static string BudgetPercent()
    {
        var plugin = Plugin.Instance;
        var total = plugin.Preview.LatestFor(plugin.Lists.Selected)?.Selected.Totals.PurchaseEstimate ?? 0;
        var cap = Math.Max(1, plugin.Configuration.MaxSpend);
        return Loc.T(L.Plan.Percent, ((int)Math.Min(999, total * 100 / cap)).ToString(Loc.Culture));
    }

    private static string GilLine()
    {
        var plugin = Plugin.Instance;
        var gil = plugin.Inventory.Gil;
        var reserve = plugin.Configuration.GilReserve;
        return Loc.T(L.Plan.GilLine, Formatting.Gil(gil), Formatting.Gil(reserve), Formatting.Gil(Math.Max(0, gil - reserve)));
    }

    private static void DrawComparison(Plugin plugin, PlanView current)
    {
        SectionTitle(Loc.T(L.Plan.Compare), Loc.T(L.Plan.CompareNote));
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var columns = ModeColumns.For(origin.X, width);
        DrawModeHeader(columns, origin.Y);
        ImGui.Dummy(new Vector2(width, ModeHeaderHeight * scale));

        var modes = current.Modes;
        var selected = current.Result.Mode;
        for (var index = 0; index < modes.Length; index++)
        {
            if (DrawModeRow(modes[index], modes[index].Mode == selected, columns, width))
            {
                plugin.Configuration.PlanningMode = PlanPreview.ToPlanningMode(modes[index].Mode);
                plugin.Configuration.SaveDebounced();
            }

            Styling.VSpace(3f);
        }
    }

    private static void DrawModeHeader(ModeColumns columns, float y)
    {
        using (Fonts.PushCaption())
        {
            var height = ModeHeaderHeight * ImGuiHelpers.GlobalScale;
            var textY = y + (height - ImGui.GetTextLineHeight()) * 0.5f;
            TextDraw.At(TextDraw.Upper(Loc.T(L.Plan.ColumnMode)), new Vector2(columns.Name, textY), Styling.TextMuted);
            TextDraw.Right(TextDraw.Upper(Loc.T(L.Plan.ColumnTotal)), columns.TotalRight, textY, Styling.TextMuted);
            TextDraw.Right(TextDraw.Upper(Loc.T(L.Plan.ColumnStops)), columns.StopsRight, textY, Styling.TextMuted);
            TextDraw.Right(TextDraw.Upper(Loc.T(L.Plan.ColumnDataCenters)), columns.DataCentersRight, textY, Styling.TextMuted);
            TextDraw.Right(TextDraw.Upper(Loc.T(L.Plan.ColumnCoverage)), columns.CoverageRight, textY, Styling.TextMuted);
        }
    }

    private static bool DrawModeRow(in ModeView mode, bool selected, ModeColumns columns, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, ModeRowHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.PushID((int)mode.Mode);
        var hit = Hit.Area("##ashop_plan_mode_row", size);
        ImGui.PopID();
        var drawList = ImGui.GetWindowDrawList();
        var fill = selected
            ? Styling.Tint(Styling.WithAlpha(Styling.Surface2, 0.7f), Styling.AccentGlow, 0.10f)
            : Styling.WithAlpha(hit.Hovered ? Styling.Surface2 : Styling.Surface1, hit.Hovered ? 0.7f : 0.45f);
        Paint.Fill(drawList, origin, origin + size, fill, 8f * scale);
        if (selected)
        {
            Paint.Stroke(drawList, origin, origin + size, Styling.WithAlpha(Styling.AccentGlow, 0.55f), 8f * scale);
        }

        var midY = origin.Y + size.Y * 0.5f;
        var textY = midY - ImGui.GetTextLineHeight() * 0.5f;
        TextDraw.At(mode.Name, new Vector2(columns.Name, textY), selected ? Styling.TextStrong : Styling.TextSecondary);
        var badgeX = columns.Name + TextDraw.Measure(mode.Name).X + 10f * scale;
        if (selected)
        {
            badgeX += Badge.DrawLeft(drawList, Loc.T(L.Plan.InUse), Styling.AccentGlow, badgeX, midY) + 6f * scale;
        }

        if (mode.Lowest)
        {
            Badge.DrawLeft(drawList, Loc.T(L.Plan.Lowest), Styling.AccentMint, badgeX, midY);
        }

        TextDraw.Right(mode.Total, columns.TotalRight, textY, mode.Complete ? Styling.TextStrong : Styling.TextDim);
        TextDraw.Right(mode.Stops, columns.StopsRight, textY, Styling.TextSecondary);
        TextDraw.Right(mode.DataCenters, columns.DataCentersRight, textY, Styling.TextSecondary);
        TextDraw.Right(mode.Coverage, columns.CoverageRight, textY, mode.Complete ? Styling.AccentMintSoft : Styling.AccentRoseSoft);
        return hit.Clicked && !selected;
    }

    private readonly record struct ModeColumns(float Name, float TotalRight, float StopsRight, float DataCentersRight, float CoverageRight)
    {
        public static ModeColumns For(float left, float width)
        {
            var scale = ImGuiHelpers.GlobalScale;
            var gap = ColumnGap * scale;
            var coverage = left + width - CardPadX * scale;
            var dataCenters = coverage - CoverageColumnWidth * scale - gap;
            var stops = dataCenters - DataCenterColumnWidth * scale - gap;
            var total = stops - StopsColumnWidth * scale - gap;
            return new ModeColumns(left + CardPadX * scale, total, stops, dataCenters, coverage);
        }
    }
}
