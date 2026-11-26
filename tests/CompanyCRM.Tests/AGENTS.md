# CompanyCRM.Tests — Agent Guide

xUnit + FluentAssertions + `WebApplicationFactory` test suite.

## Commands
- Run all tests: `dotnet test tests/CompanyCRM.Tests/CompanyCRM.Tests.csproj`
- Run filtered tests: `dotnet test tests/CompanyCRM.Tests/CompanyCRM.Tests.csproj --filter "FullyQualifiedName~GitHub"`

## Conventions
- `CrmApiTestFixture.cs`: Spins up the full API in-memory with an isolated SQLite database and provides `GetJwtTokenAsync(client, personaName)` for RBAC/integration tests.
- When testing `ExternalSync` simulators (`AzureDevOpsSimulator`, `JiraSimulator`, `GitHubSimulator`), call `.Reset()` in unit tests to keep state deterministic.
- Never relax assertions or skip tests to make a suite pass.
