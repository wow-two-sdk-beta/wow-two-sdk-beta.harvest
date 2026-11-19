using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Holds the wire options of the evidence-checked AI answers: <c>rental-terms@2</c> (the OLX capture processor in
/// <c>sources/Module</c>) and the <c>extract-event</c> and <c>extract-listing</c> use cases, which embed its terms.</summary>
internal static class EvidenceWire
{
    /// <summary>Gets strict web options: camelCase names and string enums, required constructor parameters, no unknown
    /// members and no numbers as strings.</summary>
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}

/// <summary>Refers to what the post says about one rental term.</summary>
internal enum RentalTermStatus { Unknown, Absent, Present, Conflict }

/// <summary>Refers to the unit of a rental duration.</summary>
internal enum RentalDurationUnit { Night, Day, Week, Month, Year }

/// <summary>Refers to how utilities are billed.</summary>
internal enum RentalUtilitiesBilling { Included, Separate, Partial }

/// <summary>Represents a duration such as a minimum stay.</summary>
internal sealed record RentalDuration(decimal Count, RentalDurationUnit Unit);

/// <summary>Represents a deposit: an amount, or a multiple of the rent.</summary>
internal sealed record RentalDeposit(decimal? Amount, string? Currency, decimal? RentMultiple);

/// <summary>Represents advance rent: an amount, a multiple of the rent, or a duration.</summary>
internal sealed record RentalAdvance(decimal? Amount, string? Currency, decimal? RentMultiple, RentalDuration? Duration);

/// <summary>Represents how utilities are billed.</summary>
internal sealed record RentalUtilities(RentalUtilitiesBilling Billing);

/// <summary>Represents one claim about a term: the verbatim quote and the value it states, none for an explicit absence.</summary>
internal sealed record RentalClaim<T>(string Evidence, T? Value) where T : class;

/// <summary>Represents one rental term: its state and the claims behind it.</summary>
internal sealed record RentalTerm<T>(RentalTermStatus Status, IReadOnlyList<RentalClaim<T>> Claims) where T : class
{
    internal static RentalTerm<T> Unknown { get; } = new(RentalTermStatus.Unknown, []);
}

/// <summary>Represents the four rental terms <c>rental-terms@2</c> extracts, each with its evidence.</summary>
internal sealed record RentalTermsAnswer(RentalTerm<RentalDeposit> Deposit, RentalTerm<RentalDuration> MinimumStay,
    RentalTerm<RentalAdvance> AdvanceRent, RentalTerm<RentalUtilities> Utilities)
{
    internal static RentalTermsAnswer Empty { get; } = new(RentalTerm<RentalDeposit>.Unknown,
        RentalTerm<RentalDuration>.Unknown, RentalTerm<RentalAdvance>.Unknown, RentalTerm<RentalUtilities>.Unknown);
}
