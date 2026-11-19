using System.Security.Claims;
using CompanyCRM.Api.Hubs;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/workitems")]
[Authorize]
public class WorkItemsController : ControllerBase
{
    private readonly CrmDbContext _dbContext;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly IHubContext<CrmHub, ICrmHubClient> _hubContext;

    public WorkItemsController(
        CrmDbContext dbContext,
        IEmailNotificationService emailNotificationService,
        IHubContext<CrmHub, ICrmHubClient> hubContext)
    {
        _dbContext = dbContext;
        _emailNotificationService = emailNotificationService;
        _hubContext = hubContext;
    }

    private Guid GetCallerId()
    {
        var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(val, out var id)) return id;
        throw new UnauthorizedAccessException("User identity is not recognized.");
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkItemDto>>> GetWorkItems(
        [FromQuery] Guid? projectId,
        [FromQuery] WorkItemStatus? status,
        [FromQuery] WorkItemType? type,
        [FromQuery] WorkItemPriority? priority,
        [FromQuery] Guid? assigneeId,
        [FromQuery] Guid? reviewerId,
        [FromQuery] string? sprint,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var callerId = GetCallerId();
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

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

        var matchingItems = await query.ToListAsync(ct);

        var totalCount = matchingItems.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = matchingItems
            .OrderBy(w => w.BoardPosition)
            .ThenByDescending(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var dtos = items.Select(w => MapToDto(w, callerId)).ToList();
        return Ok(new PagedResult<WorkItemDto>(dtos, totalCount, page, pageSize, totalPages));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkItemDto>> GetWorkItem([FromRoute] Guid id, CancellationToken ct)
    {
        var callerId = GetCallerId();
        var item = await _dbContext.WorkItems
            .AsNoTracking()
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (item == null)
        {
            return NotFound(new { message = $"Work item with ID '{id}' not found." });
        }

        return Ok(MapToDto(item, callerId));
    }

    [HttpPost]
    public async Task<ActionResult<WorkItemDto>> CreateWorkItem([FromBody] CreateWorkItemDto request, CancellationToken ct)
    {
        var callerId = GetCallerId();

        var project = await _dbContext.Projects.FindAsync(new object[] { request.ProjectId }, ct);
        if (project == null)
        {
            return BadRequest(new { message = $"Project '{request.ProjectId}' does not exist." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Work item Title is required." });
        }

        // Generate sequential code e.g. CRM-101
        var existingCodes = await _dbContext.WorkItems
            .Where(w => w.ProjectId == request.ProjectId && w.Code.StartsWith(project.Code + "-"))
            .Select(w => w.Code)
            .ToListAsync(ct);

        var maxNumber = 100;
        foreach (var code in existingCodes)
        {
            var parts = code.Split('-');
            if (parts.Length > 1 && int.TryParse(parts[1], out var num))
            {
                if (num > maxNumber) maxNumber = num;
            }
        }
        var nextCode = $"{project.Code}-{maxNumber + 1}";

        var status = request.ReviewerId.HasValue ? WorkItemStatus.PendingReview : WorkItemStatus.Backlog;
        var reviewStatus = request.ReviewerId.HasValue ? ReviewStatus.Pending : ReviewStatus.NotRequired;

        var workItem = new WorkItem
        {
            ProjectId = request.ProjectId,
            CreatedById = callerId,
            Code = nextCode,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Type = request.Type,
            Status = status,
            Priority = request.Priority,
            AssigneeId = request.AssigneeId,
            ReviewerId = request.ReviewerId,
            ReviewStatus = reviewStatus,
            EstimatedHours = request.EstimatedHours,
            StoryPoints = request.StoryPoints,
            SprintName = request.SprintName?.Trim(),
            BoardPosition = 0
        };

        _dbContext.WorkItems.Add(workItem);

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = workItem.Id,
            ChangedById = callerId,
            Action = "Created",
            Note = $"Work item {nextCode} created.",
            ChangedAt = DateTimeOffset.UtcNow
        });

        Employee? reviewer = null;
        if (request.ReviewerId.HasValue)
        {
            reviewer = await _dbContext.Employees.FindAsync(new object[] { request.ReviewerId.Value }, ct);
            if (reviewer != null)
            {
                _dbContext.WorkItemHistories.Add(new WorkItemHistory
                {
                    WorkItemId = workItem.Id,
                    ChangedById = callerId,
                    Action = "ReviewerAssigned",
                    FieldName = "ReviewerId",
                    NewValue = reviewer.FullName,
                    Note = $"Reviewer assigned: {reviewer.FullName}",
                    ChangedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        if (reviewer != null)
        {
            await _emailNotificationService.SendReviewAssignedEmailAsync(
                reviewer.Email,
                reviewer.FullName,
                workItem.Code,
                workItem.Title,
                workItem.Id,
                ct);

            await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(workItem.ProjectId)).ReviewQueueUpdated(new ReviewQueueEventDto(
                workItem.Id,
                workItem.Code,
                workItem.Title,
                workItem.ProjectId,
                reviewer.Id,
                "Assigned",
                ReviewStatus.Pending,
                null,
                DateTimeOffset.UtcNow
            ));
        }

        var creator = await _dbContext.Employees.FindAsync(new object[] { callerId }, ct);
        workItem.CreatedBy = creator!;
        workItem.Project = project;
        workItem.Reviewer = reviewer;

        var dto = MapToDto(workItem, callerId);
        var broadcastDto = dto with { IsTimerRunningForCurrentUser = false };
        await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(workItem.ProjectId)).WorkItemUpdated(broadcastDto);

        return CreatedAtAction(nameof(GetWorkItem), new { id = workItem.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkItemDto>> UpdateWorkItem(
        [FromRoute] Guid id,
        [FromBody] UpdateWorkItemDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (item == null)
        {
            return NotFound(new { message = $"Work item with ID '{id}' not found." });
        }

        if (request.Title != null) item.Title = request.Title.Trim();
        if (request.Description != null) item.Description = request.Description.Trim();
        if (request.Type.HasValue) item.Type = request.Type.Value;
        if (request.Priority.HasValue) item.Priority = request.Priority.Value;
        if (request.EstimatedHours.HasValue) item.EstimatedHours = request.EstimatedHours.Value;
        if (request.StoryPoints.HasValue) item.StoryPoints = request.StoryPoints.Value;
        if (request.SprintName != null) item.SprintName = request.SprintName.Trim();
        if (request.BoardPosition.HasValue) item.BoardPosition = request.BoardPosition.Value;
        if (request.AssigneeId.HasValue) item.AssigneeId = request.AssigneeId.Value;

        item.UpdatedAt = DateTimeOffset.UtcNow;
        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = item.Id,
            ChangedById = callerId,
            Action = "Updated",
            Note = "Work item properties updated.",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToDto(item, callerId);
        await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(item.ProjectId))
            .WorkItemUpdated(dto with { IsTimerRunningForCurrentUser = false });

        return Ok(dto);
    }

    [HttpPost("{id:guid}/status")]
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<WorkItemDto>> TransitionStatus(
        [FromRoute] Guid id,
        [FromBody] KanbanTransitionDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (item == null)
        {
            return NotFound(new { message = $"Work item with ID '{id}' not found." });
        }

        if (!item.CanTransitionKanban())
        {
            return BadRequest(new
            {
                message = $"Work item cannot transition on Kanban board: status is {item.Status} and review status is {item.ReviewStatus}. Items pending review, needing clarification, or denied must be approved first."
            });
        }

        var oldStatus = item.Status;
        item.Status = request.Status;
        if (request.BoardPosition.HasValue)
        {
            item.BoardPosition = request.BoardPosition.Value;
        }
        item.UpdatedAt = DateTimeOffset.UtcNow;

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = item.Id,
            ChangedById = callerId,
            Action = "StatusChanged",
            FieldName = "Status",
            OldValue = oldStatus.ToString(),
            NewValue = request.Status.ToString(),
            Note = $"Status transitioned from {oldStatus} to {request.Status}.",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToDto(item, callerId);
        await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(item.ProjectId))
            .WorkItemUpdated(dto with { IsTimerRunningForCurrentUser = false });

        return Ok(dto);
    }

    [HttpPost("{id:guid}/timer/start")]
    public async Task<ActionResult<TimeEntryDto>> StartTimer(
        [FromRoute] Guid id,
        [FromBody] StartTimerRequestDto? request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();

        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (item == null)
        {
            return NotFound(new { message = $"Work item with ID '{id}' not found." });
        }

        if (item.ReviewStatus == ReviewStatus.Denied || item.Status == WorkItemStatus.Denied)
        {
            return BadRequest(new { message = "Cannot start timer on a denied work item." });
        }

        // Auto-finalize any active running timer for current employee
        var activeTimer = await _dbContext.TimeEntries
            .Include(t => t.WorkItem)
            .FirstOrDefaultAsync(t => t.EmployeeId == callerId && t.IsRunning, ct);

        if (activeTimer != null)
        {
            activeTimer.StopTimer(DateTimeOffset.UtcNow, $"Auto-finalized upon starting timer on {item.Code}.");
            _dbContext.WorkItemHistories.Add(new WorkItemHistory
            {
                WorkItemId = activeTimer.WorkItemId,
                ChangedById = callerId,
                Action = "TimerStopped",
                Note = $"Auto-finalized timer ({activeTimer.DurationMinutes}m logged) upon starting timer on {item.Code}.",
                ChangedAt = DateTimeOffset.UtcNow
            });
        }

        var entry = new TimeEntry
        {
            WorkItemId = id,
            EmployeeId = callerId,
            StartedAt = DateTimeOffset.UtcNow,
            IsRunning = true,
            IsBillable = request?.IsBillable ?? true,
            LoggedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        _dbContext.TimeEntries.Add(entry);

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = id,
            ChangedById = callerId,
            Action = "TimerStarted",
            Note = $"Started live timer (Billable: {entry.IsBillable}).",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var caller = await _dbContext.Employees.FindAsync(new object[] { callerId }, ct);
        var entryDto = new TimeEntryDto(
            entry.Id,
            entry.WorkItemId,
            item.Code,
            item.Title,
            entry.EmployeeId,
            caller?.FullName ?? "Current User",
            entry.StartedAt,
            entry.EndedAt,
            entry.DurationMinutes,
            entry.LoggedDate,
            entry.IsRunning,
            entry.IsBillable,
            entry.Notes
        );

        // Broadcast update for work item
        var itemWithEntries = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (itemWithEntries != null)
        {
            await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(itemWithEntries.ProjectId))
                .WorkItemUpdated(MapToDto(itemWithEntries, Guid.Empty));
        }

        return Ok(entryDto);
    }

    [HttpPost("{id:guid}/timer/stop")]
    public async Task<ActionResult<TimeEntryDto>> StopTimer(
        [FromRoute] Guid id,
        [FromBody] StopTimerRequestDto? request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();

        var entry = await _dbContext.TimeEntries
            .Include(t => t.WorkItem)
            .FirstOrDefaultAsync(t => t.WorkItemId == id && t.EmployeeId == callerId && t.IsRunning, ct);

        if (entry == null)
        {
            return BadRequest(new { message = $"No running timer found for the current user on work item '{id}'." });
        }

        entry.StopTimer(DateTimeOffset.UtcNow, request?.Notes);
        if (request?.IsBillable.HasValue == true)
        {
            entry.IsBillable = request.IsBillable.Value;
        }

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = id,
            ChangedById = callerId,
            Action = "TimerStopped",
            Note = $"Stopped timer: {entry.DurationMinutes} minutes logged.",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var caller = await _dbContext.Employees.FindAsync(new object[] { callerId }, ct);
        var entryDto = new TimeEntryDto(
            entry.Id,
            entry.WorkItemId,
            entry.WorkItem?.Code ?? "CRM",
            entry.WorkItem?.Title ?? "Work Item",
            entry.EmployeeId,
            caller?.FullName ?? "Current User",
            entry.StartedAt,
            entry.EndedAt,
            entry.DurationMinutes,
            entry.LoggedDate,
            entry.IsRunning,
            entry.IsBillable,
            entry.Notes
        );

        var itemWithEntries = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (itemWithEntries != null)
        {
            await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(itemWithEntries.ProjectId))
                .WorkItemUpdated(MapToDto(itemWithEntries, Guid.Empty));
        }

        return Ok(entryDto);
    }

    [HttpGet("active-timer")]
    public async Task<ActionResult<TimeEntryDto?>> GetActiveTimer(CancellationToken ct)
    {
        var callerId = GetCallerId();

        var entry = await _dbContext.TimeEntries
            .AsNoTracking()
            .Include(t => t.WorkItem)
            .Include(t => t.Employee)
            .FirstOrDefaultAsync(t => t.EmployeeId == callerId && t.IsRunning, ct);

        if (entry == null)
        {
            return NoContent();
        }

        return Ok(new TimeEntryDto(
            entry.Id,
            entry.WorkItemId,
            entry.WorkItem?.Code ?? "CRM",
            entry.WorkItem?.Title ?? "Work Item",
            entry.EmployeeId,
            entry.Employee?.FullName ?? "Current User",
            entry.StartedAt,
            entry.EndedAt,
            entry.DurationMinutes,
            entry.LoggedDate,
            entry.IsRunning,
            entry.IsBillable,
            entry.Notes
        ));
    }

    [HttpPost("{id:guid}/time-entries")]
    public async Task<ActionResult<TimeEntryDto>> CreateManualTimeEntry(
        [FromRoute] Guid id,
        [FromBody] CreateManualTimeEntryDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();

        if (request.DurationMinutes < 1 || request.DurationMinutes > 1440)
        {
            return BadRequest(new { message = "DurationMinutes must be between 1 and 1440 (24 hours)." });
        }

        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (item == null)
        {
            return NotFound(new { message = $"Work item with ID '{id}' not found." });
        }

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-request.DurationMinutes);
        var entry = new TimeEntry
        {
            WorkItemId = id,
            EmployeeId = callerId,
            DurationMinutes = request.DurationMinutes,
            IsBillable = request.IsBillable,
            IsRunning = false,
            Notes = request.Notes?.Trim(),
            LoggedDate = request.LoggedDate,
            StartedAt = startedAt,
            EndedAt = DateTimeOffset.UtcNow
        };

        _dbContext.TimeEntries.Add(entry);

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = id,
            ChangedById = callerId,
            Action = "ManualTimeLogged",
            Note = $"Logged {request.DurationMinutes}m manual time (Billable: {request.IsBillable}). {request.Notes}",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var caller = await _dbContext.Employees.FindAsync(new object[] { callerId }, ct);
        var entryDto = new TimeEntryDto(
            entry.Id,
            entry.WorkItemId,
            item.Code,
            item.Title,
            entry.EmployeeId,
            caller?.FullName ?? "Current User",
            entry.StartedAt,
            entry.EndedAt,
            entry.DurationMinutes,
            entry.LoggedDate,
            entry.IsRunning,
            entry.IsBillable,
            entry.Notes
        );

        var itemWithEntries = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (itemWithEntries != null)
        {
            await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(itemWithEntries.ProjectId))
                .WorkItemUpdated(MapToDto(itemWithEntries, Guid.Empty));
        }

        return CreatedAtAction(nameof(GetWorkItem), new { id }, entryDto);
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<WorkItemHistoryDto>>> GetWorkItemHistory([FromRoute] Guid id, CancellationToken ct)
    {
        var exists = await _dbContext.WorkItems.AnyAsync(w => w.Id == id, ct);
        if (!exists)
        {
            return NotFound(new { message = $"Work item with ID '{id}' not found." });
        }

        var histories = await _dbContext.WorkItemHistories
            .AsNoTracking()
            .Include(h => h.ChangedBy)
            .Where(h => h.WorkItemId == id)
            .ToListAsync(ct);

        var result = histories
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new WorkItemHistoryDto(
            h.Id,
            h.WorkItemId,
            h.ChangedById,
            h.ChangedBy?.FullName ?? "System",
            h.ChangedAt,
            h.Action,
            h.FieldName,
            h.OldValue,
            h.NewValue,
            h.Note
        )).ToList();

        return Ok(result);
    }

    private static WorkItemDto MapToDto(WorkItem w, Guid currentUserId)
    {
        var totalLoggedMinutes = w.TimeEntries?.Where(t => !t.IsRunning).Sum(t => t.DurationMinutes) ?? 0;
        var billableMinutes = w.TimeEntries?.Where(t => !t.IsRunning && t.IsBillable).Sum(t => t.DurationMinutes) ?? 0;
        var isTimerRunning = w.TimeEntries?.Any(t => t.EmployeeId == currentUserId && t.IsRunning) ?? false;

        return new WorkItemDto(
            w.Id,
            w.ProjectId,
            w.Project?.Code ?? "CRM",
            w.Code,
            w.Title,
            w.Description,
            w.Type,
            w.Status,
            w.Priority,
            w.EstimatedHours,
            w.StoryPoints,
            w.SprintName,
            w.BoardPosition,
            w.CreatedById,
            w.CreatedBy?.FullName ?? "Unknown",
            w.AssigneeId,
            w.Assignee?.FullName,
            w.Assignee?.Email,
            w.ReviewerId,
            w.Reviewer?.FullName,
            w.ReviewStatus,
            w.ReviewLatestComment,
            w.ReviewedAt,
            w.ExternalSource,
            w.ExternalId,
            w.ExternalUrl,
            w.LastSyncedAt,
            w.LastSyncedContentHash,
            w.LastSyncOrigin,
            totalLoggedMinutes,
            billableMinutes,
            isTimerRunning,
            w.CreatedAt,
            w.UpdatedAt
        );
    }
}
