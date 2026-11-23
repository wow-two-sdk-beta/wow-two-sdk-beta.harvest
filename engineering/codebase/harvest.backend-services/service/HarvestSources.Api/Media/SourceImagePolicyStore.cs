using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace HarvestSources.Api.Media;

/// <summary>Stores the instance policy for collected post images in SCFD's database.</summary>
internal sealed class SourceImagePolicyStore([FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database)
{
    public async Task<string[]> ReadAsync(CancellationToken ct)
    {
        await using var command = database.CreateCommand("SELECT post_presets FROM harvest_source_image_policy WHERE singleton");
        return (string[])(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<string[]> SaveAsync(string[] presets, CancellationToken ct)
    {
        await using var command = database.CreateCommand("""
            UPDATE harvest_source_image_policy SET post_presets = @presets WHERE singleton RETURNING post_presets
            """);
        command.Parameters.AddWithValue("presets", presets);
        return (string[])(await command.ExecuteScalarAsync(ct))!;
    }
}
