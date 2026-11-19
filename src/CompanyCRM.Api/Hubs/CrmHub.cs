using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CompanyCRM.Api.Hubs;

[Authorize]
public class CrmHub : Hub<ICrmHubClient>
{
    public const string UnscopedGroup = "unscoped";

    public static string ProjectGroup(Guid projectId) => $"project_{projectId}";

    public static IReadOnlyList<string> ProjectAndUnscopedGroups(Guid projectId) =>
        projectId == Guid.Empty
            ? new[] { UnscopedGroup }
            : new[] { ProjectGroup(projectId), UnscopedGroup };

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UnscopedGroup);
        await base.OnConnectedAsync();
    }

    public async Task JoinProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UnscopedGroup);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project_{projectId.Trim()}");
    }

    public async Task LeaveProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project_{projectId.Trim()}");
    }
}
