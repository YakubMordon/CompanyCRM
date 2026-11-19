namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;

/// <summary>
/// Empirical Challenger 2 Stress Test Suite:
/// Verifies Domain State Machine Invariants, WorkItem Review Gating,
/// Planning Normalization, and Time Tracking Edge Cases.
/// </summary>
public class ChallengerDomainStateInvariantsTests
{
    // =========================================================================
    // 1. WorkItem Review Gating & Kanban Transition Invariants
    // =========================================================================

    [Theory]
    [InlineData(ReviewStatus.Pending)]
    [InlineData(ReviewStatus.ClarificationNeeded)]
    [InlineData(ReviewStatus.Denied)]
    public void CanTransitionKanban_WhenReviewStatusBlocks_ReturnsFalse_ForAllWorkItemStatuses(ReviewStatus blockedReviewStatus)
    {
        var statuses = Enum.GetValues<WorkItemStatus>();

        foreach (var status in statuses)
        {
            var workItem = new WorkItem
            {
                Status = status,
                ReviewStatus = blockedReviewStatus
            };

            var canTransition = workItem.CanTransitionKanban();
            canTransition.Should().BeFalse(
                $"Kanban transition MUST be blocked when ReviewStatus is {blockedReviewStatus}, regardless of WorkItemStatus {status}");
        }
    }

    [Theory]
    [InlineData(WorkItemStatus.PendingReview)]
    [InlineData(WorkItemStatus.ClarificationRequested)]
    [InlineData(WorkItemStatus.Denied)]
    public void CanTransitionKanban_WhenWorkItemStatusBlocks_ReturnsFalse_ForAllReviewStatuses(WorkItemStatus blockedStatus)
    {
        var reviewStatuses = Enum.GetValues<ReviewStatus>();

        foreach (var reviewStatus in reviewStatuses)
        {
            var workItem = new WorkItem
            {
                Status = blockedStatus,
                ReviewStatus = reviewStatus
            };

            var canTransition = workItem.CanTransitionKanban();
            canTransition.Should().BeFalse(
                $"Kanban transition MUST be blocked when WorkItemStatus is {blockedStatus}, regardless of ReviewStatus {reviewStatus}");
        }
    }

    [Theory]
    [InlineData(WorkItemStatus.Backlog)]
    [InlineData(WorkItemStatus.Todo)]
    [InlineData(WorkItemStatus.InProgress)]
    [InlineData(WorkItemStatus.InVerify)]
    [InlineData(WorkItemStatus.Done)]
    public void CanTransitionKanban_WhenApprovedOrNotRequired_ReturnsTrue_ForStandardStatuses(WorkItemStatus standardStatus)
    {
        var itemApproved = new WorkItem
        {
            Status = standardStatus,
            ReviewStatus = ReviewStatus.Approved
        };
        var itemNotRequired = new WorkItem
        {
            Status = standardStatus,
            ReviewStatus = ReviewStatus.NotRequired
        };

        itemApproved.CanTransitionKanban().Should().BeTrue(
            $"Standard status {standardStatus} with ReviewStatus.Approved should be movable on Kanban board");
        itemNotRequired.CanTransitionKanban().Should().BeTrue(
            $"Standard status {standardStatus} with ReviewStatus.NotRequired should be movable on Kanban board");
    }

    [Theory]
    [InlineData(ReviewStatus.Pending, false)]
    [InlineData(ReviewStatus.ClarificationNeeded, false)]
    [InlineData(ReviewStatus.Denied, false)]
    [InlineData(ReviewStatus.Approved, true)]
    [InlineData(ReviewStatus.NotRequired, true)]
    public void CanPushToExternal_ReviewStatusGating_EnforcesOutboundSyncRequirements(ReviewStatus reviewStatus, bool expectedCanPush)
    {
        var workItem = new WorkItem
        {
            Status = WorkItemStatus.Todo,
            ReviewStatus = reviewStatus
        };

        workItem.CanPushToExternal().Should().Be(expectedCanPush,
            $"Outbound sync CanPushToExternal should be {expectedCanPush} for ReviewStatus {reviewStatus}");
    }

    [Theory]
    [InlineData(WorkItemStatus.PendingReview, false)]
    [InlineData(WorkItemStatus.Denied, false)]
    [InlineData(WorkItemStatus.Todo, true)]
    [InlineData(WorkItemStatus.Backlog, true)]
    [InlineData(WorkItemStatus.InProgress, true)]
    [InlineData(WorkItemStatus.Done, true)]
    public void CanPushToExternal_WorkItemStatusGating_EnforcesOutboundSyncRequirements(WorkItemStatus status, bool expectedCanPush)
    {
        var workItem = new WorkItem
        {
            Status = status,
            ReviewStatus = ReviewStatus.Approved
        };

        workItem.CanPushToExternal().Should().Be(expectedCanPush,
            $"Outbound sync CanPushToExternal should be {expectedCanPush} for WorkItemStatus {status}");
    }

    // =========================================================================
    // 2. Review Notes Validation & Review State Mutations
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("12")]
    [InlineData("xyz")]
    [InlineData("1234")]
    [InlineData("  ab  ")] // trimmed length < 5
    [InlineData(" 123 ")] // trimmed length < 5
    [InlineData("\t\r\n ")]
    public void RequestClarification_InvalidCommentUnder5Chars_ThrowsArgumentException(string? invalidComment)
    {
        var workItem = new WorkItem();
        var reviewerId = Guid.NewGuid();

        var act = () => workItem.RequestClarification(reviewerId, invalidComment!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Clarification note must contain at least 5 characters.*");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("Valid clarification request notes")]
    [InlineData("   Needs more unit tests.   ")]
    [InlineData("日本語でコメントです")] // Unicode multi-byte characters
    public void RequestClarification_ValidComment_UpdatesReviewStateCorrectly(string validComment)
    {
        var workItem = new WorkItem
        {
            Status = WorkItemStatus.PendingReview,
            ReviewStatus = ReviewStatus.Pending
        };
        var reviewerId = Guid.NewGuid();
        var beforeTime = DateTimeOffset.UtcNow.AddSeconds(-1);

        workItem.RequestClarification(reviewerId, validComment);

        workItem.ReviewerId.Should().Be(reviewerId);
        workItem.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
        workItem.Status.Should().Be(WorkItemStatus.ClarificationRequested);
        workItem.ReviewLatestComment.Should().Be(validComment.Trim());
        workItem.ReviewedAt.Should().BeOnOrAfter(beforeTime);
        workItem.UpdatedAt.Should().BeOnOrAfter(beforeTime);
        workItem.CanTransitionKanban().Should().BeFalse();
        workItem.CanPushToExternal().Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("x")]
    [InlineData("12")]
    [InlineData("abc")]
    [InlineData("nope")]
    [InlineData("  no  ")]
    public void DenyReview_InvalidReasonUnder5Chars_ThrowsArgumentException(string? invalidReason)
    {
        var workItem = new WorkItem();
        var reviewerId = Guid.NewGuid();

        var act = () => workItem.DenyReview(reviewerId, invalidReason!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Denial reason must contain at least 5 characters.*");
    }

    [Theory]
    [InlineData("Denied: Requirements are missing")]
    [InlineData("12345")]
    [InlineData("   Not aligned with architecture design   ")]
    public void DenyReview_ValidReason_SetsDeniedStateAndBlocksSyncAndKanban(string validReason)
    {
        var workItem = new WorkItem
        {
            Status = WorkItemStatus.PendingReview,
            ReviewStatus = ReviewStatus.Pending
        };
        var reviewerId = Guid.NewGuid();

        workItem.DenyReview(reviewerId, validReason);

        workItem.ReviewerId.Should().Be(reviewerId);
        workItem.ReviewStatus.Should().Be(ReviewStatus.Denied);
        workItem.Status.Should().Be(WorkItemStatus.Denied);
        workItem.ReviewLatestComment.Should().Be(validReason.Trim());
        workItem.ReviewedAt.Should().NotBeNull();
        workItem.CanTransitionKanban().Should().BeFalse();
        workItem.CanPushToExternal().Should().BeFalse();
    }

    [Fact]
    public void ApproveReview_SetsApprovedStateAndAllowsKanbanAndSync()
    {
        var workItem = new WorkItem
        {
            Status = WorkItemStatus.PendingReview,
            ReviewStatus = ReviewStatus.Pending
        };
        var reviewerId = Guid.NewGuid();

        workItem.ApproveReview(reviewerId, startingStatus: WorkItemStatus.Todo);

        workItem.ReviewerId.Should().Be(reviewerId);
        workItem.ReviewStatus.Should().Be(ReviewStatus.Approved);
        workItem.Status.Should().Be(WorkItemStatus.Todo);
        workItem.ReviewedAt.Should().NotBeNull();
        workItem.CanTransitionKanban().Should().BeTrue();
        workItem.CanPushToExternal().Should().BeTrue();
    }

    // =========================================================================
    // 3. WeeklyPlan.NormalizeToMonday Boundary & Invariant Stress Tests
    // =========================================================================

    [Theory]
    // Standard 7 days of a typical week (May 2024: Mon May 6 to Sun May 12)
    [InlineData(2024, 5, 6, 2024, 5, 6)]   // Monday -> same Monday
    [InlineData(2024, 5, 7, 2024, 5, 6)]   // Tuesday -> Monday May 6
    [InlineData(2024, 5, 8, 2024, 5, 6)]   // Wednesday -> Monday May 6
    [InlineData(2024, 5, 9, 2024, 5, 6)]   // Thursday -> Monday May 6
    [InlineData(2024, 5, 10, 2024, 5, 6)]  // Friday -> Monday May 6
    [InlineData(2024, 5, 11, 2024, 5, 6)]  // Saturday -> Monday May 6
    [InlineData(2024, 5, 12, 2024, 5, 6)]  // Sunday -> Monday May 6
    // Month boundaries
    [InlineData(2024, 6, 1, 2024, 5, 27)]  // Saturday Jun 1 -> Monday May 27
    [InlineData(2024, 6, 2, 2024, 5, 27)]  // Sunday Jun 2 -> Monday May 27
    [InlineData(2024, 6, 3, 2024, 6, 3)]   // Monday Jun 3 -> Monday Jun 3
    // Year boundaries
    [InlineData(2023, 1, 1, 2022, 12, 26)] // Sunday Jan 1 2023 -> Monday Dec 26 2022
    [InlineData(2024, 1, 1, 2024, 1, 1)]   // Monday Jan 1 2024 -> Monday Jan 1 2024
    [InlineData(2025, 1, 1, 2024, 12, 30)] // Wednesday Jan 1 2025 -> Monday Dec 30 2024
    // Leap year leap day: 2024 is a leap year (Feb 29 was Thursday)
    [InlineData(2024, 2, 29, 2024, 2, 26)] // Thursday Feb 29 2024 -> Monday Feb 26 2024
    [InlineData(2024, 3, 1, 2024, 2, 26)]  // Friday Mar 1 2024 -> Monday Feb 26 2024
    [InlineData(2024, 3, 3, 2024, 2, 26)]  // Sunday Mar 3 2024 -> Monday Feb 26 2024
    // Century leap year (2000-02-29 was Tuesday)
    [InlineData(2000, 2, 29, 2000, 2, 28)] // Tuesday Feb 29 2000 -> Monday Feb 28 2000
    public void NormalizeToMonday_KnownDates_NormalizesToCorrectMonday(
        int inYear, int inMonth, int inDay,
        int expYear, int expMonth, int expDay)
    {
        var inputDate = new DateOnly(inYear, inMonth, inDay);
        var expectedMonday = new DateOnly(expYear, expMonth, expDay);

        var actualMonday = WeeklyPlan.NormalizeToMonday(inputDate);

        actualMonday.Should().Be(expectedMonday);
        actualMonday.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void NormalizeToMonday_Full5YearCycleStressHarness_AlwaysSatisfiesInvariants()
    {
        // Stress test across 1,826 consecutive days (2020 through 2024, including leap years)
        var startDate = new DateOnly(2020, 1, 1);
        var endDate = new DateOnly(2024, 12, 31);

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var monday = WeeklyPlan.NormalizeToMonday(date);

            // Invariant 1: Resulting date MUST always be Monday
            monday.DayOfWeek.Should().Be(DayOfWeek.Monday, $"Normalized date for {date} must be a Monday");

            // Invariant 2: Resulting Monday MUST be on or before the input date
            (monday <= date).Should().BeTrue($"Normalized Monday {monday} must be <= input date {date}");

            // Invariant 3: Difference between input date and normalized Monday must be 0..6 days
            var dayDifference = date.DayNumber - monday.DayNumber;
            dayDifference.Should().BeInRange(0, 6, $"Difference between {date} and {monday} must be between 0 and 6 days");

            // Invariant 4: Normalizing an already normalized Monday must be idempotent
            var idempotencyCheck = WeeklyPlan.NormalizeToMonday(monday);
            idempotencyCheck.Should().Be(monday, $"Normalizing {monday} again must return the exact same Monday");
        }
    }

    // =========================================================================
    // 4. TimeEntry.StopTimer Invariant & Mathematical Stress Tests
    // =========================================================================

    [Theory]
    [InlineData(0, 1)]        // 0 minutes elapsed -> Math.Max(1, 0) == 1
    [InlineData(0.2, 1)]      // 12 seconds elapsed -> 0.2 min rounds to 0 -> Math.Max(1, 0) == 1
    [InlineData(0.49, 1)]     // 29 seconds elapsed -> 0.49 min rounds to 0 -> Math.Max(1, 0) == 1
    [InlineData(0.5, 1)]      // 30 seconds -> 0.5 min rounds to 0 or 1 -> Math.Max(1, round) == 1
    [InlineData(1.0, 1)]      // 1 minute elapsed -> 1
    [InlineData(1.4, 1)]      // 1.4 min -> 1
    [InlineData(1.5, 2)]      // 1.5 min rounds to 2
    [InlineData(45.2, 45)]    // 45.2 min -> 45
    [InlineData(120.7, 121)]  // 120.7 min -> 121
    [InlineData(480.0, 480)]  // 8 hours (480 min) -> 480
    public void StopTimer_PositiveDurations_RoundsMinutesWithOneMinuteFloor(double elapsedMinutes, int expectedDurationMinutes)
    {
        var startTime = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var stopTime = startTime.AddMinutes(elapsedMinutes);

        var timeEntry = new TimeEntry
        {
            StartedAt = startTime,
            IsRunning = true
        };

        timeEntry.StopTimer(stopTime, "Task progress logged");

        timeEntry.IsRunning.Should().BeFalse();
        timeEntry.EndedAt.Should().Be(stopTime);
        timeEntry.DurationMinutes.Should().Be(expectedDurationMinutes);
        timeEntry.Notes.Should().Be("Task progress logged");
    }

    [Theory]
    [InlineData(-5.0)]   // Stopped 5 minutes before StartedAt
    [InlineData(-60.0)]  // Stopped 1 hour before StartedAt
    [InlineData(-1000.0)] // Extreme negative clock skew
    public void StopTimer_NegativeElapsedClockSkew_EnforcesOneMinuteFloorWithoutCrashing(double negativeMinutes)
    {
        var startTime = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        var stopTime = startTime.AddMinutes(negativeMinutes);

        var timeEntry = new TimeEntry
        {
            StartedAt = startTime,
            IsRunning = true
        };

        timeEntry.StopTimer(stopTime);

        timeEntry.IsRunning.Should().BeFalse();
        timeEntry.EndedAt.Should().Be(stopTime);
        // Math.Max(1, (int)Math.Round(negative)) ensures minimum 1 minute
        timeEntry.DurationMinutes.Should().Be(1,
            "Even under severe negative clock skew, duration must fall back to the 1-minute floor");
    }

    [Fact]
    public void StopTimer_CalledMultipleTimes_UpdatesDurationAndPreservesExistingNotesWhenNull()
    {
        var startTime = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var timeEntry = new TimeEntry
        {
            StartedAt = startTime,
            IsRunning = true
        };

        // First stop at 30 minutes with notes
        var firstStop = startTime.AddMinutes(30);
        timeEntry.StopTimer(firstStop, "Initial notes");

        timeEntry.IsRunning.Should().BeFalse();
        timeEntry.DurationMinutes.Should().Be(30);
        timeEntry.Notes.Should().Be("Initial notes");

        // Second stop at 45 minutes with null notes (should not erase existing notes)
        var secondStop = startTime.AddMinutes(45);
        timeEntry.StopTimer(secondStop, null);

        timeEntry.EndedAt.Should().Be(secondStop);
        timeEntry.DurationMinutes.Should().Be(45);
        timeEntry.Notes.Should().Be("Initial notes", "Passing null notes on subsequent StopTimer must NOT overwrite existing notes");

        // Third stop at 60 minutes with whitespace notes (should also preserve existing notes)
        var thirdStop = startTime.AddMinutes(60);
        timeEntry.StopTimer(thirdStop, "   ");

        timeEntry.EndedAt.Should().Be(thirdStop);
        timeEntry.DurationMinutes.Should().Be(60);
        timeEntry.Notes.Should().Be("Initial notes");

        // Fourth stop with new notes
        var fourthStop = startTime.AddMinutes(90);
        timeEntry.StopTimer(fourthStop, "Updated final notes");

        timeEntry.DurationMinutes.Should().Be(90);
        timeEntry.Notes.Should().Be("Updated final notes");
    }
}
