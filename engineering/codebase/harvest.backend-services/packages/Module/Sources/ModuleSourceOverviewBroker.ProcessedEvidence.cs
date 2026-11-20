using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Reads the quotes an extraction answer keeps behind its fields, for the processed post's own page.</summary>
internal sealed partial class ModuleSourceOverviewBroker
{
    private const string EventUseCase = "extract-event";
    private const string ListingUseCase = "extract-listing";

    /// <summary>Lists the quotes of a stored extraction answer, in the order its fields read; a field whose quote is empty
    /// (a repaired or dropped one) is left out.</summary>
    /// <param name="useCase">The answer's use case; none without an extraction.</param>
    /// <param name="json">The stored answer; none for an answer that is not done.</param>
    private static IReadOnlyList<ProcessedEvidenceModel> EvidenceOf(string? useCase, string? json)
    {
        if (json is null)
            return [];
        using var document = JsonDocument.Parse(json);
        var answer = document.RootElement;
        var found = new List<ProcessedEvidenceModel>();
        if (useCase == EventUseCase)
        {
            var index = 0;
            foreach (var run in Items(answer, "occurrences"))
                Add(found, $"occurrences[{index++}]", null, Str(run, "certainty"), Str(run, "evidence"));
            index = 0;
            foreach (var price in Items(answer, "prices"))
                Add(found, $"prices[{index++}]", null, null, Str(price, "evidence"));
            var registration = Child(answer, "registration");
            foreach (var name in new[] { "opens", "deadline" })
                if (Child(registration, name) is { ValueKind: JsonValueKind.Object } date)
                    Add(found, $"registration.{name}", Str(date, "value"), null, Str(date, "evidence"));
        }
        else if (useCase == ListingUseCase)
        {
            if (Child(answer, "price") is { ValueKind: JsonValueKind.Object } price)
                Add(found, "price", null, null, Str(price, "evidence"));
            var terms = Child(answer, "rentalTerms");
            foreach (var name in new[] { "deposit", "minimumStay", "advanceRent", "utilities", "commission" })
            {
                if (Child(terms, name) is not { ValueKind: JsonValueKind.Object } term)
                    continue;
                var claims = Items(term, "claims").ToList();
                var quotes = claims.Select(claim => Str(claim, "evidence")).OfType<string>().Where(quote => quote.Trim().Length > 0).ToList();
                var values = claims.Select(claim => ClaimValue(Child(claim, "value"))).OfType<string>().Distinct().ToList();
                if (quotes.Count > 0)
                    found.Add(new ProcessedEvidenceModel($"rentalTerms.{name}", values.Count > 0 ? string.Join("; ", values) : null, Str(term, "status"), quotes));
            }
        }
        return found;
    }

    private static void Add(List<ProcessedEvidenceModel> found, string field, string? value, string? certainty, string? quote)
    {
        if (!string.IsNullOrWhiteSpace(quote))
            found.Add(new ProcessedEvidenceModel(field, value, certainty, [quote]));
    }

    /// <summary>Reads a rental claim's value in short words: an amount and currency, a multiple of the rent, a duration or
    /// a billing; none for an explicit absence.</summary>
    private static string? ClaimValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return null;
        var parts = new List<string>();
        if (Dec(value, "amount") is { } amount)
            parts.Add(string.Join(' ', new[] { Number(amount), Str(value, "currency") is { } currency && currency != "unsure" ? currency.ToUpperInvariant() : null }.Where(part => !string.IsNullOrEmpty(part))));
        if (Dec(value, "percent") is { } percent)
            parts.Add($"{Number(percent)}%");
        if (Dec(value, "rentMultiple") is { } multiple)
            parts.Add($"{Number(multiple)}× rent");
        if (Dec(value, "count") is { } count && Str(value, "unit") is { } unit)
            parts.Add($"{Number(count)} {unit}");
        if (ClaimValue(Child(value, "duration")) is { } duration)
            parts.Add(duration);
        if (Str(value, "billing") is { } billing)
            parts.Add(billing);
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
