using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Infrastructure.ExternalSync;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

public class AzureDevOpsProviderAndSimulatorTests
{
    private readonly AzureDevOpsSimulator _simulator;
    private readonly AzureDevOpsIssueTrackerProvider _provider;

    public AzureDevOpsProviderAndSimulatorTests()
    {
        _simulator = new AzureDevOpsSimulator();
        var httpClient = new HttpClient();
        _provider = new AzureDevOpsIssueTrackerProvider(
            httpClient,
            _simulator,
            NullLogger<AzureDevOpsIssueTrackerProvider>.Instance);
    }

    [Fact]
    public async Task Simulator_DefaultSeededItems_ArePresent()
    {
        var config = new ProjectExternalConfig(
            Guid.NewGuid(),
            ExternalSourceType.AzureDevOps,
            "https://dev.azure.com/simulated-org",
            "CompanyProject",
            null,
            true,
            true,
            null);

        var items = await _simulator.FetchRemoteWorkItemsAsync(config);
        items.Should().HaveCountGreaterThanOrEqualTo(3);
        items.Should().Contain(i => i.ExternalId == "ADO-SIM-201");
        items.Should().Contain(i => i.ExternalId == "ADO-SIM-202");
        items.Should().Contain(i => i.ExternalId == "ADO-SIM-203");
    }

    [Fact]
    public async Task Simulator_PushWorkItem_GeneratesDeterministicAdoSimId()
    {
        var config = new ProjectExternalConfig(
            Guid.NewGuid(),
            ExternalSourceType.AzureDevOps,
            "https://dev.azure.com/simulated-org",
            "CompanyProject",
            null,
            true,
            true,
            null);

        var workItem = new WorkItem
        {
            Code = "CRM-550",
            Title = "Implement dark theme palette",
            Description = "Support dark mode across all pages",
            Status = WorkItemStatus.InProgress,
            Type = WorkItemType.Task
        };

        var result = await _simulator.PushWorkItemAsync(config, workItem);
        result.ExternalId.Should().Be("ADO-SIM-CRM550");
        result.ExternalUrl.Should().Contain("ADO-SIM-CRM550");
        result.Title.Should().Be("Implement dark theme palette");
        result.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Simulator_FetchRemoteWorkItems_FiltersByModifiedDate()
    {
        var config = new ProjectExternalConfig(
            Guid.NewGuid(),
            ExternalSourceType.AzureDevOps,
            "https://dev.azure.com/simulated-org",
            "CompanyProject",
            null,
            true,
            true,
            null);

        var recentItems = await _simulator.FetchRemoteWorkItemsAsync(config, DateTimeOffset.UtcNow.AddMinutes(-45));
        recentItems.Should().Contain(i => i.ExternalId == "ADO-SIM-203");
        recentItems.Should().NotContain(i => i.ExternalId == "ADO-SIM-201"); // 2 hours ago
    }

    [Fact]
    public void Simulator_SimulateRemoteModification_UpdatesItemAndTimestamp()
    {
        var updated = _simulator.SimulateRemoteModification(
            "ADO-SIM-201",
            "Updated remote title",
            "Updated remote description",
            "Closed",
            "lead@company.com");

        updated.Title.Should().Be("Updated remote title");
        updated.Status.Should().Be("Closed");
        updated.AssigneeEmail.Should().Be("lead@company.com");
    }

    [Fact]
    public void Simulator_GenerateServiceHookPayload_ProducesValidJsonWithAllFields()
    {
        var item = new ExternalWorkItemDto(
            "ADO-SIM-301",
            "https://dev.azure.com/simulated-org/CompanyProject/_workitems/edit/ADO-SIM-301",
            "Service Hook Test",
            "Test description",
            "Active",
            "Bug",
            "dev@company.com",
            DateTimeOffset.UtcNow);

        var payload = _simulator.GenerateServiceHookPayload(item);
        payload.Should().Contain("ADO-SIM-301");
        payload.Should().Contain("Service Hook Test");
        payload.Should().Contain("Active");
        payload.Should().Contain("dev@company.com");
    }

    [Fact]
    public async Task Provider_PushWorkItem_WhenSimulated_DelegatesToSimulator()
    {
        var config = new ProjectExternalConfig(
            Guid.NewGuid(),
            ExternalSourceType.AzureDevOps,
            "https://dev.azure.com/simulated-org",
            "CompanyProject",
            null,
            true,
            true, // Simulated mode
            null);

        var workItem = new WorkItem
        {
            Code = "CRM-880",
            Title = "Simulated Provider Push",
            Status = WorkItemStatus.Todo,
            Type = WorkItemType.Story
        };

        var result = await _provider.PushWorkItemAsync(config, workItem);
        result.ExternalId.Should().Be("ADO-SIM-CRM880");
        result.Type.Should().Be("User Story");
    }

    [Fact]
    public async Task Provider_ParseWebhookPayload_ValidPayload_ReturnsExternalWorkItemDto()
    {
        var item = new ExternalWorkItemDto(
            "9901",
            "https://dev.azure.com/test-org/TestProject/_workitems/edit/9901",
            "Webhook Parse Test",
            "Description of webhook item",
            "Resolved",
            "Task",
            "tester@company.com",
            DateTimeOffset.UtcNow);

        var rawJson = _simulator.GenerateServiceHookPayload(item);
        var parsed = await _provider.ParseWebhookPayloadAsync(rawJson, new Dictionary<string, string>());

        parsed.Should().NotBeNull();
        parsed!.ExternalId.Should().Be("9901");
        parsed.Title.Should().Be("Webhook Parse Test");
        parsed.Status.Should().Be("Resolved");
        parsed.Type.Should().Be("Task");
        parsed.AssigneeEmail.Should().Be("tester@company.com");
    }

    [Fact]
    public async Task Provider_ParseWebhookPayload_CorruptJson_ReturnsNull()
    {
        var parsed = await _provider.ParseWebhookPayloadAsync("{ this is not valid json }", new Dictionary<string, string>());
        parsed.Should().BeNull();
    }

    [Fact]
    public void Provider_StateAndTypeMappings_CoverAllEnums()
    {
        AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(WorkItemStatus.Draft).Should().Be("New");
        AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(WorkItemStatus.Todo).Should().Be("To Do");
        AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(WorkItemStatus.InProgress).Should().Be("Active");
        AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(WorkItemStatus.InVerify).Should().Be("Resolved");
        AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(WorkItemStatus.Done).Should().Be("Closed");

        AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus("Closed").Should().Be(WorkItemStatus.Done);
        AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus("Resolved").Should().Be(WorkItemStatus.InVerify);
        AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus("Active").Should().Be(WorkItemStatus.InProgress);
        AzureDevOpsIssueTrackerProvider.MapAdoStateToStatus("To Do").Should().Be(WorkItemStatus.Todo);

        AzureDevOpsIssueTrackerProvider.MapTypeToAdo(WorkItemType.Bug).Should().Be("Bug");
        AzureDevOpsIssueTrackerProvider.MapTypeToAdo(WorkItemType.Story).Should().Be("User Story");
        AzureDevOpsIssueTrackerProvider.MapTypeToAdo(WorkItemType.Task).Should().Be("Task");

        AzureDevOpsIssueTrackerProvider.MapPriorityToAdo(WorkItemPriority.Critical).Should().Be(1);
        AzureDevOpsIssueTrackerProvider.MapPriorityToAdo(WorkItemPriority.High).Should().Be(2);
        AzureDevOpsIssueTrackerProvider.MapPriorityToAdo(WorkItemPriority.Medium).Should().Be(3);
        AzureDevOpsIssueTrackerProvider.MapPriorityToAdo(WorkItemPriority.Low).Should().Be(4);
    }
}
