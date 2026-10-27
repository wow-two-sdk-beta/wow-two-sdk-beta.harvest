namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to what a date in a post means.</summary>
public enum DateRole
{
    /// <summary>When the thing starts.</summary>
    Start,

    /// <summary>When the thing ends.</summary>
    End,

    /// <summary>A deadline, such as for applications.</summary>
    Deadline,

    /// <summary>When the post was published.</summary>
    Posted,

    /// <summary>A date the text mentions.</summary>
    Mentioned,
}
