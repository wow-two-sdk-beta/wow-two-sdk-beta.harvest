using System.Text;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Sources;

/// <summary>The module with posts, delivery, the engine (no worker) and the Sources reads over a fresh database, a
/// tenant and the pool.</summary>
internal sealed class SourceHost(ServiceProvider services, AsyncServiceScope scope, string connectionString, FakeTimeProvider clock) : IAsyncDisposable
{
    internal FakeTimeProvider Clock { get; } = clock;
    internal IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
    internal IRunBroker Runs => scope.ServiceProvider.GetRequiredService<IRunBroker>();
    internal ISourceOverviewBroker Overview => scope.ServiceProvider.GetRequiredService<ISourceOverviewBroker>();
    internal Guid TenantId { get; private set; }
    internal Guid ContainerId { get; private set; }

    internal static async Task<SourceHost> StartAsync(DateTimeOffset start)
    {
        var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("sources");
        var clock = new FakeTimeProvider(start);
        var registration = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        registration.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        registration.AddHarvestPosts().AddHarvestDelivery().AddHarvestSources();
        registration.AddHarvestEngine();
        var provider = registration.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("test");
        var host = new SourceHost(provider, provider.CreateAsyncScope(), connectionString, clock);
        host.TenantId = Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "ocharo", Name = "Ocharo" })).Id;
        host.ContainerId = Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).Id;
        return host;
    }

    internal async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync();
    }

    internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    internal Capture Capture(string handle, string key, string text) => new()
    {
        ExternalKey = key, Url = $"https://{handle}/{key}", RequestUrl = $"https://{handle}/{key}", FinalUrl = $"https://{handle}/{key}",
        CapturedAtUtc = Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, HttpStatus = 200, ContentType = "text/html",
    };

    internal static PostEnvelope Envelope(string title, string text) => new()
    {
        Kind = PostKind.Event, Title = title, Text = text, Language = new PostLanguage { Code = "ru" },
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };

    internal static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await services.DisposeAsync();
    }
}
