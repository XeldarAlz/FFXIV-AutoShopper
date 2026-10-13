using System.Text.Json;
using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Shopping.Model;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Tasks;

namespace AutoShopper.Tests;

public sealed class ApiJsonTests
{
    private static readonly Guid Id = new("4e1a0b3c-2d5f-4a6b-8c7d-9e0f1a2b3c4d");

    [Fact]
    public void QuoteRequestRoundTripsWithItsContractNames()
    {
        var request = new QuoteRequest
        {
            Schema = ApiContract.Schema,
            Caller = "AutoGcSupply",
            CallerDisplayName = "Auto GC Supply",
            Items = [new QuoteRequestItem(5_057, 3)],
        };

        var json = ApiJson.Write(request, ApiJsonContext.Default.QuoteRequest);

        Assert.Equal(["schema", "caller", "callerDisplayName", "items"], Names(json));
        Assert.Equal(["itemId", "quantity"], Names(json, "items"));
        Assert.True(ApiJson.TryRead(json, ApiJsonContext.Default.QuoteRequest, out var read));
        Assert.Equal(request.Caller, read.Caller);
        Assert.Equal(request.Items, read.Items);
    }

    [Fact]
    public void QuoteStartResponsesRoundTripBothWays()
    {
        var accepted = QuoteStartResponse.Accept(Id);
        var refused = QuoteStartResponse.Refuse(ApiRefusal.InvalidRequest, "Cannot price these items.");

        var acceptedJson = ApiJson.Write(accepted, ApiJsonContext.Default.QuoteStartResponse);
        var refusedJson = ApiJson.Write(refused, ApiJsonContext.Default.QuoteStartResponse);

        Assert.Equal(["schema", "accepted", "quoteId", "refusal", "message"], Names(acceptedJson));
        Assert.Equal(1, Property(acceptedJson, "schema").GetInt32());
        Assert.Equal("4e1a0b3c-2d5f-4a6b-8c7d-9e0f1a2b3c4d", Property(acceptedJson, "quoteId").GetString());
        Assert.Equal(JsonValueKind.Null, Property(acceptedJson, "refusal").ValueKind);
        Assert.Equal("InvalidRequest", Property(refusedJson, "refusal").GetString());
        Assert.Equal(JsonValueKind.Null, Property(refusedJson, "quoteId").ValueKind);
        Assert.Equal(accepted, Read(acceptedJson, ApiJsonContext.Default.QuoteStartResponse));
        Assert.Equal(refused, Read(refusedJson, ApiJsonContext.Default.QuoteStartResponse));
    }

    [Fact]
    public void QuoteResultRoundTripsWithItsCovers()
    {
        var response = new QuoteResultResponse
        {
            QuoteId = ApiIds.Format(Id),
            State = QuoteState.Ready,
            FetchedAtUtc = new DateTime(2026, 10, 13, 18, 22, 5, DateTimeKind.Utc),
            Items =
            [
                new QuoteItemResult(5_057, 3, new QuoteCover(true, 3, 1_890, 630, 4.5, 1), default),
            ],
        };

        var json = ApiJson.Write(response, ApiJsonContext.Default.QuoteResultResponse);

        Assert.Equal(["schema", "quoteId", "state", "refusal", "fetchedAtUtc", "items"], Names(json));
        Assert.Equal(["itemId", "quantity", "normal", "high"], Names(json, "items"));
        Assert.Equal("Ready", Property(json, "state").GetString());
        Assert.Equal("2026-10-13T18:22:05Z", Property(json, "fetchedAtUtc").GetString());
        var normal = Property(json, "items")[0].GetProperty("normal");
        Assert.Equal(["available", "units", "total", "unitPrice", "oldestListingHours", "worlds"], Names(normal));
        Assert.Equal(4.5, normal.GetProperty("oldestListingHours").GetDouble());
        Assert.False(Property(json, "items")[0].GetProperty("high").GetProperty("available").GetBoolean());

        var read = Read(json, ApiJsonContext.Default.QuoteResultResponse);
        Assert.Equal(response.FetchedAtUtc, read.FetchedAtUtc);
        Assert.Equal(response.Items, read.Items);
        Assert.Equal(QuoteState.Ready, read.State);
    }

    [Fact]
    public void AnUnknownQuoteHasNoItems()
    {
        var json = ApiJson.Write(QuoteResultResponse.Unknown("nope"), ApiJsonContext.Default.QuoteResultResponse);

        Assert.Equal("Unknown", Property(json, "state").GetString());
        Assert.Equal(0, Property(json, "items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, Property(json, "fetchedAtUtc").ValueKind);
    }

    [Fact]
    public void RunRequestRoundTripsWithQualityNames()
    {
        var request = new RunRequest
        {
            Schema = ApiContract.Schema,
            Caller = "AutoGcSupply",
            CallerDisplayName = "Auto GC Supply",
            CallerRunId = "0d7f",
            Label = "GC supply 2026-10-13",
            MaxSpend = 150_000,
            ReturnHome = true,
            Lines = [new RunRequestLine(5_057, ItemQuality.NormalQuality, 3, 700, 3)],
        };

        var json = ApiJson.Write(request, ApiJsonContext.Default.RunRequest);

        Assert.Equal(["schema", "caller", "callerDisplayName", "callerRunId", "label", "maxSpend", "returnHome", "lines"], Names(json));
        Assert.Equal(["itemId", "quality", "quantity", "maxUnitPrice", "excessAllowance"], Names(json, "lines"));
        Assert.Equal("NormalQuality", Property(json, "lines")[0].GetProperty("quality").GetString());
        var read = Read(json, ApiJsonContext.Default.RunRequest);
        Assert.Equal(request.Lines, read.Lines);
        Assert.Equal(request.MaxSpend, read.MaxSpend);
        Assert.True(read.ReturnHome);
    }

    [Fact]
    public void StartRunResponsesRoundTripBothWays()
    {
        var accepted = StartRunResponse.Accept(Id);
        var refused = StartRunResponse.Refuse(ApiRefusal.ConsentRequired, "Cannot start: allow Auto GC Supply first.");

        var acceptedJson = ApiJson.Write(accepted, ApiJsonContext.Default.StartRunResponse);
        var refusedJson = ApiJson.Write(refused, ApiJsonContext.Default.StartRunResponse);

        Assert.Equal(["schema", "accepted", "runId", "refusal", "message"], Names(acceptedJson));
        Assert.True(Property(acceptedJson, "accepted").GetBoolean());
        Assert.Equal("ConsentRequired", Property(refusedJson, "refusal").GetString());
        Assert.Equal(accepted, Read(acceptedJson, ApiJsonContext.Default.StartRunResponse));
        Assert.Equal(refused, Read(refusedJson, ApiJsonContext.Default.StartRunResponse));
    }

    [Fact]
    public void EveryRefusalCodeIsWrittenByName()
    {
        foreach (var refusal in Enum.GetValues<ApiRefusal>())
        {
            var json = ApiJson.Write(StartRunResponse.Refuse(refusal, "reason"), ApiJsonContext.Default.StartRunResponse);

            Assert.Equal(refusal.ToString(), Property(json, "refusal").GetString());
            Assert.Equal(refusal, Read(json, ApiJsonContext.Default.StartRunResponse).Refusal);
        }
    }

    [Fact]
    public void RunStatusRoundTripsWithEveryContractField()
    {
        var response = new RunStatusResponse
        {
            RunId = ApiIds.Format(Id),
            Known = true,
            CallerRunId = "0d7f",
            DryRun = true,
            Phase = RunPhase.Shopping,
            State = RunState.Searching,
            Status = "Searching Darksteel Ore on Ultros",
            Revision = 42,
            Paused = true,
            PauseReason = PauseReason.InContent,
            Terminal = true,
            Outcome = RunOutcome.NeedsReconciliation,
            NeedsPlayer = true,
            ConfirmedSpend = 1_890,
            ReservedSpend = 700,
            Lines = [new RunStatusLine(5_057, ItemQuality.NormalQuality, 3, 3, 0)],
            Issues = [new RunStatusIssue(RunIssueReason.NoStock, 5_058)],
            ReturnHome = ApiReturnHome.Arrived,
            Refusal = ApiRefusal.PricesUnavailable,
        };

        var json = ApiJson.Write(response, ApiJsonContext.Default.RunStatusResponse);

        Assert.Equal(
            ["schema", "runId", "known", "callerRunId", "dryRun", "phase", "state", "status", "revision", "paused", "pauseReason", "terminal",
                "outcome", "needsPlayer", "confirmedSpend", "reservedSpend", "lines", "issues", "returnHome", "refusal"],
            Names(json));
        Assert.Equal(["itemId", "quality", "requested", "acquired", "missing"], Names(json, "lines"));
        Assert.Equal(["reason", "itemId"], Names(json, "issues"));
        Assert.Equal("Shopping", Property(json, "phase").GetString());
        Assert.Equal("Searching", Property(json, "state").GetString());
        Assert.Equal("InContent", Property(json, "pauseReason").GetString());
        Assert.Equal("NeedsReconciliation", Property(json, "outcome").GetString());
        Assert.Equal("NoStock", Property(json, "issues")[0].GetProperty("reason").GetString());
        Assert.Equal("Arrived", Property(json, "returnHome").GetString());
        Assert.Equal("PricesUnavailable", Property(json, "refusal").GetString());

        var read = Read(json, ApiJsonContext.Default.RunStatusResponse);
        Assert.Equal(response with { Lines = read.Lines, Issues = read.Issues }, read);
        Assert.Equal(response.Lines, read.Lines);
        Assert.Equal(response.Issues, read.Issues);
    }

    [Fact]
    public void AnUnknownRunHasDefaults()
    {
        var json = ApiJson.Write(RunStatusResponse.Unknown("gone"), ApiJsonContext.Default.RunStatusResponse);

        Assert.Equal(1, Property(json, "schema").GetInt32());
        Assert.False(Property(json, "known").GetBoolean());
        Assert.Equal("gone", Property(json, "runId").GetString());
        Assert.Equal("Idle", Property(json, "phase").GetString());
        Assert.Equal("NotNeeded", Property(json, "returnHome").GetString());
        Assert.Equal(JsonValueKind.Null, Property(json, "outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, Property(json, "pauseReason").ValueKind);
        Assert.Equal(0, Property(json, "lines").GetArrayLength());
    }

    [Fact]
    public void AWayHomeNobodyAskedForReadsAsNotNeeded()
    {
        Assert.Equal(ApiReturnHome.NotNeeded, RunStatusResponse.ReturnHomeOf(ReturnHomeResult.NotAsked));
        Assert.Equal(ApiReturnHome.AlreadyHome, RunStatusResponse.ReturnHomeOf(ReturnHomeResult.AlreadyHome));
        Assert.Equal(ApiReturnHome.Underway, RunStatusResponse.ReturnHomeOf(ReturnHomeResult.Underway));
        Assert.Equal(ApiReturnHome.Arrived, RunStatusResponse.ReturnHomeOf(ReturnHomeResult.Arrived));
        Assert.Equal(ApiReturnHome.Failed, RunStatusResponse.ReturnHomeOf(ReturnHomeResult.Failed));
        Assert.Equal(ApiReturnHome.Skipped, RunStatusResponse.ReturnHomeOf(ReturnHomeResult.Skipped));
    }

    [Fact]
    public void ReadingIgnoresUnknownFieldsAndAcceptsAnyCase()
    {
        const string json = """{"Schema":1,"CALLER":"AutoGcSupply","items":[{"itemId":5057,"quantity":3,"note":"x"}],"extra":true}""";

        Assert.True(ApiJson.TryRead(json, ApiJsonContext.Default.QuoteRequest, out var read));
        Assert.Equal(1, read.Schema);
        Assert.Equal("AutoGcSupply", read.Caller);
        Assert.Equal([new QuoteRequestItem(5_057, 3)], read.Items!);
    }

    private static T Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        Assert.True(ApiJson.TryRead(json, type, out var value));
        return value;
    }

    private static JsonElement Property(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).Clone();
    }

    private static List<string> Names(string json, string? arrayName = null)
    {
        using var document = JsonDocument.Parse(json);
        var element = arrayName is null ? document.RootElement : document.RootElement.GetProperty(arrayName)[0];
        return Names(element);
    }

    private static List<string> Names(JsonElement element)
    {
        var names = new List<string>();
        foreach (var property in element.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }
}
