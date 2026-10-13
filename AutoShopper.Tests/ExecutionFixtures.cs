using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;

namespace AutoShopper.Tests;

internal static class ExecutionFixtures
{
    public const ulong Character = 1_001;
    public const ulong OwnRetainer = 900;
    public const uint HomeWorld = 1;
    public const uint OtherAllowedWorld = 2;
    public const uint Ore = 5_000;
    public const uint Cloth = 6_000;
    public const long MaxSpend = 100_000;
    public const long Reserve = 10_000;
    public const int SearchGeneration = 3;

    public static readonly Guid RunId = new("6f1c2a4e-0d5b-4a8e-9a37-2f6a1b3c4d5e");
    public static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    public static readonly PlanDemand OreDemand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10, 2, 1_100);
    public static readonly PlanDemand ClothDemand = PlanDemand.ForPurchase(Cloth, DemandQuality.NormalQuality, 4);

    public static RunAuthorization Authorization()
    {
        var problem = RunAuthorization.TryCreate(
            RunId,
            Character,
            HomeWorld,
            [ClothDemand, OreDemand],
            [OtherAllowedWorld, HomeWorld],
            MaxSpend,
            Reserve,
            1,
            Now,
            out var authorization);
        Assert.Equal(AuthorizationProblem.None, problem);
        return authorization!;
    }

    public static ObservedListing Listing(int quantity = 5, long unitPrice = 1_000, long totalTax = 50)
    {
        return new ObservedListing(77, 501, 2_002, Ore, false, quantity, unitPrice, totalTax, false, HomeWorld, SearchGeneration, Now);
    }

    public static GuardInput ValidInput()
    {
        return new GuardInput
        {
            Authorization = Authorization(),
            ActiveRunId = RunId,
            CharacterContentId = Character,
            CurrentWorldId = HomeWorld,
            SearchGeneration = SearchGeneration,
            ExecutionAvailable = true,
            Demand = OreDemand.Key,
            Selected = Listing(),
            Current = Listing(),
            ConfirmedQuantity = 0,
            Spend = new SpendState(MaxSpend, 0, 0, Guid.Empty),
            CurrentGil = 50_000,
            FreeInventorySlots = 5,
            ItemStackSize = 999,
            OwnRetainerIds = ImmutableArray.Create(OwnRetainer),
        };
    }
}
