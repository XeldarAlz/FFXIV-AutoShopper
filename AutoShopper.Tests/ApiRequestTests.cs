using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Tests;

public sealed class ApiRequestTests
{
    private const string Caller = "AutoGcSupply";
    private const uint Ore = 5_057;
    private const uint Cloth = 5_058;

    [Fact]
    public void TheContractsQuoteRequestIsValid()
    {
        const string json = """
            {
              "schema": 1,
              "caller": "AutoGcSupply",
              "callerDisplayName": "Auto GC Supply",
              "items": [
                { "itemId": 5057, "quantity": 3 }
              ]
            }
            """;

        var check = ApiRequests.ReadQuote(json, out var request);

        Assert.True(check.IsValid);
        Assert.NotNull(request);
        Assert.Equal(Caller, request.Caller);
        Assert.Equal("Auto GC Supply", request.CallerDisplayName);
        Assert.Equal([new QuoteRequestItem(Ore, 3)], request.Items!);
    }

    [Fact]
    public void TheContractsRunRequestIsValid()
    {
        const string json = """
            {
              "schema": 1,
              "caller": "AutoGcSupply",
              "callerDisplayName": "Auto GC Supply",
              "callerRunId": "0d7f",
              "label": "GC supply 2026-10-13",
              "maxSpend": 150000,
              "returnHome": true,
              "lines": [
                { "itemId": 5057, "quality": "NormalQuality", "quantity": 3, "maxUnitPrice": 700, "excessAllowance": 3 }
              ]
            }
            """;

        var check = ApiRequests.ReadRun(json, out var request);

        Assert.True(check.IsValid);
        Assert.NotNull(request);
        Assert.Equal("0d7f", request.CallerRunId);
        Assert.Equal("GC supply 2026-10-13", request.Label);
        Assert.Equal(150_000, request.MaxSpend);
        Assert.True(request.ReturnHome);
        Assert.Equal([new RunRequestLine(Ore, ItemQuality.NormalQuality, 3, 700, 3)], request.Lines!);
    }

    [Fact]
    public void TextThatIsNotARequestIsInvalid()
    {
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadQuote(null, out _));
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadQuote("  ", out _));
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadQuote("{\"schema\":", out _));
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadRun("[1,2]", out _));
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadRun("null", out _));
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadRun("{\"schema\":1,\"maxSpend\":99999999999999999999}", out _));
        Assert.Equal(RequestCheck.Invalid("json"), ApiRequests.ReadRun("{\"schema\":1} trailing", out _));
    }

    [Fact]
    public void ALaterSchemaIsUnsupportedWhateverShapeItHas()
    {
        const string json = """{"schema":2,"caller":"AutoGcSupply","items":{"5057":{"quantity":"three"}}}""";

        var check = ApiRequests.ReadQuote(json, out var request);

        Assert.Equal(new RequestCheck(ApiRefusal.UnsupportedSchema, "schema"), check);
        Assert.Null(request);
    }

    [Fact]
    public void ARunWithoutSchemaOneIsUnsupportedBeforeItsShapeIsRead()
    {
        var unsupported = new RequestCheck(ApiRefusal.UnsupportedSchema, "schema");

        Assert.Equal(unsupported, ApiRequests.ReadRun("""{"caller":"AutoGcSupply","lines":"all of them"}""", out _));
        Assert.Equal(unsupported, ApiRequests.ReadRun("""{"schema":"1","maxSpend":1}""", out _));
        Assert.Equal(unsupported, ApiRequests.ReadRun("""{"schema":1.5,"maxSpend":1}""", out _));
        Assert.Equal(unsupported, ApiRequests.ReadRun("""{"lines":[{"schema":1}],"maxSpend":{"schema":1}}""", out _));
    }

    [Fact]
    public void TheSchemaIsFoundWhateverItsCaseAndPlace()
    {
        const string json = """{"caller":"AutoGcSupply","items":[{"itemId":5057,"quantity":3}],"SCHEMA":1}""";

        var check = ApiRequests.ReadQuote(json, out var request);

        Assert.True(check.IsValid);
        Assert.NotNull(request);
        Assert.Equal(1, request.Schema);
    }

    [Fact]
    public void AnotherSchemaIsUnsupported()
    {
        var quote = ApiRequests.Check(Quote() with { Schema = 2 });
        var run = ApiRequests.Check(Run() with { Schema = 0 });

        Assert.Equal(ApiRefusal.UnsupportedSchema, quote.Refusal);
        Assert.Equal(ApiRefusal.UnsupportedSchema, run.Refusal);
        Assert.Equal("schema", run.Field);
    }

    [Fact]
    public void TheCallerNeedsAPlainInternalName()
    {
        Assert.True(ApiRequests.Check(Quote() with { Caller = "Auto.Gc_Supply-2" }).IsValid);
        Assert.Equal(RequestCheck.Invalid("caller"), ApiRequests.Check(Quote() with { Caller = null }));
        Assert.Equal(RequestCheck.Invalid("caller"), ApiRequests.Check(Quote() with { Caller = string.Empty }));
        Assert.Equal(RequestCheck.Invalid("caller"), ApiRequests.Check(Run() with { Caller = "Auto GC Supply" }));
        Assert.Equal(RequestCheck.Invalid("caller"), ApiRequests.Check(Run() with { Caller = new string('a', ApiContract.MaximumNameLength + 1) }));
    }

    [Fact]
    public void AQuoteNeedsOneToAHundredDistinctItems()
    {
        Assert.Equal(RequestCheck.Invalid("items"), ApiRequests.Check(Quote() with { Items = null }));
        Assert.Equal(RequestCheck.Invalid("items"), ApiRequests.Check(Quote() with { Items = [] }));
        Assert.Equal(RequestCheck.Invalid("items"), ApiRequests.Check(Quote() with { Items = QuoteItems(ApiContract.MaximumEntries + 1) }));
        Assert.True(ApiRequests.Check(Quote() with { Items = QuoteItems(ApiContract.MaximumEntries) }).IsValid);
        Assert.Equal(RequestCheck.Invalid("items[1].itemId"), ApiRequests.Check(Quote() with { Items = [new(Ore, 1), new(Ore, 2)] }));
        Assert.Equal(RequestCheck.Invalid("items[0].itemId"), ApiRequests.Check(Quote() with { Items = [new(0, 1)] }));
    }

    [Fact]
    public void AQuoteQuantityIsOneTo9999()
    {
        Assert.True(ApiRequests.Check(Quote() with { Items = [new(Ore, 1), new(Cloth, ApiContract.MaximumQuoteQuantity)] }).IsValid);
        Assert.Equal(RequestCheck.Invalid("items[0].quantity"), ApiRequests.Check(Quote() with { Items = [new(Ore, 0)] }));
        Assert.Equal(RequestCheck.Invalid("items[1].quantity"), ApiRequests.Check(Quote() with { Items = [new(Ore, 1), new(Cloth, ApiContract.MaximumQuoteQuantity + 1)] }));
    }

    [Fact]
    public void ARunNeedsASpendCapInsideTheGilLimit()
    {
        Assert.Equal(RequestCheck.Invalid("maxSpend"), ApiRequests.Check(Run() with { MaxSpend = 0 }));
        Assert.Equal(RequestCheck.Invalid("maxSpend"), ApiRequests.Check(Run() with { MaxSpend = -5 }));
        Assert.Equal(RequestCheck.Invalid("maxSpend"), ApiRequests.Check(Run() with { MaxSpend = ApiContract.MaximumSpend + 1 }));
        Assert.True(ApiRequests.Check(Run() with { MaxSpend = ApiContract.MaximumSpend }).IsValid);
    }

    [Fact]
    public void ARunNeedsOneToAHundredLines()
    {
        Assert.Equal(RequestCheck.Invalid("lines"), ApiRequests.Check(Run() with { Lines = null }));
        Assert.Equal(RequestCheck.Invalid("lines"), ApiRequests.Check(Run() with { Lines = [] }));
        Assert.Equal(RequestCheck.Invalid("lines"), ApiRequests.Check(Run() with { Lines = RunLines(ApiContract.MaximumEntries + 1) }));
        Assert.True(ApiRequests.Check(Run() with { Lines = RunLines(ApiContract.MaximumEntries) }).IsValid);
    }

    [Fact]
    public void EveryLineNumberStaysInsideTheShoppingLimits()
    {
        Assert.Equal(RequestCheck.Invalid("lines[0].itemId"), ApiRequests.Check(Run() with { Lines = [Line() with { ItemId = 0 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].quality"), ApiRequests.Check(Run() with { Lines = [Line() with { Quality = (ItemQuality)7 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].quantity"), ApiRequests.Check(Run() with { Lines = [Line() with { Quantity = 0 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].quantity"), ApiRequests.Check(Run() with { Lines = [Line() with { Quantity = ShoppingLimits.MaximumQuantity + 1 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].maxUnitPrice"), ApiRequests.Check(Run() with { Lines = [Line() with { MaxUnitPrice = -1 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].maxUnitPrice"), ApiRequests.Check(Run() with { Lines = [Line() with { MaxUnitPrice = ShoppingLimits.MaximumUnitPrice + 1 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].excessAllowance"), ApiRequests.Check(Run() with { Lines = [Line() with { ExcessAllowance = -1 }] }));
        Assert.Equal(RequestCheck.Invalid("lines[0].excessAllowance"), ApiRequests.Check(Run() with { Lines = [Line() with { ExcessAllowance = ShoppingLimits.MaximumExcessAllowance + 1 }] }));
    }

    [Fact]
    public void AnItemMayAppearOncePerQuality()
    {
        var both = ApiRequests.Check(Run() with { Lines = [Line(), Line() with { Quality = ItemQuality.HighQuality }] });
        var twice = ApiRequests.Check(Run() with { Lines = [Line(), Line() with { Quantity = 9 }] });

        Assert.True(both.IsValid);
        Assert.Equal(RequestCheck.Invalid("lines[1].itemId"), twice);
    }

    [Fact]
    public void ACallerRunIdLongerThanSixtyFourCharactersIsInvalid()
    {
        Assert.True(ApiRequests.Check(Run() with { CallerRunId = null }).IsValid);
        Assert.True(ApiRequests.Check(Run() with { CallerRunId = new string('x', ApiContract.MaximumNameLength) }).IsValid);
        Assert.Equal(RequestCheck.Invalid("callerRunId"), ApiRequests.Check(Run() with { CallerRunId = new string('x', ApiContract.MaximumNameLength + 1) }));
    }

    [Fact]
    public void AQualityGivenAsANumberIsCheckedLikeAName()
    {
        const string json = """{"schema":1,"caller":"AutoGcSupply","maxSpend":10,"lines":[{"itemId":5057,"quality":9,"quantity":1}]}""";

        Assert.Equal(RequestCheck.Invalid("lines[0].quality"), ApiRequests.ReadRun(json, out _));
    }

    [Fact]
    public void ALineTheItemDataRefusesIsNamedByItsField()
    {
        var traits = new FakeRecipeBook([], [Cloth]);
        var unmarketable = ExternalRun.From(Run() with { Lines = [Line(), Line() with { ItemId = Cloth }] }, DateTime.UtcNow);
        var withoutHighQuality = ExternalRun.From(Run() with { Lines = [Line() with { Quality = ItemQuality.HighQuality }] }, DateTime.UtcNow);
        var fine = ExternalRun.From(Run(), DateTime.UtcNow);

        Assert.Equal("lines[1].itemId", ApiRequests.LineField(ListNormalizer.Validate(unmarketable.List.Lines, traits)));
        Assert.Equal("lines[0].quality", ApiRequests.LineField(ListNormalizer.Validate(withoutHighQuality.List.Lines, traits)));
        Assert.Equal("lines", ApiRequests.LineField(ListNormalizer.Validate(fine.List.Lines, traits)));
    }

    [Fact]
    public void TheDisplayNameFallsBackToTheCallerAndLosesControlCharacters()
    {
        Assert.Equal(Caller, ApiRequests.DisplayName(Caller, null));
        Assert.Equal(Caller, ApiRequests.DisplayName(Caller, " \t "));
        Assert.Equal("Auto GC Supply", ApiRequests.DisplayName(Caller, "  Auto\u0007 GC   Supply "));
    }

    private static QuoteRequest Quote() => new()
    {
        Schema = ApiContract.Schema,
        Caller = Caller,
        CallerDisplayName = "Auto GC Supply",
        Items = [new QuoteRequestItem(Ore, 3)],
    };

    private static RunRequest Run() => new()
    {
        Schema = ApiContract.Schema,
        Caller = Caller,
        CallerDisplayName = "Auto GC Supply",
        MaxSpend = 150_000,
        Lines = [Line()],
    };

    private static RunRequestLine Line() => new(Ore, ItemQuality.NormalQuality, 3, 700, 3);

    private static QuoteRequestItem[] QuoteItems(int count)
    {
        var items = new QuoteRequestItem[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = new QuoteRequestItem((uint)(index + 1), 1);
        }

        return items;
    }

    private static RunRequestLine[] RunLines(int count)
    {
        var lines = new RunRequestLine[count];
        for (var index = 0; index < count; index++)
        {
            lines[index] = Line() with { ItemId = (uint)(index + 1) };
        }

        return lines;
    }
}
