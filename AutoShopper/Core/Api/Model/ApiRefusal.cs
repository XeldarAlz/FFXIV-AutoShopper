namespace AutoShopper.Core.Api.Model;

internal enum ApiRefusal : byte
{
    UnsupportedSchema,
    InvalidRequest,
    ConsentRequired,
    ConsentDenied,
    Busy,
    SetupNeeded,
    LoggedOut,
    JournalDamaged,
    NeedsReconciliation,
    InterruptedRun,
    ReadingUnavailable,
    BuyingUnavailable,
    ListLoading,
    PricesUnavailable,
    PlanFailed,
    NothingToBuy,
    AuthorizationFailed,
    JournalWriteFailed,
    CharacterChanged,
}
