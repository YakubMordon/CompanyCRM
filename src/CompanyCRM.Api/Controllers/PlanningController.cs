using System.Security.Claims;
using System.Text.Json;
using CompanyCRM.Api.Hubs;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/planning")]
[Authorize]
public class PlanningController : ControllerBase
{
    private readonly IPlanningRepository _planningRepository;
    private readonly IWorkItemRepository _workItemRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPlanningCutoffJob _cutoffJob;
    private readonly IHubContext<CrmHub, ICrmHubClient> _hubContext;

    public PlanningController(
        IPlanningRepository planningRepository,
        IWorkItemRepository workItemRepository,
        IProjectRepository projectRepository,
        IEmployeeRepository employeeRepository,
        IPlanningCutoffJob cutoffJob,
        IHubContext<CrmHub, ICrmHubClient> hubContext)
    {
        _planningRepository = planningRepository;
        _workItemRepository = workItemRepository;
        _projectRepository = projectRepository;
        _employeeRepository = employeeRepository;
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
        DateOnly? normalizedMonday = weekStartDate.HasValue
            ? WeeklyPlan.NormalizeToMonday(weekStartDate.Value)
            : null;

        var plans = await _planningRepository.GetWeeklyPlansAsync(normalizedMonday, projectId, employeeId, ct);

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

        var plan = await _planningRepository.GetWeeklyPlanByEmployeeAndWeekAsync(callerId, normalizedMonday, trackChanges: true, ct);

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
            _planningRepository.AddWeeklyPlan(plan);
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

        await _planningRepository.SaveChangesAsync(ct);

        var caller = await _employeeRepository.GetByIdAsync(callerId, ct);
        string? projectName = null;
        if (request.ProjectId.HasValue)
        {
            var p = await _projectRepository.GetByIdAsync(request.ProjectId.Value, trackChanges: false, ct);
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
        var plans = await _planningRepository.GetDailyPlansAsync(date, employeeId, ct);

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
            var linkedProjectIds = await _workItemRepository.GetDistinctProjectIdsByWorkItemIdsAsync(workItemIds, ct);

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
                targetProjectId = await _planningRepository.GetFirstConfiguredCutoffProjectIdAsync(linkedProjectIds, ct);
            }
        }

        targetProjectId ??= request.ProjectId;

        PlanningCutoffConfig? cutoffConfig = null;
        if (targetProjectId.HasValue)
        {
            cutoffConfig = await _planningRepository.GetCutoffConfigAsync(targetProjectId.Value, trackChanges: false, ct);
        }

        cutoffConfig ??= await _planningRepository.GetCutoffConfigAsync(null, trackChanges: false, ct)
            ?? new PlanningCutoffConfig
            {
                CutoffTimeLocal = new TimeOnly(11, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true
            };

        var now = DateTimeOffset.UtcNow;

        var probePlan = new DailyPlan
        {
            PlanDate = request.PlanDate,
            SubmittedAt = now
        };

        if (!probePlan.IsSubmissionPermitted(cutoffConfig, now))
        {
            return BadRequest(new { message = $"Daily plan check-ins for {request.PlanDate} are closed. Submissions are strictly limited until 23:59 on the plan date." });
        }

        var dailyPlan = await _planningRepository.GetDailyPlanByEmployeeAndDateAsync(callerId, request.PlanDate, trackChanges: true, ct);

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
            _planningRepository.AddDailyPlan(dailyPlan);
        }
        else
        {
            dailyPlan.Blockers = request.Blockers?.Trim();
            dailyPlan.SummaryNotes = request.SummaryNotes?.Trim();
            dailyPlan.UpdatedAt = now;
            // Only evaluate submission timing on update if it wasn't already marked on-time initially
            if (!dailyPlan.IsSubmittedOnTime)
            {
                dailyPlan.EvaluateSubmissionTiming(cutoffConfig, now);
            }
            _planningRepository.ClearDailyPlanItems(dailyPlan);
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
                _planningRepository.AddDailyPlanItem(planItem);
            }
        }

        await _planningRepository.SaveChangesAsync(ct);

        var caller = await _employeeRepository.GetByIdAsync(callerId, ct);

        await _hubContext.Clients.All.DailyPlanSubmitted(new DailyPlanSubmittedDto(
            callerId,
            caller?.FullName ?? "Employee",
            dailyPlan.PlanDate,
            dailyPlan.IsSubmittedOnTime,
            dailyPlan.SubmittedAt,
            dailyPlan.Items.Count
        ));

        // Re-query with includes for response
        var reloadedPlan = await _planningRepository.GetDailyPlanByIdWithDetailsAsync(dailyPlan.Id, ct);

        var dto = new DailyPlanDto(
            reloadedPlan!.Id,
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
        var config = await _planningRepository.GetCutoffConfigAsync(projectId, trackChanges: false, ct);

        if (config == null)
        {
            // Global default fallback
            config = await _planningRepository.GetCutoffConfigAsync(null, trackChanges: false, ct);
        }

        if (config == null)
        {
            return Ok(new PlanningCutoffConfigDto(
                Guid.Empty,
                null,
                null,
                new TimeOnly(11, 0),
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
            var project = await _projectRepository.GetByIdAsync(request.ProjectId.Value, trackChanges: false, ct);
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

        var config = await _planningRepository.GetCutoffConfigAsync(request.ProjectId, trackChanges: true, ct);

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
            _planningRepository.AddCutoffConfig(config);
        }
        else
        {
            config.CutoffTimeLocal = request.CutoffTimeLocal;
            config.TimeZoneId = normalizedTimeZoneId;
            config.IsReminderEnabled = request.IsReminderEnabled;
            config.ExcludedDaysOfWeekJson = canonicalExcludedDaysJson;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _planningRepository.SaveChangesAsync(ct);

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
