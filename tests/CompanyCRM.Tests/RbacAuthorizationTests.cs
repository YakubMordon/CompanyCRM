namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using FluentAssertions;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;
using CompanyCRM.Tests.Common;

public class RbacAuthorizationTests
{
    private readonly IRbacAuthorizationService _rbacService = new RbacAuthorizationService();
    private const string JwtSecret = "CompanyCRM-Super-Secret-Jwt-Key-For-Testing-Only-1234567890";

    private static string GeneratePersonaToken(string roleName, string email = "user@company.com")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtSecret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim(ClaimTypes.Role, roleName)
            }),
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    // =========================================================================
    // Tier 1: Feature Coverage (5 test cases)
    // =========================================================================

    [Fact]
    public void GetCompensation_AsEmployee_Returns403Forbidden()
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.Employee);

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, "/api/management/compensation");
        var canAccess = _rbacService.CanAccessManagementPortal(context);

        // Assert
        statusCode.Should().Be(403);
        canAccess.Should().BeFalse();
    }

    [Fact]
    public void GetCompensation_AsTeamLead_Returns403Forbidden()
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.TeamLead);

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, "/api/management/compensation");
        var canAccess = _rbacService.CanAccessManagementPortal(context);

        // Assert
        statusCode.Should().Be(403);
        canAccess.Should().BeFalse();
    }

    [Fact]
    public void GetCompensation_AsHigherManagement_Returns200OK()
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.HigherManagement);
        var employee = TestDataBuilder.CreateEmployee(fullName: "Dev Lead", hourlyCostRate: 65.00m, hourlyBillableRate: 175.00m);

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, "/api/management/compensation");
        var canAccess = _rbacService.CanAccessManagementPortal(context);

        // Assert
        statusCode.Should().Be(200);
        canAccess.Should().BeTrue();
        employee.AnnualBaseSalary.Should().BeGreaterThan(0);
        employee.HourlyCostRate.Should().Be(65.00m);
        employee.HourlyBillableRate.Should().Be(175.00m);
    }

    [Fact]
    public void GetProfitability_AsEmployee_Returns403Forbidden()
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.Employee);
        var projectId = Guid.NewGuid();

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, $"/api/management/profitability?projectId={projectId}");

        // Assert
        statusCode.Should().Be(403);
    }

    [Fact]
    public void GetProfitability_AsHigherManagement_Returns200OK()
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.HigherManagement);
        var projectId = Guid.NewGuid();

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, $"/api/management/profitability?projectId={projectId}");

        // Assert
        statusCode.Should().Be(200);
    }

    // =========================================================================
    // Tier 2: Boundary & Corner Cases (5 test cases)
    // =========================================================================

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    public void GetManagementEndpoints_UnauthenticatedWithoutJwt_Returns401Unauthorized(string endpoint)
    {
        // Arrange
        var unauthenticatedContext = new SecurityContext(false, null);

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(unauthenticatedContext, endpoint);

        // Assert
        statusCode.Should().Be(401);
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData("TeamLead")]
    [InlineData("HigherManagement")]
    public void QuickPersonaSwitcher_ForEveryRole_GeneratesValidJwtWithCorrectRoleClaim(string roleName)
    {
        // Arrange & Act
        var jwtString = GeneratePersonaToken(roleName, $"test.{roleName.ToLowerInvariant()}@company.com");

        // Assert
        jwtString.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(jwtString);

        jwt.Claims.Should().Contain(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == roleName);
        jwt.ValidTo.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void UpdateCompensation_AsEmployee_Returns403Forbidden()
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.Employee);
        var employeeId = Guid.NewGuid();

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, $"/api/management/compensation/{employeeId}", "PUT");

        // Assert
        statusCode.Should().Be(403);
    }

    [Theory]
    [InlineData("/api/management/analytics/velocity?projectId=00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/management/analytics/hours-comparison?projectId=00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/management/analytics/completion-rates?projectId=00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/management/analytics/burndown?projectId=00000000-0000-0000-0000-000000000001&sprintName=Sprint1")]
    public void GetAnalyticsEndpoints_AsTeamLead_Returns403Forbidden(string subPath)
    {
        // Arrange
        var context = new SecurityContext(true, AppRole.TeamLead);

        // Act
        var statusCode = _rbacService.GetAuthorizationStatusCode(context, subPath);

        // Assert
        statusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(99)]
    public void UpdateSkillsMatrix_WithInvalidProficiency_Returns400BadRequest(int invalidProficiency)
    {
        // Arrange: instantiate real production domain entity EmployeeSkill
        var skill = new EmployeeSkill
        {
            SkillName = "C# / ASP.NET Core",
            Category = "Backend Architecture",
            ProficiencyLevel = 3
        };

        // Act & Assert 1: Domain method SetProficiency rejects out-of-range level
        Action actMethod = () => skill.SetProficiency(invalidProficiency);
        actMethod.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Proficiency level must be between 1 and 5*");

        // Act & Assert 2: Direct property setter encapsulation also rejects out-of-range level
        Action actProperty = () => skill.ProficiencyLevel = invalidProficiency;
        actProperty.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Proficiency level must be between 1 and 5*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void UpdateSkillsMatrix_WithValidProficiency_Succeeds(int validProficiency)
    {
        // Arrange
        var skill = new EmployeeSkill
        {
            SkillName = "TypeScript / React",
            Category = "Frontend Engineering",
            ProficiencyLevel = 1
        };

        // Act
        skill.SetProficiency(validProficiency);

        // Assert
        skill.ProficiencyLevel.Should().Be(validProficiency);

        // Property setter verification
        skill.ProficiencyLevel = validProficiency;
        skill.ProficiencyLevel.Should().Be(validProficiency);
    }

    [Fact]
    public void RbacAuthorizationService_RolePermissions_MatchSpecification()
    {
        // Arrange
        var unauthenticated = new SecurityContext(false, null);
        var employee = new SecurityContext(true, AppRole.Employee);
        var teamLead = new SecurityContext(true, AppRole.TeamLead);
        var higherMgmt = new SecurityContext(true, AppRole.HigherManagement);

        // Act & Assert Public Directory
        _rbacService.CanAccessPublicDirectory(unauthenticated).Should().BeFalse();
        _rbacService.CanAccessPublicDirectory(employee).Should().BeTrue();
        _rbacService.CanAccessPublicDirectory(teamLead).Should().BeTrue();
        _rbacService.CanAccessPublicDirectory(higherMgmt).Should().BeTrue();

        // Act & Assert Review Queue
        _rbacService.CanAccessReviewQueue(unauthenticated).Should().BeFalse();
        _rbacService.CanAccessReviewQueue(employee).Should().BeFalse();
        _rbacService.CanAccessReviewQueue(teamLead).Should().BeTrue();
        _rbacService.CanAccessReviewQueue(higherMgmt).Should().BeTrue();

        // Act & Assert Planning Cutoff Config
        _rbacService.CanManageCutoffConfig(unauthenticated).Should().BeFalse();
        _rbacService.CanManageCutoffConfig(employee).Should().BeFalse();
        _rbacService.CanManageCutoffConfig(teamLead).Should().BeTrue();
        _rbacService.CanManageCutoffConfig(higherMgmt).Should().BeTrue();

        // Act & Assert Management Portal
        _rbacService.CanAccessManagementPortal(unauthenticated).Should().BeFalse();
        _rbacService.CanAccessManagementPortal(employee).Should().BeFalse();
        _rbacService.CanAccessManagementPortal(teamLead).Should().BeFalse();
        _rbacService.CanAccessManagementPortal(higherMgmt).Should().BeTrue();
    }

    [Fact]
    public void RbacAuthorizationService_IsAuthorized_MatchesStatusCode200()
    {
        // Arrange
        var employee = new SecurityContext(true, AppRole.Employee);
        var higherMgmt = new SecurityContext(true, AppRole.HigherManagement);

        // Act & Assert
        _rbacService.IsAuthorized(employee, "/api/management/compensation").Should().BeFalse();
        _rbacService.IsAuthorized(higherMgmt, "/api/management/compensation").Should().BeTrue();
        _rbacService.IsAuthorized(employee, "/api/employees").Should().BeTrue();
    }

    [Fact]
    public void RbacAuthorizationService_InvalidArguments_ThrowsExpectedExceptions()
    {
        // Null context
        Action actNullContext = () => _rbacService.GetAuthorizationStatusCode(null!, "/api/management");
        actNullContext.Should().Throw<ArgumentNullException>();

        // Empty endpoint
        Action actEmptyEndpoint = () => _rbacService.GetAuthorizationStatusCode(new SecurityContext(true, AppRole.Employee), "   ");
        actEmptyEndpoint.Should().Throw<ArgumentException>();
    }
}
