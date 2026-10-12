using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class StackAllocatorTests
{
    private const uint Ore = 5_100;
    private const uint Cloth = 5_200;

    [Fact]
    public void PicksTheCheapestCombinationOfWholeStacks()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
        var allocation = Single(StackAllocator.AllocateAll(
            [demand],
            [Offer(Alpha, Ore, 6, 10), Offer(Alpha, Ore, 4, 11), Offer(Alpha, Ore, 10, 12)]));

        Assert.Equal(10, allocation.PlannedQuantity);
        Assert.Equal(0, allocation.MissingQuantity);
        Assert.Equal(63 + 46, allocation.Payable);
        Assert.Equal([4, 6], Quantities(allocation));
    }

    [Fact]
    public void LowestUnitPriceIsNotAlwaysTheCheapestPayable()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 5, excessAllowance: 5);
        var allocation = Single(StackAllocator.AllocateAll(
            [demand],
            [Offer(Alpha, Ore, 10, 10), Offer(Alpha, Ore, 5, 15)]));

        Assert.Equal(5, allocation.PlannedQuantity);
        Assert.Equal(78, allocation.Payable);
    }

    [Fact]
    public void ExcessAllowanceAdmitsALargerWholeStack()
    {
        var offers = ImmutableArray.Create(Offer(Alpha, Ore, 12, 5));

        var allowed = Single(StackAllocator.AllocateAll([PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10, 2)], offers));
        var refused = Single(StackAllocator.AllocateAll([PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10, 1)], offers));

        Assert.Equal(12, allowed.PlannedQuantity);
        Assert.Equal(0, allowed.MissingQuantity);
        Assert.Equal(0, refused.PlannedQuantity);
        Assert.Equal(10, refused.MissingQuantity);
    }

    [Fact]
    public void ImpossibleExactQuantityFallsBackToTheBestPartialCover()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
        var allocation = Single(StackAllocator.AllocateAll(
            [demand],
            [Offer(Alpha, Ore, 4, 10, key: "a"), Offer(Alpha, Ore, 4, 11, key: "b"), Offer(Alpha, Ore, 4, 12, key: "c")]));

        Assert.Equal(8, allocation.PlannedQuantity);
        Assert.Equal(2, allocation.MissingQuantity);
        Assert.Equal(42 + 46, allocation.Payable);
    }

    [Fact]
    public void ShortSupplyTakesEverythingThatFits()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
        var allocation = Single(StackAllocator.AllocateAll(
            [demand],
            [Offer(Alpha, Ore, 3, 10, key: "a"), Offer(Beta, Ore, 3, 10, key: "b"), Offer(Gamma, Ore, 3, 10, key: "c")]));

        Assert.Equal(9, allocation.PlannedQuantity);
        Assert.Equal(1, allocation.MissingQuantity);
    }

    [Fact]
    public void UnitCapIncludingFeeFiltersOffersEvenWhenTheyAreCheaperInTotal()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 2, excessAllowance: 3, unitCapIncludingFee: 60);
        var allocation = Single(StackAllocator.AllocateAll(
            [demand],
            [Offer(Alpha, Ore, 2, 70), Offer(Alpha, Ore, 5, 55)]));

        Assert.Equal(5, allocation.PlannedQuantity);
        Assert.All(allocation.Offers, offer => Assert.Equal(55, offer.UnitPrice));
    }

    [Fact]
    public void CapBoundaryIncludesTheFee()
    {
        var atCap = Single(StackAllocator.AllocateAll(
            [PlanDemand.ForPurchase(Ore, DemandQuality.Any, 4, unitCapIncludingFee: 105)],
            [Offer(Alpha, Ore, 4, 100)]));
        var overCap = Single(StackAllocator.AllocateAll(
            [PlanDemand.ForPurchase(Ore, DemandQuality.Any, 4, unitCapIncludingFee: 104)],
            [Offer(Alpha, Ore, 4, 100)]));

        Assert.Equal(4, atCap.PlannedQuantity);
        Assert.Equal(0, overCap.PlannedQuantity);
    }

    [Fact]
    public void StrictLinesAreServedBeforeAnySoSharedStockIsNotStolen()
    {
        var highQuality = PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 3);
        var anyQuality = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 4);
        var allocations = StackAllocator.AllocateAll(
            [anyQuality, highQuality],
            [
                Offer(Alpha, Ore, 3, 10, isHq: true),
                Offer(Alpha, Ore, 2, 20, isHq: true),
                Offer(Alpha, Ore, 2, 5),
            ]);

        var strict = Find(allocations, highQuality.Key);
        var relaxed = Find(allocations, anyQuality.Key);
        Assert.Equal(3, strict.PlannedQuantity);
        Assert.All(strict.Offers, offer => Assert.True(offer.IsHq));
        Assert.Equal(4, relaxed.PlannedQuantity);
        AssertDisjoint(allocations);
    }

    [Fact]
    public void NormalQualityLineNeverTakesHighQualityStock()
    {
        var normalQuality = PlanDemand.ForPurchase(Ore, DemandQuality.NormalQuality, 3);
        var allocation = Single(StackAllocator.AllocateAll(
            [normalQuality],
            [Offer(Alpha, Ore, 3, 1, isHq: true), Offer(Alpha, Ore, 1, 50)]));

        Assert.Equal(1, allocation.PlannedQuantity);
        Assert.All(allocation.Offers, offer => Assert.False(offer.IsHq));
    }

    [Fact]
    public void HighQualityAndNormalQualityAndAnyShareOneItemWithoutReuse()
    {
        var allocations = StackAllocator.AllocateAll(
            [
                PlanDemand.ForPurchase(Cloth, DemandQuality.HighQuality, 2),
                PlanDemand.ForPurchase(Cloth, DemandQuality.NormalQuality, 2),
                PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 4),
            ],
            [
                Offer(Alpha, Cloth, 2, 30, isHq: true, key: "h1"),
                Offer(Alpha, Cloth, 2, 31, isHq: true, key: "h2"),
                Offer(Alpha, Cloth, 2, 10, key: "n1"),
                Offer(Alpha, Cloth, 2, 11, key: "n2"),
            ]);

        Assert.All(allocations, allocation => Assert.Equal(0, allocation.MissingQuantity));
        AssertDisjoint(allocations);
    }

    [Fact]
    public void DynamicProgramMatchesBruteForceOnRandomSingleLines()
    {
        var random = new Random(20261013);
        for (var caseIndex = 0; caseIndex < 400; caseIndex++)
        {
            var required = random.Next(1, 25);
            var excess = random.Next(0, 4);
            var cap = random.Next(0, 3) == 0 ? random.Next(20, 120) : 0;
            var offerCount = random.Next(0, 11);
            var offers = new PlanOffer[offerCount];
            for (var offerIndex = 0; offerIndex < offerCount; offerIndex++)
            {
                var world = AllWorlds[random.Next(AllWorlds.Length)];
                offers[offerIndex] = Offer(world, Ore, random.Next(1, 12), random.Next(5, 120), key: $"case{caseIndex}-{offerIndex}");
            }

            var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, required, excess, cap);
            var allocation = Single(StackAllocator.AllocateAll([demand], [.. offers]));
            var expected = BruteForce.BestLine(demand, offers);

            Assert.Equal(expected.Missing, allocation.MissingQuantity);
            Assert.Equal(expected.Cost, allocation.Payable);
            Assert.InRange(allocation.PlannedQuantity, 0, demand.MaximumTotal);
            AssertDisjoint([allocation]);
        }
    }

    private static LineAllocation Single(ImmutableArray<LineAllocation> allocations)
    {
        Assert.Single(allocations);
        return allocations[0];
    }

    private static LineAllocation Find(ImmutableArray<LineAllocation> allocations, DemandKey key)
    {
        for (var index = 0; index < allocations.Length; index++)
        {
            if (allocations[index].Demand.Key == key)
            {
                return allocations[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"No allocation for {key}");
    }

    private static int[] Quantities(LineAllocation allocation)
    {
        var quantities = new int[allocation.Offers.Length];
        for (var index = 0; index < quantities.Length; index++)
        {
            quantities[index] = allocation.Offers[index].Quantity;
        }

        Array.Sort(quantities);
        return quantities;
    }

    private static void AssertDisjoint(ImmutableArray<LineAllocation> allocations)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var allocationIndex = 0; allocationIndex < allocations.Length; allocationIndex++)
        {
            var offers = allocations[allocationIndex].Offers;
            for (var offerIndex = 0; offerIndex < offers.Length; offerIndex++)
            {
                Assert.True(keys.Add(offers[offerIndex].ListingKey), $"listing {offers[offerIndex].ListingKey} used twice");
            }
        }
    }
}
