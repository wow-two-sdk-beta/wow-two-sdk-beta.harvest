namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Classifies a response as a challenge, a block or a rate limit, so the run records the outcome and backs the
/// host off — never a parse failure, never a deletion.</summary>
/// <remarks>It only classifies: nothing solves, clears or routes around what it finds. Adapters add their own detectors
/// over the generic one; the first verdict wins.</remarks>
public interface IChallengeDetector
{
    /// <summary>Gets the detector's stable key, recorded with each verdict.</summary>
    string Key { get; }

    /// <summary>Reads a response for a challenge, a block or a rate limit.</summary>
    /// <param name="probe">The response's status, headers and the start of its body.</param>
    /// <returns>The verdict, or null when the response is content.</returns>
    ChallengeVerdict? Detect(ChallengeProbe probe);
}
