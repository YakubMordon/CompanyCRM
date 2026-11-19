using CompanyCRM.Application.DTOs;

namespace CompanyCRM.Api.Hubs;

public interface ICrmHubClient
{
    Task WorkItemUpdated(WorkItemDto workItem);
    Task ReviewQueueUpdated(ReviewQueueEventDto reviewEvent);
    Task SyncStatusChanged(SyncEventDto syncEvent);
    Task DailyPlanSubmitted(DailyPlanSubmittedDto planEvent);
}
