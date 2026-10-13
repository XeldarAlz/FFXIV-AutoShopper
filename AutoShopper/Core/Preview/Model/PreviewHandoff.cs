using System.Threading;

namespace AutoShopper.Core.Preview.Model;

internal enum PlanningProgress : byte
{
    Running,
    Planned,
    Failed,
}

// Carries a planning worker's ending to the framework thread. Workers never touch the running flag: the framework
// thread ends planning only for the generation it started last, so an older worker that finishes late can neither
// publish its plan nor mark a newer planning as done, which would let a run authorize from the older plan.
internal sealed class PreviewHandoff
{
    private PreviewResult? pending;
    private long failedGeneration;

    // Worker thread. A newer result that is already waiting is kept.
    public void Complete(PreviewResult result)
    {
        while (true)
        {
            var current = Volatile.Read(ref pending);
            if (current is not null && current.Generation >= result.Generation)
            {
                return;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref pending, result, current), current))
            {
                return;
            }
        }
    }

    // Worker thread.
    public void Fail(long generation)
    {
        while (true)
        {
            var current = Interlocked.Read(ref failedGeneration);
            if (current >= generation || Interlocked.CompareExchange(ref failedGeneration, generation, current) == current)
            {
                return;
            }
        }
    }

    // Framework thread: how the planning started for this generation ended, if it has. A waiting result of an older
    // generation is dropped.
    public PlanningProgress Take(long generation, out PreviewResult? result)
    {
        result = Interlocked.Exchange(ref pending, null);
        if (result is not null && result.Generation == generation)
        {
            return PlanningProgress.Planned;
        }

        result = null;
        return Interlocked.Read(ref failedGeneration) == generation ? PlanningProgress.Failed : PlanningProgress.Running;
    }
}
