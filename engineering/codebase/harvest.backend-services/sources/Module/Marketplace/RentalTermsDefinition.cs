using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Describes the versioned OLX rental-terms use case for prompt registration.</summary>
public static class RentalTermsDefinition
{
    /// <summary>The capture processor key for this version of the extraction.</summary>
    public const string ProcessorKey = "rental-terms@2";

    /// <summary>The shared AI use case looked up at the pinned prompt version.</summary>
    public const string UseCase = "olx-rental-terms";

    internal static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The JSON schema for the model answer passed to the use case registry.</summary>
    public static string OutputSchema { get; } = BuildSchema();

    private static string BuildSchema()
    {
        var options = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        var schema = Wire.GetJsonSchemaAsNode(typeof(RentalTermsAnswer), options).AsObject();
        return schema.ToJsonString();
    }
}

/// <summary>Configured shared AI routes for the rental terms processor.</summary>
public sealed class RentalTermsOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Harvest:Processing:RentalTerms";

    /// <summary>Gets or sets the ready routes in provider/model format.</summary>
    public string[] Models { get; set; } = [];
}

internal enum RentalTermStatus { Unknown, Absent, Present, Conflict }
internal enum RentalDurationUnit { Night, Day, Week, Month, Year }
internal enum RentalUtilitiesBilling { Included, Separate, Partial }

internal sealed record RentalDuration(decimal Count, RentalDurationUnit Unit);
internal sealed record RentalDeposit(decimal? Amount, string? Currency, decimal? RentMultiple);
internal sealed record RentalAdvance(decimal? Amount, string? Currency, decimal? RentMultiple, RentalDuration? Duration);
internal sealed record RentalUtilities(RentalUtilitiesBilling Billing);
internal sealed record RentalClaim<T>(string Evidence, T? Value) where T : class;
internal sealed record RentalTerm<T>(RentalTermStatus Status, IReadOnlyList<RentalClaim<T>> Claims) where T : class
{
    internal static RentalTerm<T> Unknown { get; } = new(RentalTermStatus.Unknown, []);
}

internal sealed record RentalTermsAnswer(RentalTerm<RentalDeposit> Deposit, RentalTerm<RentalDuration> MinimumStay,
    RentalTerm<RentalAdvance> AdvanceRent, RentalTerm<RentalUtilities> Utilities)
{
    internal static RentalTermsAnswer Empty { get; } = new(RentalTerm<RentalDeposit>.Unknown,
        RentalTerm<RentalDuration>.Unknown, RentalTerm<RentalAdvance>.Unknown, RentalTerm<RentalUtilities>.Unknown);
}
