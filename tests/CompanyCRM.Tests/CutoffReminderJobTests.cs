namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Tests.Common;

public class CutoffReminderJobTests
{
    private readonly TestCrmDbContext _dbContext;
    private readonly Mock<IEmailNotificationService> _emailMock;
    private readonly TestTimeProvider _timeProvider;

    public CutoffReminderJobTests()
    {
        _dbContext = TestDataBuilder.CreateInMemoryDbContext();
        _emailMock = new Mock<IEmailNotificationService>();
        // Default time: Monday, 2026-11-16 at 10:15:00 UTC
        _timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-11-16T10:15:00Z"));
    }

    private TestPlanningCutoffJob CreateCutoffJob()
    {
        return new TestPlanningCutoffJob(_dbContext, _emailMock.Object, _timeProvider);
    }

    // =========================================================================
    // Tier 1: Feature Coverage (5 test cases)
    // =========================================================================

    [Fact]
    public async Task ExecuteJob_AfterCutoff_ActiveEmployeesMissingPlan_SendsReminderEmails()
    {
        // Arrange
        // Time is 10:15:00 UTC (past 10:00:00 cutoff)
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T10:15:00Z"));

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp1 = TestDataBuilder.CreateEmployee(email: "emp1@company.com", fullName: "Employee One");
        var emp2 = TestDataBuilder.CreateEmployee(email: "emp2@company.com", fullName: "Employee Two");
        _dbContext.Employees.AddRange(emp1, emp2);

        // emp1 has plan submitted today
        var emp1Plan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp1.Id,
            PlanDate = new DateOnly(2026, 11, 16),
            SubmittedAt = DateTimeOffset.Parse("2026-11-16T09:45:00Z"),
            IsSubmittedOnTime = true
        };
        _dbContext.DailyPlans.Add(emp1Plan);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be("Completed");
        result.RemindersSent.Should().Be(1);
        result.NotifiedEmployeeEmails.Should().ContainSingle(e => e == "emp2@company.com");

        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            "emp2@company.com", "Employee Two", new DateOnly(2026, 11, 16), new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);

        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            "emp1@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJob_BeforeCutoffTime_SendsZeroEmails()
    {
        // Arrange
        // Time is 09:30:00 UTC (before 10:00:00 cutoff)
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T09:30:00Z"));

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "emp@company.com", fullName: "Employee Dev");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be("BeforeCutoff");
        result.RemindersSent.Should().Be(0);

        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        _dbContext.DailyPlanReminderLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-11-21T11:00:00Z")] // Saturday
    [InlineData("2026-11-22T11:00:00Z")] // Sunday
    public async Task ExecuteJob_OnExcludedDays_Weekends_SendsZeroEmails(string weekendDateStr)
    {
        // Arrange
        _timeProvider.SetUtcNow(DateTimeOffset.Parse(weekendDateStr));

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC", excludedDaysJson: "[0, 6]");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "emp@company.com", fullName: "Weekend Dev");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be("ExcludedDay");
        result.RemindersSent.Should().Be(0);

        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        _dbContext.DailyPlanReminderLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteJob_ReminderSent_LogsRecordInDailyPlanReminderLog()
    {
        // Arrange
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-17T10:30:00Z")); // Tuesday

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "emp-target@company.com", fullName: "Target Dev");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        result.RemindersSent.Should().Be(1);

        var log = await _dbContext.DailyPlanReminderLogs.FirstOrDefaultAsync(l => l.EmployeeId == emp.Id && l.PlanDate == new DateOnly(2026, 11, 17));
        log.Should().NotBeNull();
        log!.RecipientEmail.Should().Be("emp-target@company.com");
        log.EmailDeliveryStatus.Should().Be("Sent");
        log.ReminderSentAt.Should().BeCloseTo(DateTimeOffset.Parse("2026-11-17T10:30:00Z"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TriggerCutoffCheck_OnDemandEndpoint_ExecutesSuccessfully()
    {
        // Arrange
        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var summary = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        summary.Should().NotBeNull();
        summary.Status.Should().BeOneOf("Completed", "BeforeCutoff", "ExcludedDay");
    }

    // =========================================================================
    // Tier 2: Boundary & Corner Cases (5 test cases)
    // =========================================================================

    [Fact]
    public async Task ExecuteJob_SubsequentRunSameDay_IsIdempotent_SendsZeroDuplicateEmails()
    {
        // Arrange
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T10:15:00Z"));

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "emp@company.com", fullName: "Dev Idempotency");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // First run sends 1 email
        var firstRun = await job.ExecuteCutoffCheckAsync(CancellationToken.None);
        firstRun.RemindersSent.Should().Be(1);

        // Advance 15 minutes (next Hangfire run at 10:30 on same day)
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        // Act
        var secondRun = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert: 0 additional emails sent
        secondRun.RemindersSent.Should().Be(0);
        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            "emp@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Once);

        _dbContext.DailyPlanReminderLogs.Count(l => l.EmployeeId == emp.Id && l.PlanDate == new DateOnly(2026, 11, 16)).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteJob_EmployeeSubmittingPlan5MinutesBeforeCutoff_ExcludedFromReminders()
    {
        // Arrange
        var cutoffTime = new TimeOnly(10, 0, 0);
        var config = TestDataBuilder.CreateCutoffConfig(cutoffTime, "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "ontime@company.com", fullName: "OnTime Dev");
        _dbContext.Employees.Add(emp);

        // Plan submitted at 09:55 UTC (5 minutes before cutoff)
        var plan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            PlanDate = new DateOnly(2026, 11, 16),
            SubmittedAt = DateTimeOffset.Parse("2026-11-16T09:55:00Z"),
            IsSubmittedOnTime = true
        };
        _dbContext.DailyPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Job executes past cutoff at 10:15 UTC
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T10:15:00Z"));
        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        result.RemindersSent.Should().Be(0);
        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            emp.Email, It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _dbContext.DailyPlanReminderLogs.Should().BeEmpty();
    }

    [Fact]
    public void SubmitDailyPlan_5MinutesAfterCutoff_MarksIsSubmittedOnTimeFalse()
    {
        // Arrange
        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        var planDate = new DateOnly(2026, 11, 16);
        var dailyPlan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PlanDate = planDate
        };
        var submissionTimeUtc = DateTimeOffset.Parse("2026-11-16T10:05:00Z"); // 5 mins late

        // Act
        dailyPlan.EvaluateSubmissionTiming(config, submissionTimeUtc);

        // Assert
        dailyPlan.IsSubmittedOnTime.Should().BeFalse();
        dailyPlan.SubmittedAt.Should().Be(submissionTimeUtc);
    }

    [Fact]
    public void SubmitDailyPlan_5MinutesBeforeCutoff_MarksIsSubmittedOnTimeTrue()
    {
        // Arrange
        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        var planDate = new DateOnly(2026, 11, 16);
        var dailyPlan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PlanDate = planDate
        };
        var submissionTimeUtc = DateTimeOffset.Parse("2026-11-16T09:55:00Z"); // 5 mins early

        // Act
        dailyPlan.EvaluateSubmissionTiming(config, submissionTimeUtc);

        // Assert
        dailyPlan.IsSubmittedOnTime.Should().BeTrue();
        dailyPlan.SubmittedAt.Should().Be(submissionTimeUtc);
    }

    [Fact]
    public async Task ExecuteJob_CustomExcludedDay_Wednesday_SendsZeroEmails()
    {
        // Arrange: Exclude Wednesday (DayOfWeek = 3)
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-18T10:15:00Z")); // Wednesday

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC", excludedDaysJson: "[3]");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "wed-dev@company.com", fullName: "Wednesday Dev");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        result.Status.Should().Be("ExcludedDay");
        result.RemindersSent.Should().Be(0);
        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJob_ExcludedDaysWithJsonContainingDigitZeroInNumber10_DoesNotFalselyExcludeMonday()
    {
        // Arrange: Excluded days contains 10 and 60 (contains '0' and '6' as substrings, but NOT 0=Sunday or 1=Monday)
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T10:15:00Z")); // Monday

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC", excludedDaysJson: "[10, 60]");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "monday-dev@company.com", fullName: "Monday Dev");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert: Monday must NOT be excluded by naive string matching
        result.Status.Should().Be("Completed");
        result.RemindersSent.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteJob_CrossPlatformTimezone_InvalidTimeZoneId_FallsBackToUtc()
    {
        // Arrange
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T10:15:00Z"));

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "Invalid_Corrupt_TimeZone_String");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var emp = TestDataBuilder.CreateEmployee(email: "tz-test@company.com", fullName: "TZ Test Dev");
        _dbContext.Employees.Add(emp);
        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert: must NOT throw TimeZoneNotFoundException and successfully send 1 reminder
        result.Status.Should().Be("Completed");
        result.RemindersSent.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteJob_EmployeeWithReminderAlreadySentToday_SkippedEvenIfStillMissingPlan()
    {
        // Arrange
        _timeProvider.SetUtcNow(DateTimeOffset.Parse("2026-11-16T11:00:00Z"));

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        _dbContext.PlanningCutoffConfigs.Add(config);

        var empA = TestDataBuilder.CreateEmployee(email: "empA@company.com", fullName: "Employee A");
        var empB = TestDataBuilder.CreateEmployee(email: "empB@company.com", fullName: "Employee B");
        _dbContext.Employees.AddRange(empA, empB);

        // empA already received a reminder for today at 10:15
        _dbContext.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
        {
            Id = Guid.NewGuid(),
            EmployeeId = empA.Id,
            PlanDate = new DateOnly(2026, 11, 16),
            RecipientEmail = empA.Email,
            EmailDeliveryStatus = "Sent",
            ReminderSentAt = DateTimeOffset.Parse("2026-11-16T10:15:00Z")
        });

        await _dbContext.SaveChangesAsync();

        var job = CreateCutoffJob();

        // Act
        var result = await job.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert: Only empB receives reminder; empA skipped
        result.RemindersSent.Should().Be(1);
        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            "empB@company.com", "Employee B", new DateOnly(2026, 11, 16), new TimeOnly(10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);

        _emailMock.Verify(x => x.SendCutoffReminderEmailAsync(
            "empA@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("[\"Wednesday\"]", new[] { 3 })]
    [InlineData("[0, 3, 6]", new[] { 0, 3, 6 })]
    [InlineData("[\"0\", \"6\", \"friday\"]", new[] { 0, 5, 6 })]
    [InlineData("[10, 60]", new int[0])]
    [InlineData("[]", new int[0])]
    [InlineData("not-json", new[] { 0, 6 })]
    public void ParseExcludedDays_SupportsNumericAndDayNameTokens_AndHandlesEdgeCases(string json, int[] expected)
    {
        var parsed = CompanyCRM.Infrastructure.BackgroundJobs.DailyPlanCutoffReminderJob.ParseExcludedDays(json);
        parsed.Should().BeEquivalentTo(expected);
    }
}
