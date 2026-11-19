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
    string Code,
    string Name,
    string? Description,
    ExternalSourceType ExternalSource = ExternalSourceType.None,
    string? ExternalOrgUrl = null,
    string? ExternalProjectName = null,
    string? ExternalPat = null,
    bool IsSyncEnabled = false,
    bool IsSimulatedMode = true
);

public record UpdateProjectDto(
    string? Name,
    string? Description,
    bool? IsActive
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
