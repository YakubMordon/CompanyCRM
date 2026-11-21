using System.Security.Claims;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/management")]
[Authorize(Roles = "HigherManagement")]
public class ManagementController : ControllerBase
{
    private readonly IManagementAnalyticsService _analyticsService;
    private readonly IEmployeeRepository _employeeRepository;

    public ManagementController(
        IManagementAnalyticsService analyticsService,
        IEmployeeRepository employeeRepository)
    {
        _analyticsService = analyticsService;
        _employeeRepository = employeeRepository;
    }

    private Guid GetCallerId()
    {
        var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(val, out var id)) return id;
        throw new UnauthorizedAccessException("User identity is not recognized.");
    }

    [HttpGet("compensation")]
    public async Task<ActionResult<IReadOnlyList<EmployeeCompensationDto>>> GetCompensation(CancellationToken ct)
    {
        var compensation = await _analyticsService.GetEmployeeCompensationAsync(ct);
        return Ok(compensation);
    }

    [HttpPost("employees")]
    public async Task<ActionResult<EmployeeCompensationDto>> CreateEmployee(
        [FromBody] CreateEmployeeRequestDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Full name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _employeeRepository.EmailExistsAsync(normalizedEmail, null, ct))
        {
            return Conflict(new { message = $"An employee with email '{request.Email}' already exists." });
        }

        if (string.IsNullOrWhiteSpace(request.Department))
        {
            return BadRequest(new { message = "Department is required." });
        }

        if (string.IsNullOrWhiteSpace(request.JobTitle))
        {
            return BadRequest(new { message = "Job title is required." });
        }

        if (request.WeeklyCapacityHours <= 0 || request.WeeklyCapacityHours > 168)
        {
            return BadRequest(new { message = "Weekly capacity hours must be between 1 and 168." });
        }

        if (request.TargetUtilizationPercent < 0 || request.TargetUtilizationPercent > 100)
        {
            return BadRequest(new { message = "Target utilization percent must be between 0 and 100." });
        }

        if (request.AnnualBaseSalary < 0 || request.HourlyCostRate < 0 || request.HourlyBillableRate < 0)
        {
            return BadRequest(new { message = "Compensation rates and salary must be non-negative." });
        }

        var employee = new Employee
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            Department = request.Department.Trim(),
            JobTitle = request.JobTitle.Trim(),
            SeniorityRank = request.SeniorityRank,
            Role = request.Role,
            AnnualBaseSalary = request.AnnualBaseSalary,
            HourlyCostRate = request.HourlyCostRate,
            HourlyBillableRate = request.HourlyBillableRate,
            WeeklyCapacityHours = request.WeeklyCapacityHours,
            TargetUtilizationPercent = request.TargetUtilizationPercent,
            AvatarUrl = request.AvatarUrl?.Trim(),
            Bio = request.Bio?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _employeeRepository.AddAsync(employee, ct);
        await _employeeRepository.SaveChangesAsync(ct);

        // Retrieve full compensation view for newly created employee
        var compensations = await _analyticsService.GetEmployeeCompensationAsync(ct);
        var createdDto = compensations.FirstOrDefault(c => c.EmployeeId == employee.Id)
            ?? new EmployeeCompensationDto(
                employee.Id,
                employee.FullName,
                employee.Email,
                employee.Department,
                employee.JobTitle,
                employee.SeniorityRank,
                employee.Role,
                employee.AnnualBaseSalary,
                employee.HourlyCostRate,
                employee.HourlyBillableRate,
                employee.WeeklyCapacityHours,
                employee.TargetUtilizationPercent,
                0.0m,
                0.0m,
                0.0m,
                0.0m,
                0.0m,
                0.0m
            );

        return CreatedAtAction(nameof(GetCompensation), new { id = employee.Id }, createdDto);
    }

    [HttpPut("employees/{id:guid}")]
    public async Task<ActionResult<EmployeeCompensationDto>> UpdateEmployee(
        [FromRoute] Guid id,
        [FromBody] UpdateEmployeeRequestDto request,
        CancellationToken ct)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, ct);
        if (employee == null)
        {
            return NotFound(new { message = $"Employee with ID '{id}' was not found." });
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Full name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _employeeRepository.EmailExistsAsync(normalizedEmail, id, ct))
        {
            return Conflict(new { message = $"An employee with email '{request.Email}' already exists." });
        }

        if (string.IsNullOrWhiteSpace(request.Department))
        {
            return BadRequest(new { message = "Department is required." });
        }

        if (string.IsNullOrWhiteSpace(request.JobTitle))
        {
            return BadRequest(new { message = "Job title is required." });
        }

        if (request.WeeklyCapacityHours <= 0 || request.WeeklyCapacityHours > 168)
        {
            return BadRequest(new { message = "Weekly capacity hours must be between 1 and 168." });
        }

        if (request.TargetUtilizationPercent < 0 || request.TargetUtilizationPercent > 100)
        {
            return BadRequest(new { message = "Target utilization percent must be between 0 and 100." });
        }

        if (request.AnnualBaseSalary < 0 || request.HourlyCostRate < 0 || request.HourlyBillableRate < 0)
        {
            return BadRequest(new { message = "Compensation rates and salary must be non-negative." });
        }

        employee.FullName = request.FullName.Trim();
        employee.Email = normalizedEmail;
        employee.Department = request.Department.Trim();
        employee.JobTitle = request.JobTitle.Trim();
        employee.SeniorityRank = request.SeniorityRank;
        employee.Role = request.Role;
        employee.AnnualBaseSalary = request.AnnualBaseSalary;
        employee.HourlyCostRate = request.HourlyCostRate;
        employee.HourlyBillableRate = request.HourlyBillableRate;
        employee.WeeklyCapacityHours = request.WeeklyCapacityHours;
        employee.TargetUtilizationPercent = request.TargetUtilizationPercent;
        employee.AvatarUrl = request.AvatarUrl?.Trim();
        if (request.Bio != null)
        {
            employee.Bio = request.Bio.Trim();
        }
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        await _employeeRepository.SaveChangesAsync(ct);

        var compensations = await _analyticsService.GetEmployeeCompensationAsync(ct);
        var updatedDto = compensations.FirstOrDefault(c => c.EmployeeId == employee.Id)
            ?? new EmployeeCompensationDto(
                employee.Id,
                employee.FullName,
                employee.Email,
                employee.Department,
                employee.JobTitle,
                employee.SeniorityRank,
                employee.Role,
                employee.AnnualBaseSalary,
                employee.HourlyCostRate,
                employee.HourlyBillableRate,
                employee.WeeklyCapacityHours,
                employee.TargetUtilizationPercent,
                0.0m,
                0.0m,
                0.0m,
                0.0m,
                0.0m,
                0.0m
            );

        return Ok(updatedDto);
    }

    [HttpDelete("employees/{id:guid}")]
    public async Task<IActionResult> DeleteEmployee([FromRoute] Guid id, CancellationToken ct)
    {
        var callerId = GetCallerId();
        if (callerId == id)
        {
            return BadRequest(new { message = "You cannot delete your own active account." });
        }

        var employee = await _employeeRepository.GetByIdAsync(id, ct);
        if (employee == null)
        {
            return NotFound(new { message = $"Employee with ID '{id}' was not found." });
        }

        await _employeeRepository.DeleteAsync(employee, callerId, ct);
        await _employeeRepository.SaveChangesAsync(ct);

        return NoContent();
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
