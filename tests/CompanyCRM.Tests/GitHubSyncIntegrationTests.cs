using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CompanyCRM.Tests;

public class GitHubSyncIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public GitHubSyncIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GitHubWebhook_WhenProjectConfiguredForGitHub_ImportsAndDeduplicatesEcho()
    {
        using var client = _fixture.CreateClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("sarah.jenkins@companycrm.local", null));
        loginResp.EnsureSuccessStatusCode();
        var auth = await loginResp.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        var projects = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects", JsonOptions);
        projects.Should().NotBeNullOrEmpty();
        var projectId = projects![0].Id;

        var updateConfig = new UpdateProjectExternalConfigDto(
            ExternalSource: ExternalSourceType.GitHub,
            ExternalOrgUrl: "https://api.github.com",
            ExternalProjectName: "companycrm/platform",
            ExternalPat: null,
            IsSyncEnabled: true,
            IsSimulatedMode: true);

        var configResp = await client.PutAsJsonAsync($"/api/projects/{projectId}/external-config", updateConfig);
        configResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var webhookPayload = """
        {
          "action": "opened",
          "issue": {
            "number": 901,
            "title": "Unique GitHub integration test issue 901",
            "body": "Testing inbound GitHub webhook",
            "state": "open",
            "html_url": "https://github.com/companycrm/platform/issues/901",
            "labels": [{ "name": "bug" }],
            "updated_at": "2026-11-26T15:00:00Z"
          }
        }
        """;

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("X-GitHub-Event", "issues");

        var resp1 = await client.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await resp1.Content.ReadFromJsonAsync<WebhookProcessingResultDto>();
        result1.Should().NotBeNull();
        result1!.Status.Should().Be("Applied");
        result1.ExternalId.Should().Be("GH-901");

        // Second identical webhook should be deduplicated as an echo
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("X-GitHub-Event", "issues");

        var resp2 = await client.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result2 = await resp2.Content.ReadFromJsonAsync<WebhookProcessingResultDto>();
        result2.Should().NotBeNull();
        result2!.IsEcho.Should().BeTrue();
    }
}
