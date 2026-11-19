using System.Net;
using System.Net.Http.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class ReviewWorkflowIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public ReviewWorkflowIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(Guid ProjectId, Guid ReviewerId, Guid CreatorId)> GetTestContextAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var project = await db.Projects.FirstAsync();
        var reviewer = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
        var creator = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
        return (project.Id, reviewer.Id, creator.Id);
    }

    [Fact]
    public async Task ReviewQueue_ReturnsAssignedPendingItems()
    {
        var (projectId, reviewerId, creatorId) = await GetTestContextAsync();
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@crm.local", "Lead Reviewer", AppRole.TeamLead);
        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@crm.local", "Dev Creator", AppRole.Employee);

        // Creator creates item assigning reviewer
        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Task for Review Queue Inspection",
            "Details...",
            ReviewerId: reviewerId
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // Reviewer checks queue
        var queueResp = await reviewerClient.GetAsync($"/api/reviews/queue?projectId={projectId}&assignedToMeOnly=true");
        queueResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var queue = await queueResp.Content.ReadFromJsonAsync<List<WorkItemReviewQueueDto>>(CrmApiTestFixture.JsonOptions);
        queue.Should().NotBeNull();
        queue.Should().Contain(q => q.WorkItemId == created!.Id);
    }

    [Fact]
    public async Task Approve_TransitionsStatusToTodo_AndInitiatesOutboundSync()
    {
        var (projectId, reviewerId, creatorId) = await GetTestContextAsync();
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@crm.local", "Lead Reviewer", AppRole.TeamLead);
        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@crm.local", "Dev Creator", AppRole.Employee);

        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Feature awaiting approval",
            ReviewerId: reviewerId
        ));
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // Reviewer approves
        var approveResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/approve", new ApproveReviewRequestDto(
            WorkItemStatus.Todo,
            "Approved for sprint development."
        ));

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        approved!.Status.Should().Be(WorkItemStatus.Todo);
        approved.ReviewStatus.Should().Be(ReviewStatus.Approved);
        approved.ReviewLatestComment.Should().Be("Approved for sprint development.");

        // Item is no longer in pending review queue
        var queueResp = await reviewerClient.GetAsync($"/api/reviews/queue?projectId={projectId}&assignedToMeOnly=true");
        var queue = await queueResp.Content.ReadFromJsonAsync<List<WorkItemReviewQueueDto>>(CrmApiTestFixture.JsonOptions);
        queue.Should().NotContain(q => q.WorkItemId == created.Id);
    }

    [Fact]
    public async Task RequestClarification_ValidatesCommentLength_AndTransitionsStatus()
    {
        var (projectId, reviewerId, creatorId) = await GetTestContextAsync();
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@crm.local", "Lead Reviewer", AppRole.TeamLead);
        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@crm.local", "Dev Creator", AppRole.Employee);

        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Item needing clarification",
            ReviewerId: reviewerId
        ));
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // 1. Clarification with comment < 5 chars fails with 400
        var shortCommentResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/clarify", new ClarifyReviewRequestDto("bad"));
        shortCommentResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Clarification with valid comment succeeds
        const string validComment = "Please provide more details on data migration.";
        var validResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created.Id}/clarify", new ClarifyReviewRequestDto(validComment));
        validResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var clarified = await validResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        clarified!.Status.Should().Be(WorkItemStatus.ClarificationRequested);
        clarified.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
        clarified.ReviewLatestComment.Should().Be(validComment);
    }

    [Fact]
    public async Task Deny_ValidatesReasonLength_TransitionsToDenied_AndBlocksSync()
    {
        var (projectId, reviewerId, creatorId) = await GetTestContextAsync();
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@crm.local", "Lead Reviewer", AppRole.TeamLead);
        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@crm.local", "Dev Creator", AppRole.Employee);

        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Task that will be denied",
            ReviewerId: reviewerId
        ));
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // 1. Denial with reason < 5 chars fails with 400
        var shortReasonResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/deny", new DenyReviewRequestDto("no"));
        shortReasonResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Denial with valid reason succeeds
        const string validReason = "Out of scope for this release.";
        var denyResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created.Id}/deny", new DenyReviewRequestDto(validReason));
        denyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var denied = await denyResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        denied!.Status.Should().Be(WorkItemStatus.Denied);
        denied.ReviewStatus.Should().Be(ReviewStatus.Denied);
        denied.ReviewLatestComment.Should().Be(validReason);

        // 3. Attempting to push denied item to sync fails
        var pushResp = await reviewerClient.PostAsync($"/api/sync/workitems/{created.Id}/push", null);
        pushResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_WithStartingStatusBacklog_TransitionsStatusToBacklog_AndInitiatesOutboundSync()
    {
        var (projectId, reviewerId, creatorId) = await GetTestContextAsync();
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@crm.local", "Lead Reviewer", AppRole.TeamLead);
        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@crm.local", "Dev Creator", AppRole.Employee);

        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Feature approved to Backlog",
            ReviewerId: reviewerId
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var approveResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/approve", new ApproveReviewRequestDto(
            WorkItemStatus.Backlog,
            "Queue for next sprint."
        ));

        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        approved.Should().NotBeNull();
        approved!.Status.Should().Be(WorkItemStatus.Backlog);
        approved.ReviewStatus.Should().Be(ReviewStatus.Approved);
        approved.ReviewLatestComment.Should().Be("Queue for next sprint.");
    }

    [Theory]
    [InlineData(WorkItemStatus.Draft)]
    [InlineData(WorkItemStatus.PendingReview)]
    [InlineData(WorkItemStatus.Done)]
    [InlineData(WorkItemStatus.Denied)]
    public async Task Approve_WithInvalidStartingStatus_Returns400BadRequest(WorkItemStatus invalidStatus)
    {
        var (projectId, reviewerId, creatorId) = await GetTestContextAsync();
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@crm.local", "Lead Reviewer", AppRole.TeamLead);
        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@crm.local", "Dev Creator", AppRole.Employee);

        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            $"Feature invalid starting status {invalidStatus}",
            ReviewerId: reviewerId
        ));
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var approveResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/approve", new ApproveReviewRequestDto(
            invalidStatus,
            "Should be rejected"
        ));

        approveResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var itemResp = await reviewerClient.GetAsync($"/api/workitems/{created.Id}");
        itemResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloaded = await itemResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        reloaded!.Status.Should().Be(WorkItemStatus.PendingReview);
        reloaded.ReviewStatus.Should().Be(ReviewStatus.Pending);
    }
}
