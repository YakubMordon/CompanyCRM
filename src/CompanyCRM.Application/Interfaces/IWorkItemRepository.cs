using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Application.Interfaces;

public interface IWorkItemRepository
{
    Task<List<WorkItem>> GetFilteredWithDetailsAsync(
        Guid? projectId,
        WorkItemStatus? status,
        WorkItemType? type,
        WorkItemPriority? priority,
        Guid? assigneeId,
        Guid? reviewerId,
        string? sprint,
        string? search,
        CancellationToken ct = default);

    Task<WorkItem?> GetByIdWithDetailsAsync(Guid id, bool trackChanges = false, CancellationToken ct = default);
    Task<WorkItem?> GetByIdWithProjectAsync(Guid id, bool trackChanges = false, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<List<string>> GetCodesByProjectPrefixAsync(Guid projectId, string prefix, CancellationToken ct = default);
    Task<List<Guid>> GetDistinctProjectIdsByWorkItemIdsAsync(IReadOnlyCollection<Guid> workItemIds, CancellationToken ct = default);
    Task<List<WorkItem>> GetReviewQueueItemsAsync(Guid? projectId, Guid? reviewerId, CancellationToken ct = default);
    void Add(WorkItem workItem);
    void AddHistory(WorkItemHistory history);
    Task<List<WorkItemHistory>> GetHistoriesByWorkItemIdAsync(Guid workItemId, CancellationToken ct = default);
    Task<TimeEntry?> GetActiveTimerForEmployeeAsync(Guid employeeId, bool trackChanges = false, CancellationToken ct = default);
    Task<TimeEntry?> GetRunningTimerAsync(Guid workItemId, Guid employeeId, bool trackChanges = false, CancellationToken ct = default);
    void AddTimeEntry(TimeEntry timeEntry);
    Task<Guid?> GetConflictProjectIdAsync(Guid conflictId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
