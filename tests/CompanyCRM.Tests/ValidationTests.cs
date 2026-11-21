using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CompanyCRM.Tests;

public class ValidationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly HttpClient _client;

    public ValidationTests(CrmApiTestFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    private async Task AuthenticateAsync(AppRole role)
    {
        var response = await _client.PostAsync($"/api/auth/switch-persona/{role}", null);
        response.EnsureSuccessStatusCode();
        var authRes = await response.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authRes!.Token);
    }

    [Fact]
    public async Task CreateProject_ExceedingCodeMaxLength_ReturnsBadRequestProblemDetails()
    {
        await AuthenticateAsync(AppRole.TeamLead);

        var payload = new CreateProjectDto(
            Code: "TOOLONGCODE123", // max 10
            Name: "Valid Name"
        );

        var response = await _client.PostAsJsonAsync("/api/projects", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Code", content);
    }

    [Fact]
    public async Task CreateProject_InvalidCodeCharacters_ReturnsBadRequest()
    {
        await AuthenticateAsync(AppRole.TeamLead);

        var payload = new CreateProjectDto(
            Code: "PRJ-99!", // non alphanumeric
            Name: "Valid Name"
        );

        var response = await _client.PostAsJsonAsync("/api/projects", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkItem_ExceedingTitleLength_ReturnsBadRequest()
    {
        await AuthenticateAsync(AppRole.Employee);

        var longTitle = new string('A', 201); // max 200
        var payload = new CreateWorkItemDto(
            ProjectId: Guid.NewGuid(),
            Title: longTitle
        );

        var response = await _client.PostAsJsonAsync("/api/workitems", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title", content);
    }

    [Fact]
    public async Task CreateManualTimeEntry_InvalidDuration_ReturnsBadRequest()
    {
        await AuthenticateAsync(AppRole.Employee);

        var payload = new CreateManualTimeEntryDto(
            DurationMinutes: 1500, // max 1440
            IsBillable: true,
            Notes: "Invalid minutes",
            LoggedDate: DateOnly.FromDateTime(DateTime.UtcNow)
        );

        var response = await _client.PostAsJsonAsync($"/api/workitems/{Guid.NewGuid()}/time-entries", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
