using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Shopping.Import;

public sealed class ImportRow
{
    public ImportRow(int sourceLine, string sourceText)
    {
        SourceLine = sourceLine;
        SourceText = sourceText;
    }

    public int SourceLine { get; }

    public string SourceText { get; }

    public string Name { get; internal set; } = string.Empty;

    // A line that starts with a bare number may be an item whose name starts with that number; the whole line is
    // tried as a name when the shorter one is unknown.
    public string? WholeName { get; internal set; }

    public string? Section { get; internal set; }

    public uint RequestedItemId { get; internal set; }

    public Guid LineId { get; internal set; }

    public int Quantity { get; internal set; } = ShoppingLimits.MinimumQuantity;

    public ItemQuality Quality { get; internal set; }

    public QuantityMode Mode { get; internal set; }

    public long MaxUnitPrice { get; internal set; }

    public int ExcessAllowance { get; internal set; }

    public ImportRowStatus Status { get; private set; }

    public ImportRowProblem Problem { get; private set; }

    public uint ItemId { get; private set; }

    public uint[] Candidates { get; private set; } = [];

    public bool IsReady => Status == ImportRowStatus.Resolved;

    public bool Choose(uint itemId)
    {
        if (Candidates.Length == 0 || Array.IndexOf(Candidates, itemId) < 0)
        {
            return false;
        }

        ItemId = itemId;
        Status = ImportRowStatus.Resolved;
        return true;
    }

    public ShoppingLine ToLine(Guid lineId) => new()
    {
        Id = lineId,
        ItemId = ItemId,
        Quality = Quality,
        Mode = Mode,
        Quantity = Quantity,
        MaxUnitPrice = MaxUnitPrice,
        ExcessAllowance = ExcessAllowance,
    };

    internal void UseWholeName()
    {
        Name = WholeName ?? Name;
        Quantity = ShoppingLimits.MinimumQuantity;
    }

    internal void Reject(ImportRowProblem problem)
    {
        if (Status == ImportRowStatus.Invalid)
        {
            return;
        }

        Status = ImportRowStatus.Invalid;
        Problem = problem;
    }

    internal void Apply(NameResolution resolution)
    {
        if (Status == ImportRowStatus.Invalid)
        {
            return;
        }

        switch (resolution.Status)
        {
            case ResolutionStatus.Resolved:
                ItemId = resolution.ItemId;
                Status = ImportRowStatus.Resolved;
                break;
            case ResolutionStatus.Ambiguous:
                Candidates = resolution.Candidates;
                Status = ImportRowStatus.Ambiguous;
                break;
            default:
                Status = ImportRowStatus.Unknown;
                break;
        }
    }
}
