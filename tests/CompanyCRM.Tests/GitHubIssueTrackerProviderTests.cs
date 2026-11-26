using System.Net;
using System.Text;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Infrastructure.ExternalSync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests;

public class GitHubIssueTrackerProviderTests
{
    private static ProjectExternalConfig CreateSimulatedConfig() =>
        new(
            ProjectId: Guid.NewGuid(),
            SourceType: ExternalSourceType.GitHub,
            OrgUrl: "https://api.github.com",
            ProjectName: "companycrm/platform",
            Pat: null,
            IsSyncEnabled: true,
            IsSimulatedMode: true,
            LastSyncRunAt: null);

    [Fact]
    public async Task SimulatedPush_CreateAndUpdate_WorksDeterministically()
    {
        var simulator = new GitHubSimulator();
        simulator.Reset();
        var provider = new GitHubIssueTrackerProvider(new HttpClient(), simulator, NullLogger<GitHubIssueTrackerProvider>.Instance);
        var config = CreateSimulatedConfig();

        var workItem = new WorkItem
        {
            Id = Guid.NewGuid(),
            Code = "CRM-501",
            Title = "Implement GitHub connector",
            Description = "Details",
            Status = WorkItemStatus.InProgress,
            Type = WorkItemType.Story
        };

        var created = await provider.PushWorkItemAsync(config, workItem);
        created.ExternalId.Should().StartWith("GH-");
        created.ExternalUrl.Should().Contain("https://github.com/companycrm/platform/issues/");
        created.Status.Should().Be("open");

        workItem.ExternalId = created.ExternalId;
        workItem.Status = WorkItemStatus.Done;
        var updated = await provider.PushWorkItemAsync(config, workItem);
        updated.ExternalId.Should().Be(created.ExternalId);
        updated.Status.Should().Be("closed");
    }

    [Fact]
    public async Task SimulatedFetch_ReturnsSeededIssuesAndPullRequests()
    {
        var simulator = new GitHubSimulator();
        simulator.Reset();
        var provider = new GitHubIssueTrackerProvider(new HttpClient(), simulator, NullLogger<GitHubIssueTrackerProvider>.Instance);

        var items = await provider.FetchRemoteWorkItemsAsync(CreateSimulatedConfig());
        items.Should().Contain(i => i.ExternalId == "GH-101");
        items.Should().Contain(i => i.ExternalId == "GH-PR-103" && i.Status == "merged");
    }

    [Fact]
    public async Task ParseWebhookPayload_IssueAndPullRequest_MapsCorrectly()
    {
        var provider = new GitHubIssueTrackerProvider(new HttpClient(), new GitHubSimulator(), NullLogger<GitHubIssueTrackerProvider>.Instance);

        var issueJson = """
        {
          "action": "opened",
          "issue": {
            "number": 42,
            "title": "Fix login redirect",
            "body": "Users get 401 on refresh",
            "state": "open",
            "html_url": "https://github.com/companycrm/platform/issues/42",
            "labels": [{ "name": "bug" }],
            "assignee": { "login": "alex-rivera" },
            "updated_at": "2026-11-26T12:00:00Z"
          }
        }
        """;

        var issueDto = await provider.ParseWebhookPayloadAsync(
            issueJson,
            new Dictionary<string, string> { ["X-GitHub-Event"] = "issues" });

        issueDto.Should().NotBeNull();
        issueDto!.ExternalId.Should().Be("GH-42");
        issueDto.Type.Should().Be("Bug");
        issueDto.Status.Should().Be("open");

        var prJson = """
        {
          "action": "closed",
          "pull_request": {
            "number": 88,
            "title": "feat: add GitHub sync",
            "body": "Implements GH sync",
            "state": "closed",
            "merged": true,
            "html_url": "https://github.com/companycrm/platform/pull/88",
            "labels": [],
            "updated_at": "2026-11-26T13:00:00Z"
          }
        }
        """;

        var prDto = await provider.ParseWebhookPayloadAsync(
            prJson,
            new Dictionary<string, string> { ["X-GitHub-Event"] = "pull_request" });

        prDto.Should().NotBeNull();
        prDto!.ExternalId.Should().Be("GH-PR-88");
        prDto.Status.Should().Be("merged");
        prDto.Type.Should().Be("Story");
    }

    [Fact]
    public async Task LivePush_CallsGitHubRestApi_WithBearerToken()
    {
        var capturedUri = string.Empty;
        var handler = new StubHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""
                {
                  "number": 77,
                  "title": "Live issue",
                  "body": "Body",
                  "state": "open",
                  "html_url": "https://github.com/octo/repo/issues/77",
                  "labels": [{ "name": "task" }],
                  "updated_at": "2026-11-26T14:00:00Z"
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        var provider = new GitHubIssueTrackerProvider(
            new HttpClient(handler),
            new GitHubSimulator(),
            NullLogger<GitHubIssueTrackerProvider>.Instance);

        var liveConfig = new ProjectExternalConfig(
            Guid.NewGuid(),
            ExternalSourceType.GitHub,
            "https://api.github.com",
            "octo/repo",
            "ghp_test_token",
            true,
            false,
            null);

        var item = new WorkItem
        {
            Id = Guid.NewGuid(),
            Code = "CRM-77",
            Title = "Live issue",
            Status = WorkItemStatus.Todo,
            Type = WorkItemType.Task
        };

        var result = await provider.PushWorkItemAsync(liveConfig, item);
        capturedUri.Should().Be("https://api.github.com/repos/octo/repo/issues");
        result.ExternalId.Should().Be("GH-77");
        result.ExternalUrl.Should().Be("https://github.com/octo/repo/issues/77");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
