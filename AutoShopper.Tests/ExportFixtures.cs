using System.Collections.Immutable;
using System.Globalization;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Exports;
using AutoShopper.Core.Planning;

namespace AutoShopper.Tests;

internal static class ExportFixtures
{
    public const uint Ore = 5_000;
    public const uint Cloth = 6_000;
    public const uint Tincture = 7_000;
    public const uint Shard = 2;

    // Values no gil amount, quantity or sequence in the fixtures can produce, so finding one in an export is a leak.
    public const ulong SecretCharacter = 18_014_398_509_482_011;
    public const ulong SecretRetainer = 33_445_566_778_899;
    public const ulong SecretListing = 9_876_543_210_123_456;
    public const ulong OtherSecretListing = 9_876_543_210_654_321;
    public const ulong ThirdSecretListing = 9_876_543_210_777_777;
    public const string CharacterName = "Wol Adventurer";
    public const string RetainerName = "Retainerina Sellsalot";
    public const char EmDash = '\u2014';

    public static readonly Guid RunId = new("6f1c2a4e-0d5b-4a8e-9a37-2f6a1b3c4d5e");
    public static readonly Guid FirstAttempt = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid SecondAttempt = new("bbbbbbbb-0000-0000-0000-000000000002");
    public static readonly Guid ThirdAttempt = new("cccccccc-0000-0000-0000-000000000003");
    public static readonly Guid FourthAttempt = new("dddddddd-0000-0000-0000-000000000004");
    public static readonly DateTime StartedAt = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime EndedAt = new(2026, 10, 13, 12, 40, 0, DateTimeKind.Utc);

    public static readonly PlanDemand OreDemand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10, 2, 1_100);
    public static readonly PlanDemand ClothDemand = PlanDemand.ForPurchase(Cloth, DemandQuality.NormalQuality, 4);
    public static readonly PlanDemand TinctureDemand = PlanDemand.ForPurchase(Tincture, DemandQuality.HighQuality, 2);

    public static readonly string[] ForbiddenFragments =
    [
        Decimal(SecretCharacter),
        Hexadecimal(SecretCharacter),
        Decimal(SecretRetainer),
        Hexadecimal(SecretRetainer),
        Decimal(SecretListing),
        Hexadecimal(SecretListing),
        Decimal(OtherSecretListing),
        Hexadecimal(OtherSecretListing),
        Decimal(ThirdSecretListing),
        Hexadecimal(ThirdSecretListing),
        CharacterName,
        RetainerName,
    ];

    public static FakeExportNames Names() => new FakeExportNames()
        .Item(Ore, "Iron Ore")
        .Item(Cloth, "Linen Cloth")
        .Item(Tincture, "Tincture")
        .Item(Shard, "Wind Shard")
        .World(Fixtures.Alpha.WorldId, "Alpha")
        .World(Fixtures.Beta.WorldId, "Beta")
        .World(Fixtures.Delta.WorldId, "Delta")
        .DataCenter(Fixtures.PrimaryDataCenter, "Light")
        .DataCenter(Fixtures.SecondaryDataCenter, "Chaos");

    public static RunAuthorization Authorization()
    {
        var problem = RunAuthorization.TryCreate(
            RunId,
            SecretCharacter,
            Fixtures.Alpha.WorldId,
            [TinctureDemand, ClothDemand, OreDemand],
            [Fixtures.Alpha.WorldId, Fixtures.Beta.WorldId],
            100_000,
            10_000,
            1,
            StartedAt,
            out var authorization);
        Assert.Equal(AuthorizationProblem.None, problem);
        return authorization!;
    }

    // Two ore stacks bought (two over the need), a cloth stack rejected, a second cloth stack unanswered, tinctures untouched.
    public static SummaryJournal MixedRun()
    {
        return new SummaryJournal(Authorization())
            .Intent(FirstAttempt, OreDemand.Key, true, 5, 5_050, Fixtures.Alpha.WorldId, SecretListing)
            .Submitted(FirstAttempt)
            .Confirmed(FirstAttempt, 5, 5_050, $"{CharacterName} bought from {RetainerName}")
            .Intent(SecondAttempt, OreDemand.Key, false, 7, 7_070, Fixtures.Beta.WorldId, OtherSecretListing)
            .Submitted(SecondAttempt)
            .Confirmed(SecondAttempt, 7, 7_070, $"listing {Decimal(OtherSecretListing)}")
            .Intent(ThirdAttempt, ClothDemand.Key, false, 4, 4_040, Fixtures.Beta.WorldId, ThirdSecretListing)
            .Submitted(ThirdAttempt)
            .Rejected(ThirdAttempt, 4_523, $"{RetainerName} changed the listing")
            .Intent(FourthAttempt, ClothDemand.Key, false, 4, 4_040, Fixtures.Alpha.WorldId, ThirdSecretListing)
            .Submitted(FourthAttempt)
            .Unknown(FourthAttempt, $"timeout for {CharacterName}")
            .Checkpoint([ListingKeys.ForGameListing(ThirdSecretListing)])
            .Ended(RunState.CompletedPartial, $"stopped by {CharacterName}", EndedAt);
    }

    public static string Decimal(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Hexadecimal(ulong value) => value.ToString("x", CultureInfo.InvariantCulture);

    public static void AssertNoIdentifiers(string output)
    {
        for (var index = 0; index < ForbiddenFragments.Length; index++)
        {
            Assert.DoesNotContain(ForbiddenFragments[index], output, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string[] Lines(string output) => output.Split('\n');
}

internal sealed class FakeExportNames : IExportNames
{
    private readonly Dictionary<uint, string> items = [];
    private readonly Dictionary<uint, string> worlds = [];
    private readonly Dictionary<uint, string> dataCenters = [];

    public FakeExportNames Item(uint itemId, string name)
    {
        items[itemId] = name;
        return this;
    }

    public FakeExportNames World(uint worldId, string name)
    {
        worlds[worldId] = name;
        return this;
    }

    public FakeExportNames DataCenter(uint dataCenterId, string name)
    {
        dataCenters[dataCenterId] = name;
        return this;
    }

    public bool TryGetName(uint itemId, out string name) => Find(items, itemId, out name);

    public bool TryGetWorldName(uint worldId, out string name) => Find(worlds, worldId, out name);

    public bool TryGetDataCenterName(uint dataCenterId, out string name) => Find(dataCenters, dataCenterId, out name);

    private static bool Find(Dictionary<uint, string> names, uint id, out string name)
    {
        if (names.TryGetValue(id, out var found))
        {
            name = found;
            return true;
        }

        name = string.Empty;
        return false;
    }
}

internal sealed class SummaryJournal
{
    private readonly List<JournalEvent?> entries = new();
    private long sequence;
    private DateTime clock;

    public SummaryJournal(RunAuthorization authorization)
    {
        clock = authorization.CreatedAtUtc;
        entries.Add(authorization.ToJournalEvent(NextSequence(), clock));
    }

    public IReadOnlyList<JournalEvent?> Entries => entries;

    public JournalProgress Replay() => JournalReplayer.Replay(entries);

    public SummaryJournal Intent(Guid attemptId, DemandKey demand, bool isHq, int quantity, long payable, uint worldId, ulong listingId)
    {
        entries.Add(new IntentRecorded
        {
            Sequence = NextSequence(),
            RunId = ExportFixtures.RunId,
            AtUtc = Tick(),
            AttemptId = attemptId,
            ItemId = demand.ItemId,
            Quality = demand.Quality,
            ListingId = listingId,
            RetainerId = ExportFixtures.SecretRetainer,
            WorldId = worldId,
            IsHq = isHq,
            Quantity = quantity,
            UnitPrice = payable / quantity,
            TotalTax = payable % quantity,
            Payable = payable,
            SearchGeneration = 1,
            PlanVersion = 1,
        });
        return this;
    }

    public SummaryJournal Submitted(Guid attemptId, bool sent = true)
    {
        entries.Add(new PurchaseSubmitted { Sequence = NextSequence(), RunId = ExportFixtures.RunId, AtUtc = Tick(), AttemptId = attemptId, Sent = sent });
        return this;
    }

    public SummaryJournal Confirmed(Guid attemptId, int quantity, long payable, string evidence)
    {
        entries.Add(new PurchaseConfirmed
        {
            Sequence = NextSequence(),
            RunId = ExportFixtures.RunId,
            AtUtc = Tick(),
            AttemptId = attemptId,
            Quantity = quantity,
            Payable = payable,
            Evidence = evidence,
        });
        return this;
    }

    public SummaryJournal Rejected(Guid attemptId, int reasonCode, string reason)
    {
        entries.Add(new PurchaseRejected { Sequence = NextSequence(), RunId = ExportFixtures.RunId, AtUtc = Tick(), AttemptId = attemptId, ReasonCode = reasonCode, Reason = reason });
        return this;
    }

    public SummaryJournal Unknown(Guid attemptId, string reason)
    {
        entries.Add(new PurchaseUnknown { Sequence = NextSequence(), RunId = ExportFixtures.RunId, AtUtc = Tick(), AttemptId = attemptId, Reason = reason });
        return this;
    }

    public SummaryJournal Reconciled(Guid attemptId, bool purchased, int quantity, long payable, string note)
    {
        entries.Add(new Reconciled
        {
            Sequence = NextSequence(),
            RunId = ExportFixtures.RunId,
            AtUtc = Tick(),
            AttemptId = attemptId,
            Purchased = purchased,
            Quantity = quantity,
            Payable = payable,
            Note = note,
        });
        return this;
    }

    public SummaryJournal Checkpoint(ImmutableArray<string> excludedListingKeys)
    {
        entries.Add(new Checkpoint
        {
            Sequence = NextSequence(),
            RunId = ExportFixtures.RunId,
            AtUtc = Tick(),
            State = RunState.Searching,
            PlanVersion = 1,
            AttemptCount = 0,
            FailedAttemptCount = 0,
            ExcludedListingKeys = excludedListingKeys,
            CompletedWorldIds = [],
        });
        return this;
    }

    public SummaryJournal Ended(RunState finalState, string reason, DateTime atUtc)
    {
        entries.Add(new RunEnded { Sequence = NextSequence(), RunId = ExportFixtures.RunId, AtUtc = atUtc, FinalState = finalState, Reason = reason });
        return this;
    }

    private long NextSequence()
    {
        sequence++;
        return sequence;
    }

    private DateTime Tick()
    {
        clock = clock.AddMinutes(1);
        return clock;
    }
}

internal sealed class InvariantCultureCheck : IDisposable
{
    private readonly CultureInfo previous;

    // German groups thousands with dots, so an export that leaked the current culture would show 12.120 instead of 12,120.
    public InvariantCultureCheck()
    {
        previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = previous;
    }
}
