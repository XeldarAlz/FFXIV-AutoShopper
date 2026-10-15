using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using AutoShopper.Core.Travel;
using AutoShopper.Core.Vendors.Model;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;

namespace AutoShopper.Core.Vendors;

internal sealed class VendorCatalog : IVendorSpots
{
    private const int EventKindShift = 16;
    private const uint GilShopKind = 4;
    private const uint TownUse = 0;
    private const uint FieldUse = 1;
    private const uint ResidentialUse = 13;
    private const string HousingLayoutSegment = "/hou/";
    private const string LayoutRoot = "bg/";
    private const string EventLayoutFile = "planevent.lgb";
    private const byte EventNpcLevelType = 8;
    private const float SameSpotMeters = 2f;

    private readonly object gate = new();
    private Task? loading;
    private volatile Snapshot? snapshot;

    public bool IsReady => snapshot is not null;

    public bool LoadFailed { get; private set; }

    public VendorPriceTable Prices => snapshot?.Prices ?? VendorPriceTable.Empty;

    public void EnsureLoading()
    {
        lock (gate)
        {
            loading ??= Task.Run(Load);
        }
    }

    public ReadOnlySpan<VendorOffer> OffersFor(uint itemId)
        => snapshot is { } current && current.OffersByItem.TryGetValue(itemId, out var offers) ? offers : [];

    public VendorSpot SpotAt(int index) => snapshot!.Spots[index];

    public string NpcName(int index) => snapshot!.NpcNames[index];

    public string PlaceName(int index) => snapshot!.PlaceNames[index];

    public VendorPriceTable ReachablePrices(Func<VendorSpot, bool> canReach)
    {
        if (snapshot is not { } current)
        {
            return VendorPriceTable.Empty;
        }

        var reachable = new bool[current.Spots.Length];
        for (var index = 0; index < reachable.Length; index++)
        {
            reachable[index] = canReach(current.Spots[index]);
        }

        var prices = new Dictionary<uint, long>(current.OffersByItem.Count);
        foreach (var (itemId, offers) in current.OffersByItem)
        {
            for (var index = 0; index < offers.Length; index++)
            {
                if (reachable[offers[index].SpotIndex] && current.Prices.TryGetPrice(itemId, out var price))
                {
                    prices.Add(itemId, price);
                    break;
                }
            }
        }

        return new VendorPriceTable(prices);
    }

    public Vector3[] PositionsOf(uint npcId, uint territoryId)
    {
        var spots = snapshot?.Spots ?? [];
        var positions = new List<Vector3>();
        for (var index = 0; index < spots.Length; index++)
        {
            if (spots[index].NpcId == npcId && spots[index].TerritoryId == territoryId)
            {
                positions.Add(spots[index].Position);
            }
        }

        return [.. positions];
    }

    private void Load()
    {
        try
        {
            var built = Build(Svc.Data);
            snapshot = built;
            RunLog.Info($"Vendor catalog ready: {built.Prices.Count} items sold by {built.Spots.Length} vendor spots.");
        }
        catch (Exception exception)
        {
            LoadFailed = true;
            RunLog.Error(exception, "The vendor catalog failed to load");
        }
    }

    private static Snapshot Build(IDataManager data)
    {
        var shopItems = ShopItems(data);
        var npcShops = NpcShops(data, shopItems);
        var spots = new List<VendorSpot>();
        var npcNames = new List<string>();
        var placeNames = new List<string>();
        PlaceVendors(data, npcShops, spots, npcNames, placeNames);

        var offersByItem = new Dictionary<uint, List<VendorOffer>>();
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var shops = npcShops[spots[spotIndex].NpcId];
            for (var shopIndex = 0; shopIndex < shops.Length; shopIndex++)
            {
                var items = shopItems[shops[shopIndex]];
                for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
                {
                    if (!offersByItem.TryGetValue(items[itemIndex], out var offers))
                    {
                        offers = [];
                        offersByItem.Add(items[itemIndex], offers);
                    }

                    offers.Add(new VendorOffer(spotIndex, shops[shopIndex]));
                }
            }
        }

        var itemSheet = data.GetExcelSheet<Item>();
        var prices = new Dictionary<uint, long>(offersByItem.Count);
        var frozenOffers = new Dictionary<uint, VendorOffer[]>(offersByItem.Count);
        foreach (var (itemId, offers) in offersByItem)
        {
            if (!itemSheet.TryGetRow(itemId, out var item) || item.PriceMid == 0 || item.IsUntradable || item.ItemSearchCategory.RowId == 0)
            {
                continue;
            }

            prices.Add(itemId, item.PriceMid);
            frozenOffers.Add(itemId, [.. offers]);
        }

        return new Snapshot([.. spots], [.. npcNames], [.. placeNames], frozenOffers, new VendorPriceTable(prices));
    }

    private static Dictionary<uint, uint[]> ShopItems(IDataManager data)
    {
        var shops = data.GetExcelSheet<GilShop>();
        var sheet = data.GetSubrowExcelSheet<GilShopItem>();
        var shopItems = new Dictionary<uint, uint[]>();
        var items = new List<uint>();
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var subrows = sheet.GetRowAt(rowIndex);
            if (!shops.TryGetRow(subrows.RowId, out var shop) || shop.FestivalId != 0 || shop.Quest.RowId != 0)
            {
                continue;
            }

            items.Clear();
            for (var subrowIndex = 0; subrowIndex < subrows.Count; subrowIndex++)
            {
                var entry = subrows[subrowIndex];
                if (entry.Item.RowId != 0 && !entry.IsHQ && entry.AchievementRequired.RowId == 0 && !RequiresQuest(entry))
                {
                    items.Add(entry.Item.RowId);
                }
            }

            if (items.Count > 0)
            {
                shopItems.Add(subrows.RowId, [.. items]);
            }
        }

        return shopItems;
    }

    private static bool RequiresQuest(GilShopItem entry)
    {
        for (var index = 0; index < entry.QuestRequired.Count; index++)
        {
            if (entry.QuestRequired[index].RowId != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<uint, uint[]> NpcShops(IDataManager data, Dictionary<uint, uint[]> shopItems)
    {
        var npcs = data.GetExcelSheet<ENpcBase>();
        var npcShops = new Dictionary<uint, uint[]>();
        var shops = new List<uint>();
        for (var rowIndex = 0; rowIndex < npcs.Count; rowIndex++)
        {
            var npc = npcs.GetRowAt(rowIndex);
            shops.Clear();
            for (var dataIndex = 0; dataIndex < npc.ENpcData.Count; dataIndex++)
            {
                var eventId = npc.ENpcData[dataIndex].RowId;
                if (eventId >> EventKindShift == GilShopKind && shopItems.ContainsKey(eventId) && !shops.Contains(eventId))
                {
                    shops.Add(eventId);
                }
            }

            if (shops.Count > 0)
            {
                npcShops.Add(npc.RowId, [.. shops]);
            }
        }

        return npcShops;
    }

    private static void PlaceVendors(IDataManager data, Dictionary<uint, uint[]> npcShops, List<VendorSpot> spots, List<string> npcNames, List<string> placeNames)
    {
        var residents = data.GetExcelSheet<ENpcResident>();
        var places = new Dictionary<uint, VendorPlace>();
        var territories = data.GetExcelSheet<TerritoryType>();
        for (var rowIndex = 0; rowIndex < territories.Count; rowIndex++)
        {
            var territory = territories.GetRowAt(rowIndex);
            var background = territory.Bg.ExtractText();
            if (!TryZoneOf(territory, background, out var zone))
            {
                continue;
            }

            var place = new VendorPlace(zone, territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty);
            places.Add(territory.RowId, place);
            if (ReadLayout(data, background) is { } layout)
            {
                PlaceFromLayout(layout, territory.RowId, place, npcShops, residents, spots, npcNames, placeNames);
            }
        }

        var levels = data.GetExcelSheet<Level>();
        for (var rowIndex = 0; rowIndex < levels.Count; rowIndex++)
        {
            var level = levels.GetRowAt(rowIndex);
            var npcId = level.Object.RowId;
            if (level.Type != EventNpcLevelType || !npcShops.ContainsKey(npcId) || !places.TryGetValue(level.Territory.RowId, out var place))
            {
                continue;
            }

            AddSpot(new VendorSpot(npcId, level.Territory.RowId, new Vector3(level.X, level.Y, level.Z), place.Zone), place.Name, residents, spots, npcNames, placeNames);
        }
    }

    private static void PlaceFromLayout(LgbFile layout, uint territoryId, VendorPlace place, Dictionary<uint, uint[]> npcShops, Lumina.Excel.ExcelSheet<ENpcResident> residents,
        List<VendorSpot> spots, List<string> npcNames, List<string> placeNames)
    {
        for (var layerIndex = 0; layerIndex < layout.Layers.Length; layerIndex++)
        {
            var layer = layout.Layers[layerIndex];
            if (layer.FestivalID != 0)
            {
                continue;
            }

            for (var objectIndex = 0; objectIndex < layer.InstanceObjects.Length; objectIndex++)
            {
                var instance = layer.InstanceObjects[objectIndex];
                if (instance.AssetType != LayerEntryType.EventNPC)
                {
                    continue;
                }

                var npcId = ((LayerCommon.ENPCInstanceObject)instance.Object).ParentData.ParentData.BaseId;
                if (!npcShops.ContainsKey(npcId))
                {
                    continue;
                }

                var translation = instance.Transform.Translation;
                AddSpot(new VendorSpot(npcId, territoryId, new Vector3(translation.X, translation.Y, translation.Z), place.Zone), place.Name, residents, spots, npcNames, placeNames);
            }
        }
    }

    private static void AddSpot(VendorSpot spot, string placeName, Lumina.Excel.ExcelSheet<ENpcResident> residents, List<VendorSpot> spots, List<string> npcNames, List<string> placeNames)
    {
        for (var index = 0; index < spots.Count; index++)
        {
            var known = spots[index];
            if (known.NpcId == spot.NpcId && known.TerritoryId == spot.TerritoryId && Vector3.Distance(known.Position, spot.Position) <= SameSpotMeters)
            {
                return;
            }
        }

        spots.Add(spot);
        npcNames.Add(NpcName(residents, spot.NpcId));
        placeNames.Add(placeName);
    }

    private static bool TryZoneOf(TerritoryType territory, string background, out VendorZone zone)
    {
        var use = territory.TerritoryIntendedUse.RowId;
        zone = use switch
        {
            TownUse => VendorZone.Town,
            FieldUse => VendorZone.Field,
            _ => VendorZone.Residential,
        };

        if (background.Length == 0)
        {
            return false;
        }

        if (use == ResidentialUse)
        {
            return ResidentialDistricts.IsDistrict(territory.RowId);
        }

        return use is TownUse or FieldUse && !background.Contains(HousingLayoutSegment, StringComparison.Ordinal);
    }

    private static LgbFile? ReadLayout(IDataManager data, string background)
    {
        var path = string.Concat(LayoutRoot, background.AsSpan(0, background.LastIndexOf('/') + 1), EventLayoutFile);
        try
        {
            return data.GetFile<LgbFile>(path);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IndexOutOfRangeException or System.IO.IOException)
        {
            RunLog.Debug($"Vendor catalog: the layout {path} could not be read: {exception.Message}");
            return null;
        }
    }

    private static string NpcName(Lumina.Excel.ExcelSheet<ENpcResident> residents, uint npcId)
    {
        var name = residents.TryGetRow(npcId, out var resident) ? resident.Singular.ExtractText().Trim() : string.Empty;
        return name.Length == 0 ? name : string.Concat(name[..1].ToUpper(CultureInfo.CurrentCulture), name.AsSpan(1));
    }

    private readonly record struct VendorPlace(VendorZone Zone, string Name);

    private sealed class Snapshot(VendorSpot[] spots, string[] npcNames, string[] placeNames, Dictionary<uint, VendorOffer[]> offersByItem, VendorPriceTable prices)
    {
        public readonly VendorSpot[] Spots = spots;
        public readonly string[] NpcNames = npcNames;
        public readonly string[] PlaceNames = placeNames;
        public readonly Dictionary<uint, VendorOffer[]> OffersByItem = offersByItem;
        public readonly VendorPriceTable Prices = prices;
    }
}
