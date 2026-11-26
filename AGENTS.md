# CompanyCRM — Agent Guide

Enterprise CRM, Project Management, and Time-Tracking platform built on **Clean Architecture**, **.NET 10** (`global.json` pins `10.0.404`), and **React 19 + TypeScript + Vite 6 + MUI v6**.

## Solution Layout
- `src/CompanyCRM.Domain` — Pure domain entities, enums, value objects (zero external dependencies).
- `src/CompanyCRM.Application` — DTOs, repository/service interfaces (`IIssueTrackerProvider`, `ISyncOrchestratorService`, etc.), business rules.
- `src/CompanyCRM.Infrastructure` — EF Core 10 (`CrmDbContext`), PostgreSQL + SQLite fallback (`DatabaseConnectionProbe`), Hangfire jobs, MailKit email, `ExternalSync` providers & simulators.
- `src/CompanyCRM.Api` — ASP.NET Core Web API, REST controllers, SignalR `CrmHub` (`/hubs/crm`), JWT + RBAC policies, Scalar/Swagger.
- `src/CompanyCRM.AppHost` / `CompanyCRM.ServiceDefaults` — .NET Aspire 9 orchestrator & OpenTelemetry defaults.
- `src/companycrm-web` — React 19 SPA with TanStack React Query v5, Orval-generated API client, `@dnd-kit`, Recharts, `@mui/x-data-grid`.
- `tests/CompanyCRM.Tests` — xUnit unit, integration (`CrmApiTestFixture`), RBAC, financial invariant, and sync tests.

## Common Commands
- **Full build**: `dotnet build CompanyCRM.sln`
- **Backend tests**: `dotnet test tests/CompanyCRM.Tests/CompanyCRM.Tests.csproj`
- **Run via Aspire**: `dotnet run --project src/CompanyCRM.AppHost`
- **Run API standalone**: `dotnet run --project src/CompanyCRM.Api` (Swagger at `http://localhost:5000/swagger`)
- **Frontend dev**: `cd src/companycrm-web && npm run dev` (`http://localhost:5173`)
- **Frontend build**: `cd src/companycrm-web && npm run build`
- **Regenerate API client** (requires API running on `:5000`): `cd src/companycrm-web && npm run generate:api`

## Key Architectural Rules
- Respect Clean Architecture dependency direction: `Domain` ← `Application` ← `Infrastructure` ← `Api`.
- Keep dual-mode persistence working: PostgreSQL when reachable, automatic SQLite fallback otherwise.
- Every external issue tracker (`AzureDevOps`, `Jira`, `GitHub`) must support both live REST API calls and a deterministic in-memory simulator (`IsSimulatedMode == true` or missing PAT).
