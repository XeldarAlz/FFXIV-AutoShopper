namespace AutoShopper;

public enum AfterRunAction
{
    StayLoggedIn,
    Logout,
    ReturnToInn,
    CloseGame,
}

public enum PlanningMode : byte
{
    Cheapest,
    Balanced,
    FewestStops,
}

public enum BuySource : byte
{
    Everything,
    MarketBoard,
    NpcVendors,
}
