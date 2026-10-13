using System.Text;

namespace AutoShopper.Core.Market.Model;

internal static class MarketCityNames
{
    // Letters and digits only, so "Ul'dah", "Ul’dah" and "uldah" read alike. An exact name wins; otherwise a fragment
    // counts when it fits a single city, by its short name or the catalog's.
    public static bool TryParse(string text, out MarketCity city)
    {
        city = default;
        var query = Normalize(text);
        if (query.Length == 0)
        {
            return false;
        }

        var cities = MarketBoardCatalog.Cities;
        for (var index = 0; index < cities.Length; index++)
        {
            var candidate = cities[index];
            if (query == Normalize(candidate.City.ToString()) || query == Normalize(candidate.Name))
            {
                city = candidate.City;
                return true;
            }
        }

        var matches = 0;
        for (var index = 0; index < cities.Length; index++)
        {
            var candidate = cities[index];
            if (!Normalize(candidate.City.ToString()).Contains(query, StringComparison.Ordinal)
                && !Normalize(candidate.Name).Contains(query, StringComparison.Ordinal))
            {
                continue;
            }

            matches++;
            city = candidate.City;
        }

        if (matches == 1)
        {
            return true;
        }

        city = default;
        return false;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsLetterOrDigit(text[index]))
            {
                builder.Append(char.ToLowerInvariant(text[index]));
            }
        }

        return builder.ToString();
    }
}
