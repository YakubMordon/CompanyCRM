using System.Net;
using System.Net.Http.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
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

    [Fact]
    public async Task CreateEmployee_WithoutAuthentication_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();
        var request = new CreateEmployeeRequestDto(
            FullName: "Unauthorized Test",
            Email: "unauth@company.com",
            Department: "Engineering",
            JobTitle: "Engineer",
            SeniorityRank: SeniorityRank.Junior,
            Role: AppRole.Employee,
            AnnualBaseSalary: 80000,
            HourlyCostRate: 40,
            HourlyBillableRate: 120,
            WeeklyCapacityHours: 40,
            TargetUtilizationPercent: 80
        );

        var response = await client.PostAsJsonAsync("/api/management/employees", request);

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

    [Fact]
    public async Task CreateEmployee_WithNonManagementRole_Returns403Forbidden()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");
        var req = new CreateEmployeeRequestDto("Test Dev", "test.dev@companycrm.local", "Engineering", "Dev");

        var response = await client.PostAsJsonAsync("/api/management/employees", req);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ManagementEmployeeCrud_WithHigherManagementRole_SucceedsEndToEnd()
    {
        var client = _fixture.CreateClientWithRole("HigherManagement");
        var uniqueEmail = $"new.employee.{Guid.NewGuid():N}@companycrm.local";

        // 1. Create Employee with Salary & Compensation
        var createDto = new CreateEmployeeRequestDto(
            FullName: "Marcus Sterling",
            Email: uniqueEmail,
            Department: "Architecture",
            JobTitle: "Lead Systems Architect",
            SeniorityRank: SeniorityRank.Principal,
            Role: AppRole.TeamLead,
            AnnualBaseSalary: 165000.00m,
            HourlyCostRate: 79.33m,
            HourlyBillableRate: 195.00m,
            WeeklyCapacityHours: 40.0m,
            TargetUtilizationPercent: 75.0m,
            Bio: "Experienced cloud systems architect."
        );

        var createRes = await client.PostAsJsonAsync("/api/management/employees", createDto);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createRes.Content.ReadFromJsonAsync<EmployeeCompensationDto>(CrmApiTestFixture.JsonOptions);
        created.Should().NotBeNull();
        created!.FullName.Should().Be("Marcus Sterling");
        created.Email.Should().Be(uniqueEmail);
        created.AnnualBaseSalary.Should().Be(165000.00m);
        created.HourlyCostRate.Should().Be(79.33m);
        created.HourlyBillableRate.Should().Be(195.00m);
        created.WeeklyCapacityHours.Should().Be(40.0m);
        created.TargetUtilizationPercent.Should().Be(75.0m);

        // 2. Update Employee
        var updateDto = new UpdateEmployeeRequestDto(
            FullName: "Marcus Sterling (Updated)",
            Email: uniqueEmail,
            Department: "Architecture & DevOps",
            JobTitle: "VP of Architecture",
            SeniorityRank: SeniorityRank.Principal,
            Role: AppRole.HigherManagement,
            AnnualBaseSalary: 185000.00m,
            HourlyCostRate: 88.94m,
            HourlyBillableRate: 225.00m,
            WeeklyCapacityHours: 40.0m,
            TargetUtilizationPercent: 70.0m,
            Bio: "Executive architect lead."
        );

        var updateRes = await client.PutAsJsonAsync($"/api/management/employees/{created.EmployeeId}", updateDto);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateRes.Content.ReadFromJsonAsync<EmployeeCompensationDto>(CrmApiTestFixture.JsonOptions);
        updated.Should().NotBeNull();
        updated!.FullName.Should().Be("Marcus Sterling (Updated)");
        updated.Department.Should().Be("Architecture & DevOps");
        updated.JobTitle.Should().Be("VP of Architecture");
        updated.AnnualBaseSalary.Should().Be(185000.00m);
        updated.HourlyCostRate.Should().Be(88.94m);
        updated.HourlyBillableRate.Should().Be(225.00m);

        // 3. Delete Employee
        var deleteRes = await client.DeleteAsync($"/api/management/employees/{created.EmployeeId}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify employee no longer in compensation list
        var listRes = await client.GetAsync("/api/management/compensation");
        var list = await listRes.Content.ReadFromJsonAsync<List<EmployeeCompensationDto>>(CrmApiTestFixture.JsonOptions);
        list.Should().NotContain(e => e.EmployeeId == created.EmployeeId);
    }

    [Fact]
    public void Controllers_DoNotDirectlyDependOnCrmDbContext()
    {
        var controllerTypes = typeof(CompanyCRM.Api.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToList();

        controllerTypes.Should().NotBeEmpty();

        foreach (var controller in controllerTypes)
        {
            var constructors = controller.GetConstructors();
            foreach (var ctor in constructors)
            {
                var parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
                parameterTypes.Should().NotContain(typeof(CrmDbContext),
                    $"Controller '{controller.Name}' must not directly depend on CrmDbContext");
            }
        }
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
