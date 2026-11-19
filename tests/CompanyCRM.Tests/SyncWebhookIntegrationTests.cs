using System.Net;
using System.Net.Http.Json;
using System.Text;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class SyncWebhookIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public SyncWebhookIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WebhookAdo_EchoPayloadMatchingLastSyncedHash_ReturnsIgnoredEcho_ZeroDbMutations()
    {
        var client = _fixture.CreateClient();

        Guid workItemId;
        string contentHash;
        string empEmail;
        DateTimeOffset initialUpdatedAt;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var emp = await db.Employees.FirstAsync();
            empEmail = emp.Email;

            contentHash = SyncContentHasher.ComputeHash(
                "Webhook Test Work Item",
                "Description for echo testing",
                WorkItemStatus.Todo,
                WorkItemType.Task,
                emp.Email);

            var item = new WorkItem
            {
                ProjectId = project.Id,
                CreatedById = emp.Id,
                Code = "CRM-ECHO-1",
                Title = "Webhook Test Work Item",
                Description = "Description for echo testing",
                Status = WorkItemStatus.Todo,
                Type = WorkItemType.Task,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "10099",
                ExternalUrl = "https://dev.azure.com/org/p/_workitems/edit/10099",
                LastSyncedContentHash = contentHash,
                LastSyncOrigin = SyncOrigin.LocalCrm,
                LastSyncedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };

            db.WorkItems.Add(item);
            await db.SaveChangesAsync();

            workItemId = item.Id;
            initialUpdatedAt = item.UpdatedAt;
        }

        // Construct standard ADO webhook payload matching existing hash
        var adoPayload = $$"""
        {
            "subscriptionId": "sub-1234",
            "eventType": "workitem.updated",
            "resource": {
                "id": 10099,
                "url": "https://dev.azure.com/org/p/_workitems/edit/10099",
                "fields": {
                    "System.Title": "Webhook Test Work Item",
                    "System.Description": "Description for echo testing",
                    "System.State": "To Do",
                    "System.WorkItemType": "Task",
                    "System.AssignedTo": "{{empEmail}}"
                }
            }
        }
        """;

        var content = new StringContent(adoPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/ado", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WebhookProcessingResultDto>(CrmApiTestFixture.JsonOptions);
        result.Should().NotBeNull();
        result!.IsEcho.Should().BeTrue();
        result.Status.Should().Be("IgnoredEcho");

        // Verify zero database mutations
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var item = await db.WorkItems.FindAsync(workItemId);
            item!.UpdatedAt.Should().Be(initialUpdatedAt);
            item.LastSyncedContentHash.Should().Be(contentHash);
        }
    }

    [Fact]
    public async Task ConflictResolution_ResolveWithSkip_MarksConflictResolved()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");

        Guid conflictId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();

            var conflict = new SyncConflict
            {
                ProjectId = project.Id,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "ADO-9999",
                ExternalTitle = "Remote conflict item to skip",
                SimilarityScore = 0.95,
                ConflictType = SyncConflictType.DuplicateCandidate,
                Status = ConflictStatus.Pending,
                ResolutionAction = ConflictResolutionAction.Unresolved,
                DetectedAt = DateTimeOffset.UtcNow
            };

            db.SyncConflicts.Add(conflict);
            await db.SaveChangesAsync();
            conflictId = conflict.Id;
        }

        // 1. Verify conflict is in pending list
        var listResp = await client.GetAsync("/api/sync/conflicts");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conflicts = await listResp.Content.ReadFromJsonAsync<List<SyncConflictDto>>(CrmApiTestFixture.JsonOptions);
        conflicts.Should().Contain(c => c.Id == conflictId);

        // 2. Resolve with Skip
        var resolveResp = await client.PostAsJsonAsync($"/api/sync/conflicts/{conflictId}/resolve", new ResolveConflictRequestDto(
            ConflictResolutionAction.Skip,
            "Skipped by test"
        ));

        resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resDto = await resolveResp.Content.ReadFromJsonAsync<ConflictResolutionResultDto>(CrmApiTestFixture.JsonOptions);
        resDto.Should().NotBeNull();
        resDto!.Resolved.Should().BeTrue();
        resDto.Action.Should().Be(ConflictResolutionAction.Skip);

        // 3. Verify in DB
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var conflict = await db.SyncConflicts.FindAsync(conflictId);
            conflict!.Status.Should().Be(ConflictStatus.Resolved);
            conflict.ResolutionAction.Should().Be(ConflictResolutionAction.Skip);
        }
    }

    [Fact]
    public async Task ConflictResolution_InvalidActionUnresolved_Returns400BadRequest()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");

        var response = await client.PostAsJsonAsync($"/api/sync/conflicts/{Guid.NewGuid()}/resolve", new ResolveConflictRequestDto(
            ConflictResolutionAction.Unresolved
        ));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PendingConflicts_IncludeLocalStatusAndAssigneeName_AndReconcileReturnsCanonicalFields()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");

        var conflictsResp = await client.GetAsync("/api/sync/conflicts");
        conflictsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conflicts = await conflictsResp.Content.ReadFromJsonAsync<List<SyncConflictDto>>(CrmApiTestFixture.JsonOptions);
        conflicts.Should().NotBeNull();

        var crm104Conflict = conflicts!.FirstOrDefault(c => c.LocalWorkItemCode == "CRM-104");
        crm104Conflict.Should().NotBeNull();
        crm104Conflict!.LocalStatus.Should().Be("InProgress");
        crm104Conflict.LocalAssigneeName.Should().Be("Maya Patel");

        Guid projectId = crm104Conflict.ProjectId;
        var reconcileResp = await client.PostAsync($"/api/sync/projects/{projectId}/reconcile", null);
        reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await reconcileResp.Content.ReadAsStringAsync();
        rawJson.Should().Contain("\"totalItemsChecked\":");
        rawJson.Should().Contain("\"conflictsCount\":");
        rawJson.Should().Contain("\"updatedCount\":");
        rawJson.Should().Contain("\"skippedCount\":");
    }

    [Fact]
    public async Task ConflictResolution_UnlinkedConflict_MergeReturns400AndLeavesPending_DtoOmitsFakeLocalText()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");

        Guid conflictId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var conflict = new SyncConflict
            {
                ProjectId = project.Id,
                LocalWorkItemId = null,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = $"ADO-UNLINKED-{Random.Shared.Next(1000, 9999)}",
                ExternalTitle = "Unlinked Remote Item",
                SimilarityScore = 0.90,
                ConflictType = SyncConflictType.DuplicateCandidate,
                Status = ConflictStatus.Pending,
                ResolutionAction = ConflictResolutionAction.Unresolved,
                DetectedAt = DateTimeOffset.UtcNow
            };
            db.SyncConflicts.Add(conflict);
            await db.SaveChangesAsync();
            conflictId = conflict.Id;
        }

        var listResp = await client.GetAsync("/api/sync/conflicts");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conflicts = await listResp.Content.ReadFromJsonAsync<List<SyncConflictDto>>(CrmApiTestFixture.JsonOptions);
        var unlinked = conflicts!.First(c => c.Id == conflictId);
        unlinked.LocalWorkItemId.Should().BeNull();
        unlinked.LocalWorkItemCode.Should().BeNull();
        unlinked.LocalTitle.Should().BeNull();

        var mergeResp = await client.PostAsJsonAsync(
            $"/api/sync/conflicts/{conflictId}/resolve",
            new ResolveConflictRequestDto(ConflictResolutionAction.Merge, "Attempt unlinked merge"));
        mergeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var persisted = await db.SyncConflicts.FindAsync(conflictId);
            persisted!.Status.Should().Be(ConflictStatus.Pending);
        }
    }
}
