using System.Text.Json;
using Microsoft.Extensions.AI;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace HarvestSources.Api.Entities;

/// <summary>Runs the optional event matching use case with one native, read-only lookup per request.</summary>
internal sealed class HaikuEventMatchAdvisor(IAiChatClients clients, IPromptsBroker prompts, AiModelRef model,
    ILogger<HaikuEventMatchAdvisor> logger) : IEventMatchAdvisor
{
    internal const string UseCase = "resolve-event-match";
    private const string LookupName = "lookup_event_candidates";
    private static readonly ChatResponseFormat DecisionFormat = ChatResponseFormat.ForJsonSchema(
        JsonSerializer.Deserialize<JsonElement>("""
        {
          "type":"object",
          "properties":{
            "decision":{"type":"string","enum":["link","create","review"]},
            "entityId":{"type":["string","null"]}
          },
          "required":["decision","entityId"],
          "additionalProperties":false
        }
        """), "event_match_decision");

    public async Task<EventMatchDecision?> AdviseAsync(EventMatchEvidence evidence,
        Func<string?, CancellationToken, Task<IReadOnlyList<EventMatchCandidate>>> lookup, CancellationToken cancellationToken)
    {
        if (!clients.IsConfigured(model.Provider)
            || await prompts.ResolveAsync(UseCase, model.Model, cancellationToken: cancellationToken)
                is not Result<PromptModel>.Success { Value: var prompt })
            return null;

        var tool = AIFunctionFactory.Create(
            (string? alternateName, CancellationToken ct) => lookup(alternateName, ct), LookupName,
            "Read up to three existing event series from this source's container. An optional alternate name may refine the lookup.");
        List<ChatMessage> messages =
        [
            new(ChatRole.System, prompt.SystemTemplate.Replace("{{eventFormats}}", PostExtractionDefinitions.EventFormatDefinitions, StringComparison.Ordinal)),
            new(ChatRole.User, prompt.UserTemplate.Replace("{{evidence}}", JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web), StringComparison.Ordinal)),
        ];

        var client = clients.Get(model);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var options = new ChatOptions
        {
            ModelId = model.Model, MaxOutputTokens = 500, Tools = [tool],
            ToolMode = ChatToolMode.RequireSpecific(LookupName), AllowMultipleToolCalls = false,
        };
        var first = await client.GetResponseAsync(messages, options, timeout.Token);
        var calls = first.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Take(2).ToArray();
        if (calls is not [var call] || call.Name != LookupName)
        {
            logger.LogDebug("Event matching prompt {Version} returned {CallCount} tool calls with finish reason {FinishReason}.",
                prompt.Version, calls.Length, first.FinishReason);
            return null;
        }

        var result = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments ?? new Dictionary<string, object?>()), timeout.Token);
        messages.AddRange(first.Messages);
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result)]));
        options.ToolMode = ChatToolMode.None;
        options.ResponseFormat = DecisionFormat;
        var final = await client.GetResponseAsync(messages, options, timeout.Token);
        var choice = JsonSerializer.Deserialize<EventMatchDecision>(final.Text, JsonSerializerOptions.Web);
        logger.LogDebug("Event matching prompt {Version} returned {Decision} using {InputTokens} input and {OutputTokens} output tokens.",
            prompt.Version, choice?.Decision, (first.Usage?.InputTokenCount ?? 0) + (final.Usage?.InputTokenCount ?? 0),
            (first.Usage?.OutputTokenCount ?? 0) + (final.Usage?.OutputTokenCount ?? 0));
        return choice;
    }
}
