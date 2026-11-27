using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/retainers")]
[Authorize(Policy = "HigherManagement")]
public class RetainersController(IRetainerService retainerService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RetainerStatusDto>>> GetRetainers(
        [FromQuery] Guid? projectId = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        var retainers = await retainerService.GetRetainersAsync(projectId, year, month, ct);
        return Ok(retainers);
    }

    [HttpPut]
    public async Task<ActionResult<RetainerStatusDto>> UpsertRetainer(
        [FromBody] UpsertRetainerRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var result = await retainerService.UpsertRetainerAsync(request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRetainer(
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        try
        {
            await retainerService.DeleteRetainerAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("evaluate-alerts")]
    public async Task<ActionResult<BudgetOverageEvaluationResultDto>> EvaluateAlerts(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        var result = await retainerService.EvaluateBudgetOveragesAsync(year, month, ct);
        return Ok(result);
    }
}
