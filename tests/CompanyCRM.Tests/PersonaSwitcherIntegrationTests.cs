using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using CompanyCRM.Application.DTOs;
using FluentAssertions;
using Xunit;

namespace CompanyCRM.Tests;

public class PersonaSwitcherIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public PersonaSwitcherIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Employee", "Employee")]
    [InlineData("TeamLead", "TeamLead")]
    [InlineData("HigherManagement", "HigherManagement")]
    public async Task SwitchPersona_ValidRole_Returns200OK_WithValidJwtAndRoleClaim(string requestedRole, string expectedRoleClaim)
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsync($"/api/auth/switch-persona/{requestedRole}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        authResponse.Should().NotBeNull();
        authResponse!.Token.Should().NotBeNullOrWhiteSpace();
        authResponse.Role.Should().Be(expectedRoleClaim);
        authResponse.User.Should().NotBeNull();
        authResponse.Permissions.Should().NotBeEmpty();

        // Validate token claims directly
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(authResponse.Token);
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role");
        roleClaim.Should().NotBeNull();
        roleClaim!.Value.Should().Be(expectedRoleClaim);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperUser")]
    [InlineData("Guest")]
    [InlineData("123")]
    public async Task SwitchPersona_InvalidRole_Returns400BadRequest(string invalidRole)
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsync($"/api/auth/switch-persona/{invalidRole}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SwitchPersona_RoundtripAuthorization_EnforcesAppropriateAccess()
    {
        var client = _fixture.CreateClient();

        // 1. Switch to HigherManagement
        var mgmtResponse = await client.PostAsync("/api/auth/switch-persona/HigherManagement", null);
        mgmtResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var mgmtAuth = await mgmtResponse.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        // Call management endpoint with HigherManagement token -> should succeed
        var authClient = _fixture.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mgmtAuth!.Token);
        var compResponse = await authClient.GetAsync("/api/management/compensation");
        compResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Switch to Employee
        var empResponse = await client.PostAsync("/api/auth/switch-persona/Employee", null);
        empResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var empAuth = await empResponse.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);

        // Call management endpoint with Employee token -> MUST return 403 Forbidden
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", empAuth!.Token);
        var forbiddenResponse = await authClient.GetAsync("/api/management/compensation");
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
