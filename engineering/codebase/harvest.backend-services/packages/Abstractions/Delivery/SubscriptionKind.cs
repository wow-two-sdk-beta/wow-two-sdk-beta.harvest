namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Refers to what a subscription follows.</summary>
public enum SubscriptionKind
{
    /// <summary>A pipeline's posts; it creates or joins the pipeline.</summary>
    Channel,

    /// <summary>Tags over the pool; it keeps no pipeline alive.</summary>
    Tag,

    /// <summary>Keywords over the pool; it may propose a pipeline, never create one.</summary>
    Keyword,
}
