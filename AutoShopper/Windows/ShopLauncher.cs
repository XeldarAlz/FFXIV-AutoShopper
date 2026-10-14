using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using Dalamud.Bindings.ImGui;

namespace AutoShopper.Windows;

// What the Start buttons say and whether they are enabled, checked once a frame.
internal static class ShopLauncher
{
    private static int cachedFrame = -1;
    private static Readiness cached;

    public static Readiness Assess()
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame)
        {
            return cached;
        }

        cached = ShopReadiness.Evaluate(Plugin.Instance);
        cachedFrame = frame;
        return cached;
    }

    public static void Start() => Plugin.Instance.Controller.Start();

    public static string Sublabel() => Loc.T(L.Shop.StartSub);

    public static string Reason(Readiness readiness) => ShopReadiness.Reason(readiness);
}
