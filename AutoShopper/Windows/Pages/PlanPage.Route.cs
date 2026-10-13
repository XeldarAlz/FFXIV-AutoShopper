using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Pages;

internal sealed partial class PlanPage
{
    private const float StopHeaderHeight = 40f;
    private const float LineRowHeight = 36f;
    private const float LineIconSize = 26f;
    private const float StopNumberRadius = 11f;
    private const float QuantityColumnWidth = 90f;
    private const float ListingsColumnWidth = 100f;
    private const float SubtotalColumnWidth = 130f;
    private const float MissingRowHeight = 46f;
    private const float FreshnessRowHeight = 26f;

    private static void DrawRoute(PlanView current)
    {
        RouteTitle(current);
        var route = current.Route;
        if (route.Length == 0)
        {
            DrawNote(Loc.T(L.Plan.NoStops), Styling.TextMuted);
            return;
        }

        for (var index = 0; index < route.Length; index++)
        {
            DrawStop(route[index]);
            Styling.VSpace(8f);
        }
    }

    private static void DrawStop(StopView stop)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var headerHeight = StopHeaderHeight * scale;
        var height = headerHeight + stop.Lines.Length * LineRowHeight * scale + CardPadY * scale * 0.5f;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, origin + new Vector2(width, height), Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface1, 0.45f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var padX = CardPadX * scale;
        var midY = origin.Y + headerHeight * 0.5f;
        var radius = StopNumberRadius * scale;
        var numberCenter = new Vector2(origin.X + padX + radius, midY);
        drawList.AddCircleFilled(numberCenter, radius, Paint.Col(Styling.WithAlpha(Styling.AccentGlow, 0.18f)));
        var numberSize = TextDraw.Measure(stop.Number);
        TextDraw.At(stop.Number, numberCenter - numberSize * 0.5f, Styling.AccentGlowSoft);

        var textY = midY - ImGui.GetTextLineHeight() * 0.5f;
        var nameX = numberCenter.X + radius + 10f * scale;
        TextDraw.At(stop.World, new Vector2(nameX, textY), Styling.TextStrong);
        var dataCenterX = nameX + TextDraw.Measure(stop.World).X + 8f * scale;
        TextDraw.At(stop.DataCenter, new Vector2(dataCenterX, textY), Styling.TextDim);
        TextDraw.Right(stop.Detail, origin.X + width - padX, textY, Styling.TextSecondary);
        Paint.Hairline(drawList, new Vector2(origin.X + padX, origin.Y + headerHeight), new Vector2(origin.X + width - padX, origin.Y + headerHeight));

        var rowY = origin.Y + headerHeight;
        for (var index = 0; index < stop.Lines.Length; index++)
        {
            DrawStopLine(stop.Lines[index], origin.X, rowY, width);
            rowY += LineRowHeight * scale;
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawStopLine(in LineView line, float left, float top, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = LineRowHeight * scale;
        if (!ImGui.IsRectVisible(new Vector2(left, top), new Vector2(left + width, top + height)))
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var padX = CardPadX * scale;
        var midY = top + height * 0.5f;
        var iconSize = LineIconSize * scale;
        var iconX = left + padX;
        ItemIcon.Draw(drawList, line.IconId, new Vector2(iconX, midY - iconSize * 0.5f), iconSize, line.HighQuality);

        var right = left + width - padX;
        var subtotalRight = right;
        var listingsRight = subtotalRight - SubtotalColumnWidth * scale;
        var quantityRight = listingsRight - ListingsColumnWidth * scale;
        var nameX = iconX + iconSize + 10f * scale;
        var nameRight = quantityRight - QuantityColumnWidth * scale;
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
        TextDraw.At(name, new Vector2(nameX, textY), Styling.TextStrong);
        if (line.Quality is { } label)
        {
            Badge.DrawLeft(drawList, label, line.HighQuality ? Styling.AccentAmber : Styling.TextDim, nameX + TextDraw.Measure(name).X + 8f * scale, midY);
        }

        TextDraw.Right(line.Quantity, quantityRight, textY, Styling.TextSecondary);
        using (Fonts.PushCaption())
        {
            TextDraw.Right(line.Listings, listingsRight, midY - ImGui.GetTextLineHeight() * 0.5f, Styling.TextDim);
        }

        TextDraw.Right(line.Subtotal, subtotalRight, textY, Styling.TextSecondary);
    }

    private static void DrawMissing(PlanView current)
    {
        var missing = current.Missing;
        if (missing.Length == 0)
        {
            return;
        }

        SectionTitle(Loc.T(L.Plan.NotCovered), Loc.T(L.Plan.NotCoveredNote));
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        for (var index = 0; index < missing.Length; index++)
        {
            var size = new Vector2(width, MissingRowHeight * scale);
            if (!ImGui.IsRectVisible(size))
            {
                ImGui.Dummy(size);
                Styling.VSpace(3f);
                continue;
            }

            DrawMissingRow(missing[index], size);
            Styling.VSpace(3f);
        }
    }

    private static void DrawMissingRow(in MissingView entry, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        Paint.Fill(drawList, origin, origin + size, Styling.Tint(Styling.WithAlpha(Styling.Surface1, 0.45f), Styling.AccentRose, 0.06f), 8f * scale);
        var inset = 6f * scale;
        Paint.Fill(drawList, new Vector2(origin.X + 2f * scale, origin.Y + inset), new Vector2(origin.X + 5f * scale, origin.Y + size.Y - inset), Styling.AccentRose, 1.5f * scale);

        var padX = CardPadX * scale;
        var midY = origin.Y + size.Y * 0.5f;
        var iconSize = LineIconSize * scale;
        ItemIcon.Draw(drawList, entry.IconId, new Vector2(origin.X + padX, midY - iconSize * 0.5f), iconSize, entry.HighQuality);

        var nameX = origin.X + padX + iconSize + 10f * scale;
        var right = origin.X + size.X - padX;
        var coverageWidth = TextDraw.Measure(entry.Coverage).X;
        float captionHeight;
        using (Fonts.PushCaption())
        {
            captionHeight = ImGui.GetTextLineHeight();
        }

        var lineHeight = ImGui.GetTextLineHeight();
        var top = midY - (lineHeight + 2f * scale + captionHeight) * 0.5f;
        var nameWidth = MathF.Max(0f, right - coverageWidth - 16f * scale - nameX);
        var name = TextDraw.Truncate(entry.Name, nameWidth);
        TextDraw.At(name, new Vector2(nameX, top), Styling.TextStrong);
        if (entry.Quality is { } quality)
        {
            Badge.DrawLeft(drawList, quality, entry.HighQuality ? Styling.AccentAmber : Styling.TextDim, nameX + TextDraw.Measure(name).X + 8f * scale, top + lineHeight * 0.5f);
        }

        TextDraw.Right(entry.Coverage, right, top, Styling.AccentRoseSoft);
        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(entry.Reason, right - nameX), new Vector2(nameX, top + lineHeight + 2f * scale), Styling.AccentAmberSoft);
        }

        ImGui.Dummy(size);
    }

    private static void DrawFreshness(PlanView current)
    {
        var rows = current.Freshness;
        if (rows.Length == 0)
        {
            return;
        }

        SectionTitle(Loc.T(L.Plan.Freshness), Loc.T(L.Plan.FreshnessNote));
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (row.DataCenter is { } dataCenter)
            {
                GroupLabel.Draw(dataCenter, index == 0 ? 0f : 8f, 2f);
            }

            var origin = ImGui.GetCursorScreenPos();
            var height = FreshnessRowHeight * scale;
            var textY = origin.Y + (height - ImGui.GetTextLineHeight()) * 0.5f;
            var dotColor = FreshnessColor(row.State);
            Paint.Dot(ImGui.GetWindowDrawList(), new Vector2(origin.X + 8f * scale, origin.Y + height * 0.5f), 3f * scale, dotColor, 0.15f);
            TextDraw.At(row.World, new Vector2(origin.X + 20f * scale, textY), Styling.TextSecondary);
            var ageX = origin.X + MathF.Min(width * 0.45f, 220f * scale);
            TextDraw.At(row.Age, new Vector2(ageX, textY), dotColor);
            TextDraw.Right(row.Items, origin.X + width - CardPadX * scale, textY, Styling.TextDim);
            ImGui.Dummy(new Vector2(width, height));
        }
    }

    private static Vector4 FreshnessColor(FreshnessState state) => state switch
    {
        FreshnessState.Fresh => Styling.AccentMintSoft,
        FreshnessState.Stale => Styling.AccentAmberSoft,
        FreshnessState.Failed => Styling.AccentRoseSoft,
        _ => Styling.TextMuted,
    };

    private static void DrawNote(string text, Vector4 color)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var size = TextDraw.MeasureWrapped(text, width);
        TextDraw.Wrapped(text, origin, width, color);
        ImGui.Dummy(new Vector2(width, size.Y));
    }
}
