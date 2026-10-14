using AutoShopper.Core.Localization;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Travel;
using AutoShopper.Windows.Components;

namespace AutoShopper.Windows.Sections.Config;

internal static class MarketSettings
{
    private const float StepperWidth = 130f;
    private const float TenthStep = 0.5f;
    private const int SecondStep = 1;

    // City names follow the game client, like every other place name; they are read once from the game data.
    private static string[]? cityLabels;

    public static void Draw(Configuration configuration)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Market.Board));

        SettingsRow.Draw(Loc.T(L.Market.City),
            Loc.T(L.Market.CityHelp),
            SettingsControls.RowComboWidth,
            () =>
            {
                var labels = cityLabels ??= BuildCityLabels();
                var selected = (int)configuration.PreferredMarketCity;
                if (SettingsControls.DrawPlainCombo("##ashop_market_city", ref selected, labels, SettingsControls.RowComboWidth)
                    && MarketBoardCatalog.Contains((MarketCity)selected))
                {
                    configuration.PreferredMarketCity = (MarketCity)selected;
                    configuration.SaveDebounced();
                }
            });

        SettingsRow.Draw(Loc.T(L.Market.SearchInterval),
            Loc.T(L.Market.SearchIntervalHelp),
            StepperWidth,
            () =>
            {
                var value = configuration.SearchIntervalSeconds;
                if (Stepper.Draw("##ashop_market_search_interval", ref value, TenthStep, Configuration.MinimumSearchIntervalSeconds,
                        Configuration.MaximumSearchIntervalSeconds, Loc.T(L.Market.TenthsFormat), StepperWidth))
                {
                    configuration.SearchIntervalSeconds = value;
                    configuration.SaveDebounced();
                }
            });

        SettingsRow.Draw(Loc.T(L.Market.PurchaseInterval),
            Loc.T(L.Market.PurchaseIntervalHelp),
            StepperWidth,
            () =>
            {
                var value = configuration.PurchaseIntervalSeconds;
                if (Stepper.Draw("##ashop_market_purchase_interval", ref value, TenthStep, Configuration.MinimumPurchaseIntervalSeconds,
                        Configuration.MaximumPurchaseIntervalSeconds, Loc.T(L.Market.TenthsFormat), StepperWidth))
                {
                    configuration.PurchaseIntervalSeconds = value;
                    configuration.SaveDebounced();
                }
            });

        SettingsRow.Draw(Loc.T(L.Market.ResultTimeout),
            Loc.T(L.Market.ResultTimeoutHelp),
            StepperWidth,
            () =>
            {
                var value = configuration.ResultTimeoutSeconds;
                if (Stepper.Draw("##ashop_market_result_timeout", ref value, SecondStep, Configuration.MinimumResultTimeoutSeconds,
                        Configuration.MaximumResultTimeoutSeconds, Loc.T(L.Market.SecondsFormat), StepperWidth))
                {
                    configuration.ResultTimeoutSeconds = value;
                    configuration.SaveDebounced();
                }
            });
    }

    private static string[] BuildCityLabels()
    {
        var cities = MarketBoardCatalog.Cities;
        var labels = new string[cities.Length];
        for (var index = 0; index < cities.Length; index++)
        {
            labels[index] = TerritoryNames.Of(cities[index].TerritoryId);
        }

        return labels;
    }
}
