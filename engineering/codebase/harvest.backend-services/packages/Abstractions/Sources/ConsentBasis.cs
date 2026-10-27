namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to who consented to a source's content reaching Harvest.</summary>
public enum ConsentBasis
{
    /// <summary>The content's owner, such as an owner-installed bot.</summary>
    Owner,

    /// <summary>The person who submitted it.</summary>
    Submitter,

    /// <summary>No one: public content.</summary>
    None,
}
