using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Posts;

internal sealed class PostHost(ServiceProvider services, AsyncServiceScope scope, string connectionString, FakeTimeProvider clock) : IAsyncDisposable
{
    internal string ConnectionString { get; } = connectionString;
    internal FakeTimeProvider Clock { get; } = clock;
    internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
    internal IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    internal Guid ContainerId { get; private set; }
    internal Guid TenantId { get; private set; }
    internal Guid SourceId { get; private set; }
    internal Guid PipelineId { get; private set; }

    internal static async Task<PostHost> StartAsync()
    {
        var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("posts");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var registration = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        registration.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        registration.AddHarvestPosts().AddHarvestDelivery();
        var provider = registration.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("test");
        var host = new PostHost(provider, provider.CreateAsyncScope(), connectionString, clock);
        host.TenantId = Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "events", Name = "Events" })).Id;
        host.ContainerId = Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).Id;
        host.SourceId = await host.SourceAsync("mstar.uz");
        host.PipelineId = Value(await host.Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = host.ContainerId, SourceId = host.SourceId,
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
        })).Id;
        return host;
    }

    internal async Task<Guid> SourceAsync(string handle, int authority = 50, string checkPolicy = "web-direct") =>
        Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = ContainerId, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, Authority = authority, CheckPolicy = checkPolicy,
        })).Id;

    internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    internal async Task<CursorPage<DeliveryEvent>> PullUntilAsync(Guid subscriptionId, Func<CursorPage<DeliveryEvent>, bool> ready, string? cursor = null, int limit = 100)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = Value(await Delivery.PullAsync(subscriptionId, cursor, limit));
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(5)) return page;
            await Task.Delay(50);
        }
    }

    internal static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await services.DisposeAsync();
    }
}
