using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.ExternalSync;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

public class BackgroundJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;

    public BackgroundJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CrmDbContext CreateContext() => new CrmDbContext(_options);

    [Fact]
    public async Task ExternalSyncReconciliationJob_ExecutesWithoutExceptions_AndLogs()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var syncOrchestratorMock = new Mock<ISyncOrchestratorService>();
        syncOrchestratorMock
            .Setup(s => s.RunReconciliationAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReconciliationResultDto(
                ProjectId: null,
                TotalItemsChecked: 5,
                UpdatedCount: 2,
                ConflictsCount: 1,
                SkippedCount: 2,
                Errors: Array.Empty<string>(),
                StartedAt: DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAt: DateTimeOffset.UtcNow));

        var services = new ServiceCollection();
        services.AddDbContext<CrmDbContext>(opts => opts.UseSqlite(_connection));
        services.AddSingleton(syncOrchestratorMock.Object);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var job = new ExternalSyncReconciliationJob(scopeFactory, NullLogger<ExternalSyncReconciliationJob>.Instance);

        var act = async () => await job.RunReconciliationAsync();
        await act.Should().NotThrowAsync();

        syncOrchestratorMock.Verify(s => s.RunReconciliationAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void DatabaseConnectionProbe_DefaultsToSqlite_WhenPostgresUnreachableOrMissing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Auto",
            ["ConnectionStrings:crmdb"] = "Host=nonexistent-host-12345.local;Port=5432;Database=crm;Username=test;Password=test"
        }).Build();

        var probe = new DatabaseConnectionProbe(config, NullLogger<DatabaseConnectionProbe>.Instance);
        var result = probe.DetermineConnection();

        result.ProviderType.Should().Be(DatabaseProviderType.Sqlite);
        result.ConnectionString.Should().Contain("crm_dev.db");
    }

    [Fact]
    public void DatabaseConnectionProbe_RespectsExplicitSqliteConfig()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite"
        }).Build();

        var probe = new DatabaseConnectionProbe(config, NullLogger<DatabaseConnectionProbe>.Instance);
        var result = probe.DetermineConnection();

        result.ProviderType.Should().Be(DatabaseProviderType.Sqlite);
        result.ConnectionString.Should().Contain("crm_dev.db");
    }
}
