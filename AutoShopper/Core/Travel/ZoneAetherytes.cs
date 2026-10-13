using Dalamud.Game;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Numerics;
using MapHelper = ECommons.GameHelpers.Map;
using UIState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState;

namespace AutoShopper.Core.Travel;

internal readonly record struct ZoneAetheryte(uint Id, string Name, Vector3 Position);

internal readonly record struct ZoneGateway(uint AetheryteId, uint TerritoryId, string Name, Vector3 Position);

internal readonly record struct AethernetShortcut(ZoneAetheryte Source, ZoneAetheryte Destination, float SavedMeters);

internal static class ZoneAetherytes
{
    // TerritoryIntendedUse 1 marks a standard overworld field zone.
    private const byte StandardFieldUse = 1;
    // Interacting with a shard, picking the stop and the short load take about as long as running this far.
    internal const float AethernetOverheadMeters = 80f;

    private static readonly Dictionary<uint, ZoneAetheryte[]> byTerritory = new();
    private static readonly Dictionary<uint, uint[]> attunableIdsByTerritory = new();
    private static readonly Dictionary<uint, ZoneGateway?> gatewayByTerritory = new();
    private static readonly Dictionary<uint, AethernetNode[]> aethernetByTerritory = new();
    private static AetheryteIndex? sheetIndex;
    private static NamedAetheryte[]? namedIndex;

    // One pass over the sheet serves every territory, where a scan per territory would read the whole sheet each time.
    private static AetheryteIndex SheetIndex => sheetIndex ??= BuildSheetIndex();

    private static NamedAetheryte[] NamedIndex => namedIndex ??= BuildNamedIndex();

    private enum NameMatch : byte { Exact, Prefix, Fragment }

    // A name a player typed, in the client language or in English: exact first, then a prefix, then a fragment, each
    // accepted only when it fits a single aetheryte.
    public static bool TryFindByName(string name, out uint territoryId, out ZoneAetheryte aetheryte)
    {
        territoryId = 0;
        aetheryte = default;
        var query = NormalizeName(name);
        if (query.Length == 0)
        {
            return false;
        }

        var index = NamedIndex;
        if (!FindUnique(index, query, NameMatch.Exact, out var found)
            && !FindUnique(index, query, NameMatch.Prefix, out found)
            && !FindUnique(index, query, NameMatch.Fragment, out found))
        {
            return false;
        }

        territoryId = found.TerritoryId;
        aetheryte = found.Aetheryte;
        return true;
    }

    public static bool TryFindById(uint aetheryteId, out uint territoryId, out ZoneAetheryte aetheryte)
    {
        var index = NamedIndex;
        for (var entryIndex = 0; entryIndex < index.Length; entryIndex++)
        {
            if (index[entryIndex].Aetheryte.Id != aetheryteId)
            {
                continue;
            }

            territoryId = index[entryIndex].TerritoryId;
            aetheryte = index[entryIndex].Aetheryte;
            return true;
        }

        territoryId = 0;
        aetheryte = default;
        return false;
    }

    // Unattuned aetherytes are skipped: a teleport to one is refused.
    public static bool TryFindNearest(uint territoryId, Vector3 target, out ZoneAetheryte nearest)
    {
        var candidates = InTerritory(territoryId);
        nearest = default;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!IsAttuned(candidate.Id))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(candidate.Position, target);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            nearest = candidate;
        }

        return bestDistance < float.MaxValue;
    }

    public static ReadOnlyMemory<ZoneAetheryte> TeleportableIn(uint territoryId) => InTerritory(territoryId);

    // A question about ids alone: InTerritory drops rows whose position will not resolve, which would read back as a zone with none.
    public static uint[] AttunableIdsIn(uint territoryId)
    {
        if (attunableIdsByTerritory.TryGetValue(territoryId, out var cached))
        {
            return cached;
        }

        var resolved = ResolveAttunableIds(territoryId);
        attunableIdsByTerritory[territoryId] = resolved;
        return resolved;
    }

    // Answers only for a territory owning no attunable aetheryte. A field zone goes through the aetheryte the game names
    // for it (The Dravanian Hinterlands through Idyllshire), a city district through the aetheryte its shards belong to
    // (Limsa Lominsa Upper Decks through the Lower Decks). Zones owning one keep their own rows, because a border shard
    // sitting in an overworld zone resolves to the neighbouring city and would park the run there.
    public static bool TryFindGateway(uint territoryId, out ZoneGateway gateway)
    {
        if (!gatewayByTerritory.TryGetValue(territoryId, out var cached))
        {
            cached = ResolveGateway(territoryId);
            gatewayByTerritory[territoryId] = cached;
        }

        gateway = cached ?? default;
        return cached is not null;
    }

    // Where an aethernet ride into the territory should stop: the node nearest the destination that has a shard to stand
    // at, because an invisible stop such as an airship landing can sit behind a lift the navmesh cannot use. A territory
    // with only invisible stops (the Hinterlands gates) falls back to the nearest of those.
    public static bool TryFindEntryNode(uint territoryId, Vector3 destination, out ZoneAetheryte entry)
    {
        var nodes = AethernetIn(territoryId);
        var index = ClosestNode(nodes, destination, usableOnly: true);
        if (index < 0)
        {
            index = ClosestNode(nodes, destination, usableOnly: false);
        }

        entry = index < 0 ? default : nodes[index].Stop;
        return index >= 0;
    }

    // Any aetheryte or shard of the territory by its row, invisible stops included.
    public static bool TryFindNode(uint territoryId, uint nodeId, out ZoneAetheryte node)
    {
        var nodes = AethernetIn(territoryId);
        for (var index = 0; index < nodes.Length; index++)
        {
            if (nodes[index].Stop.Id == nodeId)
            {
                node = nodes[index].Stop;
                return true;
            }
        }

        node = default;
        return false;
    }

    // Only a visible stop the character has attuned can be boarded at or ridden to.
    public static bool TryFindNearestUsableNode(uint territoryId, Vector3 position, out ZoneAetheryte node)
    {
        var nodes = AethernetIn(territoryId);
        var index = ClosestNode(nodes, position, usableOnly: true);
        node = index < 0 ? default : nodes[index].Stop;
        return index >= 0;
    }

    // The library boards at the node nearest the character, so that is the source measured here. The ride pays off only
    // when walking to it, plus walking from the best stop to the destination, beats walking straight there.
    public static bool TryFindAethernetShortcut(uint territoryId, Vector3 from, Vector3 to, out AethernetShortcut shortcut)
    {
        shortcut = default;
        var nodes = AethernetIn(territoryId);
        var sourceIndex = ClosestNode(nodes, from, usableOnly: false);
        if (sourceIndex < 0 || nodes[sourceIndex].PrimaryId == 0 || !IsUsable(nodes[sourceIndex]))
        {
            return false;
        }

        var source = nodes[sourceIndex];
        var destinationIndex = -1;
        var bestWalk = float.MaxValue;
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (index == sourceIndex || node.PrimaryId != source.PrimaryId || !IsUsable(node))
            {
                continue;
            }

            var walk = GroundDistance.Between(node.Stop.Position, to);
            if (walk >= bestWalk)
            {
                continue;
            }

            bestWalk = walk;
            destinationIndex = index;
        }

        if (destinationIndex < 0)
        {
            return false;
        }

        var direct = GroundDistance.Between(from, to);
        var viaAethernet = GroundDistance.Between(from, source.Stop.Position) + bestWalk + AethernetOverheadMeters;
        if (viaAethernet >= direct)
        {
            return false;
        }

        shortcut = new AethernetShortcut(source.Stop, nodes[destinationIndex].Stop, direct - viaAethernet);
        return true;
    }

    public static unsafe bool IsAttuned(uint aetheryteId)
    {
        var uiState = UIState.Instance();
        return uiState is not null && uiState->IsAetheryteUnlocked(aetheryteId);
    }

    private static bool IsUsable(AethernetNode node) => node.Visible && IsAttuned(node.Stop.Id);

    private static int ClosestNode(AethernetNode[] nodes, Vector3 position, bool usableOnly)
    {
        var closest = -1;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < nodes.Length; index++)
        {
            if (usableOnly && !IsUsable(nodes[index]))
            {
                continue;
            }

            var distance = GroundDistance.SquaredBetween(nodes[index].Stop.Position, position);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            closest = index;
        }

        return closest;
    }

    private static ZoneAetheryte[] InTerritory(uint territoryId)
    {
        if (byTerritory.TryGetValue(territoryId, out var cached))
        {
            return cached;
        }

        var resolved = ResolveTeleportableAetherytes(territoryId);
        byTerritory[territoryId] = resolved;
        return resolved;
    }

    private static AethernetNode[] AethernetIn(uint territoryId)
    {
        if (aethernetByTerritory.TryGetValue(territoryId, out var cached))
        {
            return cached;
        }

        var resolved = ResolveAethernet(territoryId);
        aethernetByTerritory[territoryId] = resolved;
        return resolved;
    }

    private static ZoneGateway? ResolveGateway(uint territoryId)
    {
        if (AttunableIdsIn(territoryId).Length > 0)
        {
            return null;
        }

        if (Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId) is not { } territory)
        {
            return null;
        }

        var gatewayId = territory.TerritoryIntendedUse.RowId == StandardFieldUse
            ? territory.Aetheryte.RowId
            : DistrictAetheryte(territoryId);
        if (gatewayId == 0)
        {
            return null;
        }

        if (Svc.Data.GetExcelSheet<Aetheryte>().GetRowOrDefault(gatewayId) is not { IsAetheryte: true } gatewayRow)
        {
            return null;
        }

        var gatewayTerritoryId = gatewayRow.Territory.RowId;
        if (gatewayTerritoryId == 0 || gatewayTerritoryId == territoryId || !TryResolvePosition(gatewayRow, out var position))
        {
            return null;
        }

        return new ZoneGateway(gatewayId, gatewayTerritoryId, ResolveName(gatewayRow), position);
    }

    private static uint DistrictAetheryte(uint territoryId)
    {
        var nodes = AethernetIn(territoryId);
        for (var index = 0; index < nodes.Length; index++)
        {
            if (nodes[index].Visible && nodes[index].PrimaryId != 0)
            {
                return nodes[index].PrimaryId;
            }
        }

        return 0;
    }

    private static uint[] ResolveAttunableIds(uint territoryId)
    {
        var rows = RowsIn(territoryId);
        var found = new List<uint>(rows.Length);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            if (rows[rowIndex].IsAetheryte)
            {
                found.Add(rows[rowIndex].RowId);
            }
        }

        return found.ToArray();
    }

    private static ZoneAetheryte[] ResolveTeleportableAetherytes(uint territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
        var ids = AttunableIdsIn(territoryId);
        var found = new List<ZoneAetheryte>(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            if (sheet.GetRowOrDefault(ids[index]) is not { } row || !TryResolvePosition(row, out var position))
            {
                continue;
            }

            found.Add(new ZoneAetheryte(row.RowId, ResolveName(row), position));
        }

        return found.ToArray();
    }

    // Every aetheryte and shard in the territory, invisible stops included, so the nearest node matches the library's own pick.
    private static AethernetNode[] ResolveAethernet(uint territoryId)
    {
        var rows = RowsIn(territoryId);
        var found = new List<AethernetNode>(rows.Length);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            if (!TryResolvePosition(row, out var position))
            {
                continue;
            }

            found.Add(new AethernetNode(new ZoneAetheryte(row.RowId, ResolveName(row), position), PrimaryOf(row), !row.Invisible));
        }

        return found.ToArray();
    }

    // Same rule as the library: an aetheryte is its own primary, a shard belongs to the first aetheryte of its group.
    private static uint PrimaryOf(Aetheryte row)
    {
        if (row.IsAetheryte)
        {
            return row.RowId;
        }

        return SheetIndex.PrimaryByGroup.TryGetValue(row.AethernetGroup, out var primary) ? primary : 0;
    }

    private static ReadOnlySpan<Aetheryte> RowsIn(uint territoryId)
        => SheetIndex.RowsByTerritory.TryGetValue(territoryId, out var rows) ? rows : [];

    private static AetheryteIndex BuildSheetIndex()
    {
        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
        var grouped = new Dictionary<uint, List<Aetheryte>>();
        var primaries = new Dictionary<byte, uint>();
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            if (row.IsAetheryte)
            {
                primaries.TryAdd(row.AethernetGroup, row.RowId);
            }

            if (!grouped.TryGetValue(row.Territory.RowId, out var rows))
            {
                rows = [];
                grouped[row.Territory.RowId] = rows;
            }

            rows.Add(row);
        }

        var rowsByTerritory = new Dictionary<uint, Aetheryte[]>(grouped.Count);
        foreach (var (territoryId, rows) in grouped)
        {
            rowsByTerritory[territoryId] = [.. rows];
        }

        return new AetheryteIndex(rowsByTerritory, primaries);
    }

    private static bool FindUnique(NamedAetheryte[] index, string query, NameMatch match, out NamedAetheryte found)
    {
        found = default;
        var matches = 0;
        for (var entryIndex = 0; entryIndex < index.Length; entryIndex++)
        {
            var entry = index[entryIndex];
            if (!Matches(entry.ClientKey, query, match) && !Matches(entry.EnglishKey, query, match))
            {
                continue;
            }

            matches++;
            if (matches > 1)
            {
                return false;
            }

            found = entry;
        }

        return matches == 1;
    }

    private static bool Matches(string candidate, string query, NameMatch match) => match switch
    {
        NameMatch.Exact => string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase),
        NameMatch.Prefix => candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase),
        _ => candidate.Contains(query, StringComparison.OrdinalIgnoreCase),
    };

    // Game names carry the typographic apostrophe; a keyboard types the straight one.
    private static string NormalizeName(string name) => name.Trim().Replace('’', '\'');

    private static NamedAetheryte[] BuildNamedIndex()
    {
        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
        var english = EnglishAetheryteSheet();
        var found = new List<NamedAetheryte>(sheet.Count);
        for (var rowIndex = 0; rowIndex < sheet.Count; rowIndex++)
        {
            var row = sheet.GetRowAt(rowIndex);
            if (!row.IsAetheryte || row.Territory.RowId == 0)
            {
                continue;
            }

            var clientName = row.PlaceName.ValueNullable?.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(clientName) || !TryResolvePosition(row, out var position))
            {
                continue;
            }

            var englishName = english?.GetRowOrDefault(row.RowId)?.PlaceName.ValueNullable?.Name.ExtractText();
            var stop = new ZoneAetheryte(row.RowId, clientName, position);
            found.Add(new NamedAetheryte(row.Territory.RowId, stop, NormalizeName(clientName), NormalizeName(string.IsNullOrWhiteSpace(englishName) ? clientName : englishName)));
        }

        return found.ToArray();
    }

    // Null on an English client, where the client names already are the English ones, and on a client without English data.
    private static ExcelSheet<Aetheryte>? EnglishAetheryteSheet()
    {
        if (Svc.ClientState.ClientLanguage == ClientLanguage.English)
        {
            return null;
        }

        try
        {
            return Svc.Data.GetExcelSheet<Aetheryte>(ClientLanguage.English);
        }
        catch (Exception exception)
        {
            RunLog.Debug($"English aetheryte names are unavailable on this client; matching client names only ({exception.Message})");
            return null;
        }
    }

    private static string ResolveName(Aetheryte row)
    {
        var placeName = row.IsAetheryte ? row.PlaceName : row.AethernetName;
        var name = placeName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? $"aetheryte #{row.RowId}" : name;
    }

    private static bool TryResolvePosition(Aetheryte row, out Vector3 position)
    {
        try
        {
            position = MapHelper.AetherytePosition(row);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Could not resolve a position for aetheryte {row.RowId}; skipping it as a travel target");
            position = default;
            return false;
        }

        return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }

    private readonly record struct AethernetNode(ZoneAetheryte Stop, uint PrimaryId, bool Visible);

    private readonly record struct NamedAetheryte(uint TerritoryId, ZoneAetheryte Aetheryte, string ClientKey, string EnglishKey);

    private sealed record AetheryteIndex(Dictionary<uint, Aetheryte[]> RowsByTerritory, Dictionary<byte, uint> PrimaryByGroup);
}
