using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CompanyCRM.Infrastructure.Persistence;

public enum DatabaseProviderType
{
    PostgreSql,
    Sqlite
}

public record DatabaseConnectionInfo(
    DatabaseProviderType ProviderType,
    string ConnectionString);

public class DatabaseConnectionProbe
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseConnectionProbe> _logger;

    public DatabaseConnectionProbe(
        IConfiguration configuration,
        ILogger<DatabaseConnectionProbe> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public DatabaseConnectionInfo DetermineConnection(string? sqlitePath = null)
    {
        var preferredProvider = _configuration["Database:Provider"]?.Trim().ToLowerInvariant() ?? "auto";
        var defaultSqliteDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(defaultSqliteDir);
        var resolvedSqlitePath = sqlitePath ?? Path.Combine(defaultSqliteDir, "crm_dev.db");
        var sqliteConnStr = $"Data Source={resolvedSqlitePath}";

        if (preferredProvider == "sqlite")
        {
            _logger.LogInformation("Database provider explicitly configured to SQLite. Using {Path}", resolvedSqlitePath);
            return new DatabaseConnectionInfo(DatabaseProviderType.Sqlite, sqliteConnStr);
        }

        var pgConnStr = _configuration.GetConnectionString("crmdb")
                     ?? _configuration.GetConnectionString("DefaultConnection")
                     ?? _configuration["Database:ConnectionString"];

        if (string.IsNullOrWhiteSpace(pgConnStr))
        {
            _logger.LogWarning("No PostgreSQL connection string provided. Falling back to SQLite at {Path}", resolvedSqlitePath);
            return new DatabaseConnectionInfo(DatabaseProviderType.Sqlite, sqliteConnStr);
        }

        try
        {
            var probeBuilder = new NpgsqlConnectionStringBuilder(pgConnStr)
            {
                Timeout = 2,
                CommandTimeout = 2
            };

            using var conn = new NpgsqlConnection(probeBuilder.ConnectionString);
            conn.Open();

            _logger.LogInformation("Successfully connected to PostgreSQL at {Host}:{Port}/{Database}. Using PostgreSQL provider.",
                probeBuilder.Host, probeBuilder.Port, probeBuilder.Database);

            return new DatabaseConnectionInfo(DatabaseProviderType.PostgreSql, pgConnStr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to PostgreSQL within 2 seconds. Gracefully falling back to SQLite at {Path}", resolvedSqlitePath);
            return new DatabaseConnectionInfo(DatabaseProviderType.Sqlite, sqliteConnStr);
        }
    }
}
