using AutoShopper.Core.Localization;
using AutoShopper.Windows.Components;

namespace AutoShopper.Windows.Sections.Config;

internal static class AfterRunSettings
{
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
}
