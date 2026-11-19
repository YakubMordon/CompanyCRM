using System.Net;
using System.Net.Http.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class RbacSecurityIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public RbacSecurityIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Guid> GetSampleProjectIdAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var project = await db.Projects.FirstOrDefaultAsync();
        return project?.Id ?? Guid.NewGuid();
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    [InlineData("/api/management/analytics/hours-comparison")]
    [InlineData("/api/management/analytics/completion-rates")]
    [InlineData("/api/management/analytics/burndown")]
    public async Task ManagementEndpoints_WithoutAuthentication_Return401Unauthorized(string endpoint)
    {
        var client = _fixture.CreateClient();
        var projectId = await GetSampleProjectIdAsync();
        var url = endpoint.Contains("?") ? $"{endpoint}&projectId={projectId}" : $"{endpoint}?projectId={projectId}";

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    [InlineData("/api/management/analytics/hours-comparison")]
    [InlineData("/api/management/analytics/completion-rates")]
    [InlineData("/api/management/analytics/burndown")]
    public async Task ManagementEndpoints_WithEmployeeRole_Return403Forbidden(string endpoint)
    {
        var client = _fixture.CreateClientWithRole("Employee");
        var projectId = await GetSampleProjectIdAsync();
        var url = endpoint.Contains("?") ? $"{endpoint}&projectId={projectId}" : $"{endpoint}?projectId={projectId}";

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    [InlineData("/api/management/analytics/hours-comparison")]
    [InlineData("/api/management/analytics/completion-rates")]
    [InlineData("/api/management/analytics/burndown")]
    public async Task ManagementEndpoints_WithTeamLeadRole_Return403Forbidden(string endpoint)
    {
        var client = _fixture.CreateClientWithRole("TeamLead");
        var projectId = await GetSampleProjectIdAsync();
        var url = endpoint.Contains("?") ? $"{endpoint}&projectId={projectId}" : $"{endpoint}?projectId={projectId}";

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCompensation_WithHigherManagementRole_Returns200OK_WithSensitiveData()
    {
        var client = _fixture.CreateClientWithRole("HigherManagement");

        var response = await client.GetAsync("/api/management/compensation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<List<EmployeeCompensationDto>>(CrmApiTestFixture.JsonOptions);
        data.Should().NotBeNull();
        data.Should().NotBeEmpty();

        var first = data![0];
        first.AnnualBaseSalary.Should().BeGreaterThan(0);
        first.HourlyCostRate.Should().BeGreaterThan(0);
        first.HourlyBillableRate.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetProfitability_WithHigherManagementRole_Returns200OK_WithFinancialCalculations()
    {
        var client = _fixture.CreateClientWithRole("HigherManagement");
        var projectId = await GetSampleProjectIdAsync();

        var response = await client.GetAsync($"/api/management/profitability?projectId={projectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<ProjectProfitabilityDto>(CrmApiTestFixture.JsonOptions);
        data.Should().NotBeNull();
        data!.ProjectId.Should().Be(projectId);
        data.ProjectCode.Should().NotBeNullOrWhiteSpace();
        data.EmployeeBreakdowns.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAnalytics_WithHigherManagementRole_Returns200OK_ForAllAnalyticsEndpoints()
    {
        var client = _fixture.CreateClientWithRole("HigherManagement");
        var projectId = await GetSampleProjectIdAsync();

        // Velocity
        var velResp = await client.GetAsync($"/api/management/analytics/velocity?projectId={projectId}");
        velResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var velocities = await velResp.Content.ReadFromJsonAsync<List<SprintVelocityDto>>(CrmApiTestFixture.JsonOptions);
        velocities.Should().NotBeNull();

        // Hours comparison
        var hoursResp = await client.GetAsync($"/api/management/analytics/hours-comparison?projectId={projectId}");
        hoursResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hours = await hoursResp.Content.ReadFromJsonAsync<List<HoursComparisonDto>>(CrmApiTestFixture.JsonOptions);
        hours.Should().NotBeNull();

        // Completion rates
        var compResp = await client.GetAsync($"/api/management/analytics/completion-rates?projectId={projectId}");
        compResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var completion = await compResp.Content.ReadFromJsonAsync<CompletionRateDto>(CrmApiTestFixture.JsonOptions);
        completion.Should().NotBeNull();
        completion!.ProjectId.Should().Be(projectId);

        // Burndown
        var burnResp = await client.GetAsync($"/api/management/analytics/burndown?projectId={projectId}");
        burnResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var burndown = await burnResp.Content.ReadFromJsonAsync<BurndownDto>(CrmApiTestFixture.JsonOptions);
        burndown.Should().NotBeNull();
        burndown!.ProjectId.Should().Be(projectId);
        burndown.Days.Should().HaveCount(10);
    }
}
