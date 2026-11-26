# CompanyCRM.Application — Agent Guide

Defines contracts, DTOs, and application-level interfaces. Depends **only** on `CompanyCRM.Domain`.

## Structure
- `DTOs/` — Immutable C# `record` types for API requests/responses and sync payloads (`ExternalWorkItemDto`, `WebhookProcessingResultDto`, `SyncConflictDto`, etc.).
- `Interfaces/` — Repository and service abstractions:
  - `IIssueTrackerProvider`: Implement this for any new external tracker (`SourceType`, `PushWorkItemAsync`, `FetchRemoteWorkItemsAsync`, `ParseWebhookPayloadAsync`).
  - `ISyncOrchestratorService`: Coordinates push, inbound webhook processing, reconciliation, and conflict resolution.
  - `IWorkItemReviewService`, `IManagementAnalyticsService`, `IPlanningCutoffJob`, `IEmailNotificationService`.

## Conventions
- Do not add EF Core, `HttpClient`, or ASP.NET Core dependencies here.
- When adding/updating DTOs, remember to regenerate the frontend Orval client (`npm run generate:api` in `src/companycrm-web`) if API contracts change.
