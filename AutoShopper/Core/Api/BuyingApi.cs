using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Tasks;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Api;

internal sealed class BuyingApi : IDisposable
{
    private const string ApiVersionGate = "AutoShopper.Api.ApiVersion";
    private const string QuoteStartGate = "AutoShopper.Api.QuoteStart";
    private const string QuoteResultGate = "AutoShopper.Api.QuoteResult";
    private const string StartRunGate = "AutoShopper.Api.StartRun";
    private const string RunStatusGate = "AutoShopper.Api.RunStatus";
    private const string StopRunGate = "AutoShopper.Api.StopRun";

    private readonly Plugin plugin;
    private readonly ICallGateProvider<int> apiVersion;
    private readonly ICallGateProvider<string, string> quoteStart;
    private readonly ICallGateProvider<string, string> quoteResult;
    private readonly ICallGateProvider<string, string> startRun;
    private readonly ICallGateProvider<string, string> runStatus;
    private readonly ICallGateProvider<string, bool> stopRun;
    private volatile bool disposed;

    public BuyingApi(Plugin plugin)
    {
        this.plugin = plugin;
        Consents = new CallerConsents(plugin.Configuration.ApiCallers);
        Quotes = new ApiQuotes(plugin.Prices, plugin.Preview, plugin.Controller);

        var pluginInterface = Plugin.PluginInterface;
        apiVersion = pluginInterface.GetIpcProvider<int>(ApiVersionGate);
        quoteStart = pluginInterface.GetIpcProvider<string, string>(QuoteStartGate);
        quoteResult = pluginInterface.GetIpcProvider<string, string>(QuoteResultGate);
        startRun = pluginInterface.GetIpcProvider<string, string>(StartRunGate);
        runStatus = pluginInterface.GetIpcProvider<string, string>(RunStatusGate);
        stopRun = pluginInterface.GetIpcProvider<string, bool>(StopRunGate);
    }

    public CallerConsents Consents { get; }

    public ApiQuotes Quotes { get; }

    public void Register()
    {
        apiVersion.RegisterFunc(ApiVersion);
        quoteStart.RegisterFunc(QuoteStart);
        quoteResult.RegisterFunc(QuoteResult);
        startRun.RegisterFunc(StartRun);
        runStatus.RegisterFunc(RunStatus);
        stopRun.RegisterFunc(StopRun);
    }

    public void Allow(string caller, string displayName)
    {
        Consents.Allow(caller, displayName);
        plugin.Configuration.Save();
        RunLog.Info($"Buying API: {displayName} ({caller}) is allowed to buy through Auto Shopper.");
    }

    public void Deny(string caller, string displayName)
    {
        Consents.Deny(caller, displayName);
        plugin.Configuration.Save();
        RunLog.Info($"Buying API: {displayName} ({caller}) is denied.");
        EndWorkOf(caller);
    }

    public void Revoke(string caller)
    {
        if (!Consents.Revoke(caller))
        {
            return;
        }

        plugin.Configuration.Save();
        RunLog.Info($"Buying API: the answer for {caller} was revoked; it asks again on its next run.");
        EndWorkOf(caller);
    }

    public ApiRefusal? CheckConsent(string caller, string displayName, long maxSpend, out string reason)
    {
        switch (Consents.StateOf(caller))
        {
            case ConsentState.Denied:
                reason = Loc.T(L.Api.ReasonDenied, displayName);
                return ApiRefusal.ConsentDenied;
            case ConsentState.Unknown:
                Consents.Ask(caller, displayName, maxSpend);
                reason = Loc.T(L.Api.ReasonConsent, displayName);
                return ApiRefusal.ConsentRequired;
            default:
                reason = string.Empty;
                return null;
        }
    }

    public void Dispose()
    {
        disposed = true;
        apiVersion.UnregisterFunc();
        quoteStart.UnregisterFunc();
        quoteResult.UnregisterFunc();
        startRun.UnregisterFunc();
        runStatus.UnregisterFunc();
        stopRun.UnregisterFunc();
        Quotes.Dispose();
    }

    private static int ApiVersion() => ApiContract.Version;

    private string QuoteStart(string json)
    {
        var check = ApiRequests.ReadQuote(json, out var request);
        var response = OnFramework(() => StartQuote(check, request), static () => QuoteStartResponse.Refuse(ApiRefusal.Busy, Loc.T(L.Api.QuoteRefused, Loc.T(L.Api.ReasonUnloading))));
        return ApiJson.Write(response, ApiJsonContext.Default.QuoteStartResponse);
    }

    private string QuoteResult(string quoteId)
    {
        var response = ApiIds.TryParse(quoteId, out var id) ? OnFramework(() => Quotes.Result(id), () => QuoteResultResponse.Unknown(quoteId)) : QuoteResultResponse.Unknown(quoteId);
        return ApiJson.Write(response, ApiJsonContext.Default.QuoteResultResponse);
    }

    private string StartRun(string json)
    {
        var check = ApiRequests.ReadRun(json, out var request);
        var response = OnFramework(() => BeginRun(check, request), static () => StartRunResponse.Refuse(ApiRefusal.Busy, Refused(Loc.T(L.Api.ReasonUnloading))));
        return ApiJson.Write(response, ApiJsonContext.Default.StartRunResponse);
    }

    private string RunStatus(string runId)
    {
        var response = ApiIds.TryParse(runId, out var id) ? OnFramework(() => plugin.Controller.ExternalStatus(id), () => RunStatusResponse.Unknown(runId)) : RunStatusResponse.Unknown(runId);
        return ApiJson.Write(response, ApiJsonContext.Default.RunStatusResponse);
    }

    private bool StopRun(string runId) => ApiIds.TryParse(runId, out var id) && OnFramework(() => plugin.Controller.StopExternal(id), static () => false);

    private QuoteStartResponse StartQuote(RequestCheck check, QuoteRequest? request)
    {
        if (check.Refusal is { } refusal)
        {
            RunLog.Info($"Buying API: a quote was refused ({refusal}, {check.Field}).");
            return QuoteStartResponse.Refuse(refusal, Loc.T(L.Api.QuoteRefused, ReasonOf(check)));
        }

        return Quotes.Start(request!, ApiRequests.DisplayName(request!.Caller!, request.CallerDisplayName));
    }

    private StartRunResponse BeginRun(RequestCheck check, RunRequest? request)
    {
        if (check.Refusal is { } refusal)
        {
            RunLog.Info($"Buying API: a run was refused ({refusal}, {check.Field}).");
            return StartRunResponse.Refuse(refusal, Refused(ReasonOf(check)));
        }

        var caller = request!.Caller!;
        var displayName = ApiRequests.DisplayName(caller, request.CallerDisplayName);
        if (CheckConsent(caller, displayName, request.MaxSpend, out var consentReason) is { } consentRefusal)
        {
            RunLog.Info($"Buying API: a run from {displayName} ({caller}) was refused ({consentRefusal}){(consentRefusal == ApiRefusal.ConsentRequired ? "; the Shop page asks the player" : string.Empty)}.");
            return StartRunResponse.Refuse(consentRefusal, Refused(consentReason));
        }

        var external = ExternalRun.From(request, DateTime.UtcNow);
        var readiness = plugin.Controller.StartExternal(external, out var runId);
        if (readiness != Readiness.Ready)
        {
            return StartRunResponse.Refuse(ShopReadiness.Refusal(readiness), Refused(ShopReadiness.Reason(readiness, external)));
        }

        RunLog.Info($"Buying API: {displayName} started run {runId} for '{external.List.Name}': {external.List.Lines.Count} line(s), cap {external.MaxSpend}, way home {external.ReturnHome}.");
        return StartRunResponse.Accept(runId);
    }

    private void EndWorkOf(string caller)
    {
        var dropped = Quotes.Drop(caller);
        var stopped = plugin.Controller.StopExternalFrom(caller);
        if (dropped == 0 && !stopped)
        {
            return;
        }

        RunLog.Info($"Buying API: {caller} may no longer buy; {(stopped ? "its run was told to stop" : "it had no run under way")}, {dropped} of its waiting quote(s) dropped.");
    }

    private static string ReasonOf(RequestCheck check)
        => check.Refusal == ApiRefusal.UnsupportedSchema ? Loc.T(L.Api.ReasonSchema) : Loc.T(L.Api.ReasonInvalid, check.Field);

    private static string Refused(string reason) => Loc.T(L.Buyer.ChatRefused, reason);

    private T OnFramework<T>(Func<T> work, Func<T> unloading)
    {
        if (disposed)
        {
            return unloading();
        }

        if (Svc.Framework.IsInFrameworkUpdateThread)
        {
            return work();
        }

        return Svc.Framework.RunOnFrameworkThread(() => disposed ? unloading() : work()).GetAwaiter().GetResult();
    }
}
