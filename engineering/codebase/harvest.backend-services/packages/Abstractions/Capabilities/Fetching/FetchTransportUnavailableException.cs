namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Reports unavailable configured routing or credentials without exposing provider secrets.</summary>
public sealed class FetchTransportUnavailableException(string message) : Exception(message);
