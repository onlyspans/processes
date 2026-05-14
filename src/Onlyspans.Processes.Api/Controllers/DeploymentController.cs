using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Onlyspans.Processes.Api.Contracts.Requests;
using Onlyspans.Processes.Api.Features.Deployment;

namespace Onlyspans.Processes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeploymentController(DeploymentService deploymentService) : ControllerBase
{
    [HttpPost]
    [RequestTimeout("DeploymentExecute")]
    public async Task<IActionResult> Execute(
        [FromBody] DeployProcessRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await deploymentService.ExecuteAsync(
                request.ProcessId,
                request.TargetId,
                request.TargetType,
                request.SnapshotKey,
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{deploymentId:guid}/logs")]
    public async Task<IActionResult> GetLogs(Guid deploymentId, CancellationToken ct)
    {
        var result = await deploymentService.GetLogsAsync(deploymentId, ct);
        return Ok(result);
    }
}
