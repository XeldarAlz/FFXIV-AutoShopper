using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace AutoShopper.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, Ready, Blocked, Running, Paused }

    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft, FontAwesomeIcon Icon, string Title, string Detail);

    // Packs the readiness, the dry run switch and the list revision into one cache key for the detail line.
    private const long ReadinessStride = 2;
    private const long RevisionStride = 64;

    private static int cachedFrame = -1;
    private static Info cached;
    private static CachedText detailText;

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
            if (controller.Stopping)
            {
                return new Info(Kind.Running, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Stop, Loc.T(L.Shop.TitleStopping), Loc.T(L.Buyer.StatusSettling));
            }

            if (controller.Paused)
            {
                var detail = controller.PauseReason == PauseReason.InContent
                    ? Loc.T(L.Shop.DetailPausedInContent)
                    : Loc.T(L.Shop.DetailPausedManual);
                return new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Pause, Loc.T(L.Shop.TitlePaused), detail);
            }

            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft, FontAwesomeIcon.ShoppingCart, Loc.T(L.Shop.TitleRunning), PhaseLabel(controller.Phase));
        }

        var readiness = ShopLauncher.Assess();
        if (readiness == Readiness.SetupNeeded)
        {
            return new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle,
                Loc.T(L.Shop.TitleSetupNeeded), Loc.T(L.Shop.DetailSetupNeeded));
        }

        var plugin = Plugin.Instance;
        var dryRun = plugin.Configuration.DryRun;
        var key = plugin.Lists.Revision * RevisionStride + (long)readiness * ReadinessStride + (dryRun ? 1 : 0);
        var text = detailText.Get(key, static packed => Detail((Readiness)(packed % RevisionStride / ReadinessStride), packed % ReadinessStride == 1));
        if (readiness == Readiness.Ready)
        {
            return new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.ShoppingCart, Loc.T(L.Shop.TitleReady), text);
        }

        if (readiness is Readiness.NeedsReconciliation or Readiness.JournalDamaged)
        {
            return new Info(Kind.Blocked, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle, Loc.T(L.Reconcile.Title), text);
        }

        return new Info(Kind.Blocked, Styling.AccentNebula, Styling.AccentGlowSoft, FontAwesomeIcon.ShoppingCart, Loc.T(L.Shop.TitleBlocked), text);
    }

    private static string Detail(Readiness readiness, bool dryRun)
    {
        if (readiness != Readiness.Ready)
        {
            return Loc.T(L.Shop.DetailBlocked, ShopLauncher.Reason(readiness));
        }

        var listName = Plugin.Instance.Lists.Selected?.Name ?? string.Empty;
        return dryRun ? Loc.T(L.Shop.DetailReadyDry, listName) : Loc.T(L.Shop.DetailReady, listName);
    }

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running     => Loc.T(L.Shell.StatusRunning),
        Kind.Paused      => Loc.T(L.Shell.StatusPaused),
        Kind.Ready       => Loc.T(L.Shell.StatusReady),
        Kind.Blocked     => Loc.T(L.Shell.StatusBlocked),
        Kind.SetupNeeded => Loc.T(L.Shell.StatusSetupNeeded),
        _                => Loc.T(L.Shell.StatusIdle),
    };

    public static string PhaseLabel(RunPhase phase) => phase switch
    {
        RunPhase.Preparing  => Loc.T(L.Run.PhasePreparing),
        RunPhase.Travelling => Loc.T(L.Run.PhaseTravelling),
        RunPhase.Shopping   => Loc.T(L.Run.PhaseShopping),
        RunPhase.Stopping   => Loc.T(L.Run.PhaseStopping),
        RunPhase.Finishing  => Loc.T(L.Run.PhaseFinishing),
        _                   => Loc.T(L.Run.PhaseStandingBy),
    };
}
