using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Tasks;

namespace AutoShopper.Core.Api.Model;

internal enum ApiReturnHome : byte
{
    NotNeeded,
    AlreadyHome,
    Underway,
    Arrived,
    Failed,
    Skipped,
}

internal readonly record struct RunRequestLine(uint ItemId, ItemQuality Quality, int Quantity, long MaxUnitPrice, int ExcessAllowance);

internal sealed record RunRequest
{
    public int Schema { get; init; }

    public string? Caller { get; init; }

    public string? CallerDisplayName { get; init; }

    public string? CallerRunId { get; init; }

    public string? Label { get; init; }

    public long MaxSpend { get; init; }

    public bool ReturnHome { get; init; }

    public RunRequestLine[]? Lines { get; init; }
}

internal sealed record StartRunResponse
{
    public int Schema { get; init; } = ApiContract.Schema;

    public bool Accepted { get; init; }

    public string? RunId { get; init; }

    public ApiRefusal? Refusal { get; init; }

    public string? Message { get; init; }

    public static StartRunResponse Accept(Guid runId) => new() { Accepted = true, RunId = ApiIds.Format(runId) };

    public static StartRunResponse Refuse(ApiRefusal refusal, string message) => new() { Refusal = refusal, Message = message };
}

internal readonly record struct RunStatusLine(uint ItemId, ItemQuality Quality, int Requested, int Acquired, int Missing);

internal readonly record struct RunStatusIssue(RunIssueReason Reason, uint ItemId);

internal sealed record RunStatusResponse
{
    public int Schema { get; init; } = ApiContract.Schema;

    public string? RunId { get; init; }

    public bool Known { get; init; }

    public string? CallerRunId { get; init; }

    public RunPhase Phase { get; init; }

    public RunState State { get; init; }

    public string Status { get; init; } = string.Empty;

    public int Revision { get; init; }

    public bool Paused { get; init; }

    public PauseReason? PauseReason { get; init; }

    public bool Terminal { get; init; }

    public RunOutcome? Outcome { get; init; }

    public bool NeedsPlayer { get; init; }

    public long ConfirmedSpend { get; init; }

    public long ReservedSpend { get; init; }

    public RunStatusLine[] Lines { get; init; } = [];

    public RunStatusIssue[] Issues { get; init; } = [];

    public ApiReturnHome ReturnHome { get; init; }

    public ApiRefusal? Refusal { get; init; }

    public static RunStatusResponse Unknown(string? runId) => new() { RunId = runId };

    public RunStatusResponse AsTerminal(RunState state, string status, RunOutcome outcome, bool needsPlayer) => this with
    {
        Phase = RunPhase.Idle,
        State = state,
        Status = status,
        Revision = checked(Revision + 1),
        Paused = false,
        PauseReason = null,
        Terminal = true,
        Outcome = outcome,
        NeedsPlayer = needsPlayer,
    };

    public static ApiReturnHome ReturnHomeOf(ReturnHomeResult result) => result switch
    {
        ReturnHomeResult.AlreadyHome => ApiReturnHome.AlreadyHome,
        ReturnHomeResult.Underway => ApiReturnHome.Underway,
        ReturnHomeResult.Arrived => ApiReturnHome.Arrived,
        ReturnHomeResult.Failed => ApiReturnHome.Failed,
        ReturnHomeResult.Skipped => ApiReturnHome.Skipped,
        _ => ApiReturnHome.NotNeeded,
    };
}
