using System.Collections.Immutable;
using System.Text.Json;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Exports;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.ExportFixtures;

namespace AutoShopper.Tests;

public sealed class RunSummaryExporterTests
{
    private const string Salt = "export-test-salt";

    [Fact]
    public void HeaderShowsShortRunIdTimesAndOutcome()
    {
        var text = Lines(RunSummaryExporter.ToText(Summarize(MixedRun())));

        Assert.Equal("Auto Shopper run summary", text[0]);
        Assert.Equal("Run 6f1c2a4e", text[1]);
        Assert.Equal("Started: 2026-10-13 12:00 UTC", text[2]);
        Assert.Equal("Ended: 2026-10-13 12:40 UTC", text[3]);
        Assert.Equal("Outcome: Needs reconciliation", text[4]);
    }

    [Fact]
    public void ConfirmedPurchasesAndSpendAgainstTheCapUseInvariantThousands()
    {
        string output;
        using (new InvariantCultureCheck())
        {
            output = RunSummaryExporter.ToText(Summarize(MixedRun()));
        }

        var text = Lines(output);
        Assert.Contains("  Confirmed: 12,120 gil of a 100,000 gil cap", text);
        Assert.Contains("  Awaiting reconciliation: 4,040 gil", text);
        Assert.Contains("  Left under the cap: 83,840 gil", text);
        var bought = Array.IndexOf(text, "Bought (2)");
        Assert.Equal("  5 x Iron Ore (HQ) for 5,050 gil on Alpha", text[bought + 1]);
        Assert.Equal("  7 x Iron Ore for 7,070 gil on Beta", text[bought + 2]);
    }

    [Fact]
    public void ExcessUnitsAndUnresolvedDemandCarryReasons()
    {
        var summary = Summarize(MixedRun());
        var text = Lines(RunSummaryExporter.ToText(summary));

        Assert.Equal(2, summary.ExcessQuantity);
        var excess = Array.IndexOf(text, "Extra units bought (2)");
        Assert.Equal("  Iron Ore: 2", text[excess + 1]);
        var needed = Array.IndexOf(text, "Still needed (2)");
        Assert.Equal("  Linen Cloth (NQ): 4 of 4 missing, a purchase result is unknown", text[needed + 1]);
        Assert.Equal("  Tincture (HQ): 2 of 2 missing, no purchase was made", text[needed + 2]);
        Assert.Equal(ShortfallReason.AwaitingReconciliation, summary.Unresolved[0].Reason);
        Assert.Equal(ShortfallReason.NotAttempted, summary.Unresolved[1].Reason);
    }

    [Fact]
    public void RejectedAndUnknownAttemptsAreListedWithReasonCodes()
    {
        var text = Lines(RunSummaryExporter.ToText(Summarize(MixedRun())));

        var rejected = Array.IndexOf(text, "Rejected (1)");
        Assert.Equal("  4 x Linen Cloth for 4,040 gil on Beta, reason code 4523", text[rejected + 1]);
        var unknown = Array.IndexOf(text, "Unknown result, reconcile before buying more (1)");
        Assert.Equal("  4 x Linen Cloth for 4,040 gil on Alpha", text[unknown + 1]);
    }

    [Fact]
    public void ReconciledAttemptsAreMarked()
    {
        var journal = MixedRun().Reconciled(FourthAttempt, true, 4, 4_040, $"{CharacterName} checked the sale history");

        var summary = Summarize(journal);
        var text = Lines(RunSummaryExporter.ToText(summary));

        Assert.Equal(RunState.CompletedPartial, summary.Outcome);
        Assert.Contains("  4 x Linen Cloth for 4,040 gil on Alpha, settled by reconciliation", text);
        Assert.Contains("Still needed (1)", text);
        Assert.Empty(summary.Unknown);
    }

    [Fact]
    public void UnsentPurchasesAreListedAsNotSent()
    {
        var journal = new SummaryJournal(Authorization())
            .Intent(FirstAttempt, OreDemand.Key, false, 5, 5_050, Fixtures.Alpha.WorldId, SecretListing)
            .Submitted(FirstAttempt, sent: false)
            .Ended(RunState.CompletedPartial, string.Empty, EndedAt);

        var text = Lines(RunSummaryExporter.ToText(Summarize(journal)));

        Assert.Contains("Bought: nothing", text);
        var notSent = Array.IndexOf(text, "Not sent (1)");
        Assert.Equal("  5 x Iron Ore for 5,050 gil on Alpha", text[notSent + 1]);
        Assert.Contains("  Iron Ore: 10 of 10 missing, purchases were rejected or not sent", text);
    }

    [Fact]
    public void NoExportCarriesCharacterRetainerSellerOrListingIdentifiers()
    {
        var journal = MixedRun();
        var standard = Summarize(journal);
        var withDiagnostics = Summarize(journal, RunSummaryOptions.WithDiagnostics(Salt));
        var randomSalt = Summarize(journal, RunSummaryOptions.WithDiagnostics());

        string[] outputs =
        [
            RunSummaryExporter.ToText(standard),
            RunSummaryExporter.ToJson(standard),
            RunSummaryExporter.ToText(withDiagnostics),
            RunSummaryExporter.ToJson(withDiagnostics),
            RunSummaryExporter.ToText(randomSalt),
            RunSummaryExporter.ToJson(randomSalt),
        ];
        for (var index = 0; index < outputs.Length; index++)
        {
            AssertNoIdentifiers(outputs[index]);
            Assert.DoesNotContain(RunId.ToString(), outputs[index], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(FirstAttempt.ToString(), outputs[index], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("game:", outputs[index], StringComparison.Ordinal);
            Assert.DoesNotContain(EmDash, outputs[index]);
        }
    }

    [Fact]
    public void DiagnosticsAreOffByDefault()
    {
        var summary = Summarize(MixedRun());

        Assert.Null(summary.Diagnostics);
        Assert.DoesNotContain("Diagnostics", RunSummaryExporter.ToText(summary), StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostics", RunSummaryExporter.ToJson(summary), StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsCorrelateListingsOnlyThroughASaltedShortHash()
    {
        var journal = MixedRun();

        var diagnostics = Summarize(journal, RunSummaryOptions.WithDiagnostics(Salt)).Diagnostics!;
        var resalted = Summarize(journal, RunSummaryOptions.WithDiagnostics("another-salt")).Diagnostics!;

        Assert.Equal(4, diagnostics.Attempts.Length);
        Assert.Equal(1, diagnostics.ExcludedListingCount);
        var first = diagnostics.Attempts[0].Listing;
        Assert.Matches("^[0-9a-f]{8}$", first);
        Assert.NotEqual(first, diagnostics.Attempts[1].Listing);
        Assert.Equal(diagnostics.Attempts[2].Listing, diagnostics.Attempts[3].Listing);
        Assert.NotEqual(first, resalted.Attempts[0].Listing);
        Assert.Equal("aaaaaaaa", diagnostics.Attempts[0].Attempt);
        Assert.Contains($"  Attempt aaaaaaaa: Confirmed, listing {first}, world 1, sequence 2", Lines(RunSummaryExporter.ToText(Summarize(journal, RunSummaryOptions.WithDiagnostics(Salt)))));
    }

    [Fact]
    public void JsonRoundTripKeepsTheSummaryShape()
    {
        var summary = Summarize(MixedRun(), RunSummaryOptions.WithDiagnostics(Salt));

        var json = RunSummaryExporter.ToJson(summary);
        var restored = JsonSerializer.Deserialize<RunSummary>(json, RunSummaryExporter.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(json, RunSummaryExporter.ToJson(restored));
        Assert.Equal(RunSummary.FormatName, restored.Format);
        Assert.Equal(RunSummary.CurrentVersion, restored.Version);
        Assert.Equal(summary.RunId, restored.RunId);
        Assert.Equal(summary.StartedAtUtc, restored.StartedAtUtc);
        Assert.Equal(summary.EndedAtUtc, restored.EndedAtUtc);
        Assert.Equal(summary.Outcome, restored.Outcome);
        Assert.Equal(summary.Spend, restored.Spend);
        AssertSameItems(summary.Purchases, restored.Purchases);
        AssertSameItems(summary.Excess, restored.Excess);
        AssertSameItems(summary.Unresolved, restored.Unresolved);
        AssertSameItems(summary.Rejected, restored.Rejected);
        AssertSameItems(summary.Unknown, restored.Unknown);
        AssertSameItems(summary.NotSent, restored.NotSent);
        AssertSameItems(summary.Diagnostics!.Attempts, restored.Diagnostics!.Attempts);
        AssertSameItems(summary.Diagnostics.Anomalies, restored.Diagnostics.Anomalies);
        Assert.Contains("\"outcome\": \"NeedsReconciliation\"", json, StringComparison.Ordinal);
        Assert.Contains("\"quality\": \"NormalQuality\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', json);
    }

    [Fact]
    public void SameJournalGivesTheSameExport()
    {
        var first = Summarize(MixedRun(), RunSummaryOptions.WithDiagnostics(Salt));
        var second = Summarize(MixedRun(), RunSummaryOptions.WithDiagnostics(Salt));

        Assert.Equal(RunSummaryExporter.ToText(first), RunSummaryExporter.ToText(second));
        Assert.Equal(RunSummaryExporter.ToJson(first), RunSummaryExporter.ToJson(second));
    }

    // ImmutableArray equality compares the backing arrays, not their items.
    private static void AssertSameItems<T>(ImmutableArray<T> expected, ImmutableArray<T> actual)
    {
        Assert.Equal(expected.AsSpan().ToArray(), actual.AsSpan().ToArray());
    }

    private static RunSummary Summarize(SummaryJournal journal, RunSummaryOptions options = default)
    {
        return RunSummaryBuilder.Build(journal.Replay(), journal.Entries, Names(), options);
    }
}
