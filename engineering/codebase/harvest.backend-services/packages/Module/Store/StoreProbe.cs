namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Store;

/// <summary>Represents what the self-test read of the database before and after migrating.</summary>
/// <param name="Encoding">The database's encoding, such as <c>UTF8</c>.</param>
/// <param name="TrigramAvailable">Whether the server offers <c>pg_trgm</c>.</param>
/// <param name="TrigramCount">How many trigrams <c>show_trgm('привет')</c> returned, once the extension is installed;
/// null before migrating.</param>
internal sealed record StoreProbe(string Encoding, bool TrigramAvailable, int? TrigramCount = null);
