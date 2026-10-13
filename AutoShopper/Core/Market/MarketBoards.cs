using System.Numerics;
using AutoShopper.Core.Market.Model;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AutoShopper.Core.Market;

internal readonly record struct MarketBoardSighting(ulong GameObjectId, uint BaseId, Vector3 Position, float Distance);

internal static unsafe class MarketBoards
{
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
            && MarketBoardCatalog.IsBoardBaseId(gameObject->BaseId)
            && gameObject->GetIsTargetable();
    }
}
