namespace AutoShopper.Core.Tasks;

internal enum RunPhase : byte { Idle, Preparing, Travelling, Finishing, Paused }

// What the windows show of a run while it goes: its phase.
internal sealed class RunProgress
{
    public RunPhase Phase { get; private set; } = RunPhase.Idle;

    public void SetPhase(RunPhase phase) => Phase = phase;

    public void Reset() => Phase = RunPhase.Idle;
}
