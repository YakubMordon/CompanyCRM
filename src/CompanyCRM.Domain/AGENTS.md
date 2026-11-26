# CompanyCRM.Domain — Agent Guide

Pure domain layer with **zero** NuGet or project dependencies.

## Key Types & Invariants
- `Enums/Enums.cs`:
  - `WorkItemStatus`: 9-stage lifecycle (`Draft` → `PendingReview` → `ClarificationRequested` / `Denied` / `Approved` → `Backlog` → `Todo` → `InProgress` → `InVerify` → `Done`).
  - `ReviewStatus`: `NotRequired`, `Pending`, `Approved`, `ClarificationRequested`, `Denied`.
  - `ExternalSourceType`: `None = 0`, `AzureDevOps = 1`, `Jira = 2`, `GitHub = 3`.
  - `AppRole`: `Employee`, `TeamLead`, `HigherManagement`.
- `Entities/WorkItem.cs`: Enforces `CanPushToExternal()` (must be approved / not denied before external sync).
- `ValueObjects/ProjectExternalConfig.cs`: Immutable config record passed to `IIssueTrackerProvider`.

## Conventions
- Keep entities persistence-ignorant (no EF Core attributes unless strictly needed; prefer fluent config in `Infrastructure`).
- Never reference `Application`, `Infrastructure`, or `Api` from `Domain`.
