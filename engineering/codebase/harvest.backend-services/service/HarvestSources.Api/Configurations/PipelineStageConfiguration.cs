using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Api.Configurations;

internal static class PipelineStageConfiguration
{
    internal static void AddProcessingModels(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Harvest:Processing:OpenAi");
        var apiKey = options["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return;
        var models = options.GetSection("Models").Get<string[]>() ?? [];
        foreach (string model in models.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            builder.Services.AddKeyedSingleton<IChatClient>(model, (_, _) =>
            {
                var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://api.openai.com/v1"),
                    RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                    NetworkTimeout = TimeSpan.FromSeconds(30),
                });
                return client.GetChatClient(model).AsIChatClient();
            });
        }
    }

    internal static async Task SeedPipelineStagesAsync(this WebApplication app, CancellationToken ct)
    {
        var entries = app.Configuration.GetSection("Harvest:PipelineStages").Get<StageSeed[]>() ?? [];
        if (entries.Length == 0)
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var stages = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        foreach (var entry in entries)
        {
            Guid? id = entry.PipelineId ?? await stages.FindSectionAsync(entry.Container, entry.Source, entry.Family, entry.SectionUrl, ct);
            if (id is null || await registry.GetPipelineAsync(id.Value, ct) is not Result<PipelineModel>.Success { Value: var pipeline }
                || await registry.GetSourceAsync(pipeline.SourceId, ct) is not Result<SourceModel>.Success { Value: var source })
            {
                app.Logger.LogWarning("No unique registered source section matches a Harvest:PipelineStages entry.");
                continue;
            }
            foreach (var schedule in entry.Schedules)
            {
                if (await stages.SaveAsync(pipeline, source, schedule, seedOnly: true, ct) is Result<bool>.Failure failure)
                    throw new InvalidOperationException($"Malformed stage settings for section '{pipeline.Id}': {failure.Error.Message}");
            }
        }
    }

    private sealed record StageSeed
    {
        public Guid? PipelineId { get; init; }
        public string? Container { get; init; }
        public string? Source { get; init; }
        public SourceFamily? Family { get; init; }
        public string? SectionUrl { get; init; }
        public ScheduleDeclaration[] Schedules { get; init; } = [];
    }
}
