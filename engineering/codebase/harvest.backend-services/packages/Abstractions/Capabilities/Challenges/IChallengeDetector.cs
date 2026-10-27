namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Tells a challenge from content, so a challenge becomes an outcome with a kind, never a parse failure.</summary>
/// <remarks>Adapters add their own detectors over the generic one; the first verdict wins.</remarks>
public interface IChallengeDetector
{
    /// <summary>Gets the detector's stable key, recorded with each verdict.</summary>
    string Key { get; }

    /// <summary>Reads a response for a challenge.</summary>
    /// <param name="probe">The response's status, headers and the start of its body.</param>
    /// <returns>The challenge, or null when the response is content.</returns>
    ChallengeVerdict? Detect(ChallengeProbe probe);
}
