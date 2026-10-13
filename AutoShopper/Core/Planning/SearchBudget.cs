using System.Threading;

namespace AutoShopper.Core.Planning;

internal sealed class SearchBudget
{
    private readonly TimeProvider timeProvider;
    private readonly long startTimestamp;
    private readonly TimeSpan timeLimit;
    private readonly int evaluationLimit;
    private readonly CancellationToken cancellationToken;

    public SearchBudget(TimeProvider timeProvider, TimeSpan timeLimit, int evaluationLimit, CancellationToken cancellationToken)
    {
        this.timeProvider = timeProvider;
        this.timeLimit = timeLimit;
        this.evaluationLimit = evaluationLimit;
        this.cancellationToken = cancellationToken;
        startTimestamp = timeProvider.GetTimestamp();
    }

    public int Evaluations { get; private set; }

    public PlanSearchLimits Reached { get; private set; }

    public bool TryCharge()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Reached != PlanSearchLimits.None)
        {
            return false;
        }

        if (Evaluations >= evaluationLimit)
        {
            Reached = PlanSearchLimits.EvaluationLimitReached;
            return false;
        }

        if (!HasTimeLeft())
        {
            return false;
        }

        Evaluations++;
        return true;
    }

    // Reads the clock without spending an evaluation, for long work between two evaluations.
    public bool HasTimeLeft()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Reached != PlanSearchLimits.None)
        {
            return false;
        }

        if (timeProvider.GetElapsedTime(startTimestamp) > timeLimit)
        {
            Reached = PlanSearchLimits.TimeBudgetReached;
            return false;
        }

        return true;
    }
}
