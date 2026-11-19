using System.Security.Claims;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly CrmDbContext _dbContext;

    public EmployeesController(CrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private Guid GetCallerId()
    {
        var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(val, out var id)) return id;
        throw new UnauthorizedAccessException("User identity is not recognized.");
    }

    [HttpGet]
    public async Task<ActionResult<List<EmployeeDirectoryDto>>> GetEmployees(
        [FromQuery] string? search,
        [FromQuery] string? department,
        CancellationToken ct = default)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Include(e => e.Skills)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(e => e.Department == department.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => e.FullName.ToLower().Contains(term) || e.Email.ToLower().Contains(term) || e.JobTitle.ToLower().Contains(term));
        }

        var employees = await query.OrderBy(e => e.FullName).ToListAsync(ct);

        // Safe DTO strictly omitting AnnualBaseSalary, HourlyCostRate, HourlyBillableRate, TargetUtilizationPercent
        var result = employees.Select(e => new EmployeeDirectoryDto(
            e.Id,
            e.FullName,
            e.Email,
            e.AvatarUrl,
            e.Department,
            e.JobTitle,
            e.SeniorityRank,
            e.Role,
            e.Bio,
            e.Skills.Select(s => new EmployeeSkillDto(
                s.Id,
                s.SkillName,
                s.Category,
                s.ProficiencyLevel,
                s.YearsOfExperience,
                s.LastEvaluatedAt
            )).ToList()
        )).ToList();

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HttpGet("{id:guid}/profile")]
    public async Task<ActionResult<EmployeeProfileDto>> GetProfile([FromRoute] Guid id, CancellationToken ct)
    {
        var e = await _dbContext.Employees
            .AsNoTracking()
            .Include(e => e.Skills)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (e == null)
        {
            return NotFound(new { message = $"Employee with ID '{id}' not found." });
        }

        var dto = new EmployeeProfileDto(
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
            e.Skills.Select(s => new EmployeeSkillDto(
                s.Id,
                s.SkillName,
                s.Category,
                s.ProficiencyLevel,
                s.YearsOfExperience,
                s.LastEvaluatedAt
            )).ToList()
        );

        return Ok(dto);
    }

    [HttpPut("{id:guid}/profile")]
    public async Task<ActionResult<EmployeeProfileDto>> UpdateProfile(
        [FromRoute] Guid id,
        [FromBody] UpdateProfileRequestDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var isHigherManagement = User.IsInRole("HigherManagement");

        if (callerId != id && !isHigherManagement)
        {
            return Forbid();
        }

        var employee = await _dbContext.Employees
            .Include(e => e.Skills)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (employee == null)
        {
            return NotFound(new { message = $"Employee with ID '{id}' not found." });
        }

        if (request.Bio != null)
        {
            if (request.Bio.Length > 2000)
            {
                return BadRequest(new { message = "Bio must not exceed 2000 characters." });
            }
            employee.Bio = request.Bio.Trim();
        }

        if (request.LayoutPreferencesJson != null)
        {
            // Validate valid JSON
            try
            {
                using var doc = JsonDocument.Parse(request.LayoutPreferencesJson);
                employee.LayoutPreferencesJson = request.LayoutPreferencesJson;
            }
            catch (JsonException)
            {
                return BadRequest(new { message = "LayoutPreferencesJson must be a valid JSON string." });
            }
        }

        employee.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var dto = new EmployeeProfileDto(
            employee.Id,
            employee.FullName,
            employee.Email,
            employee.AvatarUrl,
            employee.Department,
            employee.JobTitle,
            employee.SeniorityRank,
            employee.Role,
            employee.Bio,
            employee.LayoutPreferencesJson,
            employee.Skills.Select(s => new EmployeeSkillDto(
                s.Id,
                s.SkillName,
                s.Category,
                s.ProficiencyLevel,
                s.YearsOfExperience,
                s.LastEvaluatedAt
            )).ToList()
        );

        return Ok(dto);
    }

    [HttpGet("{id:guid}/skills")]
    public async Task<ActionResult<List<EmployeeSkillDto>>> GetSkills([FromRoute] Guid id, CancellationToken ct)
    {
        var employeeExists = await _dbContext.Employees.AnyAsync(e => e.Id == id, ct);
        if (!employeeExists)
        {
            return NotFound(new { message = $"Employee with ID '{id}' not found." });
        }

        var skills = await _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(s => s.EmployeeId == id)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.SkillName)
            .ToListAsync(ct);

        var result = skills.Select(s => new EmployeeSkillDto(
            s.Id,
            s.SkillName,
            s.Category,
            s.ProficiencyLevel,
            s.YearsOfExperience,
            s.LastEvaluatedAt
        )).ToList();

        return Ok(result);
    }

    [HttpPost("{id:guid}/skills")]
    public async Task<ActionResult<EmployeeSkillDto>> UpsertSkill(
        [FromRoute] Guid id,
        [FromBody] UpsertSkillRequestDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var isPrivileged = User.IsInRole("HigherManagement") || User.IsInRole("TeamLead");

        if (callerId != id && !isPrivileged)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.SkillName) || string.IsNullOrWhiteSpace(request.Category))
        {
            return BadRequest(new { message = "SkillName and Category are required." });
        }

        // Proficiency level strictly validated between 1 and 5
        if (request.ProficiencyLevel < 1 || request.ProficiencyLevel > 5)
        {
            return BadRequest(new { message = "Proficiency level must be an integer between 1 and 5." });
        }

        var employee = await _dbContext.Employees.FindAsync(new object[] { id }, ct);
        if (employee == null)
        {
            return NotFound(new { message = $"Employee with ID '{id}' not found." });
        }

        var normalizedName = request.SkillName.Trim();
        var skill = await _dbContext.EmployeeSkills
            .FirstOrDefaultAsync(s => s.EmployeeId == id && s.SkillName.ToLower() == normalizedName.ToLower(), ct);

        if (skill == null)
        {
            skill = new EmployeeSkill
            {
                EmployeeId = id,
                SkillName = normalizedName,
                Category = request.Category.Trim(),
                ProficiencyLevel = request.ProficiencyLevel,
                YearsOfExperience = Math.Max(0.0m, request.YearsOfExperience),
                LastEvaluatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.EmployeeSkills.Add(skill);
        }
        else
        {
            skill.Category = request.Category.Trim();
            skill.ProficiencyLevel = request.ProficiencyLevel;
            skill.YearsOfExperience = Math.Max(0.0m, request.YearsOfExperience);
            skill.LastEvaluatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new EmployeeSkillDto(
            skill.Id,
            skill.SkillName,
            skill.Category,
            skill.ProficiencyLevel,
            skill.YearsOfExperience,
            skill.LastEvaluatedAt
        ));
    }

    [HttpDelete("{id:guid}/skills/{skillName}")]
    public async Task<IActionResult> DeleteSkill(
        [FromRoute] Guid id,
        [FromRoute] string skillName,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var isPrivileged = User.IsInRole("HigherManagement") || User.IsInRole("TeamLead");

        if (callerId != id && !isPrivileged)
        {
            return Forbid();
        }

        var normalizedName = skillName.Trim().ToLower();
        var skill = await _dbContext.EmployeeSkills
            .FirstOrDefaultAsync(s => s.EmployeeId == id && s.SkillName.ToLower() == normalizedName, ct);

        if (skill == null)
        {
            return NotFound(new { message = $"Skill '{skillName}' not found for employee." });
        }

        _dbContext.EmployeeSkills.Remove(skill);
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }
}
