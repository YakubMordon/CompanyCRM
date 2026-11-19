namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;

/// <summary>
/// Empirical Adversarial Stress Test Harness (Challenger Round 2):
/// Executes deep boundary, edge-case, and bypass-resistance verification on:
/// 1. DailyPlan.EvaluateSubmissionTiming across timezone edges, corrupt timezones, and cutoff boundaries.
/// 2. EmployeeSkill.SetProficiency & ProficiencyLevel validation bypass attempts and [1, 5] boundaries.
/// 3. RbacAuthorizationService URI permutations, role combinations, and malformed inputs.
/// </summary>
public class ChallengerE2ER2StressTests
{
    private readonly IRbacAuthorizationService _rbac = RbacAuthorizationService.Instance;

    // =========================================================================
    // 1. DailyPlan.EvaluateSubmissionTiming Adversarial Stress Tests
    // =========================================================================

    [Fact]
    public void DailyPlan_AsiaTokyo_2359Utc_ShiftsToNextDay_EvaluatesCutoffCorrectly()
    {
        // Asia/Tokyo is UTC+9.
        // 2026-11-14 23:59:00 UTC converts to 2026-11-15 08:59:00 JST.
        var submissionUtc = new DateTimeOffset(2026, 11, 14, 23, 59, 0, TimeSpan.Zero);
        var config = new PlanningCutoffConfig
        {
            TimeZoneId = "Tokyo Standard Time", // Windows ID (also compatible with .NET IANA mapping)
            CutoffTimeLocal = new TimeOnly(10, 0, 0)
        };

        // Case 1: Daily plan date is for 2026-11-14 (yesterday from Tokyo's perspective).
        // Since local date in Tokyo is 2026-11-15, the submission is late for the 14th plan.
        var planForYesterday = new DailyPlan
        {
            PlanDate = new DateOnly(2026, 11, 14)
        };
        planForYesterday.EvaluateSubmissionTiming(config, submissionUtc);
        planForYesterday.IsSubmittedOnTime.Should().BeFalse(
            "Local date in Tokyo is Nov 15, which is after the Nov 14 plan date, so it must be marked late.");

        // Case 2: Daily plan date is for 2026-11-15 (today from Tokyo's perspective).
        // Local time in Tokyo is 08:59:00 JST, which is before the 10:00:00 cutoff.
        var planForToday = new DailyPlan
        {
            PlanDate = new DateOnly(2026, 11, 15)
        };
        planForToday.EvaluateSubmissionTiming(config, submissionUtc);
        planForToday.IsSubmittedOnTime.Should().BeTrue(
            "Local date in Tokyo matches Nov 15 and 08:59 JST is prior to 10:00 JST cutoff, so it must be on time.");
    }

    [Fact]
    public void DailyPlan_AmericasLosAngeles_0001Utc_ShiftsToPriorDay_EvaluatesCutoffCorrectly()
    {
        // America/Los_Angeles (Pacific Standard Time) is UTC-8.
        // 2026-11-15 00:01:00 UTC converts to 2026-11-14 16:01:00 PST.
        var submissionUtc = new DateTimeOffset(2026, 11, 15, 0, 1, 0, TimeSpan.Zero);
        var config = new PlanningCutoffConfig
        {
            TimeZoneId = "Pacific Standard Time",
            CutoffTimeLocal = new TimeOnly(10, 0, 0)
        };

        // Case 1: Daily plan is for 2026-11-15 (tomorrow from Pacific perspective).
        // Since local date in LA is 2026-11-14, the employee submitted early (evening before).
        var planForTomorrow = new DailyPlan
        {
            PlanDate = new DateOnly(2026, 11, 15)
        };
        planForTomorrow.EvaluateSubmissionTiming(config, submissionUtc);
        planForTomorrow.IsSubmittedOnTime.Should().BeTrue(
            "Local date in LA (Nov 14) is prior to PlanDate (Nov 15), meaning early submission the day before.");

        // Case 2: Daily plan is for 2026-11-14 (today from Pacific perspective).
        // Local time is 16:01:00 PST, well past the 10:00:00 cutoff for the 14th.
        var planForToday = new DailyPlan
        {
            PlanDate = new DateOnly(2026, 11, 14)
        };
        planForToday.EvaluateSubmissionTiming(config, submissionUtc);
        planForToday.IsSubmittedOnTime.Should().BeFalse(
            "Local time 16:01:00 PST is after 10:00:00 PST cutoff on the same date, so it must be late.");
    }

    [Theory]
    [InlineData("Corrupt_NonExistent_TimeZone_12345")]
    [InlineData("Invalid/Timezone/String")]
    [InlineData("")]
    [InlineData("   ")]
    public void DailyPlan_InvalidOrCorruptTimeZone_GracefullyFallsBackToUtc(string invalidTz)
    {
        var config = new PlanningCutoffConfig
        {
            TimeZoneId = invalidTz,
            CutoffTimeLocal = new TimeOnly(10, 0, 0)
        };

        // In UTC: 09:30 UTC is on time for cutoff 10:00
        var onTimePlan = new DailyPlan
        {
            PlanDate = new DateOnly(2026, 11, 14)
        };
        var onTimeSubmission = new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);
        var actOnTime = () => onTimePlan.EvaluateSubmissionTiming(config, onTimeSubmission);
        actOnTime.Should().NotThrow("Invalid timezone string must fallback to UTC instead of throwing.");
        onTimePlan.IsSubmittedOnTime.Should().BeTrue("09:30 UTC is before 10:00 UTC cutoff.");

        // In UTC: 10:30 UTC is late for cutoff 10:00
        var latePlan = new DailyPlan
        {
            PlanDate = new DateOnly(2026, 11, 14)
        };
        var lateSubmission = new DateTimeOffset(2026, 11, 14, 10, 30, 0, TimeSpan.Zero);
        var actLate = () => latePlan.EvaluateSubmissionTiming(config, lateSubmission);
        actLate.Should().NotThrow();
        latePlan.IsSubmittedOnTime.Should().BeFalse("10:30 UTC is after 10:00 UTC cutoff.");
    }

    [Fact]
    public void DailyPlan_CutoffBoundary_ExactSecond_Vs_OneSecondAfter_EvaluatesPrecisely()
    {
        var config = new PlanningCutoffConfig
        {
            TimeZoneId = "UTC",
            CutoffTimeLocal = new TimeOnly(10, 0, 0)
        };

        var planDate = new DateOnly(2026, 11, 14);

        // Sub-second before: 09:59:59.999 -> ON TIME
        var planBefore = new DailyPlan { PlanDate = planDate };
        planBefore.EvaluateSubmissionTiming(config, new DateTimeOffset(2026, 11, 14, 9, 59, 59, 999, TimeSpan.Zero));
        planBefore.IsSubmittedOnTime.Should().BeTrue("09:59:59.999 is before 10:00:00 cutoff.");

        // Exactly at cutoff second: 10:00:00.000 -> ON TIME (<=)
        var planExact = new DailyPlan { PlanDate = planDate };
        planExact.EvaluateSubmissionTiming(config, new DateTimeOffset(2026, 11, 14, 10, 0, 0, 0, TimeSpan.Zero));
        planExact.IsSubmittedOnTime.Should().BeTrue("10:00:00.000 is exactly at cutoff and must be accepted as on-time.");

        // Sub-second after: 10:00:00.001 -> LATE
        var planMsAfter = new DailyPlan { PlanDate = planDate };
        planMsAfter.EvaluateSubmissionTiming(config, new DateTimeOffset(2026, 11, 14, 10, 0, 0, 1, TimeSpan.Zero));
        planMsAfter.IsSubmittedOnTime.Should().BeFalse("10:00:00.001 is past cutoff second and must be marked late.");

        // Exactly 1 second after: 10:00:01.000 -> LATE
        var planOneSecAfter = new DailyPlan { PlanDate = planDate };
        planOneSecAfter.EvaluateSubmissionTiming(config, new DateTimeOffset(2026, 11, 14, 10, 0, 1, 0, TimeSpan.Zero));
        planOneSecAfter.IsSubmittedOnTime.Should().BeFalse("10:00:01.000 is 1 second after cutoff and must be marked late.");
    }

    [Fact]
    public void DailyPlan_EvaluateSubmissionTiming_NullConfig_ThrowsArgumentNullException()
    {
        var plan = new DailyPlan();
        var act = () => plan.EvaluateSubmissionTiming(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // 2. EmployeeSkill Invariant & Validation Bypass Adversarial Stress Tests
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void EmployeeSkill_DirectPropertyAssignment_ValidationBypass_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var skill = new EmployeeSkill("TypeScript", "Frontend", 3);
        skill.ProficiencyLevel.Should().Be(3);

        // Attempt direct property bypass
        var act = () => { skill.ProficiencyLevel = invalidLevel; };

        act.Should().Throw<ArgumentOutOfRangeException>(
            $"Direct property assignment ProficiencyLevel = {invalidLevel} must throw ArgumentOutOfRangeException");

        // Verify invariant retention: value was not corrupted by failed assignment
        skill.ProficiencyLevel.Should().Be(3, "Prior valid proficiency must be preserved upon failed assignment");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void EmployeeSkill_SetProficiency_InvalidValues_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var skill = new EmployeeSkill();

        var act = () => skill.SetProficiency(invalidLevel);

        act.Should().Throw<ArgumentOutOfRangeException>(
            $"SetProficiency({invalidLevel}) must throw ArgumentOutOfRangeException");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void EmployeeSkill_Constructor_InvalidProficiency_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var act = () => new EmployeeSkill("C#", "Backend", invalidLevel);

        act.Should().Throw<ArgumentOutOfRangeException>(
            $"Constructor with proficiency {invalidLevel} must throw ArgumentOutOfRangeException");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EmployeeSkill_ValidProficiencies_AllBoundariesSucceed(int validLevel)
    {
        // 1. Property setter
        var skill = new EmployeeSkill();
        skill.ProficiencyLevel = validLevel;
        skill.ProficiencyLevel.Should().Be(validLevel);

        // 2. SetProficiency method
        skill.SetProficiency(validLevel);
        skill.ProficiencyLevel.Should().Be(validLevel);

        // 3. Constructor
        var skillConstructed = new EmployeeSkill(".NET Core", "Backend", validLevel);
        skillConstructed.ProficiencyLevel.Should().Be(validLevel);
    }

    [Fact]
    public void EmployeeSkill_DefaultConstructor_InitializesToMinProficiency()
    {
        var skill = new EmployeeSkill();
        skill.ProficiencyLevel.Should().Be(EmployeeSkill.MinProficiency);
        skill.ProficiencyLevel.Should().Be(1);
    }

    // =========================================================================
    // 3. RbacAuthorizationService URI Permutations & Role Combinations
    // =========================================================================

    [Theory]
    [InlineData("/api/management/compensation/")]                           // Trailing slash
    [InlineData("/api/management/compensation?param=1")]                    // Query parameter
    [InlineData("/api/management/compensation?param=1&filter=all#section")] // Query and anchor
    [InlineData("/API/MANAGEMENT/PROFITABILITY")]                           // Uppercase
    [InlineData("/Api/Management/Compensation")]                            // Mixed case
    [InlineData("/api/management")]                                         // Base management route
    [InlineData("/api/management/")]                                        // Base management with trailing slash
    [InlineData("/api/management///compensation//")]                        // Redundant slashes
    [InlineData("https://crm.company.com/api/management/compensation")]    // Absolute HTTPS URI
    [InlineData("http://localhost:5001/API/MANAGEMENT/PROFITABILITY?y=26")] // Absolute HTTP URI with query
    public void Rbac_ManagementEndpoints_StrictlyEnforces403ForEmployeeAndTeamLead_200ForHigherManagement(string endpoint)
    {
        var empContext = new SecurityContext(true, AppRole.Employee);
        var tlContext = new SecurityContext(true, AppRole.TeamLead);
        var hmContext = new SecurityContext(true, AppRole.HigherManagement);

        // Employee MUST receive 403 Forbidden
        _rbac.GetAuthorizationStatusCode(empContext, endpoint).Should().Be(
            403, $"Employee must be blocked (HTTP 403) from {endpoint}");
        _rbac.IsAuthorized(empContext, endpoint).Should().BeFalse();

        // TeamLead MUST receive 403 Forbidden
        _rbac.GetAuthorizationStatusCode(tlContext, endpoint).Should().Be(
            403, $"TeamLead must be blocked (HTTP 403) from {endpoint}");
        _rbac.IsAuthorized(tlContext, endpoint).Should().BeFalse();

        // HigherManagement MUST receive 200 OK
        _rbac.GetAuthorizationStatusCode(hmContext, endpoint).Should().Be(
            200, $"HigherManagement must be authorized (HTTP 200) for {endpoint}");
        _rbac.IsAuthorized(hmContext, endpoint).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/management-public")]
    [InlineData("/api/management_stats")]
    [InlineData("/api/projects")]
    [InlineData("/api/employees")]
    public void Rbac_NonManagementEndpointsWithSimilarPrefix_DoesNotFalselyBlockEmployees(string endpoint)
    {
        var empContext = new SecurityContext(true, AppRole.Employee);

        // These do not match "/api/management" or "/api/management/*"
        _rbac.GetAuthorizationStatusCode(empContext, endpoint).Should().Be(200);
        _rbac.IsAuthorized(empContext, endpoint).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/planning/cutoff-config")]
    [InlineData("/api/reviews")]
    [InlineData("/api/projects")]
    public void Rbac_UnauthenticatedRequests_AlwaysReturn401Unauthorized_RegardlessOfRole(string endpoint)
    {
        var unauthNullRole = new SecurityContext(false, null);
        var unauthEmp = new SecurityContext(false, AppRole.Employee);
        var unauthTl = new SecurityContext(false, AppRole.TeamLead);
        var unauthHm = new SecurityContext(false, AppRole.HigherManagement);

        _rbac.GetAuthorizationStatusCode(unauthNullRole, endpoint).Should().Be(401);
        _rbac.IsAuthorized(unauthNullRole, endpoint).Should().BeFalse();

        _rbac.GetAuthorizationStatusCode(unauthEmp, endpoint).Should().Be(401);
        _rbac.IsAuthorized(unauthEmp, endpoint).Should().BeFalse();

        _rbac.GetAuthorizationStatusCode(unauthTl, endpoint).Should().Be(401);
        _rbac.IsAuthorized(unauthTl, endpoint).Should().BeFalse();

        _rbac.GetAuthorizationStatusCode(unauthHm, endpoint).Should().Be(401);
        _rbac.IsAuthorized(unauthHm, endpoint).Should().BeFalse();
    }

    [Fact]
    public void Rbac_AuthenticatedWithNullRole_BlocksRestrictedEndpoints_AllowsPublicDirectory()
    {
        var context = new SecurityContext(true, null);

        // Management portal: blocked
        _rbac.GetAuthorizationStatusCode(context, "/api/management/compensation").Should().Be(403);
        _rbac.CanAccessManagementPortal(context).Should().BeFalse();

        // Cutoff config: blocked
        _rbac.GetAuthorizationStatusCode(context, "/api/planning/cutoff-config").Should().Be(403);
        _rbac.CanManageCutoffConfig(context).Should().BeFalse();

        // Review queue: blocked
        _rbac.GetAuthorizationStatusCode(context, "/api/reviews").Should().Be(403);
        _rbac.CanAccessReviewQueue(context).Should().BeFalse();

        // Public directory / general authenticated endpoints: allowed
        _rbac.CanAccessPublicDirectory(context).Should().BeTrue();
        _rbac.GetAuthorizationStatusCode(context, "/api/projects").Should().Be(200);
    }

    [Fact]
    public void Rbac_RoleMatrix_SpecializedEndpoints_EvaluatesCorrectly()
    {
        var empContext = new SecurityContext(true, AppRole.Employee);
        var tlContext = new SecurityContext(true, AppRole.TeamLead);
        var hmContext = new SecurityContext(true, AppRole.HigherManagement);

        // Cutoff config (/api/planning/cutoff-config)
        _rbac.GetAuthorizationStatusCode(empContext, "/api/planning/cutoff-config").Should().Be(403);
        _rbac.CanManageCutoffConfig(empContext).Should().BeFalse();

        _rbac.GetAuthorizationStatusCode(tlContext, "/api/planning/cutoff-config").Should().Be(200);
        _rbac.CanManageCutoffConfig(tlContext).Should().BeTrue();

        _rbac.GetAuthorizationStatusCode(hmContext, "/api/planning/cutoff-config").Should().Be(200);
        _rbac.CanManageCutoffConfig(hmContext).Should().BeTrue();

        // Review Queue (/api/reviews)
        _rbac.GetAuthorizationStatusCode(empContext, "/api/reviews").Should().Be(403);
        _rbac.CanAccessReviewQueue(empContext).Should().BeFalse();

        _rbac.GetAuthorizationStatusCode(tlContext, "/api/reviews").Should().Be(200);
        _rbac.CanAccessReviewQueue(tlContext).Should().BeTrue();

        _rbac.GetAuthorizationStatusCode(hmContext, "/api/reviews").Should().Be(200);
        _rbac.CanAccessReviewQueue(hmContext).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rbac_MalformedOrEmptyEndpoint_ThrowsArgumentException(string? malformedEndpoint)
    {
        var context = new SecurityContext(true, AppRole.HigherManagement);

        var act = () => _rbac.GetAuthorizationStatusCode(context, malformedEndpoint!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rbac_NullSecurityContext_ThrowsArgumentNullException()
    {
        var actStatus = () => _rbac.GetAuthorizationStatusCode(null!, "/api/projects");
        actStatus.Should().Throw<ArgumentNullException>();

        var actPortal = () => _rbac.CanAccessManagementPortal(null!);
        actPortal.Should().Throw<ArgumentNullException>();

        var actCutoff = () => _rbac.CanManageCutoffConfig(null!);
        actCutoff.Should().Throw<ArgumentNullException>();

        var actQueue = () => _rbac.CanAccessReviewQueue(null!);
        actQueue.Should().Throw<ArgumentNullException>();

        var actDir = () => _rbac.CanAccessPublicDirectory(null!);
        actDir.Should().Throw<ArgumentNullException>();
    }
}
