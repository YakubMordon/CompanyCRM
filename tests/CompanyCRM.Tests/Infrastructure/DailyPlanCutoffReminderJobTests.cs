using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

public class DailyPlanCutoffReminderJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;
    private readonly Mock<IEmailNotificationService> _emailMock;

    public DailyPlanCutoffReminderJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite(_connection)
            .Options;

        _emailMock = new Mock<IEmailNotificationService>();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CrmDbContext CreateContext() => new CrmDbContext(_options);

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrmDbContext>(opts => opts.UseSqlite(_connection));
        services.AddSingleton(_emailMock.Object);
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

    [Fact]
    public async Task ExecuteJob_BeforeCutoff_SendsZeroEmails_ReturnsBeforeCutoffStatus()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Cutoff is 10:00 UTC
        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "UTC",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });
        context.Employees.Add(new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" });
        await context.SaveChangesAsync();

        // 09:30 UTC on Wednesday (Nov 18, 2026 is Wednesday)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var result = await job.ExecuteCutoffCheckAsync();

        result.Status.Should().Be("BeforeCutoff");
        result.RemindersSent.Should().Be(0);
        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJob_OnWeekend_SendsZeroEmails_ReturnsExcludedDayStatus()
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
        context.Employees.Add(new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" });
        await context.SaveChangesAsync();

        // 11:00 UTC on Saturday (Nov 14, 2026 is Saturday)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var result = await job.ExecuteCutoffCheckAsync();

        result.Status.Should().Be("ExcludedDay");
        result.RemindersSent.Should().Be(0);
        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJob_WhenDisabled_ReturnsDisabledStatus()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "UTC",
            IsReminderEnabled = false
        });
        await context.SaveChangesAsync();

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var result = await job.ExecuteCutoffCheckAsync();
        result.Status.Should().Be("Disabled");
        result.RemindersSent.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteJob_AfterCutoff_ActiveEmployeesMissingPlan_SendsReminderAndLogs()
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

        var emp1 = new Employee { FullName = "Alice", Email = "alice@company.com", Department = "Eng", JobTitle = "Dev" };
        var emp2 = new Employee { FullName = "Bob", Email = "bob@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.AddRange(emp1, emp2);
        await context.SaveChangesAsync();

        // 10:30 UTC on Wednesday (Nov 18, 2026)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var result = await job.ExecuteCutoffCheckAsync();

        result.Status.Should().Be("Completed");
        result.RemindersSent.Should().Be(2);
        result.NotifiedEmployeeEmails.Should().Contain(new[] { "alice@company.com", "bob@company.com" });

        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            "alice@company.com", "Alice", new DateOnly(2026, 11, 18), new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);
        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            "bob@company.com", "Bob", new DateOnly(2026, 11, 18), new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);

        // Verify DailyPlanReminderLog records in DB
        using var verifyContext = CreateContext();
        var logs = await verifyContext.DailyPlanReminderLogs.ToListAsync();
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.EmailDeliveryStatus == "Sent" && l.PlanDate == new DateOnly(2026, 11, 18));
    }

    [Fact]
    public async Task ExecuteJob_EmployeeSubmittedPlanBeforeCutoff_ExcludedFromReminders()
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

        var compliantEmp = new Employee { FullName = "Compliant", Email = "compliant@company.com", Department = "Eng", JobTitle = "Dev" };
        var delinquentEmp = new Employee { FullName = "Delinquent", Email = "delinquent@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.AddRange(compliantEmp, delinquentEmp);
        await context.SaveChangesAsync();

        // Compliant employee submitted daily plan at 09:15 UTC for Nov 18
        context.DailyPlans.Add(new DailyPlan
        {
            EmployeeId = compliantEmp.Id,
            PlanDate = new DateOnly(2026, 11, 18),
            SubmittedAt = new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.Zero)
        });
        await context.SaveChangesAsync();

        // 10:30 UTC on Wednesday (Nov 18, 2026)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var result = await job.ExecuteCutoffCheckAsync();

        result.Status.Should().Be("Completed");
        result.RemindersSent.Should().Be(1);
        result.NotifiedEmployeeEmails.Should().ContainSingle().Which.Should().Be("delinquent@company.com");

        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            "compliant@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            "delinquent@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJob_SubsequentRunSameDay_IsIdempotent_SendsZeroDuplicateEmails()
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

        var emp = new Employee { FullName = "Solo", Email = "solo@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 15, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        // Run 1: Sends 1 reminder
        var result1 = await job.ExecuteCutoffCheckAsync();
        result1.RemindersSent.Should().Be(1);

        // Run 2 (e.g. 15 minutes later at 10:30 UTC)
        fakeTime.SetUtcNow(new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.Zero));
        var result2 = await job.ExecuteCutoffCheckAsync();

        // Must be idempotent: 0 duplicate emails!
        result2.RemindersSent.Should().Be(0);
        result2.NotifiedEmployeeEmails.Should().BeEmpty();

        // Total email invocations across both runs remains 1
        _emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            "solo@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJob_InvalidTimeZoneId_FallsBackToUtc_AndExecutesNormally()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        context.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
        {
            ProjectId = null,
            CutoffTimeLocal = new TimeOnly(10, 0, 0),
            TimeZoneId = "Invalid/Nonexistent_Timezone",
            IsReminderEnabled = true,
            ExcludedDaysOfWeekJson = "[0, 6]"
        });

        context.Employees.Add(new Employee { FullName = "Tz Test", Email = "tz@company.com", Department = "Eng", JobTitle = "Dev" });
        await context.SaveChangesAsync();

        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        // Should not throw, but fall back to UTC and execute cutoff check
        var result = await job.ExecuteCutoffCheckAsync();
        result.Status.Should().Be("Completed");
        result.RemindersSent.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteJob_WithProjectId_UsesProjectOverride_AndScopesToProjectMembers()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var projectA = new CompanyCRM.Domain.Entities.Project { Code = "PA", Name = "Project A" };
        var projectB = new CompanyCRM.Domain.Entities.Project { Code = "PB", Name = "Project B" };
        var empA = new Employee { FullName = "Emp A", Email = "empa@company.com", Department = "Eng", JobTitle = "Dev" };
        var empB = new Employee { FullName = "Emp B", Email = "empb@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.AddRange(projectA, projectB);
        context.Employees.AddRange(empA, empB);
        await context.SaveChangesAsync();

        context.ProjectMembers.AddRange(
            new CompanyCRM.Domain.Entities.ProjectMember { ProjectId = projectA.Id, EmployeeId = empA.Id },
            new CompanyCRM.Domain.Entities.ProjectMember { ProjectId = projectB.Id, EmployeeId = empB.Id }
        );

        // Global: 10:00 UTC; Project A override: 09:00 UTC; Project B: no override
        context.PlanningCutoffConfigs.AddRange(
            new PlanningCutoffConfig
            {
                ProjectId = null,
                CutoffTimeLocal = new TimeOnly(10, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]"
            },
            new PlanningCutoffConfig
            {
                ProjectId = projectA.Id,
                CutoffTimeLocal = new TimeOnly(9, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]"
            }
        );
        await context.SaveChangesAsync();

        // 09:30 UTC on Wednesday (after Project A's 09:00 cutoff, before Global 10:00 cutoff)
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));
        var job = new DailyPlanCutoffReminderJob(CreateScopeFactory(), NullLogger<DailyPlanCutoffReminderJob>.Instance, fakeTime);

        var projAResult = await job.ExecuteCutoffCheckAsync(projectA.Id);
        projAResult.Status.Should().Be("Completed");
        projAResult.CutoffTime.Should().Be(new TimeOnly(9, 0, 0));
        projAResult.RemindersSent.Should().Be(1);
        projAResult.NotifiedEmployeeEmails.Should().ContainSingle().Which.Should().Be("empa@company.com");

        var projBResult = await job.ExecuteCutoffCheckAsync(projectB.Id);
        projBResult.Status.Should().Be("BeforeCutoff");
        projBResult.CutoffTime.Should().Be(new TimeOnly(10, 0, 0));
        projBResult.RemindersSent.Should().Be(0);

        var globalResult = await job.ExecuteCutoffCheckAsync();
        globalResult.Status.Should().Be("BeforeCutoff");
        globalResult.CutoffTime.Should().Be(new TimeOnly(10, 0, 0));
        globalResult.RemindersSent.Should().Be(0);
    }
}
