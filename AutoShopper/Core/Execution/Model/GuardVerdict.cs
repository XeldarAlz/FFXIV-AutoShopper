namespace AutoShopper.Core.Execution.Model;

internal enum GuardRejection : byte
{
    None = 0,
    ExecutionUnavailable = 1,
    WrongRun = 2,
    WrongCharacter = 3,
    WorldNotAllowed = 4,
    WrongWorld = 5,
    StaleObservation = 6,
    PurchasePending = 7,
    InvalidListing = 8,
    Mannequin = 9,
    SetListing = 10,
    OwnRetainer = 11,
    ItemMismatch = 12,
    QualityMismatch = 13,
    QuantityMismatch = 14,
    ListingChanged = 15,
    DemandNotAuthorized = 16,
    DemandExhausted = 17,
    ExcessExceeded = 18,
    ArithmeticOverflow = 19,
    UnitCapExceeded = 20,
    BudgetExceeded = 21,
    ReserveBreached = 22,
    UniqueAlreadyOwned = 23,
    InventoryFull = 24,
    // Crystals share one pouch stack per kind, which the listing would push past its limit.
    CrystalLimitExceeded = 25,
}

internal readonly record struct GuardVerdict(GuardRejection Rejection, long Payable)
{
    public bool IsAllowed => Rejection == GuardRejection.None;

    public static GuardVerdict Allowed(long payable)
    {
        return new GuardVerdict(GuardRejection.None, payable);
    }

    public static GuardVerdict Rejected(GuardRejection rejection)
    {
        return new GuardVerdict(rejection, 0);
    }
}
