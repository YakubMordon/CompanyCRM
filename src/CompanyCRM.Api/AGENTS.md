# CompanyCRM.Api — Agent Guide

ASP.NET Core Web API host, SignalR hub, and JWT/RBAC security layer.

## Key Conventions
- `Program.cs`:
  - `JsonStringEnumConverter` is configured on both MVC controllers and SignalR (`CrmHub` at `/hubs/crm`).
  - CORS policy `"Frontend"` allows `http://localhost:5173` with credentials.
  - Authorization policies: `"HigherManagementOnly"` (`HigherManagement`), `"TeamLeadOrAbove"` (`TeamLead`, `HigherManagement`).
- `Controllers/SyncController.cs`:
  - Webhook endpoints (`api/webhooks/ado`, `api/webhooks/jira`, `api/webhooks/github`) are `[AllowAnonymous]` and broadcast `SyncStatusChanged` / `WorkItemUpdated` via `IHubContext<CrmHub, ICrmHubClient>`.
- `Controllers/ProjectsController.cs`:
  - `GET/PUT /api/projects/{id}/external-config` is restricted to `TeamLeadOrAbove`.
