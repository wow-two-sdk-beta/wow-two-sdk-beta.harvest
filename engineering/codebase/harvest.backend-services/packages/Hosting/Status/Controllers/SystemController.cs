using System.Reflection;
using Harvest.Api.Status.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace Harvest.Api.Status.Controllers;

/// <summary>Exposes system status over HTTP.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(IHostEnvironment environment, IOptions<HarvestEngineOptions> worker) : ControllerBase
{
    /// <summary>Holds the name the service reports.</summary>
    private const string ServiceName = "harvest";

    /// <summary>Reports service liveness.</summary>
    /// <returns>The service name, liveness, running version and local worker setting.</returns>
    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SystemStatusDto>>(StatusCodes.Status200OK)]
    public IActionResult Status()
    {
        var version = Assembly.Load(environment.ApplicationName)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var status = new SystemStatusDto
        {
            Service = ServiceName, Status = "ok", Version = version, LocalWorkerEnabled = worker.Value.Enabled,
        };

        return Ok(ApiResponse<SystemStatusDto>.Ok(status));
    }
}
