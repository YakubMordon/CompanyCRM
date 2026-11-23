using System.Buffers;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.ExternalSync;

public class SyncOrchestratorService : ISyncOrchestratorService
{
    private readonly CrmDbContext _dbContext;
    private readonly IReadOnlyDictionary<ExternalSourceType, IIssueTrackerProvider> _providers;
    private readonly IIssueTrackerProvider? _defaultProvider;
    private readonly ILogger<SyncOrchestratorService> _logger;

    public SyncOrchestratorService(
        CrmDbContext dbContext,
        IIssueTrackerProvider provider,
        ILogger<SyncOrchestratorService> logger)
        : this(dbContext, new[] { provider }, logger)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public SyncOrchestratorService(
        CrmDbContext dbContext,
        IEnumerable<IIssueTrackerProvider> providers,
        ILogger<SyncOrchestratorService> logger)
    {
        _dbContext = dbContext;
        var dict = new Dictionary<ExternalSourceType, IIssueTrackerProvider>();
        foreach (var p in providers)
        {
            dict[p.SourceType] = p;
        }
        _providers = dict;
        _defaultProvider = dict.Values.FirstOrDefault();
        _logger = logger;
    }

    private IIssueTrackerProvider GetProvider(ExternalSourceType sourceType)
    {
        if (_providers.TryGetValue(sourceType, out var provider))
        {
            return provider;
        }

        if (_defaultProvider != null)
        {
            return _defaultProvider;
        }

        throw new NotSupportedException($"No issue tracker provider registered for source type: {sourceType}");
    }

    public async Task<SyncResultDto> PushWorkItemToExternalAsync(Guid workItemId, CancellationToken ct = default)
    {
        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.Assignee)
            .Include(w => w.CreatedBy)
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct)
            ?? throw new InvalidOperationException($"Work item '{workItemId}' not found.");

        if (!item.CanPushToExternal())
        {
            throw new InvalidOperationException(
                $"Cannot push work item '{item.Code}' to external tracker: status is '{item.Status}' and review status is '{item.ReviewStatus}'. Must be approved and not denied.");
        }

        var project = item.Project;
        if (!project.IsSyncEnabled || project.ExternalSource == ExternalSourceType.None)
        {
            return new SyncResultDto(
                Success: false,
                WorkItemId: item.Id,
                ExternalId: item.ExternalId,
                ExternalUrl: item.ExternalUrl,
                ContentHash: item.LastSyncedContentHash,
                ErrorMessage: "External synchronization is disabled for this project.",
                Origin: SyncOrigin.LocalCrm);
        }

        var canonicalHash = SyncContentHasher.ComputeHash(
            item.Title,
            item.Description,
            item.Status,
            item.Type,
            item.Assignee?.Email);

        var config = new ProjectExternalConfig(
            project.Id,
            project.ExternalSource,
            project.ExternalOrgUrl,
            project.ExternalProjectName,
            project.ExternalPat,
            project.IsSyncEnabled,
            project.IsSimulatedMode,
            project.LastSyncRunAt);

        var provider = GetProvider(project.ExternalSource);
        var externalDto = await provider.PushWorkItemAsync(config, item, ct);

        item.ExternalSource = project.ExternalSource;
        item.ExternalId = externalDto.ExternalId;
        item.ExternalUrl = externalDto.ExternalUrl;
        item.LastSyncedContentHash = canonicalHash;
        item.LastSyncOrigin = SyncOrigin.LocalCrm;
        item.LastSyncedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            Id = Guid.NewGuid(),
            WorkItemId = item.Id,
            ChangedById = item.CreatedById,
            Action = "SyncedToExternal",
            FieldName = "ExternalId",
            OldValue = null,
            NewValue = externalDto.ExternalId,
            Note = $"Synchronized to {project.ExternalSource} ({externalDto.ExternalUrl})",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        return new SyncResultDto(
            Success: true,
            WorkItemId: item.Id,
            ExternalId: externalDto.ExternalId,
            ExternalUrl: externalDto.ExternalUrl,
            ContentHash: canonicalHash,
            ErrorMessage: null,
            Origin: SyncOrigin.LocalCrm);
    }

    public async Task<WebhookProcessingResultDto> ProcessInboundWebhookAsync(
        ExternalSourceType source,
        string rawPayload,
        IDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        var provider = GetProvider(source);
        var parsedDto = await provider.ParseWebhookPayloadAsync(rawPayload, headers, ct);
        if (parsedDto == null || string.IsNullOrWhiteSpace(parsedDto.ExternalId))
        {
            return new WebhookProcessingResultDto(
                Status: "Error",
                ExternalId: null,
                WorkItemId: null,
                IsEcho: false,
                Message: "Failed to parse webhook payload or missing external work item ID.");
        }

        var localItem = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.Assignee)
            .FirstOrDefaultAsync(w => (w.ExternalSource == source && w.ExternalId == parsedDto.ExternalId) || w.ExternalId == parsedDto.ExternalId, ct);

        var remoteStatus = AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus(parsedDto.Status);
        var remoteType = AzureDevOpsIssueTrackerProvider.MapAdoTypeToType(parsedDto.Type);

        var incomingHash = SyncContentHasher.ComputeHash(
            parsedDto.Title,
            parsedDto.Description,
            remoteStatus,
            remoteType,
            parsedDto.AssigneeEmail);

        // Case 1: Existing local work item matches ExternalId
        if (localItem != null)
        {
            // Echo Loop Detection
            if (localItem.LastSyncedContentHash == incomingHash)
            {
                _logger.LogInformation("Ignored echo webhook for item {ExternalId}. Incoming hash matches LastSyncedContentHash.", parsedDto.ExternalId);
                return new WebhookProcessingResultDto(
                    Status: "IgnoredEcho",
                    ExternalId: parsedDto.ExternalId,
                    WorkItemId: localItem.Id,
                    IsEcho: true,
                    Message: "Echo ignored; incoming hash matches LastSyncedContentHash.");
            }

            // Genuine Remote Modification
            var oldStatus = localItem.Status;
            localItem.Title = TruncateRequired(parsedDto.Title, 200);
            localItem.Description = TruncateNullable(parsedDto.Description, 4000);
            localItem.Status = remoteStatus;
            localItem.Type = remoteType;
            localItem.LastSyncedContentHash = incomingHash;
            localItem.LastSyncOrigin = SyncOrigin.ExternalWebhook;
            localItem.LastSyncedAt = DateTimeOffset.UtcNow;
            localItem.UpdatedAt = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(parsedDto.AssigneeEmail))
            {
                var normEmail = parsedDto.AssigneeEmail.Trim().ToLowerInvariant();
                var assignee = await _dbContext.Employees.FirstOrDefaultAsync(e => e.Email.ToLower() == normEmail, ct);
                if (assignee != null) localItem.AssigneeId = assignee.Id;
            }
            else
            {
                localItem.AssigneeId = null;
            }

            _dbContext.WorkItemHistories.Add(new WorkItemHistory
            {
                Id = Guid.NewGuid(),
                WorkItemId = localItem.Id,
                ChangedById = localItem.CreatedById,
                Action = "SyncedFromExternal",
                FieldName = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = remoteStatus.ToString(),
                Note = $"Updated from external webhook {parsedDto.ExternalId}",
                ChangedAt = DateTimeOffset.UtcNow
            });

            await _dbContext.SaveChangesAsync(ct);

            return new WebhookProcessingResultDto(
                Status: "Applied",
                ExternalId: parsedDto.ExternalId,
                WorkItemId: localItem.Id,
                IsEcho: false,
                Message: "Remote update applied.");
        }

        // Case 2: Unlinked external work item arrived
        // Check if previously marked Skip across any sync-enabled project for this source + ExternalId
        bool wasSkipped = await _dbContext.SyncConflicts.AnyAsync(
            c => c.ExternalSource == source &&
                 c.ExternalId == parsedDto.ExternalId &&
                 c.ResolutionAction == ConflictResolutionAction.Skip, ct);

        if (wasSkipped)
        {
            return new WebhookProcessingResultDto(
                Status: "Skipped",
                ExternalId: parsedDto.ExternalId,
                WorkItemId: null,
                IsEcho: false,
                Message: "Remote work item was previously marked as Skip.");
        }

        var syncEnabledProjects = await _dbContext.Projects
            .Where(p => p.ExternalSource == source && p.IsSyncEnabled)
            .ToListAsync(ct);

        Project? project = null;
        if (!string.IsNullOrWhiteSpace(parsedDto.ExternalUrl) && syncEnabledProjects.Count > 1)
        {
            project = syncEnabledProjects.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.ExternalProjectName) &&
                parsedDto.ExternalUrl.Contains($"/{p.ExternalProjectName}/", StringComparison.OrdinalIgnoreCase));
        }

        project ??= syncEnabledProjects.FirstOrDefault()
            ?? await _dbContext.Projects.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No target project available for inbound webhook processing.");

        // Deduplication Search on unlinked items in project
        var unlinkedItems = await _dbContext.WorkItems
            .Include(w => w.Assignee)
            .Where(w => w.ProjectId == project.Id && w.ExternalId == null)
            .ToListAsync(ct);

        WorkItem? candidateMatch = null;
        double bestSimilarity = 0.0;

        foreach (var local in unlinkedItems)
        {
            var sim = ComputeTitleSimilarity(local.Title, parsedDto.Title);
            if (sim >= 0.85 && sim > bestSimilarity)
            {
                bestSimilarity = sim;
                candidateMatch = local;
            }
        }

        if (candidateMatch != null)
        {
            var existingConflict = await _dbContext.SyncConflicts.FirstOrDefaultAsync(
                c => c.ProjectId == project.Id &&
                     c.ExternalId == parsedDto.ExternalId &&
                     c.Status == ConflictStatus.Pending, ct);

            if (existingConflict == null)
            {
                existingConflict = new SyncConflict
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    LocalWorkItemId = candidateMatch.Id,
                    ExternalSource = source,
                    ExternalId = TruncateRequired(parsedDto.ExternalId, 100),
                    ExternalTitle = TruncateRequired(parsedDto.Title, 200),
                    ExternalDescription = TruncateNullable(parsedDto.Description, 4000),
                    ExternalStatus = TruncateRequired(parsedDto.Status, 50),
                    ExternalAssigneeEmail = TruncateNullable(parsedDto.AssigneeEmail, 254),
                    SimilarityScore = bestSimilarity,
                    ConflictType = SyncConflictType.DuplicateCandidate,
                    Status = ConflictStatus.Pending,
                    ResolutionAction = ConflictResolutionAction.Unresolved,
                    DetectedAt = DateTimeOffset.UtcNow
                };
                _dbContext.SyncConflicts.Add(existingConflict);
                await _dbContext.SaveChangesAsync(ct);
            }

            return new WebhookProcessingResultDto(
                Status: "ConflictDetected",
                ExternalId: parsedDto.ExternalId,
                WorkItemId: candidateMatch.Id,
                IsEcho: false,
                Message: $"Duplicate candidate conflict detected (Similarity={bestSimilarity:P0}) with local item {candidateMatch.Code}.",
                Conflict: ToConflictDto(existingConflict, candidateMatch));
        }

        // Novel remote work item: import into CRM
        var creator = await _dbContext.Employees.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No employee found to associate with imported work item.");

        var newItem = new WorkItem
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            CreatedById = creator.Id,
            Code = TruncateRequired($"EXT-{parsedDto.ExternalId}", 30),
            Title = TruncateRequired(parsedDto.Title, 200),
            Description = TruncateNullable(parsedDto.Description, 4000),
            Status = remoteStatus,
            Type = remoteType,
            ReviewStatus = ReviewStatus.NotRequired,
            ExternalSource = source,
            ExternalId = TruncateRequired(parsedDto.ExternalId, 100),
            ExternalUrl = TruncateNullable(parsedDto.ExternalUrl, 500),
            LastSyncedContentHash = incomingHash,
            LastSyncOrigin = SyncOrigin.ExternalWebhook,
            LastSyncedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WorkItems.Add(newItem);
        await _dbContext.SaveChangesAsync(ct);

        return new WebhookProcessingResultDto(
            Status: "Applied",
            ExternalId: parsedDto.ExternalId,
            WorkItemId: newItem.Id,
            IsEcho: false,
            Message: "Imported novel remote work item.");
    }

    public async Task<ReconciliationResultDto> RunReconciliationAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var query = _dbContext.Projects.AsQueryable();
        if (projectId.HasValue) query = query.Where(p => p.Id == projectId.Value);
        else query = query.Where(p => p.IsSyncEnabled && p.ExternalSource != ExternalSourceType.None);

        var projects = await query.ToListAsync(ct);
        int totalChecked = 0, updatedCount = 0, conflictsCount = 0, skippedCount = 0;
        var errors = new List<string>();

        foreach (var project in projects)
        {
            try
            {
                var config = new ProjectExternalConfig(
                    project.Id,
                    project.ExternalSource,
                    project.ExternalOrgUrl,
                    project.ExternalProjectName,
                    project.ExternalPat,
                    project.IsSyncEnabled,
                    project.IsSimulatedMode,
                    project.LastSyncRunAt);

                var provider = GetProvider(project.ExternalSource);
                var remoteItems = await provider.FetchRemoteWorkItemsAsync(config, project.LastSyncRunAt, ct);

                foreach (var remote in remoteItems)
                {
                    totalChecked++;

                    var existing = await _dbContext.WorkItems
                        .FirstOrDefaultAsync(w => (w.ExternalSource == project.ExternalSource && w.ExternalId == remote.ExternalId) || w.ExternalId == remote.ExternalId, ct);

                    var remoteStatus = AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus(remote.Status);
                    var remoteType = AzureDevOpsIssueTrackerProvider.MapAdoTypeToType(remote.Type);
                    var incomingHash = SyncContentHasher.ComputeHash(remote.Title, remote.Description, remoteStatus, remoteType, remote.AssigneeEmail);

                    if (existing != null)
                    {
                        if (existing.LastSyncedContentHash == incomingHash)
                        {
                            skippedCount++;
                            continue;
                        }

                        existing.Title = TruncateRequired(remote.Title, 200);
                        existing.Description = TruncateNullable(remote.Description, 4000);
                        existing.Status = remoteStatus;
                        existing.Type = remoteType;
                        existing.LastSyncedContentHash = incomingHash;
                        existing.LastSyncOrigin = SyncOrigin.BackgroundReconciliation;
                        existing.LastSyncedAt = DateTimeOffset.UtcNow;
                        existing.UpdatedAt = DateTimeOffset.UtcNow;
                        updatedCount++;
                    }
                    else
                    {
                        bool wasSkipped = await _dbContext.SyncConflicts.AnyAsync(
                            c => c.ExternalSource == project.ExternalSource &&
                                 c.ExternalId == remote.ExternalId &&
                                 c.ResolutionAction == ConflictResolutionAction.Skip, ct);

                        if (wasSkipped)
                        {
                            skippedCount++;
                            continue;
                        }

                        var unlinked = await _dbContext.WorkItems
                            .Where(w => w.ProjectId == project.Id && w.ExternalId == null)
                            .ToListAsync(ct);

                        bool conflictCreated = false;
                        foreach (var local in unlinked)
                        {
                            var sim = ComputeTitleSimilarity(local.Title, remote.Title);
                            if (sim >= 0.85)
                            {
                                var existingConflict = await _dbContext.SyncConflicts.FirstOrDefaultAsync(
                                    c => c.ProjectId == project.Id && c.ExternalId == remote.ExternalId && c.Status == ConflictStatus.Pending, ct);

                                if (existingConflict == null)
                                {
                                    _dbContext.SyncConflicts.Add(new SyncConflict
                                    {
                                        Id = Guid.NewGuid(),
                                        ProjectId = project.Id,
                                        LocalWorkItemId = local.Id,
                                        ExternalSource = project.ExternalSource,
                                        ExternalId = TruncateRequired(remote.ExternalId, 100),
                                        ExternalTitle = TruncateRequired(remote.Title, 200),
                                        ExternalDescription = TruncateNullable(remote.Description, 4000),
                                        ExternalStatus = TruncateRequired(remote.Status, 50),
                                        ExternalAssigneeEmail = TruncateNullable(remote.AssigneeEmail, 254),
                                        SimilarityScore = sim,
                                        ConflictType = SyncConflictType.DuplicateCandidate,
                                        Status = ConflictStatus.Pending,
                                        ResolutionAction = ConflictResolutionAction.Unresolved,
                                        DetectedAt = DateTimeOffset.UtcNow
                                    });
                                    conflictsCount++;
                                    conflictCreated = true;
                                }
                                break;
                            }
                        }

                        if (!conflictCreated) skippedCount++;
                    }
                }

                project.LastSyncRunAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed for project {ProjectId}", project.Id);
                errors.Add($"Project {project.Code}: {ex.Message}");
            }
        }

        return new ReconciliationResultDto(projectId, totalChecked, updatedCount, conflictsCount, skippedCount, errors, startedAt, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<SyncConflictDto>> GetPendingConflictsAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var query = _dbContext.SyncConflicts
            .Include(c => c.LocalWorkItem)
                .ThenInclude(w => w!.Assignee)
            .Where(c => c.Status == ConflictStatus.Pending);

        if (projectId.HasValue) query = query.Where(c => c.ProjectId == projectId.Value);

        var list = await query.ToListAsync(ct);
        return list.Select(c => ToConflictDto(c, c.LocalWorkItem)).ToList();
    }

    public async Task<ConflictResolutionResultDto> ResolveConflictAsync(Guid conflictId, ConflictResolutionAction action, CancellationToken ct = default)
    {
        var conflict = await _dbContext.SyncConflicts
            .Include(c => c.Project)
            .Include(c => c.LocalWorkItem)
            .FirstOrDefaultAsync(c => c.Id == conflictId, ct)
            ?? throw new InvalidOperationException($"Conflict '{conflictId}' not found.");

        if (conflict.Status == ConflictStatus.Resolved)
        {
            throw new InvalidOperationException($"Conflict '{conflictId}' is already resolved.");
        }

        WorkItem? localItem = null;
        if (conflict.LocalWorkItemId.HasValue)
        {
            localItem = await _dbContext.WorkItems
                .Include(w => w.Assignee)
                .FirstOrDefaultAsync(w => w.Id == conflict.LocalWorkItemId.Value, ct);
        }

        if (action == ConflictResolutionAction.Merge)
        {
            if (conflict.LocalWorkItemId == null || localItem == null)
            {
                throw new InvalidOperationException(
                    $"Cannot merge conflict '{conflictId}' because it has no linked local work item.");
            }

            if (!string.IsNullOrWhiteSpace(localItem.ExternalId) && localItem.ExternalId != conflict.ExternalId)
            {
                throw new InvalidOperationException($"Local work item '{localItem.Code}' is already linked to external ID '{localItem.ExternalId}'.");
            }
        }

        conflict.Status = ConflictStatus.Resolved;
        conflict.ResolutionAction = action;
        conflict.ResolvedAt = DateTimeOffset.UtcNow;
        conflict.ResolutionNotes = $"Resolved via {action} at {DateTimeOffset.UtcNow:O}";

        Guid? resolvedWorkItemId = null;

        switch (action)
        {
            case ConflictResolutionAction.Merge:
                localItem!.ExternalSource = conflict.ExternalSource;
                localItem.ExternalId = conflict.ExternalId;
                var projectName = conflict.Project?.ExternalProjectName ?? "CompanyProject";
                localItem.ExternalUrl = $"https://dev.azure.com/simulated-org/{projectName}/_workitems/edit/{conflict.ExternalId}";
                localItem.LastSyncedContentHash = SyncContentHasher.ComputeHash(
                    localItem.Title, localItem.Description, localItem.Status, localItem.Type, localItem.Assignee?.Email);
                localItem.LastSyncOrigin = SyncOrigin.LocalCrm;
                localItem.LastSyncedAt = DateTimeOffset.UtcNow;
                localItem.UpdatedAt = DateTimeOffset.UtcNow;

                _dbContext.WorkItemHistories.Add(new WorkItemHistory
                {
                    Id = Guid.NewGuid(),
                    WorkItemId = localItem.Id,
                    ChangedById = localItem.CreatedById,
                    Action = "ConflictResolved",
                    FieldName = "ExternalId",
                    NewValue = conflict.ExternalId,
                    Note = $"Merged with external work item {conflict.ExternalId}",
                    ChangedAt = DateTimeOffset.UtcNow
                });

                resolvedWorkItemId = localItem.Id;
                break;

            case ConflictResolutionAction.KeepBoth:
                var creator = await _dbContext.Employees.FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("No employee found in database.");

                var remoteStatus = AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus(conflict.ExternalStatus);
                var remoteType = WorkItemType.Task;
                var hash = SyncContentHasher.ComputeHash(conflict.ExternalTitle, conflict.ExternalDescription, remoteStatus, remoteType, conflict.ExternalAssigneeEmail);

                var keepBothCode = !string.IsNullOrEmpty(conflict.ExternalId) && conflict.ExternalId.StartsWith("ADO-", StringComparison.OrdinalIgnoreCase)
                    ? conflict.ExternalId
                    : $"ADO-{conflict.ExternalId}";

                var keepBothProjectName = conflict.Project?.ExternalProjectName ?? "CompanyProject";
                var newItem = new WorkItem
                {
                    Id = Guid.NewGuid(),
                    ProjectId = conflict.ProjectId,
                    CreatedById = creator.Id,
                    Code = TruncateRequired(keepBothCode, 30),
                    Title = TruncateRequired(conflict.ExternalTitle, 200),
                    Description = TruncateNullable(conflict.ExternalDescription, 4000),
                    Status = remoteStatus,
                    Type = remoteType,
                    ReviewStatus = ReviewStatus.NotRequired,
                    ExternalSource = conflict.ExternalSource,
                    ExternalId = TruncateRequired(conflict.ExternalId, 100),
                    ExternalUrl = TruncateNullable($"https://dev.azure.com/simulated-org/{keepBothProjectName}/_workitems/edit/{conflict.ExternalId}", 500),
                    LastSyncedContentHash = hash,
                    LastSyncOrigin = SyncOrigin.ExternalWebhook,
                    LastSyncedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.WorkItems.Add(newItem);
                resolvedWorkItemId = newItem.Id;
                break;

            case ConflictResolutionAction.Skip:
                // No item created or modified; remote ID permanently marked resolved with Skip
                break;
        }

        await _dbContext.SaveChangesAsync(ct);
        return new ConflictResolutionResultDto(conflict.Id, action, true, resolvedWorkItemId, $"Conflict resolved via {action}.");
    }

    public static double ComputeTitleSimilarity(string titleA, string titleB)
    {
        if (string.IsNullOrWhiteSpace(titleA) || string.IsNullOrWhiteSpace(titleB)) return 0.0;

        var s1 = titleA.Trim().ToLowerInvariant();
        var s2 = titleB.Trim().ToLowerInvariant();
        if (s1 == s2) return 1.0;

        // Token Jaccard
        var tokens1 = Tokenize(s1);
        var tokens2 = Tokenize(s2);
        double jaccard = 0.0;
        if (tokens1.Count > 0 && tokens2.Count > 0)
        {
            var intersection = tokens1.Intersect(tokens2).Count();
            var union = tokens1.Union(tokens2).Count();
            jaccard = union > 0 ? (double)intersection / union : 0.0;
        }

        // Trim common prefix and suffix before Levenshtein (exact invariant, avoids needless work)
        int maxLen = Math.Max(s1.Length, s2.Length);
        var span1 = s1.AsSpan();
        var span2 = s2.AsSpan();

        int prefix = 0;
        int minLen = Math.Min(span1.Length, span2.Length);
        while (prefix < minLen && span1[prefix] == span2[prefix]) prefix++;
        span1 = span1[prefix..];
        span2 = span2[prefix..];

        int suffix = 0;
        minLen = Math.Min(span1.Length, span2.Length);
        while (suffix < minLen && span1[span1.Length - 1 - suffix] == span2[span2.Length - 1 - suffix]) suffix++;
        span1 = span1[..(span1.Length - suffix)];
        span2 = span2[..(span2.Length - suffix)];

        // Ensure span1 is the longer span and span2 is the shorter span for O(min(m, n)) space
        if (span1.Length < span2.Length)
        {
            var tmp = span1;
            span1 = span2;
            span2 = tmp;
        }

        // Cap oversized differing substrings to prevent CPU DoS while still bounding edit distance
        const int MaxCompareLength = 4096;
        int truncatedLengthPenalty = 0;
        if (span1.Length > MaxCompareLength)
        {
            if (span2.Length > MaxCompareLength)
            {
                truncatedLengthPenalty = span1.Length - span2.Length;
                span1 = span1[..MaxCompareLength];
                span2 = span2[..MaxCompareLength];
            }
            else
            {
                truncatedLengthPenalty = span1.Length - MaxCompareLength;
                span1 = span1[..MaxCompareLength];
            }
        }

        int m = span1.Length;
        int n = span2.Length;
        int distance;
        if (n == 0)
        {
            distance = m + truncatedLengthPenalty;
        }
        else
        {
            var prevRow = ArrayPool<int>.Shared.Rent(n + 1);
            try
            {
                for (int j = 0; j <= n; j++) prevRow[j] = j;

                for (int i = 1; i <= m; i++)
                {
                    int prevDiag = prevRow[0];
                    prevRow[0] = i;
                    char c1 = span1[i - 1];

                    for (int j = 1; j <= n; j++)
                    {
                        int oldDiag = prevRow[j];
                        int cost = (c1 == span2[j - 1]) ? 0 : 1;
                        int best = Math.Min(prevRow[j] + 1, prevRow[j - 1] + 1);
                        prevRow[j] = Math.Min(best, prevDiag + cost);
                        prevDiag = oldDiag;
                    }
                }

                distance = prevRow[n] + truncatedLengthPenalty;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(prevRow);
            }
        }

        double levenshtein = Math.Max(0.0, 1.0 - ((double)distance / maxLen));

        return Math.Max(jaccard, levenshtein);
    }

    private static string TruncateRequired(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? TruncateNullable(string? value, int maxLength)
    {
        if (value == null) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static HashSet<string> Tokenize(string text)
    {
        var delimiters = new[] { ' ', '\t', '\r', '\n', '-', '_', ':', '.', ',', ';', '/', '\\', '(', ')', '[', ']' };
        return new HashSet<string>(text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static SyncConflictDto ToConflictDto(SyncConflict c, WorkItem? local) => new(
        Id: c.Id,
        ProjectId: c.ProjectId,
        LocalWorkItemId: c.LocalWorkItemId,
        LocalWorkItemCode: local?.Code,
        LocalTitle: local?.Title,
        ExternalSource: c.ExternalSource,
        ExternalId: c.ExternalId,
        ExternalTitle: c.ExternalTitle,
        ExternalDescription: c.ExternalDescription,
        ExternalStatus: c.ExternalStatus,
        ExternalAssigneeEmail: c.ExternalAssigneeEmail,
        SimilarityScore: c.SimilarityScore,
        ConflictType: c.ConflictType,
        Status: c.Status,
        ResolutionAction: c.ResolutionAction,
        ResolutionNotes: c.ResolutionNotes,
        DetectedAt: c.DetectedAt,
        ResolvedAt: c.ResolvedAt,
        LocalStatus: local?.Status.ToString(),
        LocalAssigneeName: local?.Assignee?.FullName);
}
