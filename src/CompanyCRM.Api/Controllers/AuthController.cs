using System.Security.Claims;
using CompanyCRM.Api.Services;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ITokenService _tokenService;

    public AuthController(IEmployeeRepository employeeRepository, ITokenService tokenService)
    {
        _employeeRepository = employeeRepository;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var employee = await _employeeRepository.GetByEmailWithSkillsAsync(normalizedEmail, ct);

        if (employee == null)
        {
            return Unauthorized(new { message = "Invalid email or credentials." });
        }

        var (token, expiresAt) = _tokenService.GenerateToken(employee);
        var permissions = GetPermissionsForRole(employee.Role);
        var userDto = MapProfile(employee);

        return Ok(new AuthResponseDto(token, expiresAt, userDto, employee.Role.ToString(), permissions));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> GetCurrentUser(CancellationToken ct)
    {
        var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
        {
            return Unauthorized(new { message = "Caller identity is not authenticated." });
        }

        var employee = await _employeeRepository.GetByIdWithSkillsAsync(callerId, trackChanges: false, ct);

        if (employee == null)
        {
            return NotFound(new { message = $"Employee with ID '{callerId}' not found." });
        }

        var permissions = GetPermissionsForRole(employee.Role);
        return Ok(new CurrentUserDto(
            employee.Id,
            employee.FullName,
            employee.Email,
            employee.AvatarUrl,
            employee.Department,
            employee.JobTitle,
            employee.SeniorityRank,
            employee.Role,
            permissions,
            employee.LayoutPreferencesJson
        ));
    }

    [HttpPost("switch-persona/{role}")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> SwitchPersona([FromRoute] string role, CancellationToken ct)
    {
        if (int.TryParse(role, out _) || !Enum.TryParse<AppRole>(role, true, out var targetRole) || !Enum.IsDefined(typeof(AppRole), targetRole))
        {
            return BadRequest(new { message = "Invalid persona role. Must be Employee, TeamLead, or HigherManagement." });
        }

        // Search for seeded employee by target role
        var employee = await _employeeRepository.GetFirstByRoleWithSkillsAsync(targetRole, ct);

        if (employee == null)
        {
            // If database not yet seeded, provide fallback persona
            employee = targetRole switch
            {
                AppRole.HigherManagement => new Employee
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    FullName = "Eleanor Vance",
                    Email = "eleanor.vance@companycrm.local",
                    Role = AppRole.HigherManagement,
                    Department = "Executive",
                    JobTitle = "Chief Operating Officer",
                    SeniorityRank = SeniorityRank.Principal,
                    AnnualBaseSalary = 185000m,
                    HourlyCostRate = 88.94m,
                    HourlyBillableRate = 220.00m,
                    TargetUtilizationPercent = 60.0m
                },
                AppRole.TeamLead => new Employee
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    FullName = "Sarah Jenkins",
                    Email = "sarah.jenkins@companycrm.local",
                    Role = AppRole.TeamLead,
                    Department = "Engineering",
                    JobTitle = "Staff Lead Engineer",
                    SeniorityRank = SeniorityRank.Lead,
                    AnnualBaseSalary = 145000m,
                    HourlyCostRate = 69.71m,
                    HourlyBillableRate = 175.00m,
                    TargetUtilizationPercent = 75.0m
                },
                _ => new Employee
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    FullName = "Alex Rivera",
                    Email = "alex.rivera@companycrm.local",
                    Role = AppRole.Employee,
                    Department = "Engineering",
                    JobTitle = "Senior Full-Stack Engineer",
                    SeniorityRank = SeniorityRank.Senior,
                    AnnualBaseSalary = 120000m,
                    HourlyCostRate = 57.69m,
                    HourlyBillableRate = 145.00m,
                    TargetUtilizationPercent = 85.0m
                }
            };
        }

        var (token, expiresAt) = _tokenService.GenerateToken(employee);
        var permissions = GetPermissionsForRole(employee.Role);
        var userDto = MapProfile(employee);

        return Ok(new AuthResponseDto(token, expiresAt, userDto, employee.Role.ToString(), permissions));
    }

    private static EmployeeProfileDto MapProfile(Employee e)
    {
        var skills = e.Skills?.Select(s => new EmployeeSkillDto(
            s.Id,
            s.SkillName,
            s.Category,
            s.ProficiencyLevel,
            s.YearsOfExperience,
            s.LastEvaluatedAt
        )).ToList() ?? new List<EmployeeSkillDto>();

        return new EmployeeProfileDto(
            e.Id,
            e.FullName,
            e.Email,
            e.AvatarUrl,
            e.Department,
            e.JobTitle,
            e.SeniorityRank,
            e.Role,
            e.Bio,
            e.LayoutPreferencesJson,
            skills
        );
    }

    private static IReadOnlyList<string> GetPermissionsForRole(AppRole role)
    {
        var permissions = new List<string>
        {
            "workitems:read",
            "workitems:write",
            "timer:use",
            "planning:submit",
            "directory:read"
        };

        if (role is AppRole.TeamLead or AppRole.HigherManagement)
        {
            permissions.AddRange(new[]
            {
                "reviews:manage",
                "sync:trigger",
                "planning:cutoff-manage"
            });
        }

        if (role == AppRole.HigherManagement)
        {
            permissions.AddRange(new[]
            {
                "management:view-compensation",
                "management:view-profitability",
                "management:view-analytics",
                "projects:manage",
                "employees:manage"
            });
        }

        return permissions;
    }
}
