namespace AutoShopper.Core.Shopping.Import;

public enum ResolutionStatus : byte
{
    Unknown,
    Resolved,
    Ambiguous,
}

public readonly record struct NameResolution(ResolutionStatus Status, uint ItemId, uint[] Candidates)
{
    public static readonly NameResolution None = new(ResolutionStatus.Unknown, 0, []);

    public static NameResolution Unique(uint itemId) => new(ResolutionStatus.Resolved, itemId, []);

    public static NameResolution Ambiguous(uint[] candidates) => new(ResolutionStatus.Ambiguous, 0, candidates);
}
