using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Email;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests;

public class SlackTeamsWebhookTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public SlackTeamsWebhookTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void PayloadBuilders_FormatSlackBlockKitAndTeamsMessageCard()
    {
        var slackJson = SlackTeamsWebhookNotificationService.BuildSlackBlockKitPayload(
            "Daily Plan Cutoff Reminder",
            "Alex Rivera has not submitted a daily plan.");

        using var slackDoc = JsonDocument.Parse(slackJson);
        slackDoc.RootElement.GetProperty("blocks").GetArrayLength().Should().Be(2);

        var teamsJson = SlackTeamsWebhookNotificationService.BuildTeamsMessageCardPayload(
            "Review Approved",
            "CRM-101 was approved.",
            "good");

        using var teamsDoc = JsonDocument.Parse(teamsJson);
        teamsDoc.RootElement.GetProperty("@type").GetString().Should().Be("MessageCard");
        teamsDoc.RootElement.GetProperty("themeColor").GetString().Should().Be("107C10");
    }

    [Fact]
    public async Task LiveDispatch_PostsToSlackAndTeamsUrls()
    {
        var postedUrls = new List<string>();
        var handler = new StubHttpMessageHandler(req =>
        {
            postedUrls.Add(req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var services = new ServiceCollection();
        var dbName = $"WebhookTestDb_{Guid.NewGuid()}";
        services.AddDbContext<CrmDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            db.ChatWebhookConfigs.Add(new ChatWebhookConfig
            {
                ProjectId = null,
                SlackWebhookUrl = "https://hooks.slack.com/services/T000/B000/XXX",
                TeamsWebhookUrl = "https://outlook.office.com/webhook/test",
                IsCutoffAlertsEnabled = true,
                IsReviewAlertsEnabled = true,
                IsSimulatedMode = false
            });
            await db.SaveChangesAsync();
        }

        var svc = new SlackTeamsWebhookNotificationService(
            new HttpClient(handler),
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SlackTeamsWebhookNotificationService>.Instance);

        var result = await svc.SendTestAlertAsync();
        result.IsSimulated.Should().BeFalse();
        result.SlackDelivered.Should().BeTrue();
        result.TeamsDelivered.Should().BeTrue();
        postedUrls.Should().Contain("https://hooks.slack.com/services/T000/B000/XXX");
        postedUrls.Should().Contain("https://outlook.office.com/webhook/test");
    }

    [Fact]
    public async Task NotificationsController_EnforcesRbac_AndSavesAndTestsConfig()
    {
        // Employee should get 403 Forbidden
        using var empClient = _fixture.CreateClient();
        var empLogin = await empClient.PostAsJsonAsync("/api/auth/login", new LoginRequest("alex.rivera@companycrm.local", null));
        empLogin.EnsureSuccessStatusCode();
        var empAuth = await empLogin.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        empClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", empAuth!.Token);

        var forbiddenResp = await empClient.GetAsync("/api/notifications/webhooks");
        forbiddenResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // TeamLead should be able to GET, PUT, and POST test
        using var leadClient = _fixture.CreateClient();
        var leadLogin = await leadClient.PostAsJsonAsync("/api/auth/login", new LoginRequest("sarah.jenkins@companycrm.local", null));
        leadLogin.EnsureSuccessStatusCode();
        var leadAuth = await leadLogin.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        leadClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", leadAuth!.Token);

        var updateDto = new UpdateChatWebhookConfigDto(
            ProjectId: null,
            SlackWebhookUrl: "https://hooks.slack.com/services/T123/B456/ZZZ",
            TeamsWebhookUrl: null,
            IsCutoffAlertsEnabled: true,
            IsReviewAlertsEnabled: true,
            IsSimulatedMode: true);

        var putResp = await leadClient.PutAsJsonAsync("/api/notifications/webhooks", updateDto);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var testResp = await leadClient.PostAsync("/api/notifications/webhooks/test", null);
        testResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var testResult = await testResp.Content.ReadFromJsonAsync<ChatWebhookTestResultDto>(JsonOptions);
        testResult.Should().NotBeNull();
        testResult!.IsSimulated.Should().BeTrue();
        testResult.SlackDelivered.Should().BeTrue();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
