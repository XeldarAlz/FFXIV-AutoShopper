using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;

namespace AutoShopper.Windows.Sections.Config;

internal static class GeneralSettings
{
    public static void Draw(Configuration configuration)
    {
        DrawLanguageGroup(configuration);
        DrawWindowGroup(configuration);
        DrawBehaviorGroup(configuration);
    }

    private static void DrawLanguageGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Language));

        SettingsRow.Draw(Loc.T(L.Settings.Language),
            Loc.T(L.Settings.LanguageHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.DrawLanguageCombo(configuration));
    }

    private static void DrawWindowGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralWindow));

        SettingsRow.Draw(Loc.T(L.Settings.OpenOnLogin),
            Loc.T(L.Settings.OpenOnLoginHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoShowOnLogin, value => configuration.AutoShowOnLogin = value, "##ashop_general_autoshow"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.DragAnywhere),
            Loc.T(L.Settings.DragAnywhereHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.DragWindowFromAnywhere, value => configuration.DragWindowFromAnywhere = value, "##ashop_general_draganywhere"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawBehaviorGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralBehavior));

        SettingsRow.Draw(Loc.T(L.Settings.AutoPause),
            Loc.T(L.Settings.AutoPauseHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoPauseInContent, value => configuration.AutoPauseInContent = value, "##ashop_general_autopause"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.PauseTyping),
            Loc.T(L.Settings.PauseTypingHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.PauseWhileTyping, value => configuration.PauseWhileTyping = value, "##ashop_general_pausetyping"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Session.AutoResume),
            Loc.T(L.Session.AutoResumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(configuration, () => configuration.AutoResumeOnFault, value => configuration.AutoResumeOnFault = value, "##ashop_general_autoresume"),
            SettingsRow.ToggleHeight);
    }
}
