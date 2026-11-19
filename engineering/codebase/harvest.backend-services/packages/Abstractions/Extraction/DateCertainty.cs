namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to whether a date is stated by the post or estimated from it.</summary>
public enum DateCertainty
{
    /// <summary>The post states the date.</summary>
    Stated,

    /// <summary>The date is estimated from the post, such as from a weekday or "next month", by a model's reading.</summary>
    Estimated,
}
