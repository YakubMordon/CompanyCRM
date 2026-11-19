using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.Entities;

public class ProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public ProjectRole Role { get; set; } = ProjectRole.Member;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
