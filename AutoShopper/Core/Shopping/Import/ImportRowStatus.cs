namespace AutoShopper.Core.Shopping.Import;

public enum ImportRowStatus : byte
{
    Unknown,
    Resolved,
    Ambiguous,
    Invalid,
}

public enum ImportRowProblem : byte
{
    None,
    QuantityOutOfRange,
    UnitCapOutOfRange,
    ExcessOutOfRange,
    MalformedNumber,
    UnknownQuality,
    UnknownMode,
    NameTooLong,
    MissingItem,
    UnknownRecipe,
    SkippedCraft,
    NotMarketable,
    CraftedFromOtherRows,
    AmbiguousNumber,
    UnknownDye,
}
