using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IEmployeeRepository _employeeRepository;

    public ProjectsController(
        IProjectRepository projectRepository,
        IEmployeeRepository employeeRepository)
    {
        _projectRepository = projectRepository;
        _employeeRepository = employeeRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProjectDto>>> GetProjects([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var projects = await _projectRepository.GetProjectsWithDetailsAsync(includeInactive, ct);

        var result = projects.Select(p => new ProjectDto(
            p.Id,
            p.Code,
            p.Name,
            p.Description,
            p.IsActive,
            p.ExternalSource,
            p.ExternalOrgUrl,
            p.ExternalProjectName,
            p.IsSyncEnabled,
            p.IsSimulatedMode,
            p.LastSyncRunAt,
            p.Members.Count,
            p.WorkItems.Count(w => w.Status != WorkItemStatus.Done),
            p.CreatedAt,
            p.UpdatedAt
        )).ToList();

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> GetProject([FromRoute] Guid id, CancellationToken ct)
    {
        var p = await _projectRepository.GetByIdWithDetailsAsync(id, trackChanges: false, ct);

        if (p == null)
        {
            return NotFound(new { message = $"Project with ID '{id}' not found." });
        }

        var dto = new ProjectDto(
            p.Id,
            p.Code,
            p.Name,
            p.Description,
            p.IsActive,
            p.ExternalSource,
            p.ExternalOrgUrl,
            p.ExternalProjectName,
            p.IsSyncEnabled,
            p.IsSimulatedMode,
            p.LastSyncRunAt,
            p.Members.Count,
            p.WorkItems.Count(w => w.Status != WorkItemStatus.Done),
            p.CreatedAt,
            p.UpdatedAt
        );

        return Ok(dto);
    }

    [HttpPost]
    [Authorize(Policy = "TeamLeadOrAbove")]
    public async Task<ActionResult<ProjectDto>> CreateProject([FromBody] CreateProjectDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Project Code and Name are required." });
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var exists = await _projectRepository.CodeExistsAsync(normalizedCode, ct);
        if (exists)
        {
            return Conflict(new { message = $"A project with code '{normalizedCode}' already exists." });
        }

        var project = new Project
        {
            Code = normalizedCode,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            ExternalSource = request.ExternalSource,
            ExternalOrgUrl = request.ExternalOrgUrl?.Trim(),
            ExternalProjectName = request.ExternalProjectName?.Trim(),
            ExternalPat = request.ExternalPat?.Trim(),
            IsSyncEnabled = request.IsSyncEnabled,
            IsSimulatedMode = request.IsSimulatedMode
        };

        _projectRepository.Add(project);
        await _projectRepository.SaveChangesAsync(ct);

        var dto = new ProjectDto(
            project.Id,
            project.Code,
            project.Name,
            project.Description,
            project.IsActive,
            project.ExternalSource,
            project.ExternalOrgUrl,
            project.ExternalProjectName,
            project.IsSyncEnabled,
            project.IsSimulatedMode,
            project.LastSyncRunAt,
            0,
            0,
            project.CreatedAt,
            project.UpdatedAt
        );

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "TeamLeadOrAbove")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(
        [FromRoute] Guid id,
        [FromBody] UpdateProjectDto request,
        CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdWithDetailsAsync(id, trackChanges: true, ct);

        if (project == null)
        {
            return NotFound(new { message = $"Project with ID '{id}' not found." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            project.Name = request.Name.Trim();
        }

        if (request.Description != null)
        {
            project.Description = request.Description.Trim();
        }

        if (request.IsActive.HasValue)
        {
            project.IsActive = request.IsActive.Value;
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _projectRepository.SaveChangesAsync(ct);

        var dto = new ProjectDto(
            project.Id,
            project.Code,
            project.Name,
            project.Description,
            project.IsActive,
            project.ExternalSource,
            project.ExternalOrgUrl,
            project.ExternalProjectName,
            project.IsSyncEnabled,
            project.IsSimulatedMode,
            project.LastSyncRunAt,
            project.Members.Count,
            project.WorkItems.Count(w => w.Status != WorkItemStatus.Done),
            project.CreatedAt,
            project.UpdatedAt
        );

        return Ok(dto);
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<ProjectMemberDto>>> GetMembers([FromRoute] Guid id, CancellationToken ct)
    {
        var projectExists = await _projectRepository.ExistsAsync(id, ct);
        if (!projectExists)
        {
            return NotFound(new { message = $"Project with ID '{id}' not found." });
        }

        var members = await _projectRepository.GetMembersWithEmployeeAsync(id, ct);

        var result = members.Select(m => new ProjectMemberDto(
            m.Id,
            m.ProjectId,
            m.EmployeeId,
            m.Employee.FullName,
            m.Employee.Email,
            m.Employee.AvatarUrl,
            m.Employee.Department,
            m.Employee.JobTitle,
            m.Role,
            m.JoinedAt
        )).ToList();

        return Ok(result);
    }

    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = "TeamLeadOrAbove")]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(
        [FromRoute] Guid id,
        [FromBody] AddProjectMemberDto request,
        CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(id, trackChanges: false, ct);
        if (project == null)
        {
            return NotFound(new { message = $"Project with ID '{id}' not found." });
        }

        var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId, ct);
        if (employee == null)
        {
            return NotFound(new { message = $"Employee with ID '{request.EmployeeId}' not found." });
        }

        var alreadyMember = await _projectRepository.IsMemberAsync(id, request.EmployeeId, ct);
        if (alreadyMember)
        {
            return Conflict(new { message = "Employee is already assigned to this project." });
        }

        var member = new ProjectMember
        {
            ProjectId = id,
            EmployeeId = request.EmployeeId,
            Role = request.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        _projectRepository.AddMember(member);
        await _projectRepository.SaveChangesAsync(ct);

        var dto = new ProjectMemberDto(
            member.Id,
            member.ProjectId,
            member.EmployeeId,
            employee.FullName,
            employee.Email,
            employee.AvatarUrl,
            employee.Department,
            employee.JobTitle,
            member.Role,
            member.JoinedAt
        );

        return CreatedAtAction(nameof(GetMembers), new { id }, dto);
    }

    [HttpDelete("{id:guid}/members/{employeeId:guid}")]
    [Authorize(Policy = "TeamLeadOrAbove")]
    public async Task<IActionResult> RemoveMember(
        [FromRoute] Guid id,
        [FromRoute] Guid employeeId,
        CancellationToken ct)
    {
        var member = await _projectRepository.GetMemberAsync(id, employeeId, trackChanges: true, ct);

        if (member == null)
        {
            return NotFound(new { message = "Project membership not found." });
        }

        _projectRepository.RemoveMember(member);
        await _projectRepository.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id:guid}/external-config")]
    [Authorize(Policy = "TeamLeadOrAbove")]
    public async Task<ActionResult<ProjectExternalConfigDto>> GetExternalConfig([FromRoute] Guid id, CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(id, trackChanges: false, ct);
        if (project == null)
        {
            return NotFound(new { message = $"Project with ID '{id}' not found." });
        }

        var dto = new ProjectExternalConfigDto(
            project.Id,
            project.ExternalSource,
            project.ExternalOrgUrl,
            project.ExternalProjectName,
            !string.IsNullOrWhiteSpace(project.ExternalPat),
            project.IsSyncEnabled,
            project.IsSimulatedMode,
            project.LastSyncRunAt
        );

        return Ok(dto);
    }

    [HttpPut("{id:guid}/external-config")]
    [Authorize(Policy = "TeamLeadOrAbove")]
    public async Task<ActionResult<ProjectExternalConfigDto>> UpdateExternalConfig(
        [FromRoute] Guid id,
        [FromBody] UpdateProjectExternalConfigDto request,
        CancellationToken ct)
    {
        var project = await _projectRepository.GetByIdAsync(id, trackChanges: true, ct);
        if (project == null)
        {
            return NotFound(new { message = $"Project with ID '{id}' not found." });
        }

        project.ExternalSource = request.ExternalSource;
        project.ExternalOrgUrl = request.ExternalOrgUrl?.Trim();
        project.ExternalProjectName = request.ExternalProjectName?.Trim();
        project.IsSyncEnabled = request.IsSyncEnabled;
        project.IsSimulatedMode = request.IsSimulatedMode;

        if (!string.IsNullOrWhiteSpace(request.ExternalPat))
        {
            project.ExternalPat = request.ExternalPat.Trim();
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _projectRepository.SaveChangesAsync(ct);

        var dto = new ProjectExternalConfigDto(
            project.Id,
            project.ExternalSource,
            project.ExternalOrgUrl,
            project.ExternalProjectName,
            !string.IsNullOrWhiteSpace(project.ExternalPat),
            project.IsSyncEnabled,
            project.IsSimulatedMode,
            project.LastSyncRunAt
        );

        return Ok(dto);
    }
}
