namespace AutoShopper.Core.Travel;

internal static class ResidentialDistricts
{
    private static readonly uint[] territoryIds = [339, 340, 341, 641, 979];
    private static readonly string[] lifestreamNames = ["mist", "lavender beds", "goblet", "shirogane", "empyreum"];

    public static bool IsDistrict(uint territoryId) => Array.IndexOf(territoryIds, territoryId) >= 0;

    public static bool TryGetLifestreamName(uint territoryId, out string name)
    {
        var index = Array.IndexOf(territoryIds, territoryId);
        name = index >= 0 ? lifestreamNames[index] : string.Empty;
        return index >= 0;
    }
}
