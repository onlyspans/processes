using Microsoft.AspNetCore.Mvc;
using Onlyspans.Processes.Api.Features;

namespace Onlyspans.Processes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessesController(ProcessService processService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await processService.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("by-project/{projectId:guid}")]
    public async Task<IActionResult> ListByProject(Guid projectId, CancellationToken ct)
    {
        var result = await processService.ListByProjectAsync(projectId, ct);
        return Ok(result);
    }

    [HttpGet("by-project/{projectId:guid}/version/{releaseVersion}")]
    public async Task<IActionResult> GetByProjectAndVersion(
        Guid projectId,
        string releaseVersion,
        CancellationToken ct)
    {
        var result = await processService.GetByProjectAndVersionAsync(projectId, releaseVersion, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
