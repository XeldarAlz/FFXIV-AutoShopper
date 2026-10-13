namespace AutoShopper.Core.Shopping.Model;

[Flags]
public enum LineProblems : byte
{
    None = 0,
    NotMarketable = 1 << 0,
    InvalidQuantity = 1 << 1,
    InvalidUnitCap = 1 << 2,
    InvalidExcess = 1 << 3,
    QualityUnavailable = 1 << 4,
    Duplicate = 1 << 5,
    MergedOverLimit = 1 << 6,
}
