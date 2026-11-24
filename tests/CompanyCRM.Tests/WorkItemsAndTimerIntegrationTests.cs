using System.Net;
using System.Net.Http.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class WorkItemsAndTimerIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public WorkItemsAndTimerIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(Guid ProjectId, Guid EmployeeId)> GetSeedIdsAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var project = await db.Projects.FirstAsync();
        var emp = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
        return (project.Id, emp.Id);
    }

    [Fact]
    public async Task KanbanTransition_WhenItemApproved_SucceedsWith200OK_AndLogsHistory()
    {
        var (projectId, employeeId) = await GetSeedIdsAsync();
        var client = _fixture.CreateClientWithEmployee(employeeId, "alex.rivera@companycrm.local", "Alex Rivera", AppRole.Employee);

        // 1. Create a work item without reviewer (starts in Backlog, ReviewStatus = NotRequired)
        var createResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Implement Kanban Transition Test Task",
            "Description of task",
            WorkItemType.Task,
            WorkItemPriority.High
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        created.Should().NotBeNull();
        created!.Status.Should().Be(WorkItemStatus.Backlog);

        // 2. Transition from Backlog -> InProgress
        var transResp = await client.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(
            WorkItemStatus.InProgress,
            1
        ));
        transResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await transResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        updated!.Status.Should().Be(WorkItemStatus.InProgress);

        // 3. Verify history recorded
        var historyResp = await client.GetAsync($"/api/workitems/{created.Id}/history");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var histories = await historyResp.Content.ReadFromJsonAsync<List<WorkItemHistoryDto>>(CrmApiTestFixture.JsonOptions);
        histories.Should().NotBeNull();
        histories.Should().Contain(h => h.Action == "StatusChanged" && h.NewValue == "InProgress");
    }

    [Fact]
    public async Task KanbanTransition_WhenItemPendingReview_Returns400BadRequest()
    {
        var (projectId, employeeId) = await GetSeedIdsAsync();
        var client = _fixture.CreateClientWithEmployee(employeeId, "alex.rivera@companycrm.local", "Alex Rivera", AppRole.Employee);

        Guid reviewerId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            reviewerId = lead.Id;
        }

        // Create work item with reviewer -> starts in PendingReview
        var createResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Blocked Review Item for Kanban",
            "Needs approval before moving",
            WorkItemType.Story,
            WorkItemPriority.High,
            ReviewerId: reviewerId
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        created!.Status.Should().Be(WorkItemStatus.PendingReview);

        // Attempt to move on Kanban -> MUST fail with 400 Bad Request
        var transResp = await client.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(
            WorkItemStatus.InProgress,
            0
        ));
        transResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LiveTimer_Lifecycle_Start_CheckActive_Stop_AutoFinalizesPreviousTimer()
    {
        var (projectId, employeeId) = await GetSeedIdsAsync();
        var client = _fixture.CreateClientWithEmployee(employeeId, "timer.user@companycrm.local", "Timer User", AppRole.Employee);

        // Create two work items
        var item1Resp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Timer Task 1"));
        var item1 = await item1Resp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var item2Resp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Timer Task 2"));
        var item2 = await item2Resp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // 1. Start timer on Item 1
        var start1 = await client.PostAsJsonAsync($"/api/workitems/{item1!.Id}/timer/start", new StartTimerRequestDto(true));
        start1.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry1 = await start1.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        entry1.Should().NotBeNull();
        entry1!.IsRunning.Should().BeTrue();
        entry1.WorkItemId.Should().Be(item1.Id);

        // 2. Check active timer endpoint -> returns Item 1 timer
        var activeResp = await client.GetAsync("/api/workitems/active-timer");
        activeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeEntry = await activeResp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        activeEntry.Should().NotBeNull();
        activeEntry!.WorkItemId.Should().Be(item1.Id);
        activeEntry.IsRunning.Should().BeTrue();

        // 3. Start timer on Item 2 -> auto-stops timer on Item 1
        var start2 = await client.PostAsJsonAsync($"/api/workitems/{item2!.Id}/timer/start", new StartTimerRequestDto(false));
        start2.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry2 = await start2.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        entry2!.IsRunning.Should().BeTrue();
        entry2.WorkItemId.Should().Be(item2.Id);

        // Check active timer is now Item 2
        var active2Resp = await client.GetAsync("/api/workitems/active-timer");
        active2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var active2 = await active2Resp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        active2!.WorkItemId.Should().Be(item2.Id);

        // Verify Item 1's timer was finalized
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var savedEntry1 = await db.TimeEntries.FindAsync(entry1.Id);
            savedEntry1!.IsRunning.Should().BeFalse();
            savedEntry1.EndedAt.Should().NotBeNull();
        }

        // 4. Stop timer on Item 2
        var stopResp = await client.PostAsJsonAsync($"/api/workitems/{item2.Id}/timer/stop", new StopTimerRequestDto("Finished work"));
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var stopped = await stopResp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        stopped!.IsRunning.Should().BeFalse();
        stopped.DurationMinutes.Should().BeGreaterThanOrEqualTo(1);

        // Check active timer is now null / 204
        var activeNone = await client.GetAsync("/api/workitems/active-timer");
        (activeNone.StatusCode == HttpStatusCode.NoContent || activeNone.StatusCode == HttpStatusCode.OK).Should().BeTrue();
    }

    [Fact]
    public async Task ManualTimeEntry_PersistsAndLogsWorkItemHistory()
    {
        var (projectId, employeeId) = await GetSeedIdsAsync();
        var client = _fixture.CreateClientWithEmployee(employeeId, "manual.logger@companycrm.local", "Manual Logger", AppRole.Employee);

        var itemResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Manual Log Task"));
        var item = await itemResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var manualReq = new CreateManualTimeEntryDto(
            DurationMinutes: 120,
            IsBillable: true,
            Notes: "Investigated API performance issue",
            LoggedDate: DateOnly.FromDateTime(DateTime.UtcNow)
        );

        var logResp = await client.PostAsJsonAsync($"/api/workitems/{item!.Id}/time-entries", manualReq);
        logResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var entryDto = await logResp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        entryDto.Should().NotBeNull();
        entryDto!.DurationMinutes.Should().Be(120);
        entryDto.IsBillable.Should().BeTrue();

        // Verify history contains ManualTimeLogged
        var historyResp = await client.GetAsync($"/api/workitems/{item.Id}/history");
        var histories = await historyResp.Content.ReadFromJsonAsync<List<WorkItemHistoryDto>>(CrmApiTestFixture.JsonOptions);
        histories.Should().Contain(h => h.Action == "ManualTimeLogged");
    }

    [Fact]
    public async Task GetWorkItems_UnderSqlite_SortsByBoardPositionAndCreatedAt_AndPaginatesCleanly()
    {
        var (projectId, employeeId) = await GetSeedIdsAsync();
        var client = _fixture.CreateClientWithEmployee(employeeId, "alex.rivera@companycrm.local", "Alex Rivera", AppRole.Employee);

        // Create a dedicated project so test items are isolated (requires TeamLead/HigherManagement)
        var leadClient = _fixture.CreateClientWithRole("TeamLead");
        var projResp = await leadClient.PostAsJsonAsync("/api/projects", new CreateProjectDto("SQL", "SQLite Pagination Project", "Pagination test project"));
        projResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await projResp.Content.ReadFromJsonAsync<ProjectDto>(CrmApiTestFixture.JsonOptions);
        project.Should().NotBeNull();

        // Create 3 items (BoardPosition defaults to 0 on create, CreatedAt differs)
        var r1 = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(project!.Id, "Pagination Item 1"));
        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        var i1 = await r1.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        await Task.Delay(15);
        var r2 = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(project.Id, "Pagination Item 2"));
        r2.StatusCode.Should().Be(HttpStatusCode.Created);
        var i2 = await r2.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        await Task.Delay(15);
        var r3 = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(project.Id, "Pagination Item 3"));
        r3.StatusCode.Should().Be(HttpStatusCode.Created);
        var i3 = await r3.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // Give Item 1 BoardPosition = -1 so it sorts first despite oldest CreatedAt
        var updResp = await client.PutAsJsonAsync($"/api/workitems/{i1!.Id}", new UpdateWorkItemDto(
            Title: i1.Title,
            BoardPosition: -1
        ));
        updResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Exact workspace query
        var allResp = await client.GetAsync($"/api/workitems?projectId={projectId}&page=1&pageSize=100");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allSeeded = await allResp.Content.ReadFromJsonAsync<PagedResult<WorkItemDto>>(CrmApiTestFixture.JsonOptions);
        allSeeded.Should().NotBeNull();
        allSeeded!.Items.Should().NotBeEmpty();

        // Page 1 (size 2): Item 1 (pos -1), then Item 3 (pos 0, newest CreatedAt)
        var page1Resp = await client.GetAsync($"/api/workitems?projectId={project.Id}&page=1&pageSize=2");
        page1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1 = await page1Resp.Content.ReadFromJsonAsync<PagedResult<WorkItemDto>>(CrmApiTestFixture.JsonOptions);
        page1.Should().NotBeNull();
        page1!.TotalCount.Should().Be(3);
        page1.TotalPages.Should().Be(2);
        page1.Items.Select(x => x.Id).Should().Equal(i1.Id, i3!.Id);

        // Page 2 (size 2): Item 2 (pos 0, older CreatedAt)
        var page2Resp = await client.GetAsync($"/api/workitems?projectId={project.Id}&page=2&pageSize=2");
        page2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2 = await page2Resp.Content.ReadFromJsonAsync<PagedResult<WorkItemDto>>(CrmApiTestFixture.JsonOptions);
        page2.Should().NotBeNull();
        page2!.Items.Select(x => x.Id).Should().Equal(i2!.Id);
    }
}
