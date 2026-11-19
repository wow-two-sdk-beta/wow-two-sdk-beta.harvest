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

/// <summary>Counts what a selector would deliver over a container's posts, before a subscription holds it.</summary>
[ApiController]
[Route("api/subscriptions/preview")]
public sealed class SubscriptionPreviewController(ISubscriptionPreviewBroker preview, CallerResolver callers) : ControllerBase
{
    /// <summary>Judges the container's live posts that changed in the window against a selector, saved or not, and writes nothing.</summary>
    [HttpPost]
    [Authorize(Policy = HarvestScopeConstants.SubscriptionsManage)]
    public async Task<IActionResult> Preview([FromBody] SubscriptionPreviewRequest request, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || (!caller.IsOperator && caller.TenantId != request.TenantId))
            return this.ToProblem(AppErrorFactory.NotFound("The tenant was not found."));
        return (await preview.PreviewAsync(request, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<SubscriptionPreviewModel>.Ok(value)), this.ToProblem);
    }
}
