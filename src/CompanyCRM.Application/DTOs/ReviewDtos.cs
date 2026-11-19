namespace CompanyCRM.Application.DTOs;

using CompanyCRM.Domain.Enums;

public record WorkItemReviewQueueDto(
    Guid WorkItemId,
    string WorkItemCode,
    string WorkItemTitle,
    string? DescriptionPreview,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    WorkItemType Type,
    WorkItemPriority Priority,
    Guid CreatedById,
    string CreatedByName,
    Guid? ReviewerId,
    string? ReviewerName,
    ReviewStatus ReviewStatus,
    string? LatestComment,
    DateTimeOffset CreatedAt
)
{
    // Dual-contract alias properties serialized to camelCase JSON for seamless frontend compatibility
    public Guid Id => WorkItemId;
    public string Code => WorkItemCode;
    public string Title => WorkItemTitle;
    public Guid CreatorId => CreatedById;
    public string CreatorName => CreatedByName;
    public string? ReviewComment => LatestComment;
    public DateTimeOffset RequestedAt => CreatedAt;
}

public record ApproveReviewRequestDto(
    WorkItemStatus StartingStatus = WorkItemStatus.Todo,
    string? ApprovalNote = null
);

public record ClarifyReviewRequestDto(
    string Comment
);

public record DenyReviewRequestDto(
    string Reason
);
