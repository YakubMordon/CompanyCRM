using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.ValueObjects;

public record ProjectExternalConfig(
    ExternalSourceType SourceType = ExternalSourceType.None,
    string? OrgUrl = null,
    string? ProjectName = null,
    string? Pat = null,
    bool IsSyncEnabled = false,
    bool IsSimulatedMode = true,
    Guid? ProjectId = null,
    DateTimeOffset? LastSyncRunAt = null)
{
    public ProjectExternalConfig(
        Guid projectId,
        ExternalSourceType sourceType,
        string? orgUrl,
        string? projectName,
        string? pat,
        bool isSyncEnabled,
        bool isSimulatedMode,
        DateTimeOffset? lastSyncRunAt = null)
        : this(sourceType, orgUrl, projectName, pat, isSyncEnabled, isSimulatedMode, projectId, lastSyncRunAt)
    {
    }
}
