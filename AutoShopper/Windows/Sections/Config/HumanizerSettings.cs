using AutoShopper.Core;
using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoShopper.Windows.Sections.Config;

internal static class HumanizerSettings
{
    private const float RangeStepperWidth = 118f;
    private const float RangeGap = 8f;

    private readonly record struct DelayRow(HumanAction Action, LocString Label, LocString Help, string MinId, string MaxId);

    private static readonly DelayRow[] rows =
    [
        new(HumanAction.MoveOff, L.Humanizer.MoveOff, L.Humanizer.MoveOffHelp, "##ashop_human_moveoff_min", "##ashop_human_moveoff_max"),
        new(HumanAction.Teleport, L.Humanizer.Teleport, L.Humanizer.TeleportHelp, "##ashop_human_teleport_min", "##ashop_human_teleport_max"),
    ];

    public static void Draw(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Humanizer.Group));

        SettingsRow.Draw(Loc.T(L.Humanizer.Enabled),
            Loc.T(L.Humanizer.EnabledHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.HumanizerEnabled, value => configuration.HumanizerEnabled = value, "##ashop_human_enabled"),
            SettingsRow.ToggleHeight);

        using var delays = Motion.PushSection("##ashop_human_delays", configuration.HumanizerEnabled);
        if (delays is null)
        {
            return;
        }

        var controlWidth = RangeWidth();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            SettingsRow.Draw(Loc.T(row.Label), Loc.T(row.Help), controlWidth, () => DrawRange(configuration, row));
        }
    }

    // In unscaled units, as SettingsRow scales the control width itself.
    private static float RangeWidth()
        => RangeStepperWidth * 2f + RangeGap * 2f + TextDraw.Measure(Loc.T(L.Humanizer.RangeTo)).X / ImGuiHelpers.GlobalScale;

    private static void DrawRange(Configuration configuration, in DelayRow row)
    {
        var gap = RangeGap * ImGuiHelpers.GlobalScale;
        var format = Loc.T(L.Humanizer.SecondsFormat);
        var range = configuration.DelayFor(row.Action);
        var shortest = range.MinSeconds;
        var longest = range.MaxSeconds;

        var changed = Stepper.Draw(row.MinId, ref shortest, Humanizer.SecondsStep, Humanizer.SecondsMin, Humanizer.SecondsMax, format, RangeStepperWidth);
        ImGui.SameLine(0f, gap);
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
        {
            ImGui.TextUnformatted(Loc.T(L.Humanizer.RangeTo));
        }

        ImGui.SameLine(0f, gap);
        changed |= Stepper.Draw(row.MaxId, ref longest, Humanizer.SecondsStep, Humanizer.SecondsMin, Humanizer.SecondsMax, format, RangeStepperWidth);
        if (!changed)
        {
            return;
        }

        configuration.SetDelay(row.Action, new DelayRange(shortest, longest));
        configuration.SaveDebounced();
    }
}
