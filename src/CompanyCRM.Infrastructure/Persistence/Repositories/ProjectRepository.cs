using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Persistence.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly CrmDbContext _dbContext;

    public ProjectRepository(CrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Project>> GetProjectsWithDetailsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _dbContext.Projects
            .AsNoTracking()
            .Include(p => p.Members)
            .Include(p => p.WorkItems)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        return query.OrderBy(p => p.Name).ToListAsync(ct);
    }

    public Task<Project?> GetByIdWithDetailsAsync(Guid id, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.Projects
            .Include(p => p.Members)
            .Include(p => p.WorkItems)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public Task<Project?> GetByIdAsync(Guid id, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.Projects.AsQueryable();
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }
        return query.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return _dbContext.Projects.AnyAsync(p => p.Id == id, ct);
    }

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken ct = default)
    {
        return _dbContext.Projects.AnyAsync(p => p.Code == normalizedCode, ct);
    }

    public void Add(Project project)
    {
        _dbContext.Projects.Add(project);
    }

    public Task<List<ProjectMember>> GetMembersWithEmployeeAsync(Guid projectId, CancellationToken ct = default)
    {
        return _dbContext.ProjectMembers
            .AsNoTracking()
            .Include(pm => pm.Employee)
            .Where(pm => pm.ProjectId == projectId)
            .OrderBy(pm => pm.Employee.FullName)
            .ToListAsync(ct);
    }

    public Task<ProjectMember?> GetMemberAsync(Guid projectId, Guid employeeId, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.ProjectMembers.AsQueryable();
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }
        return query.FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.EmployeeId == employeeId, ct);
    }

    public Task<bool> IsMemberAsync(Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        return _dbContext.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.EmployeeId == employeeId, ct);
    }

    public void AddMember(ProjectMember member)
    {
        _dbContext.ProjectMembers.Add(member);
    }

    public void RemoveMember(ProjectMember member)
    {
        _dbContext.ProjectMembers.Remove(member);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _dbContext.SaveChangesAsync(ct);
    }
}
