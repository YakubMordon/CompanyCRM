namespace CompanyCRM.Domain.Enums;

/// <summary>
/// Application security roles governing API and UI authorization.
/// </summary>
public enum AppRole
{
    Employee = 0,
    TeamLead = 1,
    HigherManagement = 2
}

/// <summary>
/// Seniority ranking for employees.
/// </summary>
public enum SeniorityRank
{
    Junior = 0,
    Mid = 1,
    Senior = 2,
    Lead = 3,
    Principal = 4
}

/// <summary>
/// Role of an employee within a specific project.
/// </summary>
public enum ProjectRole
{
    Member = 0,
    Lead = 1,
    Manager = 2
}

/// <summary>
/// Classification of work items.
/// </summary>
public enum WorkItemType
{
    Task = 0,
    Bug = 1,
    Story = 2
}

/// <summary>
/// Lifecycle status of a work item across review, backlog, and Kanban workflow.
/// </summary>
public enum WorkItemStatus
{
    Draft = 0,
    PendingReview = 1,
    ClarificationRequested = 2,
    Denied = 3,
    Backlog = 4,
    Todo = 5,
    InProgress = 6,
    InVerify = 7,
    Done = 8
}

/// <summary>
/// Priority level of a work item.
/// </summary>
public enum WorkItemPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Dedicated reviewer approval state machine.
/// </summary>
public enum ReviewStatus
{
    NotRequired = 0,
    Pending = 1,
    Approved = 2,
    ClarificationNeeded = 3,
    Denied = 4
}

/// <summary>
/// External issue tracker provider type.
/// </summary>
public enum ExternalSourceType
{
    None = 0,
    AzureDevOps = 1,
    Jira = 2,
    GitHub = 3
}

/// <summary>
/// Origin of the last synchronization modification to prevent echo loops.
/// </summary>
public enum SyncOrigin
{
    LocalCrm = 0,
    ExternalWebhook = 1,
    BackgroundReconciliation = 2
}

/// <summary>
/// Nature of an external synchronization conflict.
/// </summary>
public enum SyncConflictType
{
    DuplicateCandidate = 0,
    ConcurrentUpdate = 1
}

/// <summary>
/// Current status of a synchronization conflict.
/// </summary>
public enum ConflictStatus
{
    Pending = 0,
    Resolved = 1
}

/// <summary>
/// User decision applied to resolve a synchronization conflict.
/// </summary>
public enum ConflictResolutionAction
{
    Unresolved = 0,
    Merge = 1,
    KeepBoth = 2,
    Skip = 3
}

/// <summary>
/// Submission status of a weekly plan.
/// </summary>
public enum WeeklyPlanStatus
{
    Draft = 0,
    Submitted = 1,
    Completed = 2,
    Approved = 3
}
