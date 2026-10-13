using System.Globalization;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Windows.Components;

namespace AutoShopper.Windows.Sections.Config;

internal static partial class ShoppingSettings
{
    private const float GilFieldWidth = 150f;
    private const float SmallStepperWidth = 130f;
    // 999,999,999, the most gil a character holds, has nine digits.
    private const int GilDigits = 9;
    private const string ZeroHint = "0";

    private static readonly PlanningMode[] modeOrder = [PlanningMode.Cheapest, PlanningMode.Balanced, PlanningMode.FewestStops];

    // Ordered like modeOrder, because the picked index reads both.
    private static readonly SettingsControls.Choices.Choice[] modeChoices =
    [
        new(L.Shopping.ModeCheapestName, L.Shopping.ModeCheapestDetail),
        new(L.Shopping.ModeBalancedName, L.Shopping.ModeBalancedDetail),
        new(L.Shopping.ModeFewestName, L.Shopping.ModeFewestDetail),
    ];

    private static readonly GilText spendCapText = new();
    private static readonly GilText reserveText = new();
    private static readonly GilText stopPenaltyText = new();
    private static readonly GilText crossPenaltyText = new();

    public static void Draw(Plugin plugin)
    {
        var configuration = plugin.Configuration;
        DrawBudgetGroup(configuration);
        DrawPlanningGroup(configuration);
        DrawScopeGroup(configuration, plugin.Preview.Scope);
    }

    public static int ModeIndex(PlanningMode mode) => Math.Max(0, Array.IndexOf(modeOrder, mode));

    public static string ModeName(PlanningMode mode) => Loc.T(modeChoices[ModeIndex(mode)].Name);

    private static void DrawBudgetGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Shopping.Budget));

        SettingsRow.Draw(Loc.T(L.Shopping.SpendCap),
            Loc.T(L.Shopping.SpendCapHelp),
            GilFieldWidth,
            () =>
            {
                if (DrawGilField("##ashop_budget_cap", spendCapText, configuration.MaxSpend, Loc.T(L.Shopping.SpendCapHint), out var value))
                {
                    configuration.MaxSpend = value;
                    configuration.SaveDebounced();
                }
            });
        if (configuration.MaxSpend <= 0)
        {
            SettingsRow.Note(Loc.T(L.Shopping.SpendCapMissing), Styling.AccentAmberSoft);
        }

        SettingsRow.Draw(Loc.T(L.Shopping.Reserve),
            Loc.T(L.Shopping.ReserveHelp),
            GilFieldWidth,
            () =>
            {
                if (DrawGilField("##ashop_budget_reserve", reserveText, configuration.GilReserve, ZeroHint, out var value))
                {
                    configuration.GilReserve = value;
                    configuration.SaveDebounced();
                }
            });
    }

    private static void DrawPlanningGroup(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Shopping.Planning));

        var selected = ModeIndex(configuration.PlanningMode);
        SettingsRow.Draw(Loc.T(L.Shopping.Mode),
            Loc.T(L.Shopping.ModeHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##ashop_plan_mode", modeChoices, selected, choice =>
            {
                configuration.PlanningMode = modeOrder[choice];
                configuration.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(modeChoices[selected].Detail));

        SettingsRow.Draw(Loc.T(L.Shopping.StopPenalty),
            Loc.T(L.Shopping.StopPenaltyHelp),
            GilFieldWidth,
            () =>
            {
                if (DrawGilField("##ashop_plan_stop_penalty", stopPenaltyText, configuration.StopPenaltyGil, ZeroHint, out var value))
                {
                    configuration.StopPenaltyGil = value;
                    configuration.SaveDebounced();
                }
            });

        SettingsRow.Draw(Loc.T(L.Shopping.CrossPenalty),
            Loc.T(L.Shopping.CrossPenaltyHelp),
            GilFieldWidth,
            () =>
            {
                if (DrawGilField("##ashop_plan_cross_penalty", crossPenaltyText, configuration.CrossDataCenterPenaltyGil, ZeroHint, out var value))
                {
                    configuration.CrossDataCenterPenaltyGil = value;
                    configuration.SaveDebounced();
                }
            });

        SettingsRow.Draw(Loc.T(L.Shopping.Premium),
            Loc.T(L.Shopping.PremiumHelp),
            SmallStepperWidth,
            () => DrawStepper("##ashop_plan_premium", configuration, configuration.FewestStopsPremiumPercent, 0, Configuration.MaximumPremiumPercent,
                Loc.T(L.Shopping.PercentFormat), static (settings, value) => settings.FewestStopsPremiumPercent = value));

        SettingsRow.Draw(Loc.T(L.Shopping.MaxStops),
            Loc.T(L.Shopping.MaxStopsHelp),
            SmallStepperWidth,
            () => DrawStepper("##ashop_plan_max_stops", configuration, configuration.MaxStops, 0, Configuration.MaximumStopsLimit,
                Loc.T(L.Shopping.StopsFormat), static (settings, value) => settings.MaxStops = value));

        SettingsRow.Draw(Loc.T(L.Shopping.StaleAfter),
            Loc.T(L.Shopping.StaleAfterHelp),
            SmallStepperWidth,
            () => DrawStepper("##ashop_plan_stale", configuration, configuration.StaleAfterHours, Configuration.MinimumStaleHours, Configuration.MaximumStaleHours,
                Loc.T(L.Shopping.HoursFormat), static (settings, value) => settings.StaleAfterHours = value));
    }

    private static void DrawStepper(string id, Configuration configuration, int current, int minimum, int maximum, string format, Action<Configuration, int> assign)
    {
        var value = current;
        if (!Stepper.Draw(id, ref value, 1, minimum, maximum, format, SmallStepperWidth))
        {
            return;
        }

        assign(configuration, value);
        configuration.SaveDebounced();
    }

    // The text is kept between frames and parsed on every edit; a value that is not a whole number in range puts the
    // last good value back.
    private static bool DrawGilField(string id, GilText buffer, long current, string hint, out long value)
    {
        value = current;
        if (buffer.Value != current)
        {
            buffer.Value = current;
            buffer.Text = current == 0 ? string.Empty : current.ToString(CultureInfo.InvariantCulture);
        }

        if (!NumberField.Draw(id, ref buffer.Text, hint, GilFieldWidth, GilDigits))
        {
            return false;
        }

        var status = ImportFields.TryParseWhole(buffer.Text, out var parsed);
        if (status == NumberStatus.Empty)
        {
            parsed = 0;
        }
        else if (status != NumberStatus.Valid || parsed > Configuration.MaximumGil)
        {
            buffer.Text = current == 0 ? string.Empty : current.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        if (parsed == current)
        {
            return false;
        }

        buffer.Value = parsed;
        value = parsed;
        return true;
    }

    private sealed class GilText
    {
        public long Value = -1;
        public string Text = string.Empty;
    }
}
