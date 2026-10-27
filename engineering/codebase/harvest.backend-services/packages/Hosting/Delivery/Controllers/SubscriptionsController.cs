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

/// <summary>Exposes tenant-owned pipeline subscriptions and resumable pull delivery.</summary>
[ApiController]
[Route("api/subscriptions")]
public sealed class SubscriptionsController(IDeliveryBroker delivery, CallerResolver callers) : ControllerBase
{
    /// <summary>Subscribes a tenant to a pipeline in an accessible container.</summary>
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
    [Authorize(Policy = HarvestScopeConstants.SubscriptionsManage)]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null)
            return this.ToProblem(AppErrorFactory.Unauthorized());
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

    private async Task<SubscriptionModel?> OwnedAsync(Guid id, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || await delivery.GetSubscriptionAsync(id, ct) is not Result<SubscriptionModel>.Success { Value: var subscription })
            return null;
        return caller.IsOperator || subscription.TenantId == caller.TenantId ? subscription : null;
    }
}
