using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/notifications/webhooks")]
[Authorize(Policy = "TeamLeadOrAbove")]
public class NotificationsController : ControllerBase
{
    private readonly IChatWebhookNotificationService _chatWebhookService;

    public NotificationsController(IChatWebhookNotificationService chatWebhookService)
    {
        _chatWebhookService = chatWebhookService;
    }

    [HttpGet]
    public async Task<ActionResult<ChatWebhookConfigDto>> GetWebhookConfig(
        [FromQuery] Guid? projectId = null,
        CancellationToken ct = default)
    {
        var config = await _chatWebhookService.GetConfigAsync(projectId, ct);
        return Ok(config);
    }

    [HttpPut]
    public async Task<ActionResult<ChatWebhookConfigDto>> UpdateWebhookConfig(
        [FromBody] UpdateChatWebhookConfigDto request,
        CancellationToken ct = default)
    {
        var updated = await _chatWebhookService.UpsertConfigAsync(request, ct);
        return Ok(updated);
    }

    [HttpPost("test")]
    public async Task<ActionResult<ChatWebhookTestResultDto>> SendTestAlert(
        [FromQuery] Guid? projectId = null,
        CancellationToken ct = default)
    {
        var result = await _chatWebhookService.SendTestAlertAsync(projectId, ct);
        return Ok(result);
    }
}
