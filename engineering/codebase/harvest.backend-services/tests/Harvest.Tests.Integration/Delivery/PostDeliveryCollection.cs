namespace Harvest.Tests.Integration.Delivery;

/// <summary>Isolates the deliberately held transaction because PostgreSQL's snapshot horizon spans the cluster.</summary>
[CollectionDefinition("Post delivery transactions", DisableParallelization = true)]
public sealed class PostDeliveryCollection;
