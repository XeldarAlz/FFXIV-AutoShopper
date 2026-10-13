using System.Collections.Immutable;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AutoShopper.Core.Market;

internal readonly record struct MarketCheck(string Name, bool Passed, bool NeededForReading);

internal sealed class MarketCompatibilityReport
{
    public required ImmutableArray<MarketCheck> Checks { get; init; }

    public required bool ReadingAvailable { get; init; }

    public required bool ExecutionAvailable { get; init; }

    public required string Reason { get; init; }
}

// Buying needs every address and hook below; reading listings needs only the search ones. Anything missing turns
// buying off for the session, and the planner keeps working on its own.
internal static unsafe class MarketCompatibility
{
    public static nint EndRequestSlot
    {
        get
        {
            var table = InfoProxyItemSearch.StaticVirtualTablePointer;
            return table == null ? 0 : (nint)(&table->EndRequest);
        }
    }

    public static MarketCompatibilityReport Evaluate(bool requestResultHooked, bool endRequestHooked, bool purchaseResponseHooked)
    {
        ImmutableArray<MarketCheck> checks =
        [
            new("ProcessRequestResult address", InfoProxyItemSearch.Addresses.ProcessRequestResult.Value != 0, true),
            new("EndRequest virtual slot", EndRequestSlotFilled(), true),
            new("ProcessRequestResult hook", requestResultHooked, true),
            new("EndRequest hook", endRequestHooked, true),
            new("SetLastPurchasedItem address", InfoProxyItemSearch.Addresses.SetLastPurchasedItem.Value != 0, false),
            new("SendPurchaseRequestPacket address", InfoProxyItemSearch.Addresses.SendPurchaseRequestPacket.Value != 0, false),
            new("ProcessPurchaseResponse address", InfoProxyItemSearch.Addresses.ProcessPurchaseResponse.Value != 0, false),
            new("ProcessPurchaseResponse hook", purchaseResponseHooked, false),
        ];

        var reading = true;
        var execution = true;
        string? firstFailure = null;
        for (var index = 0; index < checks.Length; index++)
        {
            var check = checks[index];
            if (check.Passed)
            {
                continue;
            }

            execution = false;
            reading &= !check.NeededForReading;
            firstFailure ??= check.Name;
        }

        return new MarketCompatibilityReport
        {
            Checks = checks,
            ReadingAvailable = reading,
            ExecutionAvailable = execution,
            Reason = Describe(firstFailure, reading),
        };
    }

    private static string Describe(string? firstFailure, bool reading)
    {
        if (firstFailure is null)
        {
            return "every market address resolves and every hook is installed";
        }

        return reading
            ? $"{firstFailure} failed, so buying is off; listings can still be read"
            : $"{firstFailure} failed, so buying is off and listings cannot be read";
    }

    private static bool EndRequestSlotFilled()
    {
        var slot = EndRequestSlot;
        return slot != 0 && *(nint*)slot != 0;
    }
}
