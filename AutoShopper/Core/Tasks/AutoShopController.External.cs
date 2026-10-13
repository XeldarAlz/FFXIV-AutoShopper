using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Stats;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopController
{
    private readonly RecentStore<RunStatusResponse> externalResults = new(ApiContract.RunResultCapacity, TimeSpan.Zero);

    public Readiness StartExternal(ExternalRun external, out Guid runId)
    {
        runId = Guid.Empty;
        if (Running)
        {
            Diag($"Start for {external.CallerDisplayName} refused: a run is already running.");
            return Readiness.Busy;
        }

        var readiness = ShopReadiness.Evaluate(Plugin.Instance, external: external);
        if (readiness != Readiness.Ready)
        {
            Diag($"Start for {external.CallerDisplayName} refused: {readiness}.");
            return readiness;
        }

        runId = BeginFresh(external.List.Name, external).RunId;
        return Readiness.Ready;
    }

    public bool StopExternal(Guid runId)
    {
        if (shopRun is not { External: { } external } run || run.RunId != runId)
        {
            return false;
        }

        Diag($"Stop of run {runId} asked for by {external.CallerDisplayName}.");
        Stop();
        return true;
    }

    public RunStatusResponse ExternalStatus(Guid runId)
    {
        if (shopRun is { External: { } external } run && run.RunId == runId)
        {
            return StatusOf(run, external);
        }

        return externalResults.TryGet(runId, DateTime.UtcNow, out var stored) ? stored : RunStatusResponse.Unknown(ApiIds.Format(runId));
    }

    private RunStatusResponse StatusOf(ShopRunState run, ExternalRun external) => new()
    {
        RunId = ApiIds.Format(run.RunId),
        Known = true,
        CallerRunId = external.CallerRunId,
        DryRun = run.DryRun,
        Phase = Phase,
        State = run.State,
        Status = Status,
        Revision = run.Revision,
        Paused = Paused,
        PauseReason = Paused ? PauseReason : null,
        NeedsPlayer = run.HasUnknownOutcome,
        ConfirmedSpend = run.Spent,
        ReservedSpend = run.Spend.Reserved,
        Lines = external.StatusLines(run.Tally),
        Issues = ExternalRun.StatusIssues(run.Issues),
        ReturnHome = RunStatusResponse.ReturnHomeOf(run.ReturnHome),
        Refusal = run.Refusal,
    };

    private void RecordExternalResult()
    {
        if (shopRun is not { External: { } external } run || session is not { } ending)
        {
            return;
        }

        var outcome = ending.Interrupted ? RunOutcome.Stopped : ending.Outcome ?? (ending.EndedWithFault ? RunOutcome.Failed : RunOutcome.Stopped);
        var resumable = ending.Interrupted && run.IsAuthorized && !run.DryRun;
        var result = StatusOf(run, external).AsTerminal(
            run.FinalState ?? run.State,
            run.Refusal is null ? AutoShopRun.OutcomeLabel(outcome) : run.RefusalReason,
            outcome,
            outcome == RunOutcome.NeedsReconciliation || run.HasUnknownOutcome || resumable);
        externalResults.Add(run.RunId, result, DateTime.UtcNow);
        Diag($"Run {run.RunId} for {external.CallerDisplayName} ended {outcome}{(result.NeedsPlayer ? "; it waits for the player" : string.Empty)}.");
    }
}
