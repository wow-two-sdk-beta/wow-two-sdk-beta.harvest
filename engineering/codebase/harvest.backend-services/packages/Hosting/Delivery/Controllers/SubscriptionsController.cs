using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Delivery.Controllers;

/// <summary>Exposes tenant-owned pipeline and selector subscriptions and resumable pull delivery.</summary>
[ApiController]
[Route("api/subscriptions")]
public sealed class SubscriptionsController(IDeliveryBroker delivery, CallerResolver callers) : ControllerBase
{
    /// <summary>Subscribes a tenant to a pipeline in an accessible container, or with a selector to Layer 1 tags and sources over one.</summary>
    [HttpPost]
    [Authorize(Policy = HarvestScopeConstants.SubscriptionsManage)]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionDeclaration declaration, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || (!caller.IsOperator && caller.TenantId != declaration.TenantId))
            return this.ToProblem(AppErrorFactory.NotFound("The tenant was not found."));
        return (await delivery.SubscribeAsync(declaration, ct)).Match<IActionResult>(
            value => StatusCode(StatusCodes.Status201Created, ApiResponse<SubscriptionModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Lists subscriptions owned by the caller.</summary>
    [HttpGet]
    [Authorize(Policy = AccessPolicies.AnyCaller)]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (!caller.Scopes.Contains(HarvestScopeConstants.SubscriptionsManage)
            && !(caller.IsIdentitySession && caller.Scopes.Contains(HarvestScopeConstants.EventsRead)))
            return this.ToProblem(AppErrorFactory.Forbidden("The caller cannot list subscriptions."));
        if (!caller.IsOperator && tenantId is not null && tenantId != caller.TenantId)
            return this.ToProblem(AppErrorFactory.NotFound("The tenant was not found."));
        return (await delivery.ListSubscriptionsAsync(caller.IsOperator ? tenantId : caller.TenantId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<SubscriptionModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads a subscription owned by the caller.</summary>
    [HttpGet("{subscriptionId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.EventsRead)]
    public async Task<IActionResult> Get(Guid subscriptionId, CancellationToken ct)
    {
        SubscriptionModel? subscription = await OwnedAsync(subscriptionId, ct);
        return subscription is null ? this.ToProblem(AppErrorFactory.NotFound("The subscription was not found."))
            : Ok(ApiResponse<SubscriptionModel>.Ok(subscription));
    }

    /// <summary>Reads a snapshot-stable event page, returning a cursor suitable for a later poll.</summary>
    [HttpGet("{subscriptionId:guid}/events")]
    [Authorize(Policy = HarvestScopeConstants.EventsRead)]
    public async Task<IActionResult> Pull(Guid subscriptionId, [FromQuery] string? cursor, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (await OwnedAsync(subscriptionId, ct) is null)
            return this.ToProblem(AppErrorFactory.NotFound("The subscription was not found."));
        return (await delivery.PullAsync(subscriptionId, cursor, limit, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<CursorPage<DeliveryEvent>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Replaces a selector subscription's selector and re-judges its window on the same cursor.</summary>
    [HttpPut("{subscriptionId:guid}/selector")]
    [Authorize(Policy = HarvestScopeConstants.SubscriptionsManage)]
    public async Task<IActionResult> SetSelector(Guid subscriptionId, [FromBody] SelectorChange change, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || await OwnedAsync(caller, subscriptionId, ct) is null)
            return this.ToProblem(AppErrorFactory.NotFound("The subscription was not found."));
        var named = change with { ChangedBy = caller.IsOperator ? "operator" : $"tenant:{caller.TenantId}" };
        return (await delivery.SetSelectorAsync(subscriptionId, named, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<SelectorChangeModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Moves a subscription between active and suspended, or to archived, which is final.</summary>
    [HttpPut("{subscriptionId:guid}/state")]
    [Authorize(Policy = HarvestScopeConstants.SubscriptionsManage)]
    public async Task<IActionResult> SetState(Guid subscriptionId, [FromBody] SubscriptionStateChange change, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || await OwnedAsync(caller, subscriptionId, ct) is null)
            return this.ToProblem(AppErrorFactory.NotFound("The subscription was not found."));
        return (await delivery.SetStateAsync(subscriptionId, change.State, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<SubscriptionModel>.Ok(value)), this.ToProblem);
    }

    private async Task<SubscriptionModel?> OwnedAsync(Guid id, CancellationToken ct) =>
        await callers.ResolveAsync(User, ct) is { } caller ? await OwnedAsync(caller, id, ct) : null;

    private async Task<SubscriptionModel?> OwnedAsync(Caller caller, Guid id, CancellationToken ct)
    {
        if (await delivery.GetSubscriptionAsync(id, ct) is not Result<SubscriptionModel>.Success { Value: var subscription })
            return null;
        return caller.IsOperator || subscription.TenantId == caller.TenantId ? subscription : null;
    }
}
