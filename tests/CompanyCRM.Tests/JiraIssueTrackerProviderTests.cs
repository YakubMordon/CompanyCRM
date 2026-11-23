using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Infrastructure.ExternalSync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CompanyCRM.Tests;

public class JiraIssueTrackerProviderTests
{
    private readonly JiraSimulator _simulator;
    private readonly JiraIssueTrackerProvider _provider;

    public JiraIssueTrackerProviderTests()
    {
        _simulator = new JiraSimulator();
        var httpClient = new HttpClient();
        _provider = new JiraIssueTrackerProvider(
            httpClient,
            _simulator,
            NullLogger<JiraIssueTrackerProvider>.Instance);
    }

    [Fact]
    public void SourceType_ShouldBeJira()
    {
        _provider.SourceType.Should().Be(ExternalSourceType.Jira);
    }

    [Theory]
    [InlineData(WorkItemType.Task, "Task")]
    [InlineData(WorkItemType.Bug, "Bug")]
    [InlineData(WorkItemType.Story, "Story")]
    public void MapTypeToJira_ShouldMapCorrectly(WorkItemType type, string expected)
    {
        JiraIssueTrackerProvider.MapTypeToJira(type).Should().Be(expected);
    }

    [Theory]
    [InlineData("task", WorkItemType.Task)]
    [InlineData("bug", WorkItemType.Bug)]
    [InlineData("story", WorkItemType.Story)]
    [InlineData("epic", WorkItemType.Task)]
    public void MapTypeFromJira_ShouldMapCorrectly(string jiraType, WorkItemType expected)
    {
        JiraIssueTrackerProvider.MapTypeFromJira(jiraType).Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkItemStatus.Todo, "To Do")]
    [InlineData(WorkItemStatus.Backlog, "To Do")]
    [InlineData(WorkItemStatus.InProgress, "In Progress")]
    [InlineData(WorkItemStatus.InVerify, "In Review")]
    [InlineData(WorkItemStatus.Done, "Done")]
    public void MapStatusToJira_ShouldMapCorrectly(WorkItemStatus status, string expected)
    {
        JiraIssueTrackerProvider.MapStatusToJira(status).Should().Be(expected);
    }

    [Theory]
    [InlineData("To Do", WorkItemStatus.Todo)]
    [InlineData("In Progress", WorkItemStatus.InProgress)]
    [InlineData("In Review", WorkItemStatus.InVerify)]
    [InlineData("Done", WorkItemStatus.Done)]
    [InlineData("Closed", WorkItemStatus.Done)]
    public void MapStatusFromJira_ShouldMapCorrectly(string jiraStatus, WorkItemStatus expected)
    {
        JiraIssueTrackerProvider.MapStatusFromJira(jiraStatus).Should().Be(expected);
    }

    [Fact]
    public async Task PushWorkItemAsync_SimulatedMode_ShouldReturnSimulatedJiraDto()
    {
        var config = new ProjectExternalConfig(
            ProjectId: Guid.NewGuid(),
            SourceType: ExternalSourceType.Jira,
            OrgUrl: "https://mycorp.atlassian.net",
            ProjectName: "CRM",
            Pat: null,
            IsSyncEnabled: true,
            IsSimulatedMode: true);

        var item = new WorkItem
        {
            Id = Guid.NewGuid(),
            Code = "CRM-55",
            Title = "Implement OAuth 2.0 PKCE",
            Description = "Support PKCE flow for desktop app",
            Status = WorkItemStatus.InProgress,
            Type = WorkItemType.Story,
            Priority = WorkItemPriority.High,
            ReviewStatus = ReviewStatus.Approved
        };

        var result = await _provider.PushWorkItemAsync(config, item);

        result.Should().NotBeNull();
        result.ExternalId.Should().StartWith("CRM-");
        result.ExternalUrl.Should().Contain("mycorp.atlassian.net/browse/");
        result.Title.Should().Be("Implement OAuth 2.0 PKCE");
        result.Status.Should().Be("In Progress");
        result.Type.Should().Be("Story");
    }

    [Fact]
    public async Task ParseWebhookPayloadAsync_ValidJiraPayload_ShouldExtractWorkItem()
    {
        var itemDto = new ExternalWorkItemDto(
            ExternalId: "CRM-105",
            ExternalUrl: "https://mycorp.atlassian.net/browse/CRM-105",
            Title: "Fix race condition in timer drawer",
            Description: "When rapidly clicking pause/start, timer interval overlaps.",
            Status: "Done",
            Type: "Bug",
            AssigneeEmail: "alex.rivera@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow);

        var payloadJson = _simulator.GenerateWebhookPayload(itemDto, "jira:issue_updated");
        var headers = new Dictionary<string, string>();

        var parsed = await _provider.ParseWebhookPayloadAsync(payloadJson, headers);

        parsed.Should().NotBeNull();
        parsed!.ExternalId.Should().Be("CRM-105");
        parsed.Title.Should().Be("Fix race condition in timer drawer");
        parsed.Description.Should().Be("When rapidly clicking pause/start, timer interval overlaps.");
        parsed.Status.Should().Be("Done");
        parsed.Type.Should().Be("Bug");
        parsed.AssigneeEmail.Should().Be("alex.rivera@companycrm.local");
    }

    [Fact]
    public async Task ParseWebhookPayloadAsync_InvalidOrEmptyPayload_ShouldReturnNull()
    {
        var headers = new Dictionary<string, string>();
        var resultEmpty = await _provider.ParseWebhookPayloadAsync("", headers);
        var resultMalformed = await _provider.ParseWebhookPayloadAsync("{ \"unrelated\": 123 }", headers);

        resultEmpty.Should().BeNull();
        resultMalformed.Should().BeNull();
    }
}
