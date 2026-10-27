namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Registration;

/// <summary>Represents the parts of the service's problem response the client reads.</summary>
/// <param name="Detail">The problem's detail text.</param>
/// <param name="Title">The problem's title.</param>
internal sealed record ProblemBody(string? Detail, string? Title);
