using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class PlanningAndDirectoryIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public PlanningAndDirectoryIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EmployeeDirectory_StrictlyOmitsFinancialAndCompensationFields()
    {
        var client = _fixture.CreateClientWithRole("Employee");

        var response = await client.GetAsync("/api/employees");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await response.Content.ReadAsStringAsync();

        // Ensure sensitive compensation fields do NOT exist anywhere in directory response
        var lower = rawJson.ToLowerInvariant();
        lower.Should().NotContain("annualbasesalary");
        lower.Should().NotContain("hourlycostrate");
        lower.Should().NotContain("hourlybillablerate");
        lower.Should().NotContain("targetutilizationpercent");

        var employees = await response.Content.ReadFromJsonAsync<List<EmployeeDirectoryDto>>(CrmApiTestFixture.JsonOptions);
        employees.Should().NotBeNull();
        employees.Should().NotBeEmpty();

        var first = employees![0];
        first.FullName.Should().NotBeNullOrWhiteSpace();
        first.Email.Should().NotBeNullOrWhiteSpace();
        first.Department.Should().NotBeNullOrWhiteSpace();
        first.Skills.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(-1, false)]
    public async Task SkillsMatrix_ValidatesProficiencyLevelStrictlyBetween1And5(int proficiencyLevel, bool shouldSucceed)
    {
        Guid employeeId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var emp = await db.Employees.FirstAsync();
            employeeId = emp.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "emp@corp.local", "Emp", AppRole.Employee);

        var req = new UpsertSkillRequestDto(
            SkillName: "C# .NET 9",
            Category: "Backend",
            ProficiencyLevel: proficiencyLevel,
            YearsOfExperience: 3.5m
        );

        var response = await client.PostAsJsonAsync($"/api/employees/{employeeId}/skills", req);

        if (shouldSucceed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var skill = await response.Content.ReadFromJsonAsync<EmployeeSkillDto>(CrmApiTestFixture.JsonOptions);
            skill.Should().NotBeNull();
            skill!.ProficiencyLevel.Should().Be(proficiencyLevel);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task WeeklyPlan_NormalizesWeekStartDateToMonday()
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

        var client = _fixture.CreateClientWithEmployee(employeeId, "planner@corp.local", "Planner", AppRole.Employee);

        // Submit on a Wednesday: 2026-11-18
        var wednesday = new DateOnly(2026, 11, 18);
        var expectedMonday = new DateOnly(2026, 11, 16);

        var req = new CreateOrUpdateWeeklyPlanDto(
            WeekStartDate: wednesday,
            ProjectId: projectId,
            GoalsMarkdown: "Ship Milestone 3 API and test suites.",
            FocusAreasJson: "[\"SignalR\",\"RBAC\"]",
            KeyDeliverables: "M3 verification passing"
        );

        var response = await client.PostAsJsonAsync("/api/planning/weekly", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await response.Content.ReadFromJsonAsync<WeeklyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan.Should().NotBeNull();
        plan!.WeekStartDate.Should().Be(expectedMonday);
    }

    [Fact]
    public async Task DailyPlan_Submission_EvaluatesOnTimeFlag_AndLogsItems()
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

        var client = _fixture.CreateClientWithEmployee(employeeId, "daily.emp@corp.local", "Daily Emp", AppRole.Employee);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new SubmitDailyPlanRequestDto(
            PlanDate: today,
            Blockers: "No major blockers.",
            SummaryNotes: "Working on API endpoints.",
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(workItemId, null, 4.5m, "Implement REST controllers"),
                new(null, "Code Review with Lead", 2.0m, "Review PR #42")
            }
        );

        var response = await client.PostAsJsonAsync("/api/planning/daily", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await response.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan.Should().NotBeNull();
        plan!.PlanDate.Should().Be(today);
        plan.Items.Should().HaveCount(2);

        // Submitting with invalid planned hours (< 0.1 or > 24) fails
        var invalidReq = new SubmitDailyPlanRequestDto(
            PlanDate: today,
            Blockers: null,
            SummaryNotes: null,
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(workItemId, null, 0.05m, "Too short"),
            }
        );

        var badResp = await client.PostAsJsonAsync("/api/planning/daily", invalidReq);
        badResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DailyPlan_ResubmissionForSameEmployeeAndDate_UpdatesPlanAndReplacesItemsDeterministically()
    {
        Guid alexId;
        Guid crm104Id;
        Guid crm105Id;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var alex = await db.Employees.FirstAsync(e => e.Email == "alex.rivera@companycrm.local");
            alexId = alex.Id;
            var items = await db.WorkItems.OrderBy(w => w.Code).Take(2).ToListAsync();
            crm104Id = items[0].Id;
            crm105Id = items[1].Id;
        }

        var client = _fixture.CreateClientWithEmployee(alexId, "alex.rivera@companycrm.local", "Alex Rivera", AppRole.Employee);

        // 1. Resubmit today's seeded DailyPlan for Alex Rivera (hits the update branch deterministically)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var updateSeededReq = new SubmitDailyPlanRequestDto(
            PlanDate: today,
            Blockers: "Updated blocker note",
            SummaryNotes: "Updated summary note",
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(crm104Id, "Updated task 1", 3.5m, "Goal 1")
            }
        );

        var updateSeededResp = await client.PostAsJsonAsync("/api/planning/daily", updateSeededReq);
        updateSeededResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedSeeded = await updateSeededResp.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
        updatedSeeded.Should().NotBeNull();
        updatedSeeded!.Blockers.Should().Be("Updated blocker note");
        updatedSeeded.Items.Should().ContainSingle(i => i.CustomTaskTitle == "Updated task 1" && i.PlannedHours == 3.5m);

        // 2. Submit twice on a fresh future date for the exact same (EmployeeId, PlanDate)
        var targetDate = today.AddDays(14);
        var firstSubmit = new SubmitDailyPlanRequestDto(
            PlanDate: targetDate,
            Blockers: "Initial blocker",
            SummaryNotes: "Initial summary",
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(crm104Id, "First item A", 2.0m, "Note A"),
                new(null, "First item B", 3.0m, "Note B")
            }
        );

        var resp1 = await client.PostAsJsonAsync("/api/planning/daily", firstSubmit);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan1 = await resp1.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan1!.Items.Should().HaveCount(2);

        var secondSubmit = new SubmitDailyPlanRequestDto(
            PlanDate: targetDate,
            Blockers: "Replaced blocker",
            SummaryNotes: "Replaced summary",
            Items: new List<SubmitDailyPlanItemDto>
            {
                new(crm105Id, "Replacement item X", 5.0m, "Replacement note")
            }
        );

        var resp2 = await client.PostAsJsonAsync("/api/planning/daily", secondSubmit);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan2 = await resp2.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
        plan2.Should().NotBeNull();
        plan2!.Id.Should().Be(plan1.Id);
        plan2.Blockers.Should().Be("Replaced blocker");
        plan2.SummaryNotes.Should().Be("Replaced summary");
        plan2.Items.Should().ContainSingle(i => i.CustomTaskTitle == "Replacement item X" && i.PlannedHours == 5.0m);

        // Verify persisted items in DB directly
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var dbItems = await db.DailyPlanItems.Where(dpi => dpi.DailyPlanId == plan1.Id).ToListAsync();
            dbItems.Should().ContainSingle();
            dbItems[0].CustomTaskTitle.Should().Be("Replacement item X");
        }
    }

    [Fact]
    public async Task CutoffConfig_UpdateAndRunNow_CanonicalizesExcludedDaysAndPopulatesProjectName()
    {
        Guid projectId;
        string projectName;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var proj = await db.Projects.OrderBy(p => p.Code).FirstAsync();
            projectId = proj.Id;
            projectName = proj.Name;
        }

        var leadClient = _fixture.CreateClientWithRole("TeamLead");

        // 1. Update project-scoped config with day names -> canonicalized to sorted int JSON & ProjectName populated
        var updateProjectReq = new UpdatePlanningCutoffConfigDto(
            ProjectId: projectId,
            CutoffTimeLocal: new TimeOnly(9, 15, 0),
            TimeZoneId: "UTC",
            IsReminderEnabled: true,
            ExcludedDaysOfWeekJson: "[\"Sunday\", \"Wednesday\", \"Saturday\"]"
        );

        var putProjResp = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", updateProjectReq);
        putProjResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var projCfg = await putProjResp.Content.ReadFromJsonAsync<PlanningCutoffConfigDto>(CrmApiTestFixture.JsonOptions);
        projCfg.Should().NotBeNull();
        projCfg!.ProjectId.Should().Be(projectId);
        projCfg.ProjectName.Should().Be(projectName);
        projCfg.ExcludedDaysOfWeekJson.Should().Be("[0,3,6]");

        // 2. Update global config excluding all 7 days & verify run-now returns ExcludedDay
        var todayDayIndex = (int)DateTime.UtcNow.DayOfWeek;
        var updateGlobalReq = new UpdatePlanningCutoffConfigDto(
            ProjectId: null,
            CutoffTimeLocal: new TimeOnly(0, 0, 1),
            TimeZoneId: "UTC",
            IsReminderEnabled: true,
            ExcludedDaysOfWeekJson: $"[{todayDayIndex}]"
        );

        var putGlobalResp = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", updateGlobalReq);
        putGlobalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var runResp = await leadClient.PostAsync("/api/planning/cutoff-check/run-now", null);
        runResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await runResp.Content.ReadFromJsonAsync<CutoffExecutionSummaryDto>(CrmApiTestFixture.JsonOptions);
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("ExcludedDay");
        summary.RemindersSent.Should().Be(0);

        // Restore default global config [0, 6]
        await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
            ProjectId: null,
            CutoffTimeLocal: new TimeOnly(10, 0, 0),
            TimeZoneId: "UTC",
            IsReminderEnabled: true,
            ExcludedDaysOfWeekJson: "[0, 6]"
        ));
    }

    [Fact]
    public async Task CutoffConfig_ProjectScopedSave_AndRunNowWithProjectId_EvaluatesProjectSettings()
    {
        Guid crmId;
        PlanningCutoffConfigDto? originalGlobal = null;
        PlanningCutoffConfigDto? originalCrm = null;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var crm = await db.Projects.FirstAsync(p => p.Code == "CRM");
            crmId = crm.Id;
        }

        var leadClient = _fixture.CreateClientWithRole("TeamLead");
        originalGlobal = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>("/api/planning/cutoff-config", CrmApiTestFixture.JsonOptions);
        originalCrm = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>($"/api/planning/cutoff-config?projectId={crmId}", CrmApiTestFixture.JsonOptions);

        try
        {
            var todayDayIndex = (int)DateTime.UtcNow.DayOfWeek;

            // Global enabled with no excluded days and cutoff in the past
            var putGlobal = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                ProjectId: null,
                CutoffTimeLocal: new TimeOnly(0, 0, 1),
                TimeZoneId: "UTC",
                IsReminderEnabled: true,
                ExcludedDaysOfWeekJson: "[]"
            ));
            putGlobal.StatusCode.Should().Be(HttpStatusCode.OK);

            // Project CRM override excluding today
            var putCrm = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                ProjectId: crmId,
                CutoffTimeLocal: new TimeOnly(9, 15, 0),
                TimeZoneId: "UTC",
                IsReminderEnabled: true,
                ExcludedDaysOfWeekJson: $"[{todayDayIndex}]"
            ));
            putCrm.StatusCode.Should().Be(HttpStatusCode.OK);

            // 1. Run-now scoped to CRM must evaluate CRM override -> ExcludedDay
            var crmRunResp = await leadClient.PostAsync($"/api/planning/cutoff-check/run-now?projectId={crmId}", null);
            crmRunResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var crmSummary = await crmRunResp.Content.ReadFromJsonAsync<CutoffExecutionSummaryDto>(CrmApiTestFixture.JsonOptions);
            crmSummary!.Status.Should().Be("ExcludedDay");
            crmSummary.CutoffTime.Should().Be(new TimeOnly(9, 15, 0));
            crmSummary.RemindersSent.Should().Be(0);

            // 2. Run-now global (no projectId) must evaluate Global -> Completed
            var globalRunResp = await leadClient.PostAsync("/api/planning/cutoff-check/run-now", null);
            globalRunResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var globalSummary = await globalRunResp.Content.ReadFromJsonAsync<CutoffExecutionSummaryDto>(CrmApiTestFixture.JsonOptions);
            globalSummary!.Status.Should().Be("Completed");
            globalSummary.CutoffTime.Should().Be(new TimeOnly(0, 0, 1));

            // 3. Disable CRM project override -> run-now?projectId=crmId returns Disabled while global remains active
            var disableCrm = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                ProjectId: crmId,
                CutoffTimeLocal: new TimeOnly(9, 15, 0),
                TimeZoneId: "UTC",
                IsReminderEnabled: false,
                ExcludedDaysOfWeekJson: "[0, 6]"
            ));
            disableCrm.StatusCode.Should().Be(HttpStatusCode.OK);

            var disabledRunResp = await leadClient.PostAsync($"/api/planning/cutoff-check/run-now?projectId={crmId}", null);
            disabledRunResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var disabledSummary = await disabledRunResp.Content.ReadFromJsonAsync<CutoffExecutionSummaryDto>(CrmApiTestFixture.JsonOptions);
            disabledSummary!.Status.Should().Be("Disabled");
            disabledSummary.RemindersSent.Should().Be(0);
        }
        finally
        {
            if (originalGlobal != null)
            {
                await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                    ProjectId: null,
                    CutoffTimeLocal: originalGlobal.CutoffTimeLocal,
                    TimeZoneId: originalGlobal.TimeZoneId,
                    IsReminderEnabled: originalGlobal.IsReminderEnabled,
                    ExcludedDaysOfWeekJson: originalGlobal.ExcludedDaysOfWeekJson
                ));
            }
            if (originalCrm != null && originalCrm.ProjectId == crmId)
            {
                await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                    ProjectId: crmId,
                    CutoffTimeLocal: originalCrm.CutoffTimeLocal,
                    TimeZoneId: originalCrm.TimeZoneId,
                    IsReminderEnabled: originalCrm.IsReminderEnabled,
                    ExcludedDaysOfWeekJson: originalCrm.ExcludedDaysOfWeekJson
                ));
            }
        }
    }

    [Fact]
    public async Task CutoffConfig_ProjectWithoutOverride_RunNowWithProjectId_FallsBackToGlobalConfig()
    {
        Guid finId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var fin = await db.Projects.FirstAsync(p => p.Code == "FIN");
            finId = fin.Id;
        }

        var leadClient = _fixture.CreateClientWithRole("TeamLead");
        var originalGlobal = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>("/api/planning/cutoff-config", CrmApiTestFixture.JsonOptions);

        try
        {
            var finCfg = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>($"/api/planning/cutoff-config?projectId={finId}", CrmApiTestFixture.JsonOptions);
            finCfg!.ProjectId.Should().BeNull();

            await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                ProjectId: null,
                CutoffTimeLocal: new TimeOnly(10, 0, 0),
                TimeZoneId: "UTC",
                IsReminderEnabled: false,
                ExcludedDaysOfWeekJson: "[0, 6]"
            ));

            var finRunResp = await leadClient.PostAsync($"/api/planning/cutoff-check/run-now?projectId={finId}", null);
            finRunResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var finSummary = await finRunResp.Content.ReadFromJsonAsync<CutoffExecutionSummaryDto>(CrmApiTestFixture.JsonOptions);
            finSummary!.Status.Should().Be("Disabled");
        }
        finally
        {
            if (originalGlobal != null)
            {
                await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                    ProjectId: null,
                    CutoffTimeLocal: originalGlobal.CutoffTimeLocal,
                    TimeZoneId: originalGlobal.TimeZoneId,
                    IsReminderEnabled: originalGlobal.IsReminderEnabled,
                    ExcludedDaysOfWeekJson: originalGlobal.ExcludedDaysOfWeekJson
                ));
            }
        }
    }

    [Fact]
    public async Task SubmitDailyPlan_WithLinkedWorkItems_UsesProjectCutoffOverrideBeforeGlobal()
    {
        Guid crmId;
        Guid crmWorkItemId;
        Guid employeeId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var crm = await db.Projects.FirstAsync(p => p.Code == "CRM");
            crmId = crm.Id;
            var wi = await db.WorkItems.FirstAsync(w => w.ProjectId == crmId);
            crmWorkItemId = wi.Id;
            var emp = new Employee
            {
                FullName = "Cutoff Emp",
                Email = $"cutoff.emp.{Guid.NewGuid():N}@crm.local",
                Department = "Eng",
                JobTitle = "Dev",
                Role = AppRole.Employee
            };
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;
        }

        var leadClient = _fixture.CreateClientWithRole("TeamLead");
        var empClient = _fixture.CreateClientWithEmployee(employeeId, "cutoff.emp@crm.local", "Cutoff Emp", AppRole.Employee);
        var originalGlobal = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>("/api/planning/cutoff-config", CrmApiTestFixture.JsonOptions);
        var originalCrm = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>($"/api/planning/cutoff-config?projectId={crmId}", CrmApiTestFixture.JsonOptions);

        try
        {
            // Global cutoff is end of day (23:59:59 UTC -> on time)
            await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                ProjectId: null,
                CutoffTimeLocal: new TimeOnly(23, 59, 59),
                TimeZoneId: "UTC",
                IsReminderEnabled: true,
                ExcludedDaysOfWeekJson: "[0, 6]"
            ));

            // CRM cutoff is 00:00:01 UTC -> past cutoff!
            await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                ProjectId: crmId,
                CutoffTimeLocal: new TimeOnly(0, 0, 1),
                TimeZoneId: "UTC",
                IsReminderEnabled: true,
                ExcludedDaysOfWeekJson: "[0, 6]"
            ));

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var linkedResp = await empClient.PostAsJsonAsync("/api/planning/daily", new SubmitDailyPlanRequestDto(
                PlanDate: today,
                Blockers: null,
                SummaryNotes: "Linked to CRM item",
                Items: new List<SubmitDailyPlanItemDto>
                {
                    new(crmWorkItemId, null, 4.0m, "Work on CRM item")
                }
            ));
            linkedResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var linkedPlan = await linkedResp.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
            linkedPlan!.IsSubmittedOnTime.Should().BeFalse();

            // Custom-only item with no ProjectId evaluates against Global 23:59:59 -> on time!
            var customResp = await empClient.PostAsJsonAsync("/api/planning/daily", new SubmitDailyPlanRequestDto(
                PlanDate: today.AddDays(1),
                Blockers: null,
                SummaryNotes: "Custom global task",
                Items: new List<SubmitDailyPlanItemDto>
                {
                    new(null, "General training", 2.0m, "Training")
                },
                ProjectId: null
            ));
            customResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var customPlan = await customResp.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
            customPlan!.IsSubmittedOnTime.Should().BeTrue();
        }
        finally
        {
            if (originalGlobal != null)
            {
                await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                    ProjectId: null,
                    CutoffTimeLocal: originalGlobal.CutoffTimeLocal,
                    TimeZoneId: originalGlobal.TimeZoneId,
                    IsReminderEnabled: originalGlobal.IsReminderEnabled,
                    ExcludedDaysOfWeekJson: originalGlobal.ExcludedDaysOfWeekJson
                ));
            }
            if (originalCrm != null && originalCrm.ProjectId == crmId)
            {
                await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                    ProjectId: crmId,
                    CutoffTimeLocal: originalCrm.CutoffTimeLocal,
                    TimeZoneId: originalCrm.TimeZoneId,
                    IsReminderEnabled: originalCrm.IsReminderEnabled,
                    ExcludedDaysOfWeekJson: originalCrm.ExcludedDaysOfWeekJson
                ));
            }
        }
    }
}
