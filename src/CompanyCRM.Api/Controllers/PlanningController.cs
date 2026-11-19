using System.Security.Claims;
using System.Text.Json;
using CompanyCRM.Api.Hubs;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/planning")]
[Authorize]
public class PlanningController : ControllerBase
{
    private readonly CrmDbContext _dbContext;
    private readonly IPlanningCutoffJob _cutoffJob;
    private readonly IHubContext<CrmHub, ICrmHubClient> _hubContext;

    public PlanningController(
        CrmDbContext dbContext,
        IPlanningCutoffJob cutoffJob,
        IHubContext<CrmHub, ICrmHubClient> hubContext)
    {
        _dbContext = dbContext;
        _cutoffJob = cutoffJob;
        _hubContext = hubContext;
    }

    private Guid GetCallerId()
    {
        var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(val, out var id)) return id;
        throw new UnauthorizedAccessException("User identity is not recognized.");
    }

    [HttpGet("weekly")]
    public async Task<ActionResult<List<WeeklyPlanDto>>> GetWeeklyPlans(
        [FromQuery] DateOnly? weekStartDate,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? employeeId,
        CancellationToken ct = default)
    {
        var query = _dbContext.WeeklyPlans
            .AsNoTracking()
            .Include(wp => wp.Employee)
            .Include(wp => wp.Project)
            .AsQueryable();

        if (weekStartDate.HasValue)
        {
            var normalizedMonday = WeeklyPlan.NormalizeToMonday(weekStartDate.Value);
            query = query.Where(wp => wp.WeekStartDate == normalizedMonday);
        }

        if (projectId.HasValue)
        {
            query = query.Where(wp => wp.ProjectId == projectId.Value);
        }

        if (employeeId.HasValue)
        {
            query = query.Where(wp => wp.EmployeeId == employeeId.Value);
        }

        var plans = await query.OrderByDescending(wp => wp.WeekStartDate).ToListAsync(ct);

        var result = plans.Select(wp => new WeeklyPlanDto(
            wp.Id,
            wp.EmployeeId,
            wp.Employee?.FullName ?? "Employee",
            wp.ProjectId,
            wp.Project?.Name,
            wp.WeekStartDate,
            wp.GoalsMarkdown,
            wp.FocusAreasJson,
            wp.KeyDeliverables,
            wp.Status,
            wp.CreatedAt,
            wp.UpdatedAt
        )).ToList();

        return Ok(result);
    }

    [HttpPost("weekly")]
    public async Task<ActionResult<WeeklyPlanDto>> CreateOrUpdateWeeklyPlan(
        [FromBody] CreateOrUpdateWeeklyPlanDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var normalizedMonday = WeeklyPlan.NormalizeToMonday(request.WeekStartDate);

        var plan = await _dbContext.WeeklyPlans
            .Include(wp => wp.Employee)
            .Include(wp => wp.Project)
            .FirstOrDefaultAsync(wp =>
                wp.EmployeeId == callerId &&
                wp.WeekStartDate == normalizedMonday, ct);

        if (plan == null)
        {
            plan = new WeeklyPlan
            {
                EmployeeId = callerId,
                ProjectId = request.ProjectId,
                WeekStartDate = normalizedMonday,
                GoalsMarkdown = request.GoalsMarkdown?.Trim() ?? string.Empty,
                FocusAreasJson = request.FocusAreasJson,
                KeyDeliverables = request.KeyDeliverables?.Trim(),
                Status = request.Status,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.WeeklyPlans.Add(plan);
        }
        else
        {
            plan.ProjectId = request.ProjectId;
            plan.GoalsMarkdown = request.GoalsMarkdown?.Trim() ?? string.Empty;
            plan.FocusAreasJson = request.FocusAreasJson;
            plan.KeyDeliverables = request.KeyDeliverables?.Trim();
            plan.Status = request.Status;
            plan.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);

        var caller = await _dbContext.Employees.FindAsync(new object[] { callerId }, ct);
        string? projectName = null;
        if (request.ProjectId.HasValue)
        {
            var p = await _dbContext.Projects.FindAsync(new object[] { request.ProjectId.Value }, ct);
            projectName = p?.Name;
        }

        var dto = new WeeklyPlanDto(
            plan.Id,
            plan.EmployeeId,
            caller?.FullName ?? "Employee",
            plan.ProjectId,
            projectName,
            plan.WeekStartDate,
            plan.GoalsMarkdown,
            plan.FocusAreasJson,
            plan.KeyDeliverables,
            plan.Status,
            plan.CreatedAt,
            plan.UpdatedAt
        );

        return Ok(dto);
    }

    [HttpGet("daily")]
    public async Task<ActionResult<List<DailyPlanDto>>> GetDailyPlans(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? employeeId,
        CancellationToken ct = default)
    {
        var query = _dbContext.DailyPlans
            .AsNoTracking()
            .Include(dp => dp.Employee)
            .Include(dp => dp.Items)
                .ThenInclude(dpi => dpi.WorkItem)
            .AsQueryable();

        if (date.HasValue)
        {
            query = query.Where(dp => dp.PlanDate == date.Value);
        }

        if (employeeId.HasValue)
        {
            query = query.Where(dp => dp.EmployeeId == employeeId.Value);
        }

        var plans = await query.OrderByDescending(dp => dp.PlanDate).ToListAsync(ct);

        var result = plans.Select(dp => new DailyPlanDto(
            dp.Id,
            dp.EmployeeId,
            dp.Employee?.FullName ?? "Employee",
            dp.PlanDate,
            dp.SubmittedAt,
            dp.IsSubmittedOnTime,
            dp.Blockers,
            dp.SummaryNotes,
            dp.Items.Select(i => new DailyPlanItemDto(
                i.Id,
                i.DailyPlanId,
                i.WorkItemId,
                i.WorkItem?.Code,
                i.WorkItem?.Title,
                i.CustomTaskTitle,
                i.PlannedHours,
                i.GoalNote,
                i.IsCompleted
            )).ToList()
        )).ToList();

        return Ok(result);
    }

    [HttpPost("daily")]
    public async Task<ActionResult<DailyPlanDto>> SubmitDailyPlan(
        [FromBody] SubmitDailyPlanRequestDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();

        // Validate planned hours for each item
        if (request.Items != null)
        {
            foreach (var item in request.Items)
            {
                if (item.PlannedHours < 0.1m || item.PlannedHours > 24.0m)
                {
                    return BadRequest(new { message = "Planned hours for each task must be between 0.1 and 24.0 hours." });
                }
            }
        }

        // Resolve planning cutoff configuration (project-specific override before global default)
        Guid? targetProjectId = null;
        var workItemIds = request.Items?
            .Where(i => i.WorkItemId.HasValue)
            .Select(i => i.WorkItemId!.Value)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (workItemIds.Count > 0)
        {
            var linkedProjectIds = await _dbContext.WorkItems
                .AsNoTracking()
                .Where(w => workItemIds.Contains(w.Id))
                .Select(w => w.ProjectId)
                .Distinct()
                .ToListAsync(ct);

            if (linkedProjectIds.Count == 1)
            {
                targetProjectId = linkedProjectIds[0];
            }
            else if (linkedProjectIds.Count > 1 && request.ProjectId.HasValue && linkedProjectIds.Contains(request.ProjectId.Value))
            {
                targetProjectId = request.ProjectId.Value;
            }
            else if (linkedProjectIds.Count > 1)
            {
                targetProjectId = await _dbContext.PlanningCutoffConfigs
                    .AsNoTracking()
                    .Where(c => c.ProjectId.HasValue && linkedProjectIds.Contains(c.ProjectId.Value))
                    .OrderBy(c => c.ProjectId)
                    .Select(c => c.ProjectId)
                    .FirstOrDefaultAsync(ct);
            }
        }

        targetProjectId ??= request.ProjectId;

        PlanningCutoffConfig? cutoffConfig = null;
        if (targetProjectId.HasValue)
        {
            cutoffConfig = await _dbContext.PlanningCutoffConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ProjectId == targetProjectId.Value, ct);
        }

        cutoffConfig ??= await _dbContext.PlanningCutoffConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == null, ct)
            ?? new PlanningCutoffConfig
            {
                CutoffTimeLocal = new TimeOnly(10, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true
            };

        var dailyPlan = await _dbContext.DailyPlans
            .Include(dp => dp.Items)
            .FirstOrDefaultAsync(dp => dp.EmployeeId == callerId && dp.PlanDate == request.PlanDate, ct);

        var now = DateTimeOffset.UtcNow;

        if (dailyPlan == null)
        {
            dailyPlan = new DailyPlan
            {
                EmployeeId = callerId,
                PlanDate = request.PlanDate,
                SubmittedAt = now,
                Blockers = request.Blockers?.Trim(),
                SummaryNotes = request.SummaryNotes?.Trim()
            };
            dailyPlan.EvaluateSubmissionTiming(cutoffConfig, now);
            _dbContext.DailyPlans.Add(dailyPlan);
        }
        else
        {
            dailyPlan.Blockers = request.Blockers?.Trim();
            dailyPlan.SummaryNotes = request.SummaryNotes?.Trim();
            dailyPlan.UpdatedAt = now;
            if (dailyPlan.Items.Count > 0)
            {
                _dbContext.DailyPlanItems.RemoveRange(dailyPlan.Items.ToList());
                dailyPlan.Items.Clear();
            }
        }

        if (request.Items != null)
        {
            foreach (var item in request.Items)
            {
                var planItem = new DailyPlanItem
                {
                    DailyPlanId = dailyPlan.Id,
                    DailyPlan = dailyPlan,
                    WorkItemId = item.WorkItemId,
                    CustomTaskTitle = item.CustomTaskTitle?.Trim(),
                    PlannedHours = item.PlannedHours,
                    GoalNote = item.GoalNote?.Trim()
                };
                _dbContext.DailyPlanItems.Add(planItem);
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        var caller = await _dbContext.Employees.FindAsync(new object[] { callerId }, ct);

        await _hubContext.Clients.All.DailyPlanSubmitted(new DailyPlanSubmittedDto(
            callerId,
            caller?.FullName ?? "Employee",
            dailyPlan.PlanDate,
            dailyPlan.IsSubmittedOnTime,
            dailyPlan.SubmittedAt,
            dailyPlan.Items.Count
        ));

        // Re-query with includes for response
        var reloadedPlan = await _dbContext.DailyPlans
            .AsNoTracking()
            .Include(dp => dp.Items)
                .ThenInclude(dpi => dpi.WorkItem)
            .FirstAsync(dp => dp.Id == dailyPlan.Id, ct);

        var dto = new DailyPlanDto(
            reloadedPlan.Id,
            reloadedPlan.EmployeeId,
            caller?.FullName ?? "Employee",
            reloadedPlan.PlanDate,
            reloadedPlan.SubmittedAt,
            reloadedPlan.IsSubmittedOnTime,
            reloadedPlan.Blockers,
            reloadedPlan.SummaryNotes,
            reloadedPlan.Items.Select(i => new DailyPlanItemDto(
                i.Id,
                i.DailyPlanId,
                i.WorkItemId,
                i.WorkItem?.Code,
                i.WorkItem?.Title,
                i.CustomTaskTitle,
                i.PlannedHours,
                i.GoalNote,
                i.IsCompleted
            )).ToList()
        );

        return Ok(dto);
    }

    [HttpGet("cutoff-config")]
    public async Task<ActionResult<PlanningCutoffConfigDto>> GetCutoffConfig(
        [FromQuery] Guid? projectId,
        CancellationToken ct)
    {
        var config = await _dbContext.PlanningCutoffConfigs
            .AsNoTracking()
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

        if (config == null)
        {
            // Global default fallback
            config = await _dbContext.PlanningCutoffConfigs
                .AsNoTracking()
                .Include(c => c.Project)
                .FirstOrDefaultAsync(c => c.ProjectId == null, ct);
        }

        if (config == null)
        {
            return Ok(new PlanningCutoffConfigDto(
                Guid.Empty,
                null,
                null,
                new TimeOnly(10, 0),
                "UTC",
                true,
                "[0, 6]",
                DateTimeOffset.UtcNow
            ));
        }

        return Ok(new PlanningCutoffConfigDto(
            config.Id,
            config.ProjectId,
            config.Project?.Name,
            config.CutoffTimeLocal,
            config.TimeZoneId,
            config.IsReminderEnabled,
            config.ExcludedDaysOfWeekJson,
            config.UpdatedAt
        ));
    }

    [HttpPut("cutoff-config")]
    [Authorize(Roles = "TeamLead,HigherManagement")]
    public async Task<ActionResult<PlanningCutoffConfigDto>> UpdateCutoffConfig(
        [FromBody] UpdatePlanningCutoffConfigDto request,
        CancellationToken ct)
    {
        var normalizedTimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? "UTC"
            : request.TimeZoneId.Trim();

        if (!DailyPlanCutoffReminderJob.TryResolveTimeZone(normalizedTimeZoneId, out _))
        {
            return BadRequest(new { message = $"Unrecognized TimeZoneId '{request.TimeZoneId}'." });
        }

        string? projectName = null;
        if (request.ProjectId.HasValue)
        {
            var project = await _dbContext.Projects.FindAsync(new object[] { request.ProjectId.Value }, ct);
            if (project == null)
            {
                return NotFound(new { message = $"Project '{request.ProjectId.Value}' not found." });
            }
            projectName = project.Name;
        }

        var canonicalExcludedDaysJson = request.ExcludedDaysOfWeekJson != null
            ? JsonSerializer.Serialize(
                DailyPlanCutoffReminderJob.ParseExcludedDays(request.ExcludedDaysOfWeekJson)
                    .OrderBy(d => d)
                    .ToList())
            : "[0, 6]";

        var config = await _dbContext.PlanningCutoffConfigs
            .FirstOrDefaultAsync(c => c.ProjectId == request.ProjectId, ct);

        if (config == null)
        {
            config = new PlanningCutoffConfig
            {
                ProjectId = request.ProjectId,
                CutoffTimeLocal = request.CutoffTimeLocal,
                TimeZoneId = normalizedTimeZoneId,
                IsReminderEnabled = request.IsReminderEnabled,
                ExcludedDaysOfWeekJson = canonicalExcludedDaysJson,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.PlanningCutoffConfigs.Add(config);
        }
        else
        {
            config.CutoffTimeLocal = request.CutoffTimeLocal;
            config.TimeZoneId = normalizedTimeZoneId;
            config.IsReminderEnabled = request.IsReminderEnabled;
            config.ExcludedDaysOfWeekJson = canonicalExcludedDaysJson;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new PlanningCutoffConfigDto(
            config.Id,
            config.ProjectId,
            projectName,
            config.CutoffTimeLocal,
            config.TimeZoneId,
            config.IsReminderEnabled,
            config.ExcludedDaysOfWeekJson,
            config.UpdatedAt
        ));
    }

    [HttpPost("cutoff-check/run-now")]
    [Authorize(Roles = "TeamLead,HigherManagement")]
    public async Task<ActionResult<CutoffExecutionSummaryDto>> RunCutoffCheckNow(
        [FromQuery] Guid? projectId = null,
        CancellationToken ct = default)
    {
        var summary = projectId.HasValue
            ? await _cutoffJob.ExecuteCutoffCheckAsync(projectId, ct)
            : await _cutoffJob.ExecuteCutoffCheckAsync(ct);
        return Ok(summary);
    }
}
