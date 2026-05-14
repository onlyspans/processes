using Microsoft.AspNetCore.Mvc;
using Onlyspans.Processes.Api.Contracts.Requests;
using Onlyspans.Processes.Api.Features;

namespace Onlyspans.Processes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessesController(ProcessService processService) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(
        [FromBody] ValidateProcessRequest request,
        CancellationToken ct)
    {
        var result = await processService.ValidateAsync(
            request.Yaml,
            request.ProjectId,
            request.EnvironmentId,
            ct);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProcessRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await processService.CreateAsync(
                request.ProjectId,
                request.EnvironmentId,
                request.ReleaseVersion,
                request.Yaml,
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await processService.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("by-project/{projectId:guid}")]
    public async Task<IActionResult> ListByProject(
        Guid projectId,
        [FromQuery] Guid? environmentId,
        [FromQuery] string? releaseVersion,
        CancellationToken ct,
        [FromQuery] bool fallbackToLatestInEnvironment = false)
    {
        var result = await processService.ListByProjectAsync(
            projectId,
            environmentId,
            releaseVersion,
            fallbackToLatestInEnvironmentWhenReleaseUnmatched: fallbackToLatestInEnvironment,
            ct);

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
