using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Application.DTOs;

public record LoginRequest(string Email, string? Password);

public record AuthResponseDto(
    string Token,
    DateTimeOffset ExpiresAt,
    EmployeeProfileDto User,
    string Role,
    IReadOnlyList<string> Permissions
);

public record CurrentUserDto(
    Guid Id,
    string FullName,
    string Email,
    string? AvatarUrl,
    string Department,
    string JobTitle,
    SeniorityRank SeniorityRank,
    AppRole Role,
    IReadOnlyList<string> Permissions,
    string? LayoutPreferencesJson
);
