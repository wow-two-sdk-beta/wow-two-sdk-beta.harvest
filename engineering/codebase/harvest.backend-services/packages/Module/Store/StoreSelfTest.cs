using System.Data.Common;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Store;

/// <summary>Checks that the database can hold Harvest: UTF-8, and trigrams that see Cyrillic.</summary>
/// <remarks>Matching folds Cyrillic and Uzbek Latin into trigrams; a non-UTF-8 database or a locale that skips non-ASCII
/// letters would match nothing and fail silently, so boot refuses instead.</remarks>
internal static class StoreSelfTest
{
    /// <summary>Holds the Cyrillic probe whose trigrams must come back non-empty.</summary>
    public const string CyrillicProbe = "привет";

    /// <summary>Reads the encoding and whether <c>pg_trgm</c> is on offer — the checks that run before migrating.</summary>
    public static async Task<StoreProbe> ProbeBeforeMigrationAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var encoding = await ScalarAsync<string>(connection,
            "SELECT pg_encoding_to_char(encoding) FROM pg_database WHERE datname = current_database()", cancellationToken);
        var available = await ScalarAsync<bool>(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_trgm')", cancellationToken);
        return new StoreProbe(encoding ?? "unknown", available);
    }

    /// <summary>Adds the Cyrillic trigram count — the check that runs once the migration installed <c>pg_trgm</c>.</summary>
    public static async Task<StoreProbe> ProbeAfterMigrationAsync(DbConnection connection, StoreProbe before, CancellationToken cancellationToken)
    {
        var count = await ScalarAsync<int>(connection,
            $"SELECT coalesce(array_length(show_trgm('{CyrillicProbe}'), 1), 0)", cancellationToken);
        return before with { TrigramCount = count };
    }

    /// <summary>Lists what the probe shows is missing; empty when the database can hold Harvest.</summary>
    public static IReadOnlyList<string> Evaluate(StoreProbe probe)
    {
        var failures = new List<string>();
        if (!string.Equals(probe.Encoding, "UTF8", StringComparison.OrdinalIgnoreCase))
            failures.Add($"the database encoding is {probe.Encoding}, not UTF8 — create it with ENCODING 'UTF8' and a UTF-8 locale");
        if (!probe.TrigramAvailable)
            failures.Add("the server does not offer the pg_trgm extension — install PostgreSQL's contrib modules");
        if (probe.TrigramCount is 0)
            failures.Add($"show_trgm('{CyrillicProbe}') returned no trigrams — the database locale skips non-ASCII letters; use a UTF-8 locale such as en_US.utf8");

        return failures;
    }

    /// <summary>Formats the failures as the message boot stops with.</summary>
    public static string Describe(IReadOnlyList<string> failures) =>
        "Harvest refuses to start on this database: " + string.Join("; ", failures) + ".";

    private static async Task<T?> ScalarAsync<T>(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
