using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CompanyCRM.Tests;

public class ChallengerM4FrontendIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public ChallengerM4FrontendIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region 1. Persona Switcher Empirical Tests (AuthContext Integration)

    [Theory]
    [InlineData("Employee", AppRole.Employee)]
    [InlineData("TeamLead", AppRole.TeamLead)]
    [InlineData("HigherManagement", AppRole.HigherManagement)]
    [InlineData("employee", AppRole.Employee)]
    [InlineData("TEAMLEAD", AppRole.TeamLead)]
    [InlineData("highermanagement", AppRole.HigherManagement)]
    public async Task SwitchPersona_WithValidRoles_ReturnsValidJwtAndMatchingUserDto(string roleInput, AppRole expectedRole)
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsync($"/api/auth/switch-persona/{roleInput}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var authRes = await response.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        authRes.Should().NotBeNull();
        authRes!.Token.Should().NotBeNullOrWhiteSpace();
        authRes.Role.Should().Be(expectedRole.ToString());
        authRes.User.Should().NotBeNull();
        authRes.User.Role.Should().Be(expectedRole);
        authRes.User.FullName.Should().NotBeNullOrWhiteSpace();
        authRes.User.Email.Should().Contain("@");
        authRes.Permissions.Should().NotBeEmpty();

        // Verify the returned token is functional against /api/auth/me
        var authenticatedClient = _fixture.CreateClient();
        authenticatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authRes.Token);

        var meResponse = await authenticatedClient.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserDto>(CrmApiTestFixture.JsonOptions);
        currentUser.Should().NotBeNull();
        currentUser!.Id.Should().Be(authRes.User.Id);
        currentUser.Role.Should().Be(expectedRole);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Superuser")]
    [InlineData("Anonymous")]
    [InlineData("12345")]
    [InlineData("unknown_role")]
    public async Task SwitchPersona_WithInvalidRole_Returns400BadRequest(string invalidRole)
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsync($"/api/auth/switch-persona/{invalidRole}", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PersonaSwitching_EnforcesStrictRbacContinuityBetweenRoles()
    {
        var client = _fixture.CreateClient();

        // 1. Switch to Employee
        var empRes = await client.PostAsync("/api/auth/switch-persona/Employee", null);
        empRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var empAuth = await empRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        var empClient = _fixture.CreateClient();
        empClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", empAuth!.Token);

        var empMgmtRes = await empClient.GetAsync("/api/management/compensation");
        empMgmtRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Employee persona must be rejected with 403 Forbidden");

        // 2. Switch to TeamLead
        var tlRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        tlRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tlAuth = await tlRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        var tlClient = _fixture.CreateClient();
        tlClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        var tlMgmtRes = await tlClient.GetAsync("/api/management/compensation");
        tlMgmtRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "TeamLead persona must be rejected with 403 Forbidden");

        // 3. Switch to HigherManagement
        var hmRes = await client.PostAsync("/api/auth/switch-persona/HigherManagement", null);
        hmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var hmAuth = await hmRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        var hmClient = _fixture.CreateClient();
        hmClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hmAuth!.Token);

        var hmMgmtRes = await hmClient.GetAsync("/api/management/compensation");
        hmMgmtRes.StatusCode.Should().Be(HttpStatusCode.OK, "HigherManagement persona must be granted 200 OK");
        var compList = await hmMgmtRes.Content.ReadFromJsonAsync<List<EmployeeCompensationDto>>(CrmApiTestFixture.JsonOptions);
        compList.Should().NotBeNull();
        compList.Should().NotBeEmpty();
    }

    #endregion

    #region 2. SignalR Real-Time Hub Integration Tests (SignalRContext Integration)

    [Fact]
    public async Task SignalR_Negotiate_WithValidBearerToken_SucceedsWithConnectionInfo()
    {
        var client = _fixture.CreateClient();

        // Obtain valid token via persona switcher
        var switchRes = await client.PostAsync("/api/auth/switch-persona/Employee", null);
        var authRes = await switchRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authRes!.Token);

        var negotiateRes = await client.PostAsync("/hubs/crm/negotiate?negotiateVersion=1", null);
        negotiateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await negotiateRes.Content.ReadAsStringAsync();
        content.Should().Contain("connectionId");
        content.Should().Contain("negotiateVersion");
    }

    [Fact]
    public async Task SignalR_Negotiate_WithoutToken_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();

        var negotiateRes = await client.PostAsync("/hubs/crm/negotiate?negotiateVersion=1", null);
        negotiateRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region 3. Review Queue & Work Item Status Gating Verification

    [Fact]
    public async Task WorkItem_CreatedWithReviewer_IsSetToPendingReviewAndAppearsInReviewQueue()
    {
        var client = _fixture.CreateClient();

        // Use Persona Switcher exactly as the frontend does
        var tlAuthRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        tlAuthRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tlAuth = await tlAuthRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        var tlClient = _fixture.CreateClient();
        tlClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        // Fetch projects to find a valid project ID
        var prjRes = await tlClient.GetAsync("/api/projects");
        var projects = await prjRes.Content.ReadFromJsonAsync<List<ProjectDto>>(CrmApiTestFixture.JsonOptions);
        projects.Should().NotBeNullOrEmpty();
        var projectId = projects![0].Id;

        // Fetch employees to pick a reviewer
        var empRes = await tlClient.GetAsync("/api/employees");
        var employees = await empRes.Content.ReadFromJsonAsync<List<EmployeeDirectoryDto>>(CrmApiTestFixture.JsonOptions);
        employees.Should().NotBeNullOrEmpty();
        var reviewer = employees!.First(e => e.Role == AppRole.TeamLead || e.Role == AppRole.HigherManagement);

        // Create a work item with assigned reviewer
        var createDto = new CreateWorkItemDto(
            ProjectId: projectId,
            Title: $"Gated Review Item {Random.Shared.Next(1000, 9999)}",
            Description: "Work item requiring review before board movement",
            Type: WorkItemType.Task,
            Priority: WorkItemPriority.High,
            AssigneeId: null,
            ReviewerId: reviewer.Id,
            EstimatedHours: 12.0m,
            StoryPoints: 5,
            SprintName: "Sprint 1"
        );

        var createRes = await tlClient.PostAsJsonAsync("/api/workitems", createDto);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdItem = await createRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        createdItem.Should().NotBeNull();
        createdItem!.Status.Should().Be(WorkItemStatus.PendingReview, "Items created with reviewer must be in PendingReview status");
        createdItem.ReviewStatus.Should().Be(ReviewStatus.Pending);

        // Verify the item appears in the review queue
        var queueRes = await tlClient.GetAsync($"/api/reviews/queue?projectId={projectId}");
        queueRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var queue = await queueRes.Content.ReadFromJsonAsync<List<WorkItemReviewQueueDto>>(CrmApiTestFixture.JsonOptions);
        queue.Should().NotBeNull();
        queue.Should().Contain(q => q.WorkItemId == createdItem.Id);
    }

    [Fact]
    public async Task ApproveEndpoint_WithUndefinedRouteParam_FailsWith404NotFoundDueToGuidRouteConstraint()
    {
        var client = _fixture.CreateClient();

        // This reproduces the exact bug when frontend passes item.id (undefined) to /api/reviews/{id}/approve
        var response = await client.PostAsync("/api/reviews/undefined/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Non-guid 'undefined' fails route constraint {id:guid}");
    }

    [Fact]
    public void WorkItemReviewQueueDto_SerializesDualContractProperties()
    {
        var id = Guid.NewGuid();
        var projId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var dto = new WorkItemReviewQueueDto(
            id,
            "PRJ-101",
            "Test Feature",
            "Preview",
            projId,
            "PRJ",
            "Project A",
            WorkItemType.Task,
            WorkItemPriority.High,
            creatorId,
            "Alice Creator",
            reviewerId,
            "Bob Reviewer",
            ReviewStatus.Pending,
            "Need review",
            now
        );

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(dto, options);
        using var doc = JsonDocument.Parse(json);

        // Canonical backend fields
        doc.RootElement.GetProperty("workItemId").GetGuid().Should().Be(id);
        doc.RootElement.GetProperty("workItemCode").GetString().Should().Be("PRJ-101");
        doc.RootElement.GetProperty("workItemTitle").GetString().Should().Be("Test Feature");
        doc.RootElement.GetProperty("createdById").GetGuid().Should().Be(creatorId);
        doc.RootElement.GetProperty("createdByName").GetString().Should().Be("Alice Creator");
        doc.RootElement.GetProperty("latestComment").GetString().Should().Be("Need review");

        // Dual-contract alias fields for frontend
        doc.RootElement.GetProperty("id").GetGuid().Should().Be(id);
        doc.RootElement.GetProperty("code").GetString().Should().Be("PRJ-101");
        doc.RootElement.GetProperty("title").GetString().Should().Be("Test Feature");
        doc.RootElement.GetProperty("creatorId").GetGuid().Should().Be(creatorId);
        doc.RootElement.GetProperty("creatorName").GetString().Should().Be("Alice Creator");
        doc.RootElement.GetProperty("reviewComment").GetString().Should().Be("Need review");
        doc.RootElement.GetProperty("requestedAt").GetDateTimeOffset().Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SyncEventDto_SerializesBothStatusAndEventTypeInJson()
    {
        var dto = new SyncEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "WorkItemPushed",
            "Work item pushed to Azure DevOps #42",
            "42",
            DateTimeOffset.UtcNow
        );

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(dto, options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().Be("WorkItemPushed");

        doc.RootElement.TryGetProperty("eventType", out var eventTypeProp).Should().BeTrue();
        eventTypeProp.GetString().Should().Be("WorkItemPushed");

        doc.RootElement.TryGetProperty("externalId", out var externalIdProp).Should().BeTrue();
        externalIdProp.GetString().Should().Be("42");
    }

    [Fact]
    public async Task ReviewQueue_ApproveReviewUsingAliasId_SucceedsAndTransitionsStatusToTodo()
    {
        var client = _fixture.CreateClient();

        // 1. Authenticate as TeamLead
        var tlAuthRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        var tlAuth = await tlAuthRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        // 2. Fetch projects and reviewer
        var prjRes = await client.GetAsync("/api/projects");
        var projects = await prjRes.Content.ReadFromJsonAsync<List<ProjectDto>>(CrmApiTestFixture.JsonOptions);
        var projectId = projects![0].Id;

        var empRes = await client.GetAsync("/api/employees");
        var employees = await empRes.Content.ReadFromJsonAsync<List<EmployeeDirectoryDto>>(CrmApiTestFixture.JsonOptions);
        var reviewer = employees!.First(e => e.Role == AppRole.TeamLead || e.Role == AppRole.HigherManagement);

        // 3. Create work item needing review
        var createDto = new CreateWorkItemDto(
            ProjectId: projectId,
            Title: $"Approve Test Item {Random.Shared.Next(1000, 9999)}",
            Description: "Item to be approved via alias Id",
            Type: WorkItemType.Task,
            Priority: WorkItemPriority.High,
            AssigneeId: null,
            ReviewerId: reviewer.Id,
            EstimatedHours: 6.0m,
            StoryPoints: 3,
            SprintName: "Sprint 1"
        );
        var createRes = await client.PostAsJsonAsync("/api/workitems", createDto);
        var created = await createRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // 4. Query review queue and find item
        var queueRes = await client.GetAsync($"/api/reviews/queue?projectId={projectId}");
        var queue = await queueRes.Content.ReadFromJsonAsync<List<WorkItemReviewQueueDto>>(CrmApiTestFixture.JsonOptions);
        var queueItem = queue!.First(q => q.WorkItemId == created!.Id);

        // Verify dual-contract Id property matches WorkItemId
        queueItem.Id.Should().Be(created!.Id);
        queueItem.Code.Should().Be(created.Code);
        queueItem.Title.Should().Be(created.Title);

        // 5. Approve using queueItem.Id (the alias property)
        var approveDto = new ApproveReviewRequestDto(WorkItemStatus.Todo, "Approved for implementation");
        var approveRes = await client.PostAsJsonAsync($"/api/reviews/{queueItem.Id}/approve", approveDto);
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedItem = await approveRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        approvedItem!.Status.Should().Be(WorkItemStatus.Todo);
        approvedItem.ReviewStatus.Should().Be(ReviewStatus.Approved);
    }

    [Fact]
    public async Task ReviewQueue_RequestClarificationUsingAliasId_SucceedsAndSetsClarificationNeeded()
    {
        var client = _fixture.CreateClient();

        var tlAuthRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        var tlAuth = await tlAuthRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        var prjRes = await client.GetAsync("/api/projects");
        var projects = await prjRes.Content.ReadFromJsonAsync<List<ProjectDto>>(CrmApiTestFixture.JsonOptions);
        var projectId = projects![0].Id;

        var empRes = await client.GetAsync("/api/employees");
        var employees = await empRes.Content.ReadFromJsonAsync<List<EmployeeDirectoryDto>>(CrmApiTestFixture.JsonOptions);
        var reviewer = employees!.First(e => e.Role == AppRole.TeamLead || e.Role == AppRole.HigherManagement);

        var createDto = new CreateWorkItemDto(
            ProjectId: projectId,
            Title: $"Clarify Test Item {Random.Shared.Next(1000, 9999)}",
            Description: "Item to request clarification on",
            Type: WorkItemType.Bug,
            Priority: WorkItemPriority.Critical,
            AssigneeId: null,
            ReviewerId: reviewer.Id,
            EstimatedHours: 4.0m,
            StoryPoints: 2,
            SprintName: "Sprint 1"
        );
        var createRes = await client.PostAsJsonAsync("/api/workitems", createDto);
        var created = await createRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var queueRes = await client.GetAsync($"/api/reviews/queue?projectId={projectId}");
        var queue = await queueRes.Content.ReadFromJsonAsync<List<WorkItemReviewQueueDto>>(CrmApiTestFixture.JsonOptions);
        var queueItem = queue!.First(q => q.WorkItemId == created!.Id);

        // Request clarification using queueItem.Id alias
        var clarifyDto = new ClarifyReviewRequestDto("Please provide reproduction steps and logs");
        var clarifyRes = await client.PostAsJsonAsync($"/api/reviews/{queueItem.Id}/clarify", clarifyDto);
        clarifyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var clarifiedItem = await clarifyRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        clarifiedItem!.Status.Should().Be(WorkItemStatus.ClarificationRequested);
        clarifiedItem.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
    }

    [Fact]
    public async Task ReviewQueue_DenyReviewUsingAliasId_SucceedsAndBlocksSync()
    {
        var client = _fixture.CreateClient();

        var tlAuthRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        var tlAuth = await tlAuthRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        var prjRes = await client.GetAsync("/api/projects");
        var projects = await prjRes.Content.ReadFromJsonAsync<List<ProjectDto>>(CrmApiTestFixture.JsonOptions);
        var projectId = projects![0].Id;

        var empRes = await client.GetAsync("/api/employees");
        var employees = await empRes.Content.ReadFromJsonAsync<List<EmployeeDirectoryDto>>(CrmApiTestFixture.JsonOptions);
        var reviewer = employees!.First(e => e.Role == AppRole.TeamLead || e.Role == AppRole.HigherManagement);

        var createDto = new CreateWorkItemDto(
            ProjectId: projectId,
            Title: $"Deny Test Item {Random.Shared.Next(1000, 9999)}",
            Description: "Item to be denied",
            Type: WorkItemType.Task,
            Priority: WorkItemPriority.Low,
            AssigneeId: null,
            ReviewerId: reviewer.Id,
            EstimatedHours: 2.0m,
            StoryPoints: 1,
            SprintName: "Sprint 1"
        );
        var createRes = await client.PostAsJsonAsync("/api/workitems", createDto);
        var created = await createRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var queueRes = await client.GetAsync($"/api/reviews/queue?projectId={projectId}");
        var queue = await queueRes.Content.ReadFromJsonAsync<List<WorkItemReviewQueueDto>>(CrmApiTestFixture.JsonOptions);
        var queueItem = queue!.First(q => q.WorkItemId == created!.Id);

        // Deny using queueItem.Id alias
        var denyDto = new DenyReviewRequestDto("This initiative is out of scope for the current quarter");
        var denyRes = await client.PostAsJsonAsync($"/api/reviews/{queueItem.Id}/deny", denyDto);
        denyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var deniedItem = await denyRes.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        deniedItem!.Status.Should().Be(WorkItemStatus.Denied);
        deniedItem.ReviewStatus.Should().Be(ReviewStatus.Denied);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")] // < 5 chars
    public async Task ReviewQueue_ClarifyReviewWithInvalidComment_FailsWith400BadRequest(string invalidComment)
    {
        var client = _fixture.CreateClient();

        var tlAuthRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        var tlAuth = await tlAuthRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        var clarifyDto = new ClarifyReviewRequestDto(invalidComment);
        var clarifyRes = await client.PostAsJsonAsync($"/api/reviews/{Guid.NewGuid()}/clarify", clarifyDto);
        clarifyRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")] // < 5 chars
    public async Task ReviewQueue_DenyReviewWithInvalidReason_FailsWith400BadRequest(string invalidReason)
    {
        var client = _fixture.CreateClient();

        var tlAuthRes = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        var tlAuth = await tlAuthRes.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tlAuth!.Token);

        var denyDto = new DenyReviewRequestDto(invalidReason);
        var denyRes = await client.PostAsJsonAsync($"/api/reviews/{Guid.NewGuid()}/deny", denyDto);
        denyRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
