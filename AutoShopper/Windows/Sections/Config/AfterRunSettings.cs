using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoShopper.Windows.Sections.Config;

internal static class AfterRunSettings
{
    private const int ChatCommandsMaxLength = 1000;
    private const int ChatCommandsMinLines = 3;
    private const int ChatCommandsMaxLines = 8;

    private static readonly AfterRunAction[] afterRunOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    // Ordered like afterRunOrder, because the picked index reads both.
    private static readonly SettingsControls.Choices.Choice[] afterRunChoices =
    [
        new(L.Travel.AfterStayName, L.Travel.AfterStayDetail),
        new(L.Travel.AfterInnName, L.Travel.AfterInnDetail),
        new(L.Travel.AfterLogoutName, L.Travel.AfterLogoutDetail),
        new(L.Travel.AfterCloseName, L.Travel.AfterCloseDetail),
    ];

    public static void Draw(Configuration configuration)
    {
        DrawWhenDone(configuration);
        DrawChatCommands(configuration);
    }

    private static void DrawWhenDone(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Travel.WhenDone));

        SettingsRow.Draw(Loc.T(L.Travel.ReturnHome),
            Loc.T(L.Travel.ReturnHomeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.ReturnHomeAfterRun, value => configuration.ReturnHomeAfterRun = value, "##ashop_travel_returnhome"),
            SettingsRow.ToggleHeight);

        var selected = Math.Max(0, Array.IndexOf(afterRunOrder, configuration.AfterRun));
        SettingsRow.Draw(Loc.T(L.Travel.WhenDone),
            Loc.T(L.Travel.WhenDoneHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##ashop_travel_afterrun", afterRunChoices, selected, choice =>
            {
                configuration.AfterRun = afterRunOrder[choice];
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(afterRunChoices[selected].Detail));
    }

    private static void DrawChatCommands(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.PostRunGroup));

        SettingsRow.DrawBlock(Loc.T(L.Settings.PostRunLabel), Loc.T(L.Settings.PostRunHelp), () => DrawChatCommandsEditor(configuration));
        SettingsRow.Caption(Loc.T(L.Settings.PostRunCaption));
    }

    private static void DrawChatCommandsEditor(Configuration configuration)
    {
        var commands = configuration.PostRunCommands;
        var lineCount = 1;
        for (var characterIndex = 0; characterIndex < commands.Length; characterIndex++)
        {
            if (commands[characterIndex] == '\n')
            {
                lineCount++;
            }
        }

        var scale = ImGuiHelpers.GlobalScale;
        var height = ImGui.GetTextLineHeight() * Math.Clamp(lineCount + 1, ChatCommandsMinLines, ChatCommandsMaxLines) + ImGui.GetStyle().FramePadding.Y * 2f;
        var width = SettingsGroup.ContentRightEdge - ImGui.GetCursorScreenPos().X;

        using var colors = SettingsControls.PushFrameColors();
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f)
            .Push(ImGuiStyleVar.FrameRounding, Styling.FrameRounding * scale);
        if (!ImGui.InputTextMultiline("##ashop_travel_postrun", ref commands, ChatCommandsMaxLength, new Vector2(width, height)))
        {
            return;
        }

        configuration.PostRunCommands = commands;
        configuration.SaveDebounced();
    }
}
