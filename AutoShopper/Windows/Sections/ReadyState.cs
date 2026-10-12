using AutoShopper.Core.External;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, ComingSoon, Running, Paused }

    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft, FontAwesomeIcon Icon, string Title, string Detail);

    private static int cachedFrame = -1;
    private static Info cached;

    public static Info Resolve(AutoShopController controller)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame)
        {
            return cached;
        }

        cached = Compute(controller);
        cachedFrame = frame;
        return cached;
    }

    private static Info Compute(AutoShopController controller)
    {
        if (controller.Running)
        {
            if (controller.Paused)
            {
                var detail = controller.PauseReason == PauseReason.InContent
                    ? Loc.T(L.Shop.DetailPausedInContent)
                    : Loc.T(L.Shop.DetailPausedManual);
                return new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Pause, Loc.T(L.Shop.TitlePaused), detail);
            }

            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft, FontAwesomeIcon.ShoppingCart, Loc.T(L.Shop.TitleRunning), PhaseLabel(controller.Phase));
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle,
                Loc.T(L.Shop.TitleSetupNeeded), Loc.T(L.Shop.DetailSetupNeeded));
        }

        // Until the shop run task exists there is nothing to start, and the headline says so.
        return new Info(Kind.ComingSoon, Styling.AccentNebula, Styling.AccentGlowSoft, FontAwesomeIcon.ShoppingCart,
            Loc.T(L.Shop.TitleComingSoon), Loc.T(L.Shop.DetailComingSoon));
    }

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running     => Loc.T(L.Shell.StatusRunning),
        Kind.Paused      => Loc.T(L.Shell.StatusPaused),
        Kind.ComingSoon  => Loc.T(L.Shell.StatusComingSoon),
        Kind.SetupNeeded => Loc.T(L.Shell.StatusSetupNeeded),
        _                => Loc.T(L.Shell.StatusIdle),
    };

    public static string PhaseLabel(RunPhase phase) => phase switch
    {
        RunPhase.Preparing  => Loc.T(L.Run.PhasePreparing),
        RunPhase.Travelling => Loc.T(L.Run.PhaseTravelling),
        RunPhase.Finishing  => Loc.T(L.Run.PhaseFinishing),
        _                   => Loc.T(L.Run.PhaseStandingBy),
    };
}
