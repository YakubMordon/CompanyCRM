using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyCRM.Api.Services;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CompanyCRM.Tests;

public class CrmApiTestFixture : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"crm_test_{Guid.NewGuid():N}.db");
    public Mock<IEmailNotificationService> MockEmailService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../src/CompanyCRM.Api"));
        if (!Directory.Exists(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src/CompanyCRM.Api"));
        }
        if (!Directory.Exists(candidate))
        {
            candidate = @"c:\Users\Ja-dy\source\repos\CompanyCRM\src\CompanyCRM.Api";
        }
        builder.UseContentRoot(candidate);

        builder.ConfigureServices(services =>
        {
            // Replace CrmDbContext with isolated temp SQLite database
            services.RemoveAll<DbContextOptions<CrmDbContext>>();
            services.AddDbContext<CrmDbContext>(opts =>
            {
                opts.UseSqlite($"Data Source={_dbPath}");
            });

            // Mock email notifications
            services.RemoveAll<IEmailNotificationService>();
            services.AddSingleton(MockEmailService.Object);
        });
    }

    public HttpClient CreateClientWithRole(string roleName)
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        if (!Enum.TryParse<AppRole>(roleName, true, out var role))
        {
            role = AppRole.Employee;
        }

        var (token, _) = tokenService.GenerateToken(
            Guid.NewGuid(),
            $"{roleName.ToLower()}@test.local",
            $"Test {roleName}",
            role,
            "Engineering",
            "Software Engineer"
        );

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateClientWithEmployee(Guid id, string email, string fullName, AppRole role)
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var (token, _) = tokenService.GenerateToken(
            id,
            email,
            fullName,
            role,
            "Engineering",
            "Software Engineer"
        );

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore file cleanup on process exit
        }
    }
}
