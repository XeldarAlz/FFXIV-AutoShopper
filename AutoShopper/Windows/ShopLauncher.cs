using AutoShopper.Core.External;
using AutoShopper.Core.Localization;

namespace AutoShopper.Windows;

// What the Start button says. Nothing can start until the shop run task exists, so the only question is which reason to show.
internal static class ShopLauncher
{
    public enum Readiness : byte
    {
        SetupNeeded,
        ComingSoon,
    }

    public static Readiness Assess() => ExternalPlugins.AllRequiredInstalled() ? Readiness.ComingSoon : Readiness.SetupNeeded;

    public static void Start() => Plugin.Instance.Controller.Start();

    public static string Sublabel() => Loc.T(L.Shop.StartSub);

    public static string Reason(Readiness readiness)
        => Loc.T(readiness == Readiness.SetupNeeded ? L.Shop.ReasonInstall : L.Shop.ReasonComingSoon);
}
