using System.ComponentModel.DataAnnotations;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Application.DTOs;

public record ProjectDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    ExternalSourceType ExternalSource,
    string? ExternalOrgUrl,
    string? ExternalProjectName,
    bool IsSyncEnabled,
    bool IsSimulatedMode,
    DateTimeOffset? LastSyncRunAt,
    int MemberCount,
    int ActiveWorkItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateProjectDto(
    [Required, StringLength(10, MinimumLength = 2), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Project Code must be alphanumeric.")] string Code,
    [Required, StringLength(100)] string Name,
    [StringLength(1000)] string? Description = null,
    ExternalSourceType ExternalSource = ExternalSourceType.None,
    [StringLength(500)] string? ExternalOrgUrl = null,
    [StringLength(100)] string? ExternalProjectName = null,
    [StringLength(500)] string? ExternalPat = null,
    bool IsSyncEnabled = false,
    bool IsSimulatedMode = true
);

public record UpdateProjectDto(
    [StringLength(100)] string? Name = null,
    [StringLength(1000)] string? Description = null,
    bool? IsActive = null
);

public record ProjectMemberDto(
    Guid Id,
    Guid ProjectId,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeEmail,
    string? EmployeeAvatarUrl,
    string Department,
    string JobTitle,
    ProjectRole Role,
    DateTimeOffset JoinedAt
);

public record AddProjectMemberDto(
    Guid EmployeeId,
    ProjectRole Role = ProjectRole.Member
);

public record ProjectExternalConfigDto(
    Guid ProjectId,
    ExternalSourceType ExternalSource,
    string? ExternalOrgUrl,
    string? ExternalProjectName,
    bool HasPatConfigured,
    bool IsSyncEnabled,
    bool IsSimulatedMode,
    DateTimeOffset? LastSyncRunAt
);

public record UpdateProjectExternalConfigDto(
    ExternalSourceType ExternalSource,
    string? ExternalOrgUrl,
    string? ExternalProjectName,
    string? ExternalPat,
    bool IsSyncEnabled,
    bool IsSimulatedMode
);
