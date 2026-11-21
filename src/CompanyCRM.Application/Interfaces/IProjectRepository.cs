using CompanyCRM.Domain.Entities;

namespace CompanyCRM.Application.Interfaces;

public interface IProjectRepository
{
    Task<List<Project>> GetProjectsWithDetailsAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Project?> GetByIdWithDetailsAsync(Guid id, bool trackChanges = false, CancellationToken ct = default);
    Task<Project?> GetByIdAsync(Guid id, bool trackChanges = false, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct = default);
    void Add(Project project);
    Task<List<ProjectMember>> GetMembersWithEmployeeAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectMember?> GetMemberAsync(Guid projectId, Guid employeeId, bool trackChanges = false, CancellationToken ct = default);
    Task<bool> IsMemberAsync(Guid projectId, Guid employeeId, CancellationToken ct = default);
    void AddMember(ProjectMember member);
    void RemoveMember(ProjectMember member);
    Task SaveChangesAsync(CancellationToken ct = default);
}
