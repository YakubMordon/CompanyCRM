using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public class ChallengerM3AdversarialSuiteTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public ChallengerM3AdversarialSuiteTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region 1. Financial Calculations & Division-by-Zero Stress Testing

    [Fact]
    public async Task Profitability_ProjectWithZeroHoursAndZeroWorkItems_ReturnsZeroMarginsWithoutException()
    {
        // Arrange: Create a project with zero items and zero time logs
        Guid emptyProjectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emptyProject = new Project
            {
                Code = $"EMPZ{Random.Shared.Next(100, 999)}",
                Name = "Zero Activity Stress Project",
                Description = "Adversarial test project with no work items or time entries",
                IsActive = true
            };
            db.Projects.Add(emptyProject);
            await db.SaveChangesAsync();
            emptyProjectId = emptyProject.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");

        // Act
        var response = await mgmtClient.GetAsync($"/api/management/profitability?projectId={emptyProjectId}");

        // Assert: Must return 200 OK with 0.00% margin, zero revenue, zero internal cost, no NaN or DivideByZeroException
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ProjectProfitabilityDto>(CrmApiTestFixture.JsonOptions);
        result.Should().NotBeNull();
        result!.ProjectId.Should().Be(emptyProjectId);
        result.TotalLoggedHours.Should().Be(0.0m);
        result.TotalBillableHours.Should().Be(0.0m);
        result.TotalNonBillableHours.Should().Be(0.0m);
        result.TotalRevenue.Should().Be(0.0m);
        result.TotalInternalCost.Should().Be(0.0m);
        result.GrossProfit.Should().Be(0.0m);
        result.GrossMarginPercent.Should().Be(0.0m);
        result.EmployeeBreakdowns.Should().BeEmpty();
    }

    [Fact]
    public async Task Profitability_ProjectWithNonZeroCostAndZeroRevenue_ReturnsZeroMarginWithoutDivisionByZero()
    {
        Guid testProjectId;
        Guid testEmpId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

            var zeroRevEmp = new Employee
            {
                FullName = "Cost Only Employee",
                Email = $"costonly_{Guid.NewGuid():N}@test.local",
                Department = "Internal",
                JobTitle = "Intern",
                Role = AppRole.Employee,
                AnnualBaseSalary = 50000m,
                HourlyCostRate = 45.0m,
                HourlyBillableRate = 0.0m, // 0 billing rate
                WeeklyCapacityHours = 40.0m
            };
            db.Employees.Add(zeroRevEmp);

            var project = new Project
            {
                Code = $"ZREV{Random.Shared.Next(100, 999)}",
                Name = "Zero Revenue Project",
                IsActive = true
            };
            db.Projects.Add(project);

            var item = new WorkItem
            {
                ProjectId = project.Id,
                CreatedById = zeroRevEmp.Id,
                Code = $"{project.Code}-1",
                Title = "Internal Non-Billable Task",
                Status = WorkItemStatus.Todo,
                ReviewStatus = ReviewStatus.NotRequired
            };
            db.WorkItems.Add(item);

            // Add non-billable time entry: 120 minutes @ $45 cost, $0 billable
            db.TimeEntries.Add(new TimeEntry
            {
                WorkItemId = item.Id,
                EmployeeId = zeroRevEmp.Id,
                DurationMinutes = 120,
                IsBillable = false, // Not billable
                IsRunning = false,
                LoggedDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });

            await db.SaveChangesAsync();
            testProjectId = project.Id;
            testEmpId = zeroRevEmp.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");

        // Act
        var response = await mgmtClient.GetAsync($"/api/management/profitability?projectId={testProjectId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ProjectProfitabilityDto>(CrmApiTestFixture.JsonOptions);
        result.Should().NotBeNull();
        result!.TotalRevenue.Should().Be(0.0m);
        result.TotalInternalCost.Should().Be(90.0m); // 2 hours * $45
        result.GrossProfit.Should().Be(-90.0m); // Loss of $90
        result.GrossMarginPercent.Should().Be(0.0m); // Revenue is 0, so margin percent must safely be 0.00% without division-by-zero
    }

    [Fact]
    public async Task Compensation_EmployeeWithZeroCapacity_ReturnsZeroUtilizationWithoutException()
    {
        Guid zeroCapEmpId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

            var zeroCapEmp = new Employee
            {
                FullName = "Zero Capacity Contractor",
                Email = $"zerocap_{Guid.NewGuid():N}@test.local",
                Department = "Contractors",
                JobTitle = "Advisor",
                Role = AppRole.Employee,
                AnnualBaseSalary = 0.0m,
                HourlyCostRate = 0.0m,
                HourlyBillableRate = 0.0m,
                WeeklyCapacityHours = 0.0m, // 0 capacity hours!
                TargetUtilizationPercent = 0.0m
            };
            db.Employees.Add(zeroCapEmp);
            await db.SaveChangesAsync();
            zeroCapEmpId = zeroCapEmp.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");

        // Act
        var response = await mgmtClient.GetAsync("/api/management/compensation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<EmployeeCompensationDto>>(CrmApiTestFixture.JsonOptions);
        list.Should().NotBeNull();

        var empDto = list!.FirstOrDefault(e => e.EmployeeId == zeroCapEmpId);
        empDto.Should().NotBeNull();
        empDto!.WeeklyCapacityHours.Should().Be(0.0m);
        empDto.ActualUtilizationPercent.Should().Be(0.0m); // Utilization must be 0.0m, not DivideByZeroException
        empDto.DailyPlanCompliancePercent.Should().Be(0.0m);
        empDto.OnTimeSubmissionPercent.Should().Be(0.0m);
    }

    [Fact]
    public async Task Velocity_ProjectWithZeroWorkItems_ReturnsEmptyListWithoutException()
    {
        Guid emptyProjectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = new Project { Code = $"VELZ{Random.Shared.Next(100, 999)}", Name = "Empty Velocity Project" };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            emptyProjectId = p.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");
        var response = await mgmtClient.GetAsync($"/api/management/analytics/velocity?projectId={emptyProjectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var velocities = await response.Content.ReadFromJsonAsync<List<SprintVelocityDto>>(CrmApiTestFixture.JsonOptions);
        velocities.Should().NotBeNull();
        velocities.Should().BeEmpty();
    }

    [Fact]
    public async Task HoursComparison_ProjectWithZeroWorkItems_ReturnsEmptyListWithoutException()
    {
        Guid emptyProjectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = new Project { Code = $"HRSZ{Random.Shared.Next(100, 999)}", Name = "Empty Hours Project" };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            emptyProjectId = p.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");
        var response = await mgmtClient.GetAsync($"/api/management/analytics/hours-comparison?projectId={emptyProjectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var comparisons = await response.Content.ReadFromJsonAsync<List<HoursComparisonDto>>(CrmApiTestFixture.JsonOptions);
        comparisons.Should().NotBeNull();
        comparisons.Should().BeEmpty();
    }

    [Fact]
    public async Task CompletionRates_ProjectWithZeroWorkItems_ReturnsZeroCompletionSafely()
    {
        Guid emptyProjectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = new Project { Code = $"CMPZ{Random.Shared.Next(100, 999)}", Name = "Empty Completion Project" };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            emptyProjectId = p.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");
        var response = await mgmtClient.GetAsync($"/api/management/analytics/completion-rates?projectId={emptyProjectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rates = await response.Content.ReadFromJsonAsync<CompletionRateDto>(CrmApiTestFixture.JsonOptions);
        rates.Should().NotBeNull();
        rates!.ProjectId.Should().Be(emptyProjectId);
        rates.TotalWorkItems.Should().Be(0);
        rates.CompletedWorkItems.Should().Be(0);
        rates.OverallCompletionPercent.Should().Be(0.0m); // Division by zero handled safely
    }

    [Fact]
    public async Task Burndown_ProjectWithZeroWorkItemsAndZeroPoints_ReturnsTenDaysOfZeroPointsSafely()
    {
        Guid emptyProjectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = new Project { Code = $"BDNZ{Random.Shared.Next(100, 999)}", Name = "Empty Burndown Project" };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            emptyProjectId = p.Id;
        }

        var mgmtClient = _fixture.CreateClientWithRole("HigherManagement");
        var response = await mgmtClient.GetAsync($"/api/management/analytics/burndown?projectId={emptyProjectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var burndown = await response.Content.ReadFromJsonAsync<BurndownDto>(CrmApiTestFixture.JsonOptions);
        burndown.Should().NotBeNull();
        burndown!.ProjectId.Should().Be(emptyProjectId);
        burndown.TotalStoryPoints.Should().Be(0);
        burndown.TotalEstimatedHours.Should().Be(0.0m);
        burndown.Days.Should().HaveCount(10);
        burndown.Days.All(d => d.IdealRemainingPoints == 0.0m && d.ActualRemainingPoints == 0.0m).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 1000, -1000, 0)]
    [InlineData(-100, 100, -200, 0)]
    [InlineData(500, 250, 250, 50.0)]
    public void FinancialCalculator_CalculateProfitability_StressEdgeCases(decimal revenue, decimal cost, decimal expectedProfit, decimal expectedMargin)
    {
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);
        profit.Should().Be(expectedProfit);
        margin.Should().Be(expectedMargin);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(40, 0, 0)]
    [InlineData(40, -10, 0)]
    [InlineData(20, 40, 50.0)]
    public void FinancialCalculator_CalculateUtilizationPercent_ZeroCapacityGuards(decimal billableHours, decimal capacity, decimal expectedUtil)
    {
        var util = FinancialCalculator.CalculateUtilizationPercent(billableHours, capacity);
        util.Should().Be(expectedUtil);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(-5, 2, 1, 0, 0)]
    [InlineData(20, 20, 18, 100.0, 90.0)]
    public void FinancialCalculator_CalculatePlanCompliance_ZeroWorkingDaysGuards(int workingDays, int submitted, int onTime, decimal expComp, decimal expOnTime)
    {
        var (comp, onTimeRate) = FinancialCalculator.CalculatePlanCompliance(workingDays, submitted, onTime);
        comp.Should().Be(expComp);
        onTimeRate.Should().Be(expOnTime);
    }

    #endregion

    #region 2. Work Item & Reviewer Invariants Adversarial Testing

    [Fact]
    public async Task KanbanTransition_WhenItemPendingReview_MovingToInProgressOrDone_Returns400BadRequest()
    {
        Guid projectId;
        Guid reviewerId;
        Guid creatorId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = await db.Projects.FirstAsync();
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            var dev = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
            projectId = p.Id;
            reviewerId = lead.Id;
            creatorId = dev.Id;
        }

        var client = _fixture.CreateClientWithEmployee(creatorId, "dev@test.local", "Dev", AppRole.Employee);

        // Create item with reviewer -> ReviewStatus == Pending, Status == PendingReview
        var createResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Adversarial Gate Bypass Task",
            ReviewerId: reviewerId
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        created!.ReviewStatus.Should().Be(ReviewStatus.Pending);

        // 1. Try to transition to InProgress -> MUST FAIL WITH 400
        var moveInProgress = await client.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(WorkItemStatus.InProgress));
        moveInProgress.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Try to transition to Done -> MUST FAIL WITH 400
        var moveDone = await client.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(WorkItemStatus.Done));
        moveDone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task KanbanTransition_WhenItemClarificationNeeded_MovingToInProgressOrDone_Returns400BadRequest()
    {
        Guid projectId;
        Guid reviewerId;
        Guid creatorId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = await db.Projects.FirstAsync();
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            var dev = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
            projectId = p.Id;
            reviewerId = lead.Id;
            creatorId = dev.Id;
        }

        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@test.local", "Dev", AppRole.Employee);
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@test.local", "Lead", AppRole.TeamLead);

        // Create item
        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Item Needing Clarification",
            ReviewerId: reviewerId
        ));
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // Lead requests clarification
        var clarifyResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/clarify", new ClarifyReviewRequestDto("Please add architecture diagrams."));
        clarifyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now item has ReviewStatus == ClarificationNeeded, Status == ClarificationRequested
        // Attempt transition to InProgress -> MUST return 400
        var moveInProgress = await creatorClient.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(WorkItemStatus.InProgress));
        moveInProgress.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Attempt transition to Done -> MUST return 400
        var moveDone = await creatorClient.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(WorkItemStatus.Done));
        moveDone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task KanbanTransition_WhenItemDenied_MovingToInProgressOrDone_Returns400BadRequest()
    {
        Guid projectId;
        Guid reviewerId;
        Guid creatorId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var p = await db.Projects.FirstAsync();
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            var dev = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
            projectId = p.Id;
            reviewerId = lead.Id;
            creatorId = dev.Id;
        }

        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "dev@test.local", "Dev", AppRole.Employee);
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@test.local", "Lead", AppRole.TeamLead);

        // Create item
        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            projectId,
            "Item Destined to be Denied",
            ReviewerId: reviewerId
        ));
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // Lead denies
        var denyResp = await reviewerClient.PostAsJsonAsync($"/api/reviews/{created!.Id}/deny", new DenyReviewRequestDto("Not aligned with roadmap goals."));
        denyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now item has ReviewStatus == Denied, Status == Denied
        // Attempt transition to InProgress -> MUST return 400
        var moveInProgress = await creatorClient.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(WorkItemStatus.InProgress));
        moveInProgress.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Attempt transition to Done -> MUST return 400
        var moveDone = await creatorClient.PostAsJsonAsync($"/api/workitems/{created.Id}/status", new KanbanTransitionDto(WorkItemStatus.Done));
        moveDone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]            // 0 chars
    [InlineData("a")]           // 1 char
    [InlineData("abcd")]        // 4 chars
    [InlineData("    ")]        // whitespace only
    [InlineData(" \t \n ")]     // tabs/newlines
    public async Task Reviews_Clarify_WithShortOrWhitespaceComment_Returns400BadRequest(string comment)
    {
        Guid reviewerId;
        Guid workItemId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            var item = await db.WorkItems.FirstAsync();
            reviewerId = lead.Id;
            workItemId = item.Id;
        }

        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@test.local", "Lead", AppRole.TeamLead);

        var response = await reviewerClient.PostAsJsonAsync($"/api/reviews/{workItemId}/clarify", new ClarifyReviewRequestDto(comment));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]            // 0 chars
    [InlineData("x")]           // 1 char
    [InlineData("nope")]        // 4 chars
    [InlineData("    ")]        // whitespace only
    [InlineData(" \r\n ")]      // CRLF whitespace
    public async Task Reviews_Deny_WithShortOrWhitespaceReason_Returns400BadRequest(string reason)
    {
        Guid reviewerId;
        Guid workItemId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            var item = await db.WorkItems.FirstAsync();
            reviewerId = lead.Id;
            workItemId = item.Id;
        }

        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "lead@test.local", "Lead", AppRole.TeamLead);

        var response = await reviewerClient.PostAsJsonAsync($"/api/reviews/{workItemId}/deny", new DenyReviewRequestDto(reason));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region 3. Planning Invariants

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(24.1)]
    [InlineData(100.0)]
    public async Task DailyPlan_InvalidPlannedHours_Returns400BadRequest(decimal invalidHours)
    {
        Guid employeeId;
        Guid workItemId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emp = await db.Employees.FirstAsync();
            var item = await db.WorkItems.FirstAsync();
            employeeId = emp.Id;
            workItemId = item.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "emp@corp.local", "Emp", AppRole.Employee);

        var req = new SubmitDailyPlanRequestDto(
            PlanDate: DateOnly.FromDateTime(DateTime.UtcNow),
            Blockers: null,
            SummaryNotes: "Testing bounds",
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(workItemId, null, invalidHours, "Bound test task")
            }
        );

        var response = await client.PostAsJsonAsync("/api/planning/daily", req);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0.1)]   // Min boundary
    [InlineData(8.0)]   // Standard work day
    [InlineData(24.0)]  // Max boundary
    public async Task DailyPlan_ValidPlannedHours_BoundarySucceedsWith200OK(decimal validHours)
    {
        Guid employeeId;
        Guid workItemId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emp = await db.Employees.FirstAsync();
            var item = await db.WorkItems.FirstAsync();
            employeeId = emp.Id;
            workItemId = item.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "emp.bounds@corp.local", "Emp Bounds", AppRole.Employee);

        var req = new SubmitDailyPlanRequestDto(
            PlanDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(Random.Shared.Next(10, 100))),
            Blockers: null,
            SummaryNotes: "Valid bounds test",
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(workItemId, null, validHours, "Valid task")
            }
        );

        var response = await client.PostAsJsonAsync("/api/planning/daily", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await response.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan.Should().NotBeNull();
        plan!.Items.Should().ContainSingle(i => i.PlannedHours == validHours);
    }

    [Fact]
    public async Task WeeklyPlan_SubmittedOnWednesday_NormalizesToMonday()
    {
        Guid employeeId;
        Guid projectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emp = await db.Employees.FirstAsync();
            var proj = await db.Projects.FirstAsync();
            employeeId = emp.Id;
            projectId = proj.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "weekly.planner@corp.local", "Planner", AppRole.Employee);

        // Wednesday: 2026-11-25 -> Expected Monday: 2026-11-23
        var wednesday = new DateOnly(2026, 11, 25);
        var expectedMonday = new DateOnly(2026, 11, 23);

        var req = new CreateOrUpdateWeeklyPlanDto(
            WeekStartDate: wednesday,
            ProjectId: projectId,
            GoalsMarkdown: "Wednesday submission test",
            FocusAreasJson: "[\"Backend\"]",
            KeyDeliverables: "Adversarial test passes"
        );

        var response = await client.PostAsJsonAsync("/api/planning/weekly", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await response.Content.ReadFromJsonAsync<WeeklyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan.Should().NotBeNull();
        plan!.WeekStartDate.Should().Be(expectedMonday);
    }

    [Fact]
    public async Task WeeklyPlan_SubmittedOnSaturday_NormalizesToMonday()
    {
        Guid employeeId;
        Guid projectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emp = await db.Employees.FirstAsync();
            var proj = await db.Projects.FirstAsync();
            employeeId = emp.Id;
            projectId = proj.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "saturday.planner@corp.local", "Planner", AppRole.Employee);

        // Saturday: 2026-11-28 -> Expected Monday: 2026-11-23
        var saturday = new DateOnly(2026, 11, 28);
        var expectedMonday = new DateOnly(2026, 11, 23);

        var req = new CreateOrUpdateWeeklyPlanDto(
            WeekStartDate: saturday,
            ProjectId: projectId,
            GoalsMarkdown: "Saturday submission test",
            FocusAreasJson: "[\"Backend\"]",
            KeyDeliverables: "Saturday test"
        );

        var response = await client.PostAsJsonAsync("/api/planning/weekly", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await response.Content.ReadFromJsonAsync<WeeklyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan.Should().NotBeNull();
        plan!.WeekStartDate.Should().Be(expectedMonday);
    }

    [Fact]
    public async Task WeeklyPlan_SubmittedOnSunday_NormalizesToMonday()
    {
        Guid employeeId;
        Guid projectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emp = await db.Employees.FirstAsync();
            var proj = await db.Projects.FirstAsync();
            employeeId = emp.Id;
            projectId = proj.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "sunday.planner@corp.local", "Planner", AppRole.Employee);

        // Sunday: 2026-11-29 -> Expected Monday: 2026-11-23
        var sunday = new DateOnly(2026, 11, 29);
        var expectedMonday = new DateOnly(2026, 11, 23);

        var req = new CreateOrUpdateWeeklyPlanDto(
            WeekStartDate: sunday,
            ProjectId: projectId,
            GoalsMarkdown: "Sunday submission test",
            FocusAreasJson: "[\"Backend\"]",
            KeyDeliverables: "Sunday test"
        );

        var response = await client.PostAsJsonAsync("/api/planning/weekly", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await response.Content.ReadFromJsonAsync<WeeklyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan.Should().NotBeNull();
        plan!.WeekStartDate.Should().Be(expectedMonday);
    }

    [Theory]
    [InlineData(2026, 11, 16, 2026, 11, 16)] // Monday -> Monday
    [InlineData(2026, 11, 17, 2026, 11, 16)] // Tuesday -> Monday
    [InlineData(2026, 11, 18, 2026, 11, 16)] // Wednesday -> Monday
    [InlineData(2026, 11, 19, 2026, 11, 16)] // Thursday -> Monday
    [InlineData(2026, 11, 20, 2026, 11, 16)] // Friday -> Monday
    [InlineData(2026, 11, 21, 2026, 11, 16)] // Saturday -> Monday
    [InlineData(2026, 11, 22, 2026, 11, 16)] // Sunday -> Monday
    public void WeeklyPlan_NormalizeToMonday_AllDaysOfWeek(int y, int m, int d, int expY, int expM, int expD)
    {
        var inputDate = new DateOnly(y, m, d);
        var expectedMonday = new DateOnly(expY, expM, expD);

        var normalized = WeeklyPlan.NormalizeToMonday(inputDate);
        normalized.Should().Be(expectedMonday);
        normalized.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    #endregion
}
