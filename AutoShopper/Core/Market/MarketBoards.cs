using System.Collections.Immutable;
using System.Numerics;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AutoShopper.Core.Market;

// A city with market boards. The teleport aetheryte may stand in a neighbouring territory (Gridania, Ul'dah,
// Ishgard); the approach node is the aethernet shard or aetheryte closest to a board, with that straight-line distance.
internal readonly record struct MarketBoardCity(
    string Name,
    uint TerritoryId,
    uint BoardBaseId,
    uint TeleportAetheryteId,
    uint TeleportTerritoryId,
    uint ApproachNodeId,
    float ApproachDistance,
    ImmutableArray<Vector3> BoardPositions);

internal readonly record struct MarketBoardSighting(ulong GameObjectId, uint BaseId, Vector3 Position, float Distance);

// Board positions are read from the territory layouts of game data 2026.09.15 and are not yet checked in game.
internal static unsafe class MarketBoards
{
    // EObj rows named "market board" that are placed in the world; the Ul'dah and Kugane boards and the Crystarium
    // ones use their own rows.
    public const uint CommonBoardBaseId = 2_000_402;
    public const uint UldahKuganeBoardBaseId = 2_000_442;
    public const uint CrystariumBoardBaseId = 2_010_285;

    public static readonly ImmutableArray<MarketBoardCity> Cities =
    [
        new("Limsa Lominsa Lower Decks", 129, CommonBoardBaseId, 8, 129, 49, 10.2f,
        [
            new(-119.0f, 18.0f, 21.0f), new(-123.4f, 18.0f, 10.1f), new(-223.8f, 16.0f, 51.5f),
            new(-167.0f, 18.0f, 28.5f), new(-270.9f, 16.0f, 51.8f), new(-270.9f, 16.0f, 39.9f),
        ]),
        new("Old Gridania", 133, CommonBoardBaseId, 2, 132, 26, 55.7f,
        [
            new(159.1f, 15.6f, -100.8f), new(159.4f, 15.6f, -91.2f), new(150.9f, 15.8f, -56.5f),
            new(162.1f, 15.8f, -52.3f), new(162.1f, 15.8f, -139.6f), new(150.9f, 15.8f, -135.7f),
        ]),
        new("Ul'dah - Steps of Thal", 131, UldahKuganeBoardBaseId, 9, 130, 125, 17.4f,
        [
            new(149.1f, 4.0f, -32.5f), new(145.4f, 4.0f, -46.4f), new(156.3f, 4.1f, 9.4f),
            new(105.7f, 4.2f, -80.9f), new(125.8f, 4.1f, -95.1f),
        ]),
        new("Ishgard - The Pillars", 419, CommonBoardBaseId, 70, 418, 84, 32.2f, [new(-152.8f, -12.5f, -42.0f)]),
        new("Kugane", 628, UldahKuganeBoardBaseId, 111, 628, 115, 35.1f, [new(2.5f, 4.0f, 48.0f)]),
        new("The Crystarium", 819, CrystariumBoardBaseId, 133, 819, 149, 46.7f, [new(-49.8f, -7.5f, 132.2f), new(-53.6f, -7.7f, 140.6f)]),
        new("Old Sharlayan", 962, CommonBoardBaseId, 182, 962, 182, 40.6f, [new(19.8f, 2.4f, -35.6f)]),
        new("Tuliyollal", 1185, CommonBoardBaseId, 216, 1185, 221, 18.4f, [new(2.9f, -14.0f, 135.8f)]),
    ];

    public static bool IsBoardBaseId(uint baseId)
        => baseId is CommonBoardBaseId or UldahKuganeBoardBaseId or CrystariumBoardBaseId;

    public static bool TryFindCity(uint territoryId, out MarketBoardCity city)
    {
        for (var index = 0; index < Cities.Length; index++)
        {
            if (Cities[index].TerritoryId == territoryId)
            {
                city = Cities[index];
                return true;
            }
        }

        city = default;
        return false;
    }

    // The nearest targetable market board in the object table, at any distance; the caller judges the reach.
    public static bool TryFindNearestBoard(out MarketBoardSighting sighting)
    {
        sighting = default;
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var origin = player.Position;
        var found = false;
        var bestDistance = float.MaxValue;
        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            var gameObject = (GameObject*)objects.GetObjectAddress(index);
            if (!IsTargetableBoard(gameObject))
            {
                continue;
            }

            Vector3 position = gameObject->Position;
            var distance = Vector3.Distance(origin, position);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            sighting = new MarketBoardSighting(gameObject->GetGameObjectId(), gameObject->BaseId, position, distance);
            found = true;
        }

        return found;
    }

    private static bool IsTargetableBoard(GameObject* gameObject)
    {
        return gameObject != null
            && gameObject->ObjectKind == ObjectKind.EventObj
            && IsBoardBaseId(gameObject->BaseId)
            && gameObject->GetIsTargetable();
    }
}
