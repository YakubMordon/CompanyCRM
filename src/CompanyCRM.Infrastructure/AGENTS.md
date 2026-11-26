# CompanyCRM.Infrastructure — Agent Guide

Implements persistence, background jobs, email, and external integrations.

## Key Subsystems
- `Persistence/CrmDbContext.cs` & `DatabaseConnectionProbe.cs`: Dual-mode DB (PostgreSQL primary, SQLite automatic fallback). `DatabaseSeeder.cs` seeds 6 demo personas and sample projects/work items on startup.
- `ExternalSync/`:
  - Providers: `AzureDevOpsIssueTrackerProvider`, `JiraIssueTrackerProvider`, `GitHubIssueTrackerProvider`.
  - Simulators: `AzureDevOpsSimulator`, `JiraSimulator`, `GitHubSimulator` (registered as singletons; support `.Reset()` for tests).
  - `SyncOrchestratorService.cs`: Uses `SyncContentHasher` (SHA-256) for echo-loop prevention and Jaccard + Levenshtein title similarity (`>= 0.85`) for conflict detection.
- `BackgroundJobs/`: `DailyPlanCutoffReminderJob` and `ExternalSyncReconciliationJob` scheduled via Hangfire (`UseMemoryStorage()`).
- `Email/`: `MailKitEmailNotificationService` with in-database fallback logging.

## DI Registration
- All infrastructure services, `HttpClient`s, simulators, and `IIssueTrackerProvider` implementations are wired in `ServiceCollectionExtensions.cs` (`AddCrmInfrastructure`).
