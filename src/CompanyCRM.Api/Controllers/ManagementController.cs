using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/management")]
[Authorize(Roles = "HigherManagement")]
public class ManagementController : ControllerBase
{
    private readonly IManagementAnalyticsService _analyticsService;

    public ManagementController(IManagementAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet("compensation")]
    public async Task<ActionResult<IReadOnlyList<EmployeeCompensationDto>>> GetCompensation(CancellationToken ct)
    {
        var compensation = await _analyticsService.GetEmployeeCompensationAsync(ct);
        return Ok(compensation);
    }

    [HttpGet("profitability")]
    public async Task<ActionResult<ProjectProfitabilityDto>> GetProfitability(
        [FromQuery] Guid? projectId = null,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            var profitability = await _analyticsService.GetProjectProfitabilityAsync(projectId, startDate, endDate, ct);
            return Ok(profitability);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("analytics/velocity")]
    public async Task<ActionResult<IReadOnlyList<SprintVelocityDto>>> GetVelocity(
        [FromQuery] Guid? projectId = null,
        [FromQuery] int sprintsCount = 5,
        CancellationToken ct = default)
    {
        var velocity = await _analyticsService.GetSprintVelocityAsync(projectId, sprintsCount, ct);
        return Ok(velocity);
    }

    [HttpGet("analytics/hours-comparison")]
    public async Task<ActionResult<IReadOnlyList<HoursComparisonDto>>> GetHoursComparison(
        [FromQuery] Guid? projectId = null,
        CancellationToken ct = default)
    {
        var comparison = await _analyticsService.GetHoursComparisonAsync(projectId, ct);
        return Ok(comparison);
    }

    [HttpGet("analytics/completion-rates")]
    public async Task<ActionResult<CompletionRateDto>> GetCompletionRates(
        [FromQuery] Guid? projectId = null,
        CancellationToken ct = default)
    {
        var completionRates = await _analyticsService.GetCompletionRateAsync(projectId, ct);
        return Ok(completionRates);
    }

    [HttpGet("analytics/burndown")]
    public async Task<ActionResult<BurndownDto>> GetBurndown(
        [FromQuery] Guid? projectId = null,
        [FromQuery] string? sprintName = null,
        CancellationToken ct = default)
    {
        var burndown = await _analyticsService.GetBurndownAsync(projectId, sprintName, ct);
        return Ok(burndown);
    }
}
