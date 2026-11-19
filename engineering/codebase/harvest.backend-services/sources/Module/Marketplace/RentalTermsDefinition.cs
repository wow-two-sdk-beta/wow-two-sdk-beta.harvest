using System.Text.Json;
using System.Text.Json.Schema;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Describes the versioned OLX rental-terms use case for prompt registration.</summary>
public static class RentalTermsDefinition
{
    /// <summary>The capture processor key for this version of the extraction.</summary>
    public const string ProcessorKey = "rental-terms@2";

    /// <summary>The shared AI use case looked up at the pinned prompt version.</summary>
    public const string UseCase = "olx-rental-terms";

    // The answer types, their wire options and the evidence check live in the module's PostExtraction lane, which
    // embeds the same terms in extract-listing.
    internal static JsonSerializerOptions Wire => EvidenceWire.Options;

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
