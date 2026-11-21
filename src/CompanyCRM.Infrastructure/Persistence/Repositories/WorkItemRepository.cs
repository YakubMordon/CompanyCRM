using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Persistence.Repositories;

public class WorkItemRepository : IWorkItemRepository
{
    private readonly CrmDbContext _dbContext;

    public WorkItemRepository(CrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<WorkItem>> GetFilteredWithDetailsAsync(
        Guid? projectId,
        WorkItemStatus? status,
        WorkItemType? type,
        WorkItemPriority? priority,
        Guid? assigneeId,
        Guid? reviewerId,
        string? sprint,
        string? search,
        CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .AsNoTracking()
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .AsQueryable();

        if (projectId.HasValue) query = query.Where(w => w.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(w => w.Status == status.Value);
        if (type.HasValue) query = query.Where(w => w.Type == type.Value);
        if (priority.HasValue) query = query.Where(w => w.Priority == priority.Value);
        if (assigneeId.HasValue) query = query.Where(w => w.AssigneeId == assigneeId.Value);
        if (reviewerId.HasValue) query = query.Where(w => w.ReviewerId == reviewerId.Value);
        if (!string.IsNullOrWhiteSpace(sprint)) query = query.Where(w => w.SprintName == sprint.Trim());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(w => w.Title.ToLower().Contains(term) || w.Code.ToLower().Contains(term));
        }

        return query.ToListAsync(ct);
    }

    public Task<WorkItem?> GetByIdWithDetailsAsync(Guid id, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public Task<WorkItem?> GetByIdWithProjectAsync(Guid id, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .Include(w => w.Project)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return _dbContext.WorkItems.AnyAsync(w => w.Id == id, ct);
    }

    public Task<List<string>> GetCodesByProjectPrefixAsync(Guid projectId, string prefix, CancellationToken ct = default)
    {
        return _dbContext.WorkItems
            .Where(w => w.ProjectId == projectId && w.Code.StartsWith(prefix))
            .Select(w => w.Code)
            .ToListAsync(ct);
    }

    public Task<List<Guid>> GetDistinctProjectIdsByWorkItemIdsAsync(IReadOnlyCollection<Guid> workItemIds, CancellationToken ct = default)
    {
        return _dbContext.WorkItems
            .AsNoTracking()
            .Where(w => workItemIds.Contains(w.Id))
            .Select(w => w.ProjectId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task<List<WorkItem>> GetReviewQueueItemsAsync(Guid? projectId, Guid? reviewerId, CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .AsNoTracking()
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Reviewer)
            .Where(w => w.ReviewStatus == ReviewStatus.Pending || w.ReviewStatus == ReviewStatus.ClarificationNeeded);

        if (reviewerId.HasValue)
        {
            query = query.Where(w => w.ReviewerId == reviewerId.Value);
        }

        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        return query.ToListAsync(ct);
    }

    public void Add(WorkItem workItem)
    {
        _dbContext.WorkItems.Add(workItem);
    }

    public void AddHistory(WorkItemHistory history)
    {
        _dbContext.WorkItemHistories.Add(history);
    }

    public async Task<List<WorkItemHistory>> GetHistoriesByWorkItemIdAsync(Guid workItemId, CancellationToken ct = default)
    {
        var items = await _dbContext.WorkItemHistories
            .AsNoTracking()
            .Include(h => h.ChangedBy)
            .Where(h => h.WorkItemId == workItemId)
            .ToListAsync(ct);

        return items.OrderByDescending(h => h.ChangedAt).ToList();
    }

    public Task<TimeEntry?> GetActiveTimerForEmployeeAsync(Guid employeeId, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.TimeEntries
            .Include(t => t.WorkItem)
            .Include(t => t.Employee)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(t => t.EmployeeId == employeeId && t.IsRunning, ct);
    }

    public Task<TimeEntry?> GetRunningTimerAsync(Guid workItemId, Guid employeeId, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.TimeEntries
            .Include(t => t.WorkItem)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(t => t.WorkItemId == workItemId && t.EmployeeId == employeeId && t.IsRunning, ct);
    }

    public void AddTimeEntry(TimeEntry timeEntry)
    {
        _dbContext.TimeEntries.Add(timeEntry);
    }

    public async Task<Guid?> GetConflictProjectIdAsync(Guid conflictId, CancellationToken ct = default)
    {
        return await _dbContext.SyncConflicts
            .AsNoTracking()
            .Where(c => c.Id == conflictId)
            .Select(c => (Guid?)c.ProjectId)
            .FirstOrDefaultAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _dbContext.SaveChangesAsync(ct);
    }
}
