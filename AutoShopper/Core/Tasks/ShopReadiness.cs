using AutoShopper.Core.Api.Model;
using AutoShopper.Core.External;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Shopping.Model;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal enum Readiness : byte
{
    Ready = 0,
    SetupNeeded = 1,
    LoggedOut = 2,
    JournalDamaged = 3,
    NeedsReconciliation = 4,
    InterruptedRun = 5,
    NoList = 6,
    ListLoading = 7,
    ListHasErrors = 8,
    NoSpendCap = 9,
    ReadingUnavailable = 10,
    BuyingUnavailable = 11,
    Busy = 12,
}

// The checks Start can make at once, in the order a player fixes them. Prices, the plan and owned counts take time and
// are checked by the run itself before it is authorized.
internal static class ShopReadiness
{
    // A run checking itself is the task that runs, so it does not count as another task in the way.
    public static Readiness Evaluate(Plugin plugin, bool forRun = false, ExternalRun? external = null)
    {
        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return Readiness.SetupNeeded;
        }

        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null || Svc.PlayerState.ContentId == 0)
        {
            return Readiness.LoggedOut;
        }

        var ledger = plugin.Ledger;
        var review = ledger.Review;
        if (ledger.ReadError is not null || review.IsDamaged)
        {
            return Readiness.JournalDamaged;
        }

        if (review.NeedsReconciliation)
        {
            return Readiness.NeedsReconciliation;
        }

        if (review.Interrupted is not null)
        {
            return Readiness.InterruptedRun;
        }

        var list = external?.List ?? plugin.Lists.Selected;
        if (list is null)
        {
            return Readiness.NoList;
        }

        var validation = external is null
            ? plugin.Lists.Analyze(list).Validation
            : ListAnalysis.Build(list, plugin.Items.IsReady ? plugin.Items : null).Validation;
        if (!validation.ItemsChecked && (external is null || forRun))
        {
            return Readiness.ListLoading;
        }

        if (validation.HasErrors)
        {
            return Readiness.ListHasErrors;
        }

        var configuration = plugin.Configuration;
        if ((external?.MaxSpend ?? configuration.MaxSpend) <= 0)
        {
            return Readiness.NoSpendCap;
        }

        var compatibility = plugin.Market.Compatibility;
        var marketNeeded = external is not null || !configuration.BuysOnlyFromVendors;
        if (marketNeeded && !compatibility.ReadingAvailable)
        {
            return Readiness.ReadingUnavailable;
        }

        if (marketNeeded && !compatibility.ExecutionAvailable)
        {
            return Readiness.BuyingUnavailable;
        }

        return !forRun && clib.Services.Svc.Automation.Running ? Readiness.Busy : Readiness.Ready;
    }

    public static string Reason(Readiness readiness) => readiness switch
    {
        Readiness.SetupNeeded => Loc.T(L.Shop.ReasonInstall),
        Readiness.LoggedOut => Loc.T(L.Shop.ReasonLoggedOut),
        Readiness.JournalDamaged => Loc.T(L.Shop.ReasonJournal),
        Readiness.NeedsReconciliation => Loc.T(L.Shop.ReasonReconcile),
        Readiness.InterruptedRun => Loc.T(L.Shop.ReasonInterrupted),
        Readiness.NoList => Loc.T(L.Shop.ReasonNoList),
        Readiness.ListLoading => Loc.T(L.Shop.ReasonListLoading),
        Readiness.ListHasErrors => Loc.T(L.Shop.ReasonListErrors),
        Readiness.NoSpendCap => Loc.T(L.Shop.ReasonNoCap),
        Readiness.ReadingUnavailable => Loc.T(L.Shop.ReasonReadingOff),
        Readiness.BuyingUnavailable => Loc.T(L.Shop.ReasonBuyingOff),
        Readiness.Busy => Loc.T(L.Shop.ReasonBusy),
        _ => string.Empty,
    };

    public static string Reason(Readiness readiness, ExternalRun external)
    {
        if (readiness != Readiness.ListHasErrors)
        {
            return Reason(readiness);
        }

        var items = Plugin.Instance.Items;
        var validation = ListAnalysis.Build(external.List, items.IsReady ? items : null).Validation;
        return Loc.T(L.Api.ReasonInvalid, ApiRequests.LineField(validation));
    }

    public static ApiRefusal Refusal(Readiness readiness) => readiness switch
    {
        Readiness.SetupNeeded => ApiRefusal.SetupNeeded,
        Readiness.LoggedOut => ApiRefusal.LoggedOut,
        Readiness.JournalDamaged => ApiRefusal.JournalDamaged,
        Readiness.NeedsReconciliation => ApiRefusal.NeedsReconciliation,
        Readiness.InterruptedRun => ApiRefusal.InterruptedRun,
        Readiness.ListLoading => ApiRefusal.ListLoading,
        Readiness.ReadingUnavailable => ApiRefusal.ReadingUnavailable,
        Readiness.BuyingUnavailable => ApiRefusal.BuyingUnavailable,
        Readiness.Busy => ApiRefusal.Busy,
        _ => ApiRefusal.InvalidRequest,
    };
}
