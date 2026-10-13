using AutoShopper.Core.Localization;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoShopper.Windows.Sections.Config;

internal static partial class ShoppingSettings
{
    private const float ChipHeight = 26f;
    private const float ChipGap = 6f;
    private const float DataCenterGap = 8f;
    private const string WorldChipId = "##ashop_scope_world";

    private static readonly ScopePreset[] presetOrder = [ScopePreset.CurrentWorld, ScopePreset.HomeDataCenter, ScopePreset.CurrentDataCenter, ScopePreset.Region];

    // Ordered like presetOrder, because the picked index reads both.
    private static readonly SettingsControls.Choices.Choice[] presetChoices =
    [
        new(L.Shopping.PresetCurrentWorldName, L.Shopping.PresetCurrentWorldDetail),
        new(L.Shopping.PresetHomeName, L.Shopping.PresetHomeDetail),
        new(L.Shopping.PresetCurrentName, L.Shopping.PresetCurrentDetail),
        new(L.Shopping.PresetRegionName, L.Shopping.PresetRegionDetail),
    ];

    private static CachedText worldCountText;

    private static void DrawScopeGroup(Configuration configuration, ResolvedScope? scope)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Shopping.Scope));

        var selected = Math.Max(0, Array.IndexOf(presetOrder, configuration.ScopePreset));
        SettingsRow.Draw(Loc.T(L.Shopping.Preset),
            Loc.T(L.Shopping.PresetHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##ashop_scope_preset", presetChoices, selected, choice => configuration.SetScopePreset(presetOrder[choice])));
        SettingsRow.Caption(Loc.T(presetChoices[selected].Detail));

        if (scope is null)
        {
            SettingsRow.Note(Loc.T(L.Shopping.LogInForWorlds));
            return;
        }

        SettingsRow.DrawBlock(Loc.T(L.Shopping.Worlds), Loc.T(L.Shopping.WorldsHelp), () => DrawWorldPicker(configuration, scope));
        if (scope.Home.RegionId != ScopeResolver.JapanRegion && scope.Current.RegionId != ScopeResolver.JapanRegion)
        {
            return;
        }

        SettingsRow.Note(Loc.T(L.Shopping.JapanNote), Styling.AccentAmberSoft);
        SettingsRow.Draw(Loc.T(L.Route.AllowJapan),
            Loc.T(L.Route.AllowJapanHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AllowJapaneseDataCenters, configuration.SetAllowJapaneseDataCenters, "##ashop_scope_japan"),
            SettingsRow.ToggleHeight);
    }

    // A chip reads the settings directly rather than the resolved scope, which follows on the next framework tick, so
    // a click lights or dims its chip at once.
    private static void DrawWorldPicker(Configuration configuration, ResolvedScope scope)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = ChipHeight * scale;
        var gap = ChipGap * scale;
        var right = SettingsGroup.ContentRightEdge;
        var candidates = scope.Candidates;
        var preset = configuration.ScopePreset;
        var included = 0;
        var dataCenter = uint.MaxValue;
        var lineStart = ImGui.GetCursorScreenPos().X;
        var x = lineStart;
        var y = ImGui.GetCursorScreenPos().Y;
        for (var index = 0; index < candidates.Length; index++)
        {
            var world = candidates[index];
            if (world.DataCenterId != dataCenter)
            {
                if (dataCenter != uint.MaxValue)
                {
                    ImGui.SetCursorScreenPos(new Vector2(lineStart, y + height));
                    Styling.VSpace(DataCenterGap);
                }

                dataCenter = world.DataCenterId;
                GroupLabel.Draw(world.DataCenterName, 0f, 4f);
                x = lineStart;
                y = ImGui.GetCursorScreenPos().Y;
            }

            var inPreset = ScopeResolver.InPreset(preset, world, scope.Current, scope.Home);
            var leftOut = ScopeResolver.IsLeftOut(world, scope.Current, configuration.AllowJapaneseDataCenters);
            var isIncluded = !leftOut && ScopeResolver.IsIncluded(preset, world, scope.Current, scope.Home, configuration.AllowedWorlds, configuration.ExcludedWorlds);
            if (isIncluded)
            {
                included++;
            }

            FontAwesomeIcon? lockIcon = leftOut ? FontAwesomeIcon.Lock : null;
            var width = FilterChip.Width(world.Name, null, lockIcon);
            if (x + width > right && x > lineStart)
            {
                x = lineStart;
                y += height + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(x, y));
            ImGui.PushID((int)world.WorldId);
            var clicked = FilterChip.Draw(WorldChipId, world.Name, null, Styling.AccentGlow, isIncluded, height, lockIcon, leftOut ? Loc.T(L.Route.JapanLeftOutChip) : WorldTooltip(world, scope));
            ImGui.PopID();
            if (clicked && !leftOut)
            {
                configuration.SetWorldIncluded(world.WorldId, inPreset, !isIncluded);
            }

            x += width + gap;
        }

        ImGui.SetCursorScreenPos(new Vector2(lineStart, y + height));
        Styling.VSpace(DataCenterGap);
        DrawPickerFooter(configuration, included);
    }

    private static string? WorldTooltip(in ScopeWorld world, ResolvedScope scope)
    {
        if (world.WorldId == scope.Current.WorldId)
        {
            return Loc.T(L.Shopping.CurrentWorld);
        }

        return world.WorldId == scope.Home.WorldId ? Loc.T(L.Shopping.HomeWorld) : null;
    }

    private static void DrawPickerFooter(Configuration configuration, int included)
    {
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = Layout.ActionPillHeight * ImGuiHelpers.GlobalScale;
        var count = worldCountText.Get(included, static value => Loc.Plural(L.Shopping.WorldCount, (int)value));
        var countSize = TextDraw.Measure(count);
        TextDraw.At(count, new Vector2(origin.X, origin.Y + (lineHeight - countSize.Y) * 0.5f), included == 0 ? Styling.AccentRoseSoft : Styling.TextDim);
        if (configuration.AllowedWorlds.Count > 0 || configuration.ExcludedWorlds.Count > 0)
        {
            var label = Loc.T(L.Shopping.ResetWorlds);
            ImGui.SetCursorScreenPos(new Vector2(SettingsGroup.ContentRightEdge - PillButton.Width(label, FontAwesomeIcon.Undo), origin.Y));
            if (PillButton.Draw("##ashop_scope_reset", label, Styling.AccentGlow, PillButton.Emphasis.Ghost, FontAwesomeIcon.Undo, height: Layout.ActionPillHeight))
            {
                configuration.ResetWorlds();
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(SettingsGroup.ContentRightEdge - origin.X, lineHeight));
        if (included == 0)
        {
            SettingsRow.Note(Loc.T(L.Shopping.NoWorlds), Styling.AccentRoseSoft);
        }
    }
}
