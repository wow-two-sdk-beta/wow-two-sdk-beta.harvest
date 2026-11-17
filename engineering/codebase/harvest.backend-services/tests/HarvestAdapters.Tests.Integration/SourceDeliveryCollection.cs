namespace HarvestAdapters.Tests.Integration;

// The adapter tests share a PostgreSQL instance. An open write transaction can hold back another
// test's delivery cursor until the cluster-wide snapshot horizon advances.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SourceDeliveryCollection
{
    public const string Name = "Source delivery transactions";
}
