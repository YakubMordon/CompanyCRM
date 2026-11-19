using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.Email;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CompanyCRM.Tests;

public class ChallengerM2DatabaseAndJobsStressTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;

    public ChallengerM2DatabaseAndJobsStressTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }

        _options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CrmDbContext CreateContext() => new CrmDbContext(_options);

    private IServiceScopeFactory CreateScopeFactory(IEmailNotificationService? emailService = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrmDbContext>(opts => opts.UseSqlite(_connection));
        if (emailService != null)
        {
            services.AddSingleton(emailService);
        }
        else
        {
            services.AddSingleton<IEmailNotificationService>(new Mock<IEmailNotificationService>().Object);
        }
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    // =========================================================================
    // 1. EF CORE DATABASE CONSTRAINTS & SQLITE/POSTGRES INTEGRITY
    // =========================================================================

    [Fact]
    public async Task ChallengeUniqueConstraint_ProjectMember_DuplicateMember_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "UNIQ1", Name = "Unique Member Project" };
        var employee = new Employee { FullName = "Member 1", Email = "pm1@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        // First insertion succeeds
        context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, EmployeeId = employee.Id, Role = ProjectRole.Member });
        await context.SaveChangesAsync();

        // Second duplicate insertion with identical (ProjectId, EmployeeId) MUST fail
        context.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, EmployeeId = employee.Id, Role = ProjectRole.Lead });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ChallengeUniqueConstraint_DailyPlan_DuplicateEmployeeAndDate_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee { FullName = "Planner", Email = "planner@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var date = new DateOnly(2026, 11, 20);

        // First plan succeeds
        context.DailyPlans.Add(new DailyPlan { EmployeeId = employee.Id, PlanDate = date });
        await context.SaveChangesAsync();

        // Duplicate plan for same employee on same date MUST fail
        context.DailyPlans.Add(new DailyPlan { EmployeeId = employee.Id, PlanDate = date });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ChallengeUniqueConstraint_DailyPlanReminderLog_DuplicateEmployeeAndDate_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var employee = new Employee { FullName = "Remindee", Email = "remindee@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var date = new DateOnly(2026, 11, 20);

        // First reminder log succeeds
        context.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
        {
            EmployeeId = employee.Id,
            PlanDate = date,
            RecipientEmail = employee.Email,
            EmailDeliveryStatus = "Sent"
        });
        await context.SaveChangesAsync();

        // Duplicate reminder log for same employee on same date MUST fail
        context.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
        {
            EmployeeId = employee.Id,
            PlanDate = date,
            RecipientEmail = employee.Email,
            EmailDeliveryStatus = "Sent"
        });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ChallengeUniqueConstraint_ProjectCode_DuplicateCode_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.Projects.Add(new Project { Code = "ALPHA", Name = "Project Alpha Original" });
        await context.SaveChangesAsync();

        // Duplicate Code MUST fail
        context.Projects.Add(new Project { Code = "ALPHA", Name = "Project Alpha Clone" });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ChallengeUniqueConstraint_EmployeeEmail_DuplicateEmail_ThrowsDbUpdateException()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.Employees.Add(new Employee { FullName = "Original User", Email = "duplicate.test@company.com", Department = "Eng", JobTitle = "Dev" });
        await context.SaveChangesAsync();

        // Duplicate Email MUST fail
        context.Employees.Add(new Employee { FullName = "Impostor User", Email = "duplicate.test@company.com", Department = "Sales", JobTitle = "Rep" });
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ChallengeCascadeDelete_Project_DeletesMembersAndWorkItems()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CASCADE", Name = "Cascade Project" };
        var employee = new Employee { FullName = "Member Eva", Email = "eva.cascade@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var member = new ProjectMember { ProjectId = project.Id, EmployeeId = employee.Id, Role = ProjectRole.Member };
        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CASCADE-1",
            Title = "Child Work Item"
        };
        context.ProjectMembers.Add(member);
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        // Verify inserted
        (await context.Projects.CountAsync(p => p.Id == project.Id)).Should().Be(1);
        (await context.ProjectMembers.CountAsync(pm => pm.ProjectId == project.Id)).Should().Be(1);
        (await context.WorkItems.CountAsync(w => w.ProjectId == project.Id)).Should().Be(1);

        // Delete the Project
        context.Projects.Remove(project);
        await context.SaveChangesAsync();

        // Verify Cascade: Project, Members, and WorkItems are all removed
        (await context.Projects.CountAsync(p => p.Id == project.Id)).Should().Be(0);
        (await context.ProjectMembers.CountAsync(pm => pm.ProjectId == project.Id)).Should().Be(0);
        (await context.WorkItems.CountAsync(w => w.ProjectId == project.Id)).Should().Be(0);
        // Employee itself must NOT be deleted
        (await context.Employees.CountAsync(e => e.Id == employee.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ChallengeCascadeDelete_WorkItem_DeletesHistoryAndTimeEntries()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "WICASC", Name = "Work Item Cascade Project" };
        var employee = new Employee { FullName = "Dev Dan", Email = "dan.wi@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "WICASC-1",
            Title = "WorkItem to Delete"
        };
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        var history = new WorkItemHistory
        {
            WorkItemId = workItem.Id,
            ChangedById = employee.Id,
            Action = "Created"
        };
        var timeEntry = new TimeEntry
        {
            WorkItemId = workItem.Id,
            EmployeeId = employee.Id,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            EndedAt = DateTimeOffset.UtcNow,
            DurationMinutes = 120,
            LoggedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        context.WorkItemHistories.Add(history);
        context.TimeEntries.Add(timeEntry);
        await context.SaveChangesAsync();

        // Delete the WorkItem
        context.WorkItems.Remove(workItem);
        await context.SaveChangesAsync();

        // Verify Cascade: WorkItem, Histories, and TimeEntries are all deleted
        (await context.WorkItems.CountAsync(w => w.Id == workItem.Id)).Should().Be(0);
        (await context.WorkItemHistories.CountAsync(h => h.WorkItemId == workItem.Id)).Should().Be(0);
        (await context.TimeEntries.CountAsync(t => t.WorkItemId == workItem.Id)).Should().Be(0);
    }

    [Fact]
    public async Task ChallengeRestrictDelete_CreatorEmployee_PreventedFromDeletionWhileWorkItemExists()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "RESTR", Name = "Restrict Project" };
        var creator = new Employee { FullName = "Creator Carl", Email = "carl.creator@company.com", Department = "Eng", JobTitle = "Lead" };
        context.Projects.Add(project);
        context.Employees.Add(creator);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = creator.Id,
            Code = "RESTR-1",
            Title = "Created Work Item"
        };
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        // Attempting to delete the Creator Employee while the work item exists MUST be restricted
        using var deleteContext = CreateContext();
        var creatorToDelete = await deleteContext.Employees.FirstAsync(e => e.Id == creator.Id);
        deleteContext.Employees.Remove(creatorToDelete);

        var act = async () => await deleteContext.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>(); // DbUpdateException or InvalidOperationException due to Restrict
    }

    // =========================================================================
    // 2. CUTOFF REMINDER JOB ADVERSARIAL SCENARIOS
    // =========================================================================

    [Fact]
    public async Task ChallengeTimezone_PositiveUtcOffset_AsiaTokyo_EvaluatesCutoffCorrectly()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Cutoff 10:00 Tokyo time (UTC+9)
        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "Asia/Tokyo",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        var emp = new Employee { FullName = "Tokyo Dev", Email = "tokyo@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();

        // Wednesday Nov 18, 2026.
        // At 00:30 UTC -> Tokyo local time is 09:30 AM (BEFORE 10:00 AM cutoff)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var resultBefore = await job.ExecuteCutoffCheckAsync();
        resultBefore.Status.Should().Be("BeforeCutoff");
        resultBefore.RemindersSent.Should().Be(0);
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        // Advance to 01:30 UTC -> Tokyo local time is 10:30 AM (AFTER 10:00 AM cutoff)
        fakeTime.SetUtcNow(new DateTimeOffset(2026, 11, 18, 1, 30, 0, TimeSpan.Zero));
        var resultAfter = await job.ExecuteCutoffCheckAsync();

        resultAfter.Status.Should().Be("Completed");
        resultAfter.RemindersSent.Should().Be(1);
        resultAfter.NotifiedEmployeeEmails.Should().ContainSingle().Which.Should().Be("tokyo@company.com");
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync("tokyo@company.com", "Tokyo Dev", new DateOnly(2026, 11, 18), new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChallengeTimezone_NegativeUtcOffset_AmericaNewYork_EvaluatesCutoffCorrectly()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Cutoff 10:00 New York time (UTC-5 in Nov 2026)
        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "America/New_York",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        var emp = new Employee { FullName = "NY Dev", Email = "ny@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();

        // Wednesday Nov 18, 2026.
        // At 14:30 UTC -> NY local time is 09:30 AM (BEFORE 10:00 AM cutoff)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 14, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var resultBefore = await job.ExecuteCutoffCheckAsync();
        resultBefore.Status.Should().Be("BeforeCutoff");
        resultBefore.RemindersSent.Should().Be(0);

        // Advance to 15:30 UTC -> NY local time is 10:30 AM (AFTER 10:00 AM cutoff)
        fakeTime.SetUtcNow(new DateTimeOffset(2026, 11, 18, 15, 30, 0, TimeSpan.Zero));
        var resultAfter = await job.ExecuteCutoffCheckAsync();

        resultAfter.Status.Should().Be("Completed");
        resultAfter.RemindersSent.Should().Be(1);
        resultAfter.NotifiedEmployeeEmails.Should().ContainSingle().Which.Should().Be("ny@company.com");
    }

    [Fact]
    public async Task ChallengeTimezone_EuropeBerlin_EvaluatesCutoffCorrectly()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Cutoff 10:00 Berlin time (UTC+1 in Nov 2026)
        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "Europe/Berlin",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        var emp = new Employee { FullName = "Berlin Dev", Email = "berlin@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();

        // Wednesday Nov 18, 2026.
        // At 08:30 UTC -> Berlin local time is 09:30 AM (BEFORE cutoff)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 8, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var resultBefore = await job.ExecuteCutoffCheckAsync();
        resultBefore.Status.Should().Be("BeforeCutoff");

        // Advance to 09:30 UTC -> Berlin local time is 10:30 AM (AFTER cutoff)
        fakeTime.SetUtcNow(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));
        var resultAfter = await job.ExecuteCutoffCheckAsync();

        resultAfter.Status.Should().Be("Completed");
        resultAfter.RemindersSent.Should().Be(1);
        resultAfter.NotifiedEmployeeEmails.Should().ContainSingle().Which.Should().Be("berlin@company.com");
    }

    [Fact]
    public async Task ChallengeTimezone_InvalidTimezoneString_CleanlyFallsBackToUtcWithoutThrowing()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "Unknown/Zone",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        var emp = new Employee { FullName = "Fallback Dev", Email = "fallback@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();

        // 10:30 UTC on Wednesday Nov 18, 2026
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var act = async () => await job.ExecuteCutoffCheckAsync();
        var result = await act.Should().NotThrowAsync();

        result.Subject.Status.Should().Be("Completed");
        result.Subject.RemindersSent.Should().Be(1);
    }

    [Fact]
    public async Task ChallengeWeekendExclusion_SaturdayAndSunday_SendsZeroEmailsAndReturnsExcludedDay()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "UTC",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]" // Sunday=0, Saturday=6
        });

        var emp = new Employee { FullName = "Weekend Dev", Email = "weekend@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();

        // Case 1: Saturday Nov 14, 2026 at 11:00 UTC
        var satTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.Zero));
        var satJob = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, satTime);
        var satResult = await satJob.ExecuteCutoffCheckAsync();

        satResult.Status.Should().Be("ExcludedDay");
        satResult.RemindersSent.Should().Be(0);

        // Case 2: Sunday Nov 15, 2026 at 11:00 UTC
        var sunTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero));
        var sunJob = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, sunTime);
        var sunResult = await sunJob.ExecuteCutoffCheckAsync();

        sunResult.Status.Should().Be("ExcludedDay");
        sunResult.RemindersSent.Should().Be(0);

        // Case 3: Monday Nov 16, 2026 at 11:00 UTC (Workday) -> MUST send reminders
        var monTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 11, 0, 0, TimeSpan.Zero));
        var monJob = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, monTime);
        var monResult = await monJob.ExecuteCutoffCheckAsync();

        monResult.Status.Should().Be("Completed");
        monResult.RemindersSent.Should().Be(1);

        emailMock.Verify(e => e.SendCutoffReminderEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChallengeCutoffDeadline_ExactBoundary_0959_vs_1001()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "UTC",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        var emp = new Employee { FullName = "Boundary Dev", Email = "boundary@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();

        // Sub-test 1: 09:59:59 UTC on Wednesday Nov 18, 2026
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 59, 59, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var resultBefore = await job.ExecuteCutoffCheckAsync();
        resultBefore.Status.Should().Be("BeforeCutoff");
        resultBefore.RemindersSent.Should().Be(0);
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        // Sub-test 2: 10:01:00 UTC on Wednesday Nov 18, 2026
        fakeTime.SetUtcNow(new DateTimeOffset(2026, 11, 18, 10, 1, 0, TimeSpan.Zero));
        var resultAfter = await job.ExecuteCutoffCheckAsync();

        resultAfter.Status.Should().Be("Completed");
        resultAfter.RemindersSent.Should().Be(1);
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChallengeIdempotency_RunCutoffJobTenTimesConsecutively_SendsAtMostOneEmailPerDay()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "UTC",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        var emp1 = new Employee { FullName = "Idem 1", Email = "idem1@company.com", Department = "Eng", JobTitle = "Dev" };
        var emp2 = new Employee { FullName = "Idem 2", Email = "idem2@company.com", Department = "Eng", JobTitle = "Dev" };
        var compliant = new Employee { FullName = "Compliant Emp", Email = "compliant@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.AddRange(emp1, emp2, compliant);
        await context.SaveChangesAsync();

        var date = new DateOnly(2026, 11, 18);
        context.DailyPlans.Add(new DailyPlan { EmployeeId = compliant.Id, PlanDate = date });
        await context.SaveChangesAsync();

        var emailMock = new Mock<IEmailNotificationService>();
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 15, 0, TimeSpan.Zero));

        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(emailMock.Object), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        // Run 1: Sends exactly 2 emails (for emp1 and emp2)
        var run1Result = await job.ExecuteCutoffCheckAsync();
        run1Result.Status.Should().Be("Completed");
        run1Result.RemindersSent.Should().Be(2);

        // Runs 2 to 10: Must find existing DailyPlanReminderLog and send ZERO emails
        for (int i = 2; i <= 10; i++)
        {
            fakeTime.SetUtcNow(new DateTimeOffset(2026, 11, 18, 10, 15 + i, 0, TimeSpan.Zero));
            var subsequentResult = await job.ExecuteCutoffCheckAsync();

            subsequentResult.Status.Should().Be("Completed");
            subsequentResult.RemindersSent.Should().Be(0);
            subsequentResult.NotifiedEmployeeEmails.Should().BeEmpty();
        }

        // Verify emailMock was invoked EXACTLY twice across all 10 runs
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync("idem1@company.com", "Idem 1", date, new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync("idem2@company.com", "Idem 2", date, new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);
        emailMock.Verify(e => e.SendCutoffReminderEmailAsync("compliant@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify DB contains exactly 2 reminder logs
        using var verifyContext = CreateContext();
        var logs = await verifyContext.DailyPlanReminderLogs.Where(l => l.PlanDate == date).ToListAsync();
        logs.Should().HaveCount(2);
    }

    // =========================================================================
    // 3. MAILKIT EMAIL SERVICE FAULT FALLBACK
    // =========================================================================

    [Fact]
    public async Task ChallengeMailKitFallback_UnreachableSmtp_SendReviewAssigned_DoesNotThrowAndPersistsFailedLog()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "nonexistent-smtp-host.invalid.local",
            ["Smtp:Port"] = "59998",
            ["Smtp:SenderEmail"] = "no-reply@companycrm.local",
            ["Smtp:SenderName"] = "CompanyCRM"
        }).Build();

        var service = new MailKitEmailNotificationService(
            CreateScopeFactory(),
            config,
            NullLogger<MailKitEmailNotificationService>.Instance);

        var workItemId = Guid.NewGuid();
        var act = async () => await service.SendReviewAssignedEmailAsync(
            "reviewer@test.com",
            "Lead Reviewer",
            "REV-101",
            "Unreachable SMTP Check",
            workItemId);

        await act.Should().NotThrowAsync();

        using var verifyContext = CreateContext();
        var log = await verifyContext.EmailNotificationLogs.FirstOrDefaultAsync(l => l.RecipientEmail == "reviewer@test.com");
        log.Should().NotBeNull();
        log!.Success.Should().BeFalse();
        log.TriggerEvent.Should().Be("ReviewerAssigned");
        log.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(ReviewStatus.Approved, "ReviewApproved")]
    [InlineData(ReviewStatus.ClarificationNeeded, "ReviewClarificationRequested")]
    [InlineData(ReviewStatus.Denied, "ReviewDenied")]
    public async Task ChallengeMailKitFallback_UnreachableSmtp_SendReviewDecision_AllStatuses_DoesNotThrowAndPersistsFailedLog(
        ReviewStatus status,
        string expectedTriggerEvent)
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "nonexistent-smtp-host.invalid.local",
            ["Smtp:Port"] = "59998"
        }).Build();

        var service = new MailKitEmailNotificationService(
            CreateScopeFactory(),
            config,
            NullLogger<MailKitEmailNotificationService>.Instance);

        var act = async () => await service.SendReviewDecisionEmailAsync(
            "creator@test.com",
            "Author",
            "DEC-202",
            "Review Decision Fallback",
            status,
            "Comment note");

        await act.Should().NotThrowAsync();

        using var verifyContext = CreateContext();
        var log = await verifyContext.EmailNotificationLogs.FirstOrDefaultAsync(l => l.RecipientEmail == "creator@test.com" && l.TriggerEvent == expectedTriggerEvent);
        log.Should().NotBeNull();
        log!.Success.Should().BeFalse();
        log.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChallengeMailKitFallback_UnreachableSmtp_SendCutoffReminder_DoesNotThrowAndPersistsFailedLog()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "nonexistent-smtp-host.invalid.local",
            ["Smtp:Port"] = "59998"
        }).Build();

        var service = new MailKitEmailNotificationService(
            CreateScopeFactory(),
            config,
            NullLogger<MailKitEmailNotificationService>.Instance);

        var act = async () => await service.SendCutoffReminderEmailAsync(
            "delinquent@test.com",
            "Delinquent User",
            new DateOnly(2026, 11, 20),
            new TimeOnly(10, 0, 0));

        await act.Should().NotThrowAsync();

        using var verifyContext = CreateContext();
        var log = await verifyContext.EmailNotificationLogs.FirstOrDefaultAsync(l => l.RecipientEmail == "delinquent@test.com");
        log.Should().NotBeNull();
        log!.Success.Should().BeFalse();
        log.TriggerEvent.Should().Be("DailyPlanCutoffReminder");
        log.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChallengeMailKitFallback_CallingTransaction_IsNotRolledBackOnEmailFailure()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "nonexistent-smtp-host.invalid.local",
            ["Smtp:Port"] = "59998"
        }).Build();

        var service = new MailKitEmailNotificationService(
            CreateScopeFactory(),
            config,
            NullLogger<MailKitEmailNotificationService>.Instance);

        // Simulate a business transaction in the caller: create Project and WorkItem
        var project = new Project { Code = "TXTEST", Name = "Transaction Test Project" };
        var employee = new Employee { FullName = "Tx Dev", Email = "tx@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "TXTEST-1",
            Title = "Transaction Work Item"
        };
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        // Dispatch review notification using unreachable SMTP
        var emailAct = async () => await service.SendReviewAssignedEmailAsync(
            "tx.reviewer@company.com",
            "Tx Reviewer",
            workItem.Code,
            workItem.Title,
            workItem.Id);

        await emailAct.Should().NotThrowAsync();

        // Verify the caller's database records are intact and NOT corrupted or rolled back!
        using var verifyContext = CreateContext();
        var savedItem = await verifyContext.WorkItems.FirstOrDefaultAsync(w => w.Id == workItem.Id);
        savedItem.Should().NotBeNull();
        savedItem!.Code.Should().Be("TXTEST-1");

        // Verify email log recorded the failure cleanly
        var log = await verifyContext.EmailNotificationLogs.FirstOrDefaultAsync(l => l.RecipientEmail == "tx.reviewer@company.com");
        log.Should().NotBeNull();
        log!.Success.Should().BeFalse();
    }
}
