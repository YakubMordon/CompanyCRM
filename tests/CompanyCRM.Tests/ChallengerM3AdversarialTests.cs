using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CompanyCRM.Tests;

public class ChallengerM3AdversarialTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public ChallengerM3AdversarialTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Guid> GetSampleProjectIdAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var project = await db.Projects.FirstOrDefaultAsync();
        return project?.Id ?? Guid.NewGuid();
    }

    private static string CreateCustomToken(
        string secret = "CompanyCRM_SuperSecret_Jwt_Key_For_Development_Only_Min256Bits!",
        string issuer = "CompanyCRM",
        string audience = "CompanyCRM.Web",
        string role = "HigherManagement",
        DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "test.user@companycrm.local"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // =========================================================================
    // FOCUS 1: RBAC BOUNDARIES & PERSONA SWITCHER STRESS TESTING
    // =========================================================================

    [Fact]
    public async Task Management_ExpiredJwtToken_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();
        var expiredToken = CreateCustomToken(expires: DateTime.UtcNow.AddMinutes(-30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/management/compensation");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Expired JWT tokens must never grant access.");
    }

    [Fact]
    public async Task Management_CorruptedJwtTokenSignature_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();
        var validToken = CreateCustomToken();
        var corruptedToken = validToken[..^8] + "CORRUPT8";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corruptedToken);

        var response = await client.GetAsync("/api/management/compensation");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Corrupted JWT signature must be rejected.");
    }

    [Fact]
    public async Task Management_ForgedJwtToken_WrongSecret_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();
        var forgedToken = CreateCustomToken(secret: "CompletelyDifferentSecretKeyThatIsNotValidMin256Bits!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/management/compensation");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Forged signature with invalid key must be rejected.");
    }

    [Fact]
    public async Task Management_ForgedJwtToken_UntrustedIssuer_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();
        var forgedToken = CreateCustomToken(issuer: "UntrustedAttackerIssuer");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/management/compensation");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Untrusted issuer must be rejected.");
    }

    [Fact]
    public async Task Management_ForgedJwtToken_UntrustedAudience_Returns401Unauthorized()
    {
        var client = _fixture.CreateClient();
        var forgedToken = CreateCustomToken(audience: "UntrustedAttackerAudience");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/management/compensation");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Untrusted audience must be rejected.");
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    [InlineData("/api/management/analytics/hours-comparison")]
    [InlineData("/api/management/analytics/completion-rates")]
    [InlineData("/api/management/analytics/burndown")]
    public async Task Management_AllSixEndpoints_Strict403Forbidden_ForEmployeeRole(string endpoint)
    {
        var client = _fixture.CreateClientWithRole("Employee");
        var projectId = await GetSampleProjectIdAsync();
        var url = endpoint.Contains("?") ? $"{endpoint}&projectId={projectId}" : $"{endpoint}?projectId={projectId}";

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Employee must receive 403 Forbidden on {endpoint}");
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    [InlineData("/api/management/analytics/hours-comparison")]
    [InlineData("/api/management/analytics/completion-rates")]
    [InlineData("/api/management/analytics/burndown")]
    public async Task Management_AllSixEndpoints_Strict403Forbidden_ForTeamLeadRole(string endpoint)
    {
        var client = _fixture.CreateClientWithRole("TeamLead");
        var projectId = await GetSampleProjectIdAsync();
        var url = endpoint.Contains("?") ? $"{endpoint}&projectId={projectId}" : $"{endpoint}?projectId={projectId}";

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"TeamLead must receive 403 Forbidden on {endpoint}");
    }

    [Theory]
    [InlineData("/api/management/compensation")]
    [InlineData("/api/management/profitability")]
    [InlineData("/api/management/analytics/velocity")]
    [InlineData("/api/management/analytics/hours-comparison")]
    [InlineData("/api/management/analytics/completion-rates")]
    [InlineData("/api/management/analytics/burndown")]
    public async Task Management_AllSixEndpoints_Strict401Unauthorized_ForAnonymous(string endpoint)
    {
        var client = _fixture.CreateClient();
        var projectId = await GetSampleProjectIdAsync();
        var url = endpoint.Contains("?") ? $"{endpoint}&projectId={projectId}" : $"{endpoint}?projectId={projectId}";

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"Anonymous caller must receive 401 Unauthorized on {endpoint}");
    }

    [Theory]
    [InlineData("employee", "Employee")]
    [InlineData("EMPLOYEE", "Employee")]
    [InlineData("Employee", "Employee")]
    [InlineData("teamlead", "TeamLead")]
    [InlineData("TEAMLEAD", "TeamLead")]
    [InlineData("TeamLead", "TeamLead")]
    [InlineData("highermanagement", "HigherManagement")]
    [InlineData("HIGHERMANAGEMENT", "HigherManagement")]
    [InlineData("HigherManagement", "HigherManagement")]
    public async Task PersonaSwitcher_CaseInsensitiveMatching_Succeeds(string requestedRole, string expectedRole)
    {
        var client = _fixture.CreateClient();
        var response = await client.PostAsync($"/api/auth/switch-persona/{requestedRole}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var authDto = await response.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        authDto.Should().NotBeNull();
        authDto!.Role.Should().Be(expectedRole);
        authDto.Token.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(authDto.Token);
        var roleClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role");
        roleClaim.Should().NotBeNull();
        roleClaim!.Value.Should().Be(expectedRole);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperUser")]
    [InlineData("Root")]
    [InlineData("Guest")]
    [InlineData("Manager")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("999")]
    [InlineData("' OR '1'='1")]
    [InlineData("admin'--")]
    [InlineData("'; DROP TABLE Employees;--")]
    [InlineData("<script>alert(1)</script>")]
    public async Task PersonaSwitcher_InvalidRolesAndInjectionStrings_Returns400BadRequest(string invalidInput)
    {
        var client = _fixture.CreateClient();
        var encoded = Uri.EscapeDataString(invalidInput);
        var response = await client.PostAsync($"/api/auth/switch-persona/{encoded}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"Invalid persona role '{invalidInput}' must be rejected with 400 Bad Request.");
    }

    [Theory]
    [InlineData(" Employee ", "Employee")]
    [InlineData(" TeamLead ", "TeamLead")]
    [InlineData(" HigherManagement ", "HigherManagement")]
    public async Task PersonaSwitcher_WhitespaceAroundValidRoles_IsNormalizedOrHandledCleanly(string roleWithWhitespace, string expectedRole)
    {
        var client = _fixture.CreateClient();
        var encoded = Uri.EscapeDataString(roleWithWhitespace);
        var response = await client.PostAsync($"/api/auth/switch-persona/{encoded}", null);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var authDto = await response.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
            authDto.Should().NotBeNull();
            authDto!.Role.Should().Be(expectedRole);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    // =========================================================================
    // FOCUS 2: AZURE DEVOPS SYNC WEBHOOK & ECHO LOOP PREVENTION
    // =========================================================================

    [Fact]
    public async Task WebhookAdo_MalformedJsonPayload_SafelyRejected_WithZeroDbMutations()
    {
        var client = _fixture.CreateClient();
        var content = new StringContent("{ \"eventType\": \"workitem.updated\", \"unclosedJson", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/webhooks/ado", content);

        // Webhook handler returns either 400 Bad Request or 200 OK with Status = "Error" to prevent ADO service hook disabling
        (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.OK).Should().BeTrue();
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<WebhookProcessingResultDto>(CrmApiTestFixture.JsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("Error");
            result.IsEcho.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WebhookAdo_EmptyPayloads_Returns400BadRequest(string payload)
    {
        var client = _fixture.CreateClient();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/webhooks/ado", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WebhookAdo_EmptyJsonPayload_ReturnsHandledErrorDto()
    {
        var client = _fixture.CreateClient();
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/webhooks/ado", content);

        (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.OK).Should().BeTrue();
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<WebhookProcessingResultDto>(CrmApiTestFixture.JsonOptions);
            result.Should().NotBeNull();
            result!.Status.Should().Be("Error");
        }
    }

    [Fact]
    public async Task WebhookAdo_EchoPayload_IdenticalHash_ReturnsIgnoredEcho_AndZeroMutations()
    {
        var client = _fixture.CreateClient();

        Guid workItemId;
        string contentHash;
        string empEmail;
        DateTimeOffset originalUpdatedAt;
        DateTimeOffset originalSyncedAt;
        int originalHistoryCount;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var emp = await db.Employees.FirstAsync();
            empEmail = emp.Email;

            contentHash = SyncContentHasher.ComputeHash(
                "Adversarial Echo Test Work Item",
                "Testing strict echo loop suppression",
                WorkItemStatus.InProgress,
                WorkItemType.Bug,
                empEmail);

            originalUpdatedAt = DateTimeOffset.UtcNow.AddHours(-2);
            originalSyncedAt = DateTimeOffset.UtcNow.AddHours(-2);

            var item = new WorkItem
            {
                ProjectId = project.Id,
                CreatedById = emp.Id,
                Code = "CRM-ECHO-ADV",
                Title = "Adversarial Echo Test Work Item",
                Description = "Testing strict echo loop suppression",
                Status = WorkItemStatus.InProgress,
                Type = WorkItemType.Bug,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "987654",
                ExternalUrl = "https://dev.azure.com/org/p/_workitems/edit/987654",
                LastSyncedContentHash = contentHash,
                LastSyncOrigin = SyncOrigin.LocalCrm,
                LastSyncedAt = originalSyncedAt,
                UpdatedAt = originalUpdatedAt
            };

            db.WorkItems.Add(item);
            db.WorkItemHistories.Add(new WorkItemHistory
            {
                WorkItemId = item.Id,
                ChangedById = emp.Id,
                Action = "Created",
                Note = "Initial seed for echo test",
                ChangedAt = originalUpdatedAt
            });

            await db.SaveChangesAsync();
            workItemId = item.Id;

            originalHistoryCount = await db.WorkItemHistories.CountAsync(h => h.WorkItemId == workItemId);
        }

        var echoPayload = $$"""
        {
            "subscriptionId": "sub-adv-1",
            "eventType": "workitem.updated",
            "resource": {
                "id": 987654,
                "url": "https://dev.azure.com/org/p/_workitems/edit/987654",
                "fields": {
                    "System.Title": "Adversarial Echo Test Work Item",
                    "System.Description": "Testing strict echo loop suppression",
                    "System.State": "In Progress",
                    "System.WorkItemType": "Bug",
                    "System.AssignedTo": "{{empEmail}}"
                }
            }
        }
        """;

        var content = new StringContent(echoPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/ado", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WebhookProcessingResultDto>(CrmApiTestFixture.JsonOptions);
        result.Should().NotBeNull();
        result!.IsEcho.Should().BeTrue("Incoming hash matches existing content hash");
        result.Status.Should().Be("IgnoredEcho");
        result.WorkItemId.Should().Be(workItemId);

        // Verify zero mutations occurred
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var item = await db.WorkItems.FindAsync(workItemId);
            item.Should().NotBeNull();
            item!.UpdatedAt.Should().Be(originalUpdatedAt, "UpdatedAt must not change on ignored echo");
            item.LastSyncedAt.Should().Be(originalSyncedAt, "LastSyncedAt must not change on ignored echo");
            item.LastSyncedContentHash.Should().Be(contentHash);

            var historyCount = await db.WorkItemHistories.CountAsync(h => h.WorkItemId == workItemId);
            historyCount.Should().Be(originalHistoryCount, "No duplicate history records may be written on ignored echo");
        }
    }

    [Fact]
    public async Task WebhookAdo_EchoPayloadWithWhitespaceDifferences_IsCanonicalizedAndIgnored()
    {
        var client = _fixture.CreateClient();

        Guid workItemId;
        string empEmail;

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var emp = await db.Employees.FirstAsync();
            empEmail = emp.Email;

            var contentHash = SyncContentHasher.ComputeHash(
                "Whitespace Normalized Item",
                "Description with normalized spacing",
                WorkItemStatus.Todo,
                WorkItemType.Task,
                empEmail);

            var item = new WorkItem
            {
                ProjectId = project.Id,
                CreatedById = emp.Id,
                Code = "CRM-WS-1",
                Title = "Whitespace Normalized Item",
                Description = "Description with normalized spacing",
                Status = WorkItemStatus.Todo,
                Type = WorkItemType.Task,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "987655",
                LastSyncedContentHash = contentHash,
                LastSyncOrigin = SyncOrigin.LocalCrm,
                LastSyncedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            db.WorkItems.Add(item);
            await db.SaveChangesAsync();
            workItemId = item.Id;
        }

        // Webhook payload with leading/trailing spaces and \r\n vs \n
        var wsPayload = $$"""
        {
            "subscriptionId": "sub-ws-1",
            "eventType": "workitem.updated",
            "resource": {
                "id": 987655,
                "fields": {
                    "System.Title": "  Whitespace Normalized Item  ",
                    "System.Description": "Description with normalized spacing\r\n",
                    "System.State": "To Do",
                    "System.WorkItemType": "Task",
                    "System.AssignedTo": "  {{empEmail}}  "
                }
            }
        }
        """;

        var content = new StringContent(wsPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/webhooks/ado", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WebhookProcessingResultDto>(CrmApiTestFixture.JsonOptions);
        result.Should().NotBeNull();
        result!.IsEcho.Should().BeTrue("Whitespace differences must be trimmed/normalized in canonical content hash");
        result.Status.Should().Be("IgnoredEcho");
    }

    [Fact]
    public async Task ConflictResolution_DuplicateResolution_Returns400BadRequest()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");

        Guid conflictId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();

            var conflict = new SyncConflict
            {
                ProjectId = project.Id,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "ADO-DUP-1",
                ExternalTitle = "Item to resolve twice",
                SimilarityScore = 0.9,
                ConflictType = SyncConflictType.DuplicateCandidate,
                Status = ConflictStatus.Pending,
                ResolutionAction = ConflictResolutionAction.Unresolved,
                DetectedAt = DateTimeOffset.UtcNow
            };

            db.SyncConflicts.Add(conflict);
            await db.SaveChangesAsync();
            conflictId = conflict.Id;
        }

        // First resolution: Skip (succeeds)
        var firstResp = await client.PostAsJsonAsync($"/api/sync/conflicts/{conflictId}/resolve", new ResolveConflictRequestDto(
            ConflictResolutionAction.Skip,
            "First resolution"
        ));
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second resolution attempt: Merge (must fail)
        var secondResp = await client.PostAsJsonAsync($"/api/sync/conflicts/{conflictId}/resolve", new ResolveConflictRequestDto(
            ConflictResolutionAction.Merge,
            "Attempt second resolution"
        ));
        secondResp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Resolving an already-resolved conflict must return 400 Bad Request");
    }

    [Fact]
    public async Task ConflictResolution_InvalidAction_Unresolved_Returns400BadRequest()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");
        var response = await client.PostAsJsonAsync($"/api/sync/conflicts/{Guid.NewGuid()}/resolve", new ResolveConflictRequestDto(
            ConflictResolutionAction.Unresolved
        ));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Action Unresolved must be rejected");
    }

    [Fact]
    public async Task ConflictResolution_NonExistentConflictId_Returns400BadRequestOr404()
    {
        var client = _fixture.CreateClientWithRole("TeamLead");
        var nonExistentId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/sync/conflicts/{nonExistentId}/resolve", new ResolveConflictRequestDto(
            ConflictResolutionAction.Skip,
            "Resolving non-existent"
        ));

        (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue("Non-existent conflict ID must return 400 Bad Request or 404 Not Found");
    }

    // =========================================================================
    // FOCUS 3: CONCURRENT TIMER START/STOP STRESS TESTING
    // =========================================================================

    [Fact]
    public async Task ConcurrentTimer_StartingItemB_AutoFinalizesItemA_WithoutCorruption()
    {
        Guid projectId;
        Guid employeeId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var emp = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
            projectId = project.Id;
            employeeId = emp.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "timer.challenger@companycrm.local", "Timer Challenger", AppRole.Employee);

        // 1. Create Work Item A and Work Item B
        var itemAResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Work Item A for Timer Test"));
        itemAResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var itemA = await itemAResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var itemBResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Work Item B for Timer Test"));
        itemBResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var itemB = await itemBResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // 2. Start timer on Item A (Billable)
        var startAResp = await client.PostAsJsonAsync($"/api/workitems/{itemA!.Id}/timer/start", new StartTimerRequestDto(true));
        startAResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var entryA = await startAResp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        entryA.Should().NotBeNull();
        entryA!.IsRunning.Should().BeTrue();
        entryA.WorkItemId.Should().Be(itemA.Id);

        // 3. Verify active timer endpoint returns Item A
        var active1 = await client.GetAsync("/api/workitems/active-timer");
        active1.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeEntry1 = await active1.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        activeEntry1!.WorkItemId.Should().Be(itemA.Id);

        // 4. Start timer on Item B (Non-Billable) -> must auto-finalize Item A
        var startBResp = await client.PostAsJsonAsync($"/api/workitems/{itemB!.Id}/timer/start", new StartTimerRequestDto(false));
        startBResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var entryB = await startBResp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        entryB.Should().NotBeNull();
        entryB!.IsRunning.Should().BeTrue();
        entryB.WorkItemId.Should().Be(itemB.Id);
        entryB.IsBillable.Should().BeFalse();

        // 5. Verify Item A's timer in database: finalized, not running, DurationMinutes >= 1
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var savedA = await db.TimeEntries.FindAsync(entryA.Id);
            savedA.Should().NotBeNull();
            savedA!.IsRunning.Should().BeFalse("Item A timer must be stopped");
            savedA.EndedAt.Should().NotBeNull("Item A timer must have EndedAt set");
            savedA.DurationMinutes.Should().BeGreaterThanOrEqualTo(1, "Duration minutes must be at least 1 minute");

            // Verify Item A history contains TimerStopped with Auto-finalized note
            var historiesA = await db.WorkItemHistories.Where(h => h.WorkItemId == itemA.Id).ToListAsync();
            historiesA.Should().Contain(h => h.Action == "TimerStopped" && h.Note!.Contains("Auto-finalized"));
        }

        // 6. Verify active timer endpoint now returns Item B
        var active2 = await client.GetAsync("/api/workitems/active-timer");
        active2.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeEntry2 = await active2.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        activeEntry2!.WorkItemId.Should().Be(itemB.Id);
        activeEntry2.IsRunning.Should().BeTrue();

        // 7. Stop timer on Item B
        var stopBResp = await client.PostAsJsonAsync($"/api/workitems/{itemB.Id}/timer/stop", new StopTimerRequestDto("Finished B"));
        stopBResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var stoppedB = await stopBResp.Content.ReadFromJsonAsync<TimeEntryDto>(CrmApiTestFixture.JsonOptions);
        stoppedB!.IsRunning.Should().BeFalse();

        // 8. Verify active timer endpoint now returns 204 NoContent
        var active3 = await client.GetAsync("/api/workitems/active-timer");
        (active3.StatusCode == HttpStatusCode.NoContent || active3.StatusCode == HttpStatusCode.OK).Should().BeTrue();
        if (active3.StatusCode == HttpStatusCode.OK)
        {
            var body = await active3.Content.ReadAsStringAsync();
            body.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ConcurrentTimer_RapidAlternatingStarts_PreservesExclusivityAndAuditTrail()
    {
        Guid projectId;
        Guid employeeId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var emp = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
            projectId = project.Id;
            employeeId = emp.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "rapid.timer@companycrm.local", "Rapid Timer", AppRole.Employee);

        // Create 3 items
        var item1Resp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Rapid Item 1"));
        var item1 = await item1Resp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var item2Resp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Rapid Item 2"));
        var item2 = await item2Resp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var item3Resp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Rapid Item 3"));
        var item3 = await item3Resp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var workItemSequence = new[] { item1!.Id, item2!.Id, item3!.Id, item1.Id, item2.Id, item3.Id };

        foreach (var itemId in workItemSequence)
        {
            var startResp = await client.PostAsJsonAsync($"/api/workitems/{itemId}/timer/start", new StartTimerRequestDto(true));
            startResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Invariant: Exactly ONE timer is running in DB for this employee
            using var scope = _fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var runningTimers = await db.TimeEntries.Where(t => t.EmployeeId == employeeId && t.IsRunning).ToListAsync();
            runningTimers.Should().HaveCount(1, "Exactly one timer must be running at any given time for an employee");
            runningTimers[0].WorkItemId.Should().Be(itemId);
        }

        // Stop the final running timer
        var stopResp = await client.PostAsJsonAsync($"/api/workitems/{item3!.Id}/timer/stop", new StopTimerRequestDto("Finished sequence"));
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify zero running timers remain
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var remainingRunning = await db.TimeEntries.Where(t => t.EmployeeId == employeeId && t.IsRunning).ToListAsync();
            remainingRunning.Should().BeEmpty("All timers must be stopped");

            var sessionEntries = await db.TimeEntries.Where(t => workItemSequence.Contains(t.WorkItemId)).ToListAsync();
            sessionEntries.Should().HaveCount(6, "All 6 timer sessions for the test items must be persisted");
            sessionEntries.Should().OnlyContain(e => !e.IsRunning && e.EndedAt != null && e.DurationMinutes >= 1);
        }
    }

    [Fact]
    public async Task ConcurrentTimer_StartOnDeniedWorkItem_Returns400BadRequest_DoesNotAffectActiveTimer()
    {
        Guid projectId;
        Guid employeeId;
        Guid leadId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var project = await db.Projects.FirstAsync();
            var emp = await db.Employees.FirstAsync(e => e.Role == AppRole.Employee);
            var lead = await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead);
            projectId = project.Id;
            employeeId = emp.Id;
            leadId = lead.Id;
        }

        var client = _fixture.CreateClientWithEmployee(employeeId, "denied.timer@companycrm.local", "Denied Timer", AppRole.Employee);

        // 1. Create valid Item A and start timer on it
        var itemAResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projectId, "Active Task A"));
        var itemA = await itemAResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var startA = await client.PostAsJsonAsync($"/api/workitems/{itemA!.Id}/timer/start", new StartTimerRequestDto(true));
        startA.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Create Item B and have it denied
        Guid itemBId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var itemB = new WorkItem
            {
                ProjectId = projectId,
                CreatedById = employeeId,
                Code = "CRM-DENIED-1",
                Title = "Denied Work Item",
                Status = WorkItemStatus.Denied,
                ReviewStatus = ReviewStatus.Denied,
                Type = WorkItemType.Task
            };
            db.WorkItems.Add(itemB);
            await db.SaveChangesAsync();
            itemBId = itemB.Id;
        }

        // 3. Attempt to start timer on denied Item B -> must return 400 Bad Request
        var startDeniedResp = await client.PostAsJsonAsync($"/api/workitems/{itemBId}/timer/start", new StartTimerRequestDto(true));
        startDeniedResp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Starting timer on denied work item must be blocked");

        // 4. Verify Item A's timer is STILL running and was NOT auto-finalized by the rejected start
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var activeTimers = await db.TimeEntries.Where(t => t.EmployeeId == employeeId && t.IsRunning).ToListAsync();
            activeTimers.Should().HaveCount(1);
            activeTimers[0].WorkItemId.Should().Be(itemA.Id, "Item A's timer must remain running despite failed start on denied item");
        }

        // Clean up
        await client.PostAsJsonAsync($"/api/workitems/{itemA.Id}/timer/stop", new StopTimerRequestDto("Cleanup"));
    }
}
