using System.Collections.Immutable;
using System.Numerics;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Vendors.Model;

namespace AutoShopper.Tests;

public sealed class VendorModelTests
{
    private const uint Dye = 52254;
    private const uint Lantern = 6637;
    private const uint Partition = 24514;
    private const uint Bonsai = 9717;
    private const uint Town = 129;
    private const uint OtherTown = 133;
    private const uint Mist = 339;
    private const uint ShopA = 262_654;
    private const uint ShopB = 262_657;
    private const uint TownShop = 262_191;

    private static readonly VendorPriceTable prices = new(new Dictionary<uint, long>
    {
        [Dye] = 216,
        [Lantern] = 3_000,
        [Partition] = 1_500,
        [Bonsai] = 16_236,
    });

    [Fact]
    public void AnItemTheMarketSellsDearerGoesToTheVendor()
    {
        var demand = PlanDemand.ForPurchase(Dye, DemandQuality.Any, 10);

        var split = VendorSplit.Split([demand], [Offer(Dye, 5, 300), Offer(Dye, 20, 250)], prices);

        Assert.Empty(split.Market);
        var line = Assert.Single(split.Vendor);
        Assert.Equal(10, line.Quantity);
        Assert.Equal(216, line.UnitPrice);
        Assert.Equal(2_160, split.VendorTotal);
    }

    [Fact]
    public void AnItemTheMarketSellsCheaperStaysOnTheMarket()
    {
        var demand = PlanDemand.ForPurchase(Dye, DemandQuality.Any, 10);

        var split = VendorSplit.Split([demand], [Offer(Dye, 10, 150)], prices);

        Assert.Equal(demand, Assert.Single(split.Market));
        Assert.Empty(split.Vendor);
    }

    [Fact]
    public void AFewCheapListingsDoNotOutweighTheRest()
    {
        var demand = PlanDemand.ForPurchase(Dye, DemandQuality.Any, 10);

        var split = VendorSplit.Split([demand], [Offer(Dye, 1, 100), Offer(Dye, 20, 400)], prices);

        Assert.Single(split.Vendor);
    }

    [Fact]
    public void TooFewListingsSendTheWholeLineToTheVendor()
    {
        var demand = PlanDemand.ForPurchase(Dye, DemandQuality.Any, 10);

        var split = VendorSplit.Split([demand], [Offer(Dye, 3, 100)], prices);

        Assert.Equal(10, Assert.Single(split.Vendor).Quantity);
    }

    [Fact]
    public void HighQualityLinesNeverGoToAVendor()
    {
        var demand = PlanDemand.ForPurchase(Dye, DemandQuality.HighQuality, 10);

        var split = VendorSplit.Split([demand], [], prices);

        Assert.Single(split.Market);
    }

    [Fact]
    public void ANormalQualityLineIgnoresHighQualityListings()
    {
        var demand = PlanDemand.ForPurchase(Dye, DemandQuality.NormalQuality, 10);

        var split = VendorSplit.Split([demand], [Offer(Dye, 50, 10, isHq: true)], prices);

        Assert.Single(split.Vendor);
    }

    [Fact]
    public void AVendorAboveTheLineCapIsNotUsed()
    {
        var demand = PlanDemand.ForPurchase(Bonsai, DemandQuality.Any, 1, unitCapIncludingFee: 10_000);

        var split = VendorSplit.Split([demand], [], prices);

        Assert.Single(split.Market);
        Assert.Empty(split.Vendor);
    }

    [Fact]
    public void ItemsNoVendorSellsStayOnTheMarket()
    {
        var demand = PlanDemand.ForPurchase(99_999, DemandQuality.Any, 4);

        var split = VendorSplit.Split([demand], [], prices);

        Assert.Single(split.Market);
        Assert.Empty(split.Vendor);
    }

    [Fact]
    public void WithoutVendorPricesEverythingStaysOnTheMarket()
    {
        ImmutableArray<PlanDemand> demands = [PlanDemand.ForPurchase(Dye, DemandQuality.Any, 4)];

        var split = VendorSplit.Split(demands, [], VendorPriceTable.Empty);

        Assert.Equal(demands, split.Market);
        Assert.Equal(0, split.VendorTotal);
    }

    [Fact]
    public void PriceTablesWithTheSameEntriesAreTheSame()
    {
        var copy = new VendorPriceTable(new Dictionary<uint, long> { [Dye] = 216, [Lantern] = 3_000, [Partition] = 1_500, [Bonsai] = 16_236 });
        var cheaper = new VendorPriceTable(new Dictionary<uint, long> { [Dye] = 200, [Lantern] = 3_000, [Partition] = 1_500, [Bonsai] = 16_236 });

        Assert.True(prices.SamePrices(copy));
        Assert.False(prices.SamePrices(cheaper));
        Assert.False(prices.SamePrices(VendorPriceTable.Empty));
    }

    [Fact]
    public void OneVendorSellingSeveralItemsIsOneStop()
    {
        var spots = new FakeSpots();
        var townDyes = spots.Add(new VendorSpot(10, OtherTown, Vector3.Zero, VendorZone.Town), (Dye, TownShop));
        var merchant = spots.Add(new VendorSpot(20, Mist, Vector3.One, VendorZone.Residential), (Dye, ShopB), (Lantern, ShopB), (Partition, ShopA));

        var plan = VendorRoute.Plan([Line(Dye), Line(Lantern), Line(Partition)], spots, Town, static _ => true);

        var stop = Assert.Single(plan.Stops);
        Assert.Equal(merchant, stop.SpotIndex);
        Assert.NotEqual(townDyes, stop.SpotIndex);
        Assert.Equal([ShopA, ShopB, ShopB], ShopIds(stop.Purchases));
        Assert.Empty(plan.Unreachable);
    }

    [Fact]
    public void AVendorInThisTerritoryWinsATie()
    {
        var spots = new FakeSpots();
        spots.Add(new VendorSpot(10, OtherTown, Vector3.Zero, VendorZone.Town), (Dye, TownShop));
        var here = spots.Add(new VendorSpot(11, Town, Vector3.Zero, VendorZone.Town), (Dye, TownShop));

        var plan = VendorRoute.Plan([Line(Dye)], spots, Town, static _ => true);

        Assert.Equal(here, Assert.Single(plan.Stops).SpotIndex);
    }

    [Fact]
    public void ATownVendorIsPreferredOverAResidentialOne()
    {
        var spots = new FakeSpots();
        spots.Add(new VendorSpot(20, Mist, Vector3.Zero, VendorZone.Residential), (Dye, ShopB));
        var town = spots.Add(new VendorSpot(10, OtherTown, Vector3.Zero, VendorZone.Town), (Dye, TownShop));

        var plan = VendorRoute.Plan([Line(Dye)], spots, Town, static _ => true);

        Assert.Equal(town, Assert.Single(plan.Stops).SpotIndex);
    }

    [Fact]
    public void LinesNoReachableVendorSellsAreReturned()
    {
        var spots = new FakeSpots();
        spots.Add(new VendorSpot(20, Mist, Vector3.Zero, VendorZone.Residential), (Lantern, ShopB));
        spots.Add(new VendorSpot(10, OtherTown, Vector3.Zero, VendorZone.Town), (Dye, TownShop));

        var plan = VendorRoute.Plan([Line(Dye), Line(Lantern)], spots, Town, static spot => spot.Zone != VendorZone.Residential);

        Assert.Equal(Dye, Assert.Single(Assert.Single(plan.Stops).Purchases).Line.Demand.ItemId);
        Assert.Equal(Lantern, Assert.Single(plan.Unreachable).Demand.ItemId);
    }

    [Fact]
    public void StopsSortTheCheapestTripFirst()
    {
        var spots = new FakeSpots();
        var residential = spots.Add(new VendorSpot(20, Mist, Vector3.Zero, VendorZone.Residential), (Lantern, ShopB), (Partition, ShopA));
        var here = spots.Add(new VendorSpot(11, Town, Vector3.Zero, VendorZone.Town), (Dye, TownShop));

        var plan = VendorRoute.Plan([Line(Lantern), Line(Partition), Line(Dye)], spots, Town, static _ => true);

        Assert.Equal([here, residential], SpotIndexes(plan.Stops));
    }

    [Fact]
    public void AMatchingPurseConfirmsAVendorPurchase()
    {
        var decision = VendorPurchaseDecision.Decide(new PurseReading(10_000, 2), new PurseReading(9_352, 5), 3, 648, false, 100, 200);

        Assert.Equal(PurchaseOutcomeKind.Confirmed, decision.Kind);
    }

    [Fact]
    public void AVendorPurchaseWaitsWhileTheShopIsBusy()
    {
        var decision = VendorPurchaseDecision.Decide(new PurseReading(10_000, 2), new PurseReading(10_000, 2), 3, 648, true, 0, 5_000);

        Assert.Equal(PurchaseOutcomeKind.Pending, decision.Kind);
    }

    [Fact]
    public void NothingChangingIsARefusalOnceTheShopSettles()
    {
        var before = new PurseReading(10_000, 2);

        var early = VendorPurchaseDecision.Decide(before, before, 3, 648, false, 1_000, 1_500);
        var settled = VendorPurchaseDecision.Decide(before, before, 3, 648, false, PurchaseReconciliation.SettleWindowMs, 4_000);

        Assert.Equal(PurchaseOutcomeKind.Pending, early.Kind);
        Assert.Equal(PurchaseOutcomeKind.Rejected, settled.Kind);
    }

    [Fact]
    public void SilenceWithoutASeenTransactionIsNeverARefusal()
    {
        var before = new PurseReading(10_000, 2);

        var settled = VendorPurchaseDecision.Decide(before, before, 3, 648, false, VendorPurchaseDecision.NeverBusy, 5_000);
        var late = VendorPurchaseDecision.Decide(before, before, 3, 648, false, VendorPurchaseDecision.NeverBusy, VendorPurchaseDecision.DeadlineMs);

        Assert.Equal(PurchaseOutcomeKind.Pending, settled.Kind);
        Assert.Equal(PurchaseOutcomeKind.Unknown, late.Kind);
        Assert.Equal(PurchaseUnknownCause.Timeout, late.Cause);
    }

    [Fact]
    public void ABusyShopPastTheDeadlineIsUncertain()
    {
        var before = new PurseReading(10_000, 2);

        var decision = VendorPurchaseDecision.Decide(before, before, 3, 648, true, 0, VendorPurchaseDecision.DeadlineMs);

        Assert.Equal(PurchaseUnknownCause.Timeout, decision.Cause);
    }

    [Fact]
    public void APartialChangeIsUncertainAtTheDeadline()
    {
        var before = new PurseReading(10_000, 2);
        var after = new PurseReading(9_784, 3);

        var waiting = VendorPurchaseDecision.Decide(before, after, 3, 648, false, 4_000, 5_000);
        var late = VendorPurchaseDecision.Decide(before, after, 3, 648, false, 9_000, VendorPurchaseDecision.DeadlineMs);

        Assert.Equal(PurchaseOutcomeKind.Pending, waiting.Kind);
        Assert.Equal(PurchaseOutcomeKind.Unknown, late.Kind);
        Assert.Equal(PurchaseUnknownCause.InventoryMismatch, late.Cause);
    }

    [Fact]
    public void AnUnreadablePurseIsUncertainAtTheDeadline()
    {
        var decision = VendorPurchaseDecision.Decide(PurseReading.Unreadable, new PurseReading(9_352, 5), 3, 648, false, 9_000, VendorPurchaseDecision.DeadlineMs);

        Assert.Equal(PurchaseUnknownCause.PurseUnreadable, decision.Cause);
    }

    private static uint[] ShopIds(ImmutableArray<VendorPurchase> purchases)
    {
        var shopIds = new uint[purchases.Length];
        for (var index = 0; index < purchases.Length; index++)
        {
            shopIds[index] = purchases[index].ShopId;
        }

        return shopIds;
    }

    private static int[] SpotIndexes(ImmutableArray<VendorStop> stops)
    {
        var indexes = new int[stops.Length];
        for (var index = 0; index < stops.Length; index++)
        {
            indexes[index] = stops[index].SpotIndex;
        }

        return indexes;
    }

    private static PlanOffer Offer(uint itemId, int quantity, long unitPrice, bool isHq = false)
        => new(1, 1, itemId, isHq, quantity, unitPrice, 0, $"listing:{itemId}:{quantity}:{unitPrice}:{isHq}", DateTime.UnixEpoch, false);

    private static VendorLine Line(uint itemId)
        => new(PlanDemand.ForPurchase(itemId, DemandQuality.Any, 1), 1, prices.TryGetPrice(itemId, out var price) ? price : 0);

    private sealed class FakeSpots : IVendorSpots
    {
        private readonly List<VendorSpot> spots = [];
        private readonly Dictionary<uint, List<VendorOffer>> offers = [];

        public int Add(VendorSpot spot, params (uint ItemId, uint ShopId)[] sold)
        {
            var index = spots.Count;
            spots.Add(spot);
            for (var soldIndex = 0; soldIndex < sold.Length; soldIndex++)
            {
                if (!offers.TryGetValue(sold[soldIndex].ItemId, out var list))
                {
                    list = [];
                    offers.Add(sold[soldIndex].ItemId, list);
                }

                list.Add(new VendorOffer(index, sold[soldIndex].ShopId));
            }

            return index;
        }

        public ReadOnlySpan<VendorOffer> OffersFor(uint itemId)
            => offers.TryGetValue(itemId, out var list) ? list.ToArray() : [];

        public VendorSpot SpotAt(int index) => spots[index];
    }
}
