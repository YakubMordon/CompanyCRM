using CompanyCRM.Application.Interfaces;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.Email;
using CompanyCRM.Infrastructure.ExternalSync;
using CompanyCRM.Infrastructure.Persistence;
using CompanyCRM.Infrastructure.Services;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrmInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Dual-Mode Database Registration (PostgreSQL with SQLite fallback)
        services.AddSingleton<DatabaseConnectionProbe>();

        services.AddDbContext<CrmDbContext>((sp, options) =>
        {
            var probe = sp.GetRequiredService<DatabaseConnectionProbe>();
            var connInfo = probe.DetermineConnection();

            if (connInfo.ProviderType == DatabaseProviderType.PostgreSql)
            {
                options.UseNpgsql(connInfo.ConnectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                });
            }
            else
            {
                options.UseSqlite(connInfo.ConnectionString, sqliteOptions =>
                {
                    sqliteOptions.CommandTimeout(30);
                });
            }
        });

        // 2. Database Seeder
        services.AddScoped<DatabaseSeeder>();

        // Repositories
        services.AddScoped<IEmployeeRepository, Persistence.Repositories.EmployeeRepository>();
        services.AddScoped<IProjectRepository, Persistence.Repositories.ProjectRepository>();
        services.AddScoped<IWorkItemRepository, Persistence.Repositories.WorkItemRepository>();
        services.AddScoped<IPlanningRepository, Persistence.Repositories.PlanningRepository>();

        // 3. External Issue Tracker Integrations & Simulators (Azure DevOps, Jira, GitHub)
        services.AddSingleton<AzureDevOpsSimulator>();
        services.AddHttpClient<AzureDevOpsIssueTrackerProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<JiraSimulator>();
        services.AddHttpClient<JiraIssueTrackerProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<GitHubSimulator>();
        services.AddHttpClient<GitHubIssueTrackerProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IIssueTrackerProvider>(sp => sp.GetRequiredService<AzureDevOpsIssueTrackerProvider>());
        services.AddScoped<IIssueTrackerProvider>(sp => sp.GetRequiredService<JiraIssueTrackerProvider>());
        services.AddScoped<IIssueTrackerProvider>(sp => sp.GetRequiredService<GitHubIssueTrackerProvider>());

        // 4. Sync Orchestrator Service
        services.AddScoped<ISyncOrchestratorService>(sp =>
        {
            var dbContext = sp.GetRequiredService<CrmDbContext>();
            var providers = sp.GetServices<IIssueTrackerProvider>();
            var logger = sp.GetRequiredService<ILogger<SyncOrchestratorService>>();
            return new SyncOrchestratorService(dbContext, providers, logger);
        });

        // 5. Work Item Review Service
        services.AddScoped<IWorkItemReviewService, WorkItemReviewService>();

        // 6. Management Analytics Service
        services.AddScoped<IManagementAnalyticsService, ManagementAnalyticsService>();

        // 7. Email Notifications (MailKit with MailPit SMTP & In-App DB log) & Chat Webhooks (Slack / Teams)
        services.AddScoped<IEmailNotificationService, MailKitEmailNotificationService>();
        services.AddHttpClient<SlackTeamsWebhookNotificationService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IChatWebhookNotificationService>(sp => sp.GetRequiredService<SlackTeamsWebhookNotificationService>());

        // 8. Background Jobs & Hangfire Configuration
        services.AddScoped<IPlanningCutoffJob, DailyPlanCutoffReminderJob>();
        services.AddScoped<DailyPlanCutoffReminderJob>();
        services.AddScoped<ExternalSyncReconciliationJob>();

        services.AddHangfire((sp, hangfireConfig) =>
        {
            hangfireConfig
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseMemoryStorage();
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = Math.Max(Environment.ProcessorCount, 2);
        });

        return services;
    }
}
