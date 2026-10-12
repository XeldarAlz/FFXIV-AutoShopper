namespace AutoShopper.Core.Shopping.Model;

public sealed class NormalizedList
{
    public const int NoDemand = -1;

    private readonly NormalizedDemand[] demands;
    private readonly int[] demandIndexByLine;

    public NormalizedList(NormalizedDemand[] demands, int[] demandIndexByLine)
    {
        this.demands = demands;
        this.demandIndexByLine = demandIndexByLine;
    }

    public ReadOnlySpan<NormalizedDemand> Demands => demands;

    public int DemandIndexOf(int lineIndex)
        => (uint)lineIndex < (uint)demandIndexByLine.Length ? demandIndexByLine[lineIndex] : NoDemand;

    public int StillNeeded(int lineIndex, OwnedCount owned)
    {
        var demandIndex = DemandIndexOf(lineIndex);
        return demandIndex == NoDemand ? 0 : ListNormalizer.Shortfall(demands, demandIndex, owned);
    }
}
