using ECommons.Reflection;
using System.Threading.Tasks;

namespace AutoShopper.Core.External;

// The install finishes on a thread pool thread while the Plugins page reads the in-flight set every frame, so the set
// is only touched under its lock.
public static class PluginInstaller
{
    private static readonly HashSet<ExternalPlugin> InFlight = [];
    private static readonly object Gate = new();

    public static bool IsInstalling(ExternalPlugin plugin)
    {
        lock (Gate)
        {
            return InFlight.Contains(plugin);
        }
    }

    public static async Task<bool> Install(ExternalPlugin plugin)
    {
        lock (Gate)
        {
            if (!InFlight.Add(plugin))
            {
                return false;
            }
        }

        try
        {
            var info = ExternalPlugins.Catalog[plugin];
            RunLog.Info($"Installing {info.DisplayName} from {info.RepoUrl}");
            var installed = await DalamudReflector.AddPlugin(info.RepoUrl, info.InternalName);
            RunLog.Info(installed
                ? $"{info.DisplayName} installed."
                : $"{info.DisplayName} install reported failure; the repo may need to be added manually.");
            return installed;
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Installing {plugin} threw");
            return false;
        }
        finally
        {
            lock (Gate)
            {
                InFlight.Remove(plugin);
            }
        }
    }
}
