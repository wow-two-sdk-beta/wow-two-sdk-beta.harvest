namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Allows a reader to recognize sufficient HTML before the response body ends.</summary>
public interface IFetchHtmlPrefixPolicy
{
    /// <summary>Checks whether this public destination can be read as a prefix.</summary>
    bool AllowsPrefix(Uri destination);

    /// <summary>Checks whether the UTF-8 prefix contains all the markup the reader needs.</summary>
    /// <param name="destination">The final response URL.</param>
    /// <param name="prefix">The decoded response bytes available so far.</param>
    /// <param name="previousLength">How many bytes were available before the latest read.</param>
    /// <returns>True when the remaining bytes can be abandoned.</returns>
    bool ShouldStopReading(Uri destination, ReadOnlySpan<byte> prefix, int previousLength);
}
