namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Registration;

/// <summary>Represents the service's success envelope around a value.</summary>
/// <typeparam name="T">The value's type.</typeparam>
/// <param name="Data">The value.</param>
internal sealed record DataEnvelope<T>(T? Data);
