namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps the key of a seeded row of <c>harvest_check_policies</c>; the policy itself is the engine's to read.</summary>
internal sealed class CheckPolicyRow
{
    public const string TableName = "harvest_check_policies";

    public required Guid Id { get; set; }

    public required string Key { get; set; }
}
