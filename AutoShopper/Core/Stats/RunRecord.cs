namespace AutoShopper.Core.Stats;

// Fields added after the first release default when an older history file lacks them, and null reads as unknown.
[Serializable]
public sealed class RunRecord
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }

    public string WorldName { get; set; } = "";
    public string DataCenterName { get; set; } = "";
    public bool CrossedDataCenter { get; set; }
    public RunOutcome? Outcome { get; set; }

    public string ListName { get; set; } = "";
    public bool DryRun { get; set; }
    public long Spent { get; set; }
    public int Units { get; set; }
    public int Purchases { get; set; }

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public bool Finished => Outcome is RunOutcome.Completed or RunOutcome.CompletedPartial;
}
