using System.Security.Claims;
using CompanyCRM.Api.Hubs;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly IWorkItemRepository _workItemRepository;
    private readonly IWorkItemReviewService _reviewService;
    private readonly IHubContext<CrmHub, ICrmHubClient> _hubContext;

    public ReviewsController(
        IWorkItemRepository workItemRepository,
        IWorkItemReviewService reviewService,
        IHubContext<CrmHub, ICrmHubClient> hubContext)
    {
        _workItemRepository = workItemRepository;
        _reviewService = reviewService;
        _hubContext = hubContext;
    }

    private Guid GetCallerId()
    {
        var val = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(val, out var id)) return id;
        throw new UnauthorizedAccessException("User identity is not recognized.");
    }

    [HttpGet("queue")]
    public async Task<ActionResult<List<WorkItemReviewQueueDto>>> GetReviewQueue(
        [FromQuery] Guid? projectId,
        [FromQuery] bool assignedToMeOnly = false,
        CancellationToken ct = default)
    {
        var callerId = GetCallerId();
        var reviewerId = assignedToMeOnly ? (Guid?)callerId : null;

        var items = await _workItemRepository.GetReviewQueueItemsAsync(projectId, reviewerId, ct);

        var result = items.OrderByDescending(w => w.CreatedAt).Select(w => new WorkItemReviewQueueDto(
            w.Id,
            w.Code,
            w.Title,
            w.Description != null && w.Description.Length > 100 ? w.Description[..100] + "..." : w.Description,
            w.ProjectId,
            w.Project?.Code ?? "CRM",
            w.Project?.Name ?? "Project",
            w.Type,
            w.Priority,
            w.CreatedById,
            w.CreatedBy?.FullName ?? "Unknown",
            w.ReviewerId,
            w.Reviewer?.FullName,
            w.ReviewStatus,
            w.ReviewLatestComment,
            w.CreatedAt
        )).ToList();

        return Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<WorkItemDto>> Approve(
        [FromRoute] Guid id,
        [FromBody] ApproveReviewRequestDto? request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();
        var startingStatus = request?.StartingStatus ?? WorkItemStatus.Todo;

        if (startingStatus != WorkItemStatus.Todo && startingStatus != WorkItemStatus.Backlog)
        {
            return BadRequest(new { message = "StartingStatus must be either 'Todo' or 'Backlog'." });
        }

        try
        {
            var updatedDto = await _reviewService.ApproveReviewAsync(id, callerId, startingStatus, request?.ApprovalNote, ct);

            var groups = CrmHub.ProjectAndUnscopedGroups(updatedDto.ProjectId);
            await _hubContext.Clients.Groups(groups).ReviewQueueUpdated(new ReviewQueueEventDto(
                updatedDto.Id,
                updatedDto.Code,
                updatedDto.Title,
                updatedDto.ProjectId,
                updatedDto.ReviewerId,
                "Approved",
                ReviewStatus.Approved,
                request?.ApprovalNote,
                DateTimeOffset.UtcNow
            ));

            await _hubContext.Clients.Groups(groups).WorkItemUpdated(updatedDto with { IsTimerRunningForCurrentUser = false });

            return Ok(updatedDto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/clarify")]
    public async Task<ActionResult<WorkItemDto>> Clarify(
        [FromRoute] Guid id,
        [FromBody] ClarifyReviewRequestDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();

        if (string.IsNullOrWhiteSpace(request?.Comment) || request.Comment.Trim().Length < 5)
        {
            return BadRequest(new { message = "Clarification comment is required and must contain at least 5 characters." });
        }

        try
        {
            var updatedDto = await _reviewService.RequestClarificationAsync(id, callerId, request.Comment.Trim(), ct);

            var groups = CrmHub.ProjectAndUnscopedGroups(updatedDto.ProjectId);
            await _hubContext.Clients.Groups(groups).ReviewQueueUpdated(new ReviewQueueEventDto(
                updatedDto.Id,
                updatedDto.Code,
                updatedDto.Title,
                updatedDto.ProjectId,
                updatedDto.ReviewerId,
                "ClarificationRequested",
                ReviewStatus.ClarificationNeeded,
                request.Comment.Trim(),
                DateTimeOffset.UtcNow
            ));

            await _hubContext.Clients.Groups(groups).WorkItemUpdated(updatedDto with { IsTimerRunningForCurrentUser = false });

            return Ok(updatedDto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/deny")]
    public async Task<ActionResult<WorkItemDto>> Deny(
        [FromRoute] Guid id,
        [FromBody] DenyReviewRequestDto request,
        CancellationToken ct)
    {
        var callerId = GetCallerId();

        if (string.IsNullOrWhiteSpace(request?.Reason) || request.Reason.Trim().Length < 5)
        {
            return BadRequest(new { message = "Denial reason is required and must contain at least 5 characters." });
        }

        try
        {
            var updatedDto = await _reviewService.DenyReviewAsync(id, callerId, request.Reason.Trim(), ct);

            var groups = CrmHub.ProjectAndUnscopedGroups(updatedDto.ProjectId);
            await _hubContext.Clients.Groups(groups).ReviewQueueUpdated(new ReviewQueueEventDto(
                updatedDto.Id,
                updatedDto.Code,
                updatedDto.Title,
                updatedDto.ProjectId,
                updatedDto.ReviewerId,
                "Denied",
                ReviewStatus.Denied,
                request.Reason.Trim(),
                DateTimeOffset.UtcNow
            ));

            await _hubContext.Clients.Groups(groups).WorkItemUpdated(updatedDto with { IsTimerRunningForCurrentUser = false });

            return Ok(updatedDto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
