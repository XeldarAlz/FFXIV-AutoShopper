using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

internal static class BuyChoices
{
    private const float RowGap = 14f;
    private const float LabelGap = 16f;
    private const float CaptionGap = 6f;

    private static readonly BuySource[] sources = [BuySource.Everything, BuySource.MarketBoard, BuySource.NpcVendors];
    private static readonly PlanningMode[] modes = [PlanningMode.Cheapest, PlanningMode.Balanced, PlanningMode.FewestStops];
    private static readonly Segmented.Item[] sourceItems = new Segmented.Item[3];
    private static readonly Segmented.Item[] modeItems = new Segmented.Item[3];
    private static LanguageInfo? labelsLanguage;

    public static void Draw(Configuration configuration)
    {
        EnsureLabels();
        var labelColumn = LabelColumn();
        var source = Math.Max(0, Array.IndexOf(sources, configuration.BuySource));
        if (DrawRow(Loc.T(L.Shop.BuyFrom), "##ashop_shop_source", sourceItems, ref source, labelColumn, Loc.T(SourceHelp(configuration.BuySource))))
        {
            configuration.BuySource = sources[source];
            configuration.SaveDebounced();
        }

        if (configuration.BuysOnlyFromVendors)
        {
            return;
        }

        Styling.VSpace(RowGap);
        var mode = Math.Max(0, Array.IndexOf(modes, configuration.PlanningMode));
        if (DrawRow(Loc.T(L.Shop.Route), "##ashop_shop_mode", modeItems, ref mode, labelColumn, Loc.T(ModeHelp(configuration.PlanningMode))))
        {
            configuration.PlanningMode = modes[mode];
            configuration.SaveDebounced();
        }
    }

    private static bool DrawRow(string label, string id, Segmented.Item[] items, ref int selected, float labelColumn, string caption)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.SegmentHeight * scale;
        using (Fonts.PushCaption())
        {
            var labelSize = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(origin.X, origin.Y + (height - labelSize.Y) * 0.5f), Styling.TextDim);
        }

        var fieldWidth = MathF.Max(1f, width - labelColumn);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + labelColumn, origin.Y));
        var changed = Segmented.Draw(id, items, ref selected, true, Layout.SegmentHeight, fieldWidth);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height + CaptionGap * scale));
        using (Fonts.PushCaption())
        {
            var captionOrigin = ImGui.GetCursorScreenPos();
            var captionSize = TextDraw.MeasureWrapped(caption, fieldWidth);
            TextDraw.Wrapped(caption, new Vector2(captionOrigin.X + labelColumn, captionOrigin.Y), fieldWidth, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, captionSize.Y));
        }

        return changed;
    }

    private static float LabelColumn()
    {
        using (Fonts.PushCaption())
        {
            return MathF.Max(TextDraw.Measure(Loc.T(L.Shop.BuyFrom)).X, TextDraw.Measure(Loc.T(L.Shop.Route)).X) + LabelGap * ImGuiHelpers.GlobalScale;
        }
    }

    private static void EnsureLabels()
    {
        if (ReferenceEquals(labelsLanguage, Loc.Current))
        {
            return;
        }

        labelsLanguage = Loc.Current;
        sourceItems[0] = new Segmented.Item(FontAwesomeIcon.LayerGroup, Loc.T(L.Shop.SourceEverything));
        sourceItems[1] = new Segmented.Item(FontAwesomeIcon.Store, Loc.T(L.Shop.SourceMarket));
        sourceItems[2] = new Segmented.Item(FontAwesomeIcon.UserTag, Loc.T(L.Shop.SourceVendors));
        modeItems[0] = new Segmented.Item(FontAwesomeIcon.Coins, Loc.T(L.Shopping.ModeCheapestName));
        modeItems[1] = new Segmented.Item(FontAwesomeIcon.BalanceScale, Loc.T(L.Shopping.ModeBalancedName));
        modeItems[2] = new Segmented.Item(FontAwesomeIcon.Route, Loc.T(L.Shopping.ModeFewestName));
    }

    private static LocString SourceHelp(BuySource source) => source switch
    {
        BuySource.MarketBoard => L.Shop.SourceMarketHelp,
        BuySource.NpcVendors => L.Shop.SourceVendorsHelp,
        _ => L.Shop.SourceEverythingHelp,
    };

    private static LocString ModeHelp(PlanningMode mode) => mode switch
    {
        PlanningMode.Cheapest => L.Shop.ModeCheapestHelp,
        PlanningMode.FewestStops => L.Shop.ModeFewestHelp,
        _ => L.Shop.ModeBalancedHelp,
    };
}
