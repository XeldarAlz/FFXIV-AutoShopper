using ECommons.DalamudServices;

namespace AutoShopper.Core.External;

public enum ExternalPlugin
{
    Vnavmesh,
    Lifestream,
}

public sealed record ExternalPluginInfo(
    string InternalName,
    string DisplayName,
    string RepoUrl,
    string Purpose,
    bool Required,
    // Alternate InternalNames (community forks with the same IPC surface).
    string[]? Aliases = null);

public static class ExternalPlugins
{
    private static readonly ExternalPlugin[] all = [ExternalPlugin.Vnavmesh, ExternalPlugin.Lifestream];

    public static readonly IReadOnlyDictionary<ExternalPlugin, ExternalPluginInfo> Catalog
        = new Dictionary<ExternalPlugin, ExternalPluginInfo>
    {
        [ExternalPlugin.Vnavmesh] = new(
            InternalName: "vnavmesh",
            DisplayName: "vnavmesh",
            RepoUrl: "https://puni.sh/api/repository/veyn",
            Purpose: "Pathfinding and movement for walking to the market board.",
            Required: true),
        [ExternalPlugin.Lifestream] = new(
            InternalName: "Lifestream",
            DisplayName: "Lifestream",
            RepoUrl: "https://love.puni.sh/ment.json",
            Purpose: "World and data center travel between the worlds you shop on.",
            Required: true),
    };

    public static IReadOnlyList<ExternalPlugin> All => all;

    public static bool IsInstalled(ExternalPlugin plugin)
    {
        var info = Catalog[plugin];
        foreach (var installed in Svc.PluginInterface.InstalledPlugins)
        {
            if (!installed.IsLoaded)
            {
                continue;
            }

            if (installed.InternalName == info.InternalName)
            {
                return true;
            }

            if (info.Aliases is not null && Array.IndexOf(info.Aliases, installed.InternalName) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool AllRequiredInstalled()
    {
        for (var index = 0; index < all.Length; index++)
        {
            if (Catalog[all[index]].Required && !IsInstalled(all[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static string MissingRequiredNames()
    {
        var missing = new List<string>(all.Length);
        for (var index = 0; index < all.Length; index++)
        {
            var info = Catalog[all[index]];
            if (info.Required && !IsInstalled(all[index]))
            {
                missing.Add(info.DisplayName);
            }
        }

        return string.Join(", ", missing);
    }
}
