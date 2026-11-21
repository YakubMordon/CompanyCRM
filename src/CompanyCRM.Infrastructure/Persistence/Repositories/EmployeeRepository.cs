using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly CrmDbContext _dbContext;

    public EmployeeRepository(CrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public Task<Employee?> GetByIdWithSkillsAsync(Guid id, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.Employees.Include(e => e.Skills).AsQueryable();
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }
        return query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public Task<Employee?> GetByEmailWithSkillsAsync(string normalizedEmail, CancellationToken ct = default)
    {
        return _dbContext.Employees
            .Include(e => e.Skills)
            .FirstOrDefaultAsync(e => e.Email.ToLower() == normalizedEmail.ToLower(), ct);
    }

    public Task<Employee?> GetFirstByRoleWithSkillsAsync(AppRole role, CancellationToken ct = default)
    {
        return _dbContext.Employees
            .Include(e => e.Skills)
            .OrderBy(e => e.FullName)
            .FirstOrDefaultAsync(e => e.Role == role, ct);
    }

    public Task<List<Employee>> GetDirectoryAsync(string? search, string? department, Guid? projectId, CancellationToken ct = default)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Include(e => e.Skills)
            .AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(e => e.ProjectMemberships.Any(pm => pm.ProjectId == projectId.Value));
        }

        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(e => e.Department == department.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => e.FullName.ToLower().Contains(term) ||
                                     e.Email.ToLower().Contains(term) ||
                                     e.JobTitle.ToLower().Contains(term));
        }

        return query.OrderBy(e => e.FullName).ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return _dbContext.Employees.AnyAsync(e => e.Id == id, ct);
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeEmployeeId = null, CancellationToken ct = default)
    {
        var query = _dbContext.Employees.AsQueryable();
        if (excludeEmployeeId.HasValue)
        {
            query = query.Where(e => e.Id != excludeEmployeeId.Value);
        }
        var target = normalizedEmail.Trim().ToLowerInvariant();
        return query.AnyAsync(e => e.Email.ToLower() == target, ct);
    }

    public async Task AddAsync(Employee employee, CancellationToken ct = default)
    {
        await _dbContext.Employees.AddAsync(employee, ct);
    }

    public async Task DeleteAsync(Employee employee, Guid? reassignCreatedItemsToId = null, CancellationToken ct = default)
    {
        var targetId = employee.Id;

        // Reassign or clear WorkItems where this employee is Assignee or Reviewer
        var assignedItems = await _dbContext.WorkItems
            .Where(w => w.AssigneeId == targetId || w.ReviewerId == targetId)
            .ToListAsync(ct);

        foreach (var item in assignedItems)
        {
            if (item.AssigneeId == targetId) item.AssigneeId = null;
            if (item.ReviewerId == targetId) item.ReviewerId = null;
        }

        // Reassign WorkItems created by this employee if reassignCreatedItemsToId is provided
        if (reassignCreatedItemsToId.HasValue && reassignCreatedItemsToId.Value != targetId)
        {
            var createdItems = await _dbContext.WorkItems
                .Where(w => w.CreatedById == targetId)
                .ToListAsync(ct);

            foreach (var item in createdItems)
            {
                item.CreatedById = reassignCreatedItemsToId.Value;
            }

            var histories = await _dbContext.WorkItemHistories
                .Where(h => h.ChangedById == targetId)
                .ToListAsync(ct);

            foreach (var h in histories)
            {
                h.ChangedById = reassignCreatedItemsToId.Value;
            }
        }
        else
        {
            // If no reassign target provided, look for another HigherManagement or fallback employee
            var fallback = await _dbContext.Employees
                .Where(e => e.Id != targetId)
                .OrderBy(e => e.Role == AppRole.HigherManagement ? 0 : 1)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);

            if (fallback.HasValue)
            {
                var createdItems = await _dbContext.WorkItems
                    .Where(w => w.CreatedById == targetId)
                    .ToListAsync(ct);

                foreach (var item in createdItems)
                {
                    item.CreatedById = fallback.Value;
                }

                var histories = await _dbContext.WorkItemHistories
                    .Where(h => h.ChangedById == targetId)
                    .ToListAsync(ct);

                foreach (var h in histories)
                {
                    h.ChangedById = fallback.Value;
                }
            }
        }

        // Remove TimeEntries associated with this employee (DeleteBehavior.Restrict)
        var timeEntries = await _dbContext.TimeEntries
            .Where(t => t.EmployeeId == targetId)
            .ToListAsync(ct);

        if (timeEntries.Count > 0)
        {
            _dbContext.TimeEntries.RemoveRange(timeEntries);
        }

        // Remove the employee entity
        _dbContext.Employees.Remove(employee);
    }

    public Task<List<EmployeeSkill>> GetSkillsAsync(Guid employeeId, CancellationToken ct = default)
    {
        return _dbContext.EmployeeSkills
            .AsNoTracking()
            .Where(s => s.EmployeeId == employeeId)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.SkillName)
            .ToListAsync(ct);
    }

    public Task<EmployeeSkill?> GetSkillByNameAsync(Guid employeeId, string skillName, CancellationToken ct = default)
    {
        var normalized = skillName.Trim().ToLower();
        return _dbContext.EmployeeSkills
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.SkillName.ToLower() == normalized, ct);
    }

    public void AddSkill(EmployeeSkill skill)
    {
        _dbContext.EmployeeSkills.Add(skill);
    }

    public void RemoveSkill(EmployeeSkill skill)
    {
        _dbContext.EmployeeSkills.Remove(skill);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _dbContext.SaveChangesAsync(ct);
    }
}
