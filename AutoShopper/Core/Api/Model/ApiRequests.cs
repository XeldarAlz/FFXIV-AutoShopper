using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Api.Model;

internal readonly record struct RequestCheck(ApiRefusal? Refusal, string Field)
{
    public static readonly RequestCheck Valid = new(null, string.Empty);

    public bool IsValid => Refusal is null;

    public static RequestCheck Invalid(string field) => new(ApiRefusal.InvalidRequest, field);
}

internal static class ApiRequests
{
    private const string JsonField = "json";
    private const string SchemaField = "schema";
    private const string CallerField = "caller";
    private const string CallerRunIdField = "callerRunId";
    private const string ItemsField = "items";
    private const string LinesField = "lines";
    private const string MaxSpendField = "maxSpend";

    public static RequestCheck ReadQuote(string? json, out QuoteRequest? request)
    {
        var read = Read(json, ApiJsonContext.Default.QuoteRequest, out request);
        return read.IsValid ? Check(request!) : read;
    }

    public static RequestCheck ReadRun(string? json, out RunRequest? request)
    {
        var read = Read(json, ApiJsonContext.Default.RunRequest, out request);
        return read.IsValid ? Check(request!) : read;
    }

    public static RequestCheck Check(QuoteRequest request)
    {
        if (request.Schema != ApiContract.Schema)
        {
            return new RequestCheck(ApiRefusal.UnsupportedSchema, SchemaField);
        }

        if (!IsCallerName(request.Caller))
        {
            return RequestCheck.Invalid(CallerField);
        }

        var items = request.Items;
        if (items is null || items.Length is 0 or > ApiContract.MaximumEntries)
        {
            return RequestCheck.Invalid(ItemsField);
        }

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (item.ItemId == 0 || IndexOfItem(items, item.ItemId) < index)
            {
                return RequestCheck.Invalid(Field(ItemsField, index, "itemId"));
            }

            if (item.Quantity is < 1 or > ApiContract.MaximumQuoteQuantity)
            {
                return RequestCheck.Invalid(Field(ItemsField, index, "quantity"));
            }
        }

        return RequestCheck.Valid;
    }

    public static RequestCheck Check(RunRequest request)
    {
        if (request.Schema != ApiContract.Schema)
        {
            return new RequestCheck(ApiRefusal.UnsupportedSchema, SchemaField);
        }

        if (!IsCallerName(request.Caller))
        {
            return RequestCheck.Invalid(CallerField);
        }

        if (request.CallerRunId is { Length: > ApiContract.MaximumNameLength })
        {
            return RequestCheck.Invalid(CallerRunIdField);
        }

        if (request.MaxSpend is < 1 or > ApiContract.MaximumSpend)
        {
            return RequestCheck.Invalid(MaxSpendField);
        }

        var lines = request.Lines;
        if (lines is null || lines.Length is 0 or > ApiContract.MaximumEntries)
        {
            return RequestCheck.Invalid(LinesField);
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var problem = LineProblem(lines, index);
            if (problem is not null)
            {
                return RequestCheck.Invalid(Field(LinesField, index, problem));
            }
        }

        return RequestCheck.Valid;
    }

    public static bool IsCallerName([NotNullWhen(true)] string? caller)
    {
        if (caller is null || caller.Length is 0 or > ApiContract.MaximumNameLength)
        {
            return false;
        }

        for (var index = 0; index < caller.Length; index++)
        {
            var character = caller[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public static string LineField(ListValidation validation)
    {
        for (var index = 0; index < validation.LineCount; index++)
        {
            var problems = validation.At(index);
            if (ListValidation.IsError(problems))
            {
                return Field(LinesField, index, (problems & LineProblems.QualityUnavailable) != 0 ? "quality" : "itemId");
            }
        }

        return LinesField;
    }

    public static string DisplayName(string caller, string? displayName)
    {
        var cleaned = ShoppingLimits.CleanName(displayName, ApiContract.MaximumNameLength);
        return cleaned.Length > 0 ? cleaned : caller;
    }

    private static RequestCheck Read<T>(string? json, JsonTypeInfo<T> type, out T? request)
        where T : class
    {
        request = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return RequestCheck.Invalid(JsonField);
        }

        var utf8 = Encoding.UTF8.GetBytes(json);
        if (!ApiJson.TryReadSchema(utf8, out var schema))
        {
            return RequestCheck.Invalid(JsonField);
        }

        if (schema != ApiContract.Schema)
        {
            return new RequestCheck(ApiRefusal.UnsupportedSchema, SchemaField);
        }

        return ApiJson.TryRead(utf8, type, out request) ? RequestCheck.Valid : RequestCheck.Invalid(JsonField);
    }

    private static string? LineProblem(RunRequestLine[] lines, int index)
    {
        var line = lines[index];
        if (line.ItemId == 0)
        {
            return "itemId";
        }

        if (line.Quality is not (ItemQuality.Any or ItemQuality.HighQuality or ItemQuality.NormalQuality))
        {
            return "quality";
        }

        if (IndexOfLine(lines, line.ItemId, line.Quality) < index)
        {
            return "itemId";
        }

        if (!ShoppingLimits.IsValidQuantity(line.Quantity))
        {
            return "quantity";
        }

        if (!ShoppingLimits.IsValidUnitPrice(line.MaxUnitPrice))
        {
            return "maxUnitPrice";
        }

        return ShoppingLimits.IsValidExcess(line.ExcessAllowance) ? null : "excessAllowance";
    }

    private static int IndexOfItem(QuoteRequestItem[] items, uint itemId)
    {
        for (var index = 0; index < items.Length; index++)
        {
            if (items[index].ItemId == itemId)
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfLine(RunRequestLine[] lines, uint itemId, ItemQuality quality)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].ItemId == itemId && lines[index].Quality == quality)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Field(string list, int index, string name)
        => string.Create(CultureInfo.InvariantCulture, $"{list}[{index}].{name}");
}
