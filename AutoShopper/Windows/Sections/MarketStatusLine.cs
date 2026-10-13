using AutoShopper.Core.Localization;
using AutoShopper.Core.Market;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

// One line under the headline: whether listings can be read and bought on this game version, and a badge while
// dry run is on.
internal static class MarketStatusLine
{
    private const float DotRadius = 3.5f;
    private const float DotGap = 10f;
    private const float LabelGap = 8f;
    private const float BadgeGap = 10f;
    private const long ReadingFlag = 1;
    private const long BuyingFlag = 2;

    private static CachedText statusText;

    public static void Draw(MarketCompatibilityReport compatibility, bool dryRun)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        var reading = compatibility.ReadingAvailable;
        var buying = compatibility.ExecutionAvailable;
        var accent = buying ? Styling.AccentMint : reading ? Styling.AccentAmber : Styling.AccentRose;

        using (Fonts.PushCaption())
        {
            var lineHeight = ImGui.GetTextLineHeight();
            var midY = origin.Y + lineHeight * 0.5f;
            var radius = DotRadius * scale;
            Paint.Dot(drawList, new Vector2(origin.X + radius + 1f * scale, midY), radius, accent);

            var x = origin.X + radius * 2f + DotGap * scale;
            var label = Loc.T(L.Market.StatusLabel);
            var labelSize = TextDraw.SmallCapsSize(label);
            TextDraw.SmallCaps(label, new Vector2(x, midY - labelSize.Y * 0.5f), Styling.TextMuted);
            x += labelSize.X + LabelGap * scale;

            var key = (reading ? ReadingFlag : 0) | (buying ? BuyingFlag : 0);
            var text = statusText.Get(key, static _ => Describe(Plugin.Instance.Market.Compatibility));
            var badgeLabel = Loc.T(L.Market.DryRunBadge);
            var badgeWidth = dryRun ? TextDraw.Measure(badgeLabel).X + 2f * BadgeGap * scale : 0f;
            var shown = TextDraw.Truncate(text, MathF.Max(0f, origin.X + width - x - badgeWidth));
            TextDraw.At(shown, new Vector2(x, midY - lineHeight * 0.5f), buying ? Styling.TextDim : Styling.AccentAmberSoft);
            var textEnd = x + TextDraw.Measure(shown).X;
            if (dryRun)
            {
                Badge.DrawLeft(drawList, badgeLabel, Styling.AccentBlue, textEnd + BadgeGap * scale, midY);
            }

            if (!buying && Hit.HoveringRect(new Vector2(origin.X, origin.Y), new Vector2(textEnd, origin.Y + lineHeight)))
            {
                Tooltip.Show(Loc.T(L.Market.StatusOffHelp));
            }

            ImGui.Dummy(new Vector2(width, lineHeight));
        }
    }

    private static string Describe(MarketCompatibilityReport compatibility)
    {
        if (!compatibility.ReadingAvailable)
        {
            return Loc.T(L.Market.StatusReadingOff, compatibility.FirstFailure);
        }

        var buying = compatibility.ExecutionAvailable
            ? Loc.T(L.Market.StatusBuying)
            : Loc.T(L.Market.StatusBuyingOff, compatibility.FirstFailure);
        return string.Concat(Loc.T(L.Market.StatusReading), TextDraw.Separator, buying);
    }
}
