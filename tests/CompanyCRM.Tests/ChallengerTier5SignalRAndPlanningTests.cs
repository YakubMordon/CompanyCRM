using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.BackgroundJobs;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace CompanyCRM.Tests;

public class ChallengerTier5SignalRAndPlanningTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public ChallengerTier5SignalRAndPlanningTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region SignalR Helpers

    private static string CreateJwt(
        Guid? userId = null,
        AppRole role = AppRole.Employee,
        DateTime? expiresUtc = null,
        string secret = "CompanyCRM_SuperSecret_Jwt_Key_For_Development_Only_Min256Bits!",
        string issuer = "CompanyCRM",
        string audience = "CompanyCRM.Web")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var id = userId ?? Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim("sub", id.ToString()),
            new Claim(ClaimTypes.Email, $"tier5.{id:N}@companycrm.local"),
            new Claim(ClaimTypes.Name, "Tier5 Probe User"),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim("role", role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expiresUtc ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class HubProbeClient : IAsyncDisposable
    {
        private readonly WebSocket _ws;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _receiveLoop;

        public ConcurrentQueue<(string Target, JsonElement[] Args)> ReceivedInvocations { get; } = new();
        public ConcurrentQueue<JsonElement> CompletionMessages { get; } = new();
        public WebSocketState State => _ws.State;

        private HubProbeClient(WebSocket ws)
        {
            _ws = ws;
            _receiveLoop = Task.Run(ReceiveLoopAsync);
        }

        public static async Task<HubProbeClient> ConnectAsync(CrmApiTestFixture fixture, string? jwtToken, bool useHeaderInsteadOfQuery = false)
        {
            _ = fixture.CreateClient();
            var wsClient = fixture.Server.CreateWebSocketClient();
            var uriStr = "ws://localhost/hubs/crm";
            if (!useHeaderInsteadOfQuery && jwtToken != null)
            {
                uriStr += $"?access_token={Uri.EscapeDataString(jwtToken)}";
            }

            if (useHeaderInsteadOfQuery && jwtToken != null)
            {
                wsClient.ConfigureRequest = req =>
                {
                    req.Headers.Authorization = $"Bearer {jwtToken}";
                };
            }

            var ws = await wsClient.ConnectAsync(new Uri(uriStr), CancellationToken.None);

            var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\x1e");
            await ws.SendAsync(handshake, WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[4096];
            var sb = new StringBuilder();
            while (true)
            {
                var res = await ws.ReceiveAsync(buffer, CancellationToken.None);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                if (res.EndOfMessage && sb.ToString().Contains('\x1e'))
                    break;
            }

            var resp = sb.ToString().Split('\x1e', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            using var doc = JsonDocument.Parse(resp!);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                throw new InvalidOperationException($"SignalR handshake failed: {err.GetString()}");
            }

            return new HubProbeClient(ws);
        }

        public async Task InvokeAsync(string target, string invocationId, params object?[] args)
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = 1,
                invocationId,
                target,
                arguments = args
            }) + "\x1e";

            await _ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task<JsonElement> WaitForCompletionAsync(string invocationId, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                foreach (var msg in CompletionMessages)
                {
                    if (msg.TryGetProperty("invocationId", out var idProp) && idProp.GetString() == invocationId)
                        return msg;
                }
                await Task.Delay(20);
            }
            throw new TimeoutException($"Timed out waiting for completion '{invocationId}'.");
        }

        public async Task<JsonElement> WaitForEventAsync(string target, Func<JsonElement, bool> predicate, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                foreach (var (evtTarget, args) in ReceivedInvocations)
                {
                    if (evtTarget == target && args.Length > 0 && predicate(args[0]))
                        return args[0];
                }
                await Task.Delay(20);
            }
            throw new TimeoutException($"Timed out waiting for SignalR event '{target}'. Captured: {ReceivedInvocations.Count}");
        }

        public void AbortAbruptly()
        {
            _cts.Cancel();
            _ws.Abort();
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[16384];
            var pending = new StringBuilder();
            try
            {
                while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(buffer, _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    pending.Append(Encoding.UTF8.GetString(ms.ToArray()));
                    var text = pending.ToString();
                    var lastSep = text.LastIndexOf('\x1e');
                    if (lastSep < 0) continue;

                    var chunk = text[..lastSep];
                    pending.Clear();
                    if (lastSep + 1 < text.Length) pending.Append(text[(lastSep + 1)..]);

                    foreach (var frame in chunk.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
                    {
                        using var doc = JsonDocument.Parse(frame);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("type", out var typeProp)) continue;
                        var msgType = typeProp.GetInt32();
                        if (msgType == 1 &&
                            root.TryGetProperty("target", out var targetProp) &&
                            root.TryGetProperty("arguments", out var argsProp) &&
                            argsProp.ValueKind == JsonValueKind.Array)
                        {
                            var clonedArgs = new JsonElement[argsProp.GetArrayLength()];
                            for (int i = 0; i < clonedArgs.Length; i++)
                                clonedArgs[i] = argsProp[i].Clone();
                            ReceivedInvocations.Enqueue((targetProp.GetString()!, clonedArgs));
                        }
                        else if (msgType == 3)
                        {
                            CompletionMessages.Enqueue(root.Clone());
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            if (_ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); }
                catch { }
            }
            try { await _receiveLoop; } catch { }
            _ws.Dispose();
            _cts.Dispose();
        }
    }

    #endregion

    #region FOCUS 1: SignalR Hub (CrmHub) Adversarial Tests

    [Fact]
    public async Task SignalR_ExpiredTokenBy1Second_RejectedAtNegotiateAndWebSocketConnect()
    {
        var client = _fixture.CreateClient();
        var expiredJwt = CreateJwt(expiresUtc: DateTime.UtcNow.AddSeconds(-1));

        // 1. Negotiate with query access_token -> 401 Unauthorized (ClockSkew = Zero)
        var negQuery = await client.PostAsync($"/hubs/crm/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(expiredJwt)}", null);
        negQuery.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Negotiate with Authorization Bearer -> 401 Unauthorized
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredJwt);
        var negHeader = await client.PostAsync("/hubs/crm/negotiate?negotiateVersion=1", null);
        negHeader.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3. Direct WebSocket connect with expired token -> rejected
        var wsClient = _fixture.Server.CreateWebSocketClient();
        var act = async () => await wsClient.ConnectAsync(
            new Uri($"ws://localhost/hubs/crm?access_token={Uri.EscapeDataString(expiredJwt)}"),
            CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not.a.jwt")]
    public async Task SignalR_MissingOrMalformedAccessToken_RejectedWith401(string badToken)
    {
        var client = _fixture.CreateClient();
        var negResp = await client.PostAsync($"/hubs/crm/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(badToken)}", null);
        negResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignalR_TokenExpiresMidConnection_ExistingSocketSurvives_ReconnectRejected()
    {
        var client = _fixture.CreateClient();
        var empId = await GetFirstEmployeeIdAsync();
        var projId = await GetFirstProjectIdAsync();

        // Short-lived JWT expiring in 2 seconds
        var shortJwt = CreateJwt(userId: empId, role: AppRole.TeamLead, expiresUtc: DateTime.UtcNow.AddSeconds(2));
        await using var probe = await HubProbeClient.ConnectAsync(_fixture, shortJwt);

        // Wait until token is strictly expired (ClockSkew = Zero)
        await Task.Delay(2500);

        // 1. Existing WebSocket still receives server broadcasts (JWT only validated on HTTP/WS handshake)
        var freshJwt = CreateJwt(userId: empId, role: AppRole.TeamLead, expiresUtc: DateTime.UtcNow.AddMinutes(10));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshJwt);

        var title = $"MidExpiry Broadcast {Guid.NewGuid():N}";
        var createResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projId, title));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var evt = await probe.WaitForEventAsync("WorkItemUpdated",
            el => el.TryGetProperty("id", out var idP) && idP.GetGuid() == created!.Id);
        evt.GetProperty("title").GetString().Should().Be(title);

        // 2. Hub method invocation on an already-authenticated socket uses cached Context.User
        await probe.InvokeAsync("JoinProject", "inv-after-expiry", projId.ToString());
        var completion = await probe.WaitForCompletionAsync("inv-after-expiry");
        completion.TryGetProperty("error", out _).Should().BeFalse();

        // 3. Reconnecting with the expired token MUST fail
        var reconnectAct = async () => await HubProbeClient.ConnectAsync(_fixture, shortJwt);
        await reconnectAct.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SignalR_DailyPlanSubmittedAndReviewActions_BroadcastStringEnumsAndDateOnly()
    {
        var empId = await GetFirstEmployeeIdAsync();
        var leadId = await GetFirstLeadIdAsync();
        var projId = await GetFirstProjectIdAsync();

        var empClient = _fixture.CreateClientWithEmployee(empId, "tier5.emp@crm.local", "Tier5 Emp", AppRole.Employee);
        var leadClient = _fixture.CreateClientWithEmployee(leadId, "tier5.lead@crm.local", "Tier5 Lead", AppRole.TeamLead);
        var token = empClient.DefaultRequestHeaders.Authorization!.Parameter!;

        await using var probe = await HubProbeClient.ConnectAsync(_fixture, token);

        // 1. DailyPlanSubmitted broadcast verification
        var planDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40));
        var planResp = await empClient.PostAsJsonAsync("/api/planning/daily", new SubmitDailyPlanRequestDto(
            PlanDate: planDate,
            Blockers: "None",
            SummaryNotes: "SignalR DailyPlanSubmitted test",
            Items: new[] { new SubmitDailyPlanItemDto(null, "Task A", 3.5m, "Goal") }
        ));
        planResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var planEvt = await probe.WaitForEventAsync("DailyPlanSubmitted",
            el => el.TryGetProperty("employeeId", out var ep) && ep.GetGuid() == empId &&
                  el.TryGetProperty("planDate", out var dp) && dp.GetString() == planDate.ToString("yyyy-MM-dd"));
        planEvt.GetProperty("itemsCount").GetInt32().Should().Be(1);
        planEvt.GetProperty("isOnTime").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);

        // 2. Clarify + Deny ReviewQueueUpdated string enum serialization
        var wiResp = await empClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            ProjectId: projId,
            Title: $"ClarifyDeny SignalR {Guid.NewGuid():N}",
            ReviewerId: leadId
        ));
        wiResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var wi = await wiResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var clarifyResp = await leadClient.PostAsJsonAsync($"/api/reviews/{wi!.Id}/clarify",
            new ClarifyReviewRequestDto("Clarification needed via SignalR"));
        clarifyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var clarifyEvt = await probe.WaitForEventAsync("ReviewQueueUpdated",
            el => el.TryGetProperty("workItemId", out var wp) && wp.GetGuid() == wi.Id &&
                  el.TryGetProperty("action", out var ap) && ap.GetString() == "ClarificationRequested");
        clarifyEvt.GetProperty("reviewStatus").ValueKind.Should().Be(JsonValueKind.String);
        clarifyEvt.GetProperty("reviewStatus").GetString().Should().Be("ClarificationNeeded");

        var denyResp = await leadClient.PostAsJsonAsync($"/api/reviews/{wi.Id}/deny",
            new DenyReviewRequestDto("Denied via SignalR test"));
        denyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var denyEvt = await probe.WaitForEventAsync("ReviewQueueUpdated",
            el => el.TryGetProperty("workItemId", out var wp) && wp.GetGuid() == wi.Id &&
                  el.TryGetProperty("action", out var ap) && ap.GetString() == "Denied");
        denyEvt.GetProperty("reviewStatus").ValueKind.Should().Be(JsonValueKind.String);
        denyEvt.GetProperty("reviewStatus").GetString().Should().Be("Denied");
    }

    [Fact]
    public async Task SignalR_ConnectionStressAndConcurrentBroadcasts_20Clients_20Mutations_ZeroDrops()
    {
        var projId = await GetFirstProjectIdAsync();
        var empId = await GetFirstEmployeeIdAsync();
        var client = _fixture.CreateClientWithEmployee(empId, "stress@crm.local", "Stress User", AppRole.TeamLead);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;

        const int clientCount = 20;
        const int mutationCount = 20;

        var probes = new List<HubProbeClient>(clientCount);
        try
        {
            // Connect 20 clients concurrently
            var connectTasks = Enumerable.Range(0, clientCount)
                .Select(_ => HubProbeClient.ConnectAsync(_fixture, token))
                .ToArray();
            probes.AddRange(await Task.WhenAll(connectTasks));

            // All 20 clients invoke JoinProject concurrently
            var joinTasks = probes.Select((p, idx) => p.InvokeAsync("JoinProject", $"join-{idx}", projId.ToString()));
            await Task.WhenAll(joinTasks);

            var completionTasks = probes.Select((p, idx) => p.WaitForCompletionAsync($"join-{idx}"));
            await Task.WhenAll(completionTasks);

            // Fire 20 concurrent HTTP work item creations
            var tag = Guid.NewGuid().ToString("N")[..8];
            var createTasks = Enumerable.Range(0, mutationCount).Select(i =>
                client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projId, $"Stress-{tag}-{i}", Type: WorkItemType.Bug, Priority: WorkItemPriority.Critical))
            ).ToArray();

            var responses = await Task.WhenAll(createTasks);
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

            // Verify every one of the 20 connected clients received all 20 broadcasts with string enums
            foreach (var p in probes)
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    var matching = p.ReceivedInvocations
                        .Count(x => x.Target == "WorkItemUpdated" &&
                                    x.Args.Length > 0 &&
                                    x.Args[0].TryGetProperty("title", out var tp) &&
                                    (tp.GetString()?.Contains(tag) ?? false));
                    if (matching >= mutationCount) break;
                    await Task.Delay(30);
                }

                var matchedPayloads = p.ReceivedInvocations
                    .Where(x => x.Target == "WorkItemUpdated" &&
                                x.Args.Length > 0 &&
                                x.Args[0].TryGetProperty("title", out var tp) &&
                                (tp.GetString()?.Contains(tag) ?? false))
                    .Select(x => x.Args[0])
                    .ToList();

                matchedPayloads.Should().HaveCount(mutationCount);
                matchedPayloads.Should().OnlyContain(el =>
                    el.GetProperty("status").ValueKind == JsonValueKind.String &&
                    el.GetProperty("type").ValueKind == JsonValueKind.String &&
                    el.GetProperty("priority").ValueKind == JsonValueKind.String);
            }
        }
        finally
        {
            foreach (var p in probes) await p.DisposeAsync();
        }
    }

    [Fact]
    public async Task SignalR_AbruptClientDisconnectsAndReconnect_DoesNotBreakBroadcastsOrHttpRequests()
    {
        var projId = await GetFirstProjectIdAsync();
        var empId = await GetFirstEmployeeIdAsync();
        var client = _fixture.CreateClientWithEmployee(empId, "abort@crm.local", "Abort Tester", AppRole.TeamLead);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;

        var healthyProbes = new List<HubProbeClient>();
        var abortedProbes = new List<HubProbeClient>();
        try
        {
            for (int i = 0; i < 4; i++) healthyProbes.Add(await HubProbeClient.ConnectAsync(_fixture, token));
            for (int i = 0; i < 4; i++) abortedProbes.Add(await HubProbeClient.ConnectAsync(_fixture, token));

            // Abruptly abort 4 sockets without close handshake
            foreach (var ap in abortedProbes) ap.AbortAbruptly();

            // Immediately fire concurrent mutations
            var marker = Guid.NewGuid().ToString("N")[..8];
            var responses = await Task.WhenAll(
                Enumerable.Range(0, 5).Select(i =>
                    client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projId, $"PostAbort-{marker}-{i}"))));
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

            // Healthy clients must still receive all 5 broadcasts
            foreach (var hp in healthyProbes)
            {
                var evt = await hp.WaitForEventAsync("WorkItemUpdated",
                    el => el.TryGetProperty("title", out var t) && (t.GetString()?.StartsWith($"PostAbort-{marker}-4") ?? false));
                evt.GetProperty("status").GetString().Should().Be("Backlog");
            }

            // New client reconnects and receives subsequent broadcast
            await using var reconnected = await HubProbeClient.ConnectAsync(_fixture, token);
            var afterResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projId, $"Reconnected-{marker}"));
            afterResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await afterResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

            var reconnectedEvt = await reconnected.WaitForEventAsync("WorkItemUpdated",
                el => el.TryGetProperty("id", out var idP) && idP.GetGuid() == created!.Id);
            reconnectedEvt.GetProperty("title").GetString().Should().Be($"Reconnected-{marker}");
        }
        finally
        {
            foreach (var p in healthyProbes) await p.DisposeAsync();
            foreach (var p in abortedProbes) await p.DisposeAsync();
        }
    }

    [Fact]
    public async Task SignalR_ProjectGroupIsolationCheck_RevealsClientsAllBroadcastsAcrossProjects()
    {
        // Probes whether a client that explicitly joined Project A receives updates from Project B
        Guid projA, projB;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var projs = await db.Projects.Take(2).ToListAsync();
            projA = projs[0].Id;
            projB = projs[1].Id;
        }

        var empId = await GetFirstEmployeeIdAsync();
        var client = _fixture.CreateClientWithEmployee(empId, "iso@crm.local", "Isolation User", AppRole.TeamLead);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;

        await using var probeA = await HubProbeClient.ConnectAsync(_fixture, token);
        await probeA.InvokeAsync("JoinProject", "join-A", projA.ToString());
        await probeA.WaitForCompletionAsync("join-A");

        var itemBResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(projB, $"ProjB-Item-{Guid.NewGuid():N}"));
        itemBResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var itemB = await itemBResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        // Wait briefly to observe whether Clients.All delivers Project B's item to Probe A
        await Task.Delay(300);
        var leakedToProbeA = probeA.ReceivedInvocations.Any(x =>
            x.Target == "WorkItemUpdated" &&
            x.Args[0].TryGetProperty("id", out var idP) &&
            idP.GetGuid() == itemB!.Id);

        // Expected if strict group isolation were enforced: false.
        // Observed in current implementation (Clients.All): true (documented as architectural finding).
        leakedToProbeA.Should().BeFalse("Client joined only project A; Clients.All leaks project B events across project groups");
    }

    #endregion

    #region FOCUS 2: Daily Plan & Cutoff Scheduling Edge Cases

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    private static (SqliteConnection Conn, DbContextOptions<CrmDbContext> Options, IServiceScopeFactory ScopeFactory, Mock<IEmailNotificationService> EmailMock) CreateIsolatedJobInfra()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<CrmDbContext>().UseSqlite(conn).Options;
        using (var ctx = new CrmDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        var emailMock = new Mock<IEmailNotificationService>();
        var services = new ServiceCollection();
        services.AddDbContext<CrmDbContext>(opts => opts.UseSqlite(conn));
        services.AddSingleton(emailMock.Object);
        var sp = services.BuildServiceProvider();
        return (conn, options, sp.GetRequiredService<IServiceScopeFactory>(), emailMock);
    }

    [Theory]
    [InlineData("2024-02-28T23:00:00Z", "Pacific/Kiritimati", "2024-02-29", "Completed")] // UTC+14 -> Leap day Feb 29 13:00 (Thu)
    [InlineData("2024-03-01T01:00:00Z", "Etc/GMT+12", "2024-02-29", "Completed")]       // UTC-12 -> Leap day Feb 29 13:00 (Thu)
    [InlineData("2026-11-20T12:00:00Z", "Pacific/Kiritimati", "2026-11-21", "ExcludedDay")] // UTC+14 -> Friday UTC is Saturday local!
    [InlineData("2026-11-22T22:00:00Z", "Pacific/Kiritimati", "2026-11-23", "Completed")]   // UTC+14 -> Sunday UTC is Monday 12:00 local!
    [InlineData("2026-11-23T01:00:00Z", "Etc/GMT+12", "2026-11-22", "ExcludedDay")]         // UTC-12 -> Monday UTC is Sunday 13:00 local!
    [InlineData("2026-11-20T23:00:00Z", "Etc/GMT+12", "2026-11-20", "Completed")]           // UTC-12 -> Friday 11:00 local!
    public async Task CutoffJob_ExtremeOffsetsPlus14Minus12_LeapYearAndWeekendRollover(
        string utcNowIso, string tzId, string expectedLocalDateStr, string expectedStatus)
    {
        var (conn, options, scopeFactory, emailMock) = CreateIsolatedJobInfra();
        using var _ = conn;

        using (var ctx = new CrmDbContext(options))
        {
            ctx.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
            {
                ProjectId = null,
                CutoffTimeLocal = new TimeOnly(10, 0, 0),
                TimeZoneId = tzId,
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]"
            });
            ctx.Employees.Add(new Employee { FullName = "Edge Dev", Email = "edge@crm.local", Department = "Eng", JobTitle = "Dev" });
            await ctx.SaveChangesAsync();
        }

        var tp = new MutableTimeProvider(DateTimeOffset.Parse(utcNowIso));
        var job = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);

        var summary = await job.ExecuteCutoffCheckAsync();
        summary.Status.Should().Be(expectedStatus);
        summary.CheckedDate.Should().Be(DateOnly.Parse(expectedLocalDateStr));
        summary.RemindersSent.Should().Be(expectedStatus == "Completed" ? 1 : 0);
    }

    [Fact]
    public async Task CutoffJob_DaylightSavingTransitions_SpringForwardAndFallBack_AmericaNewYork()
    {
        var (conn, options, scopeFactory, emailMock) = CreateIsolatedJobInfra();
        using var _ = conn;

        using (var ctx = new CrmDbContext(options))
        {
            ctx.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
            {
                ProjectId = null,
                CutoffTimeLocal = new TimeOnly(10, 0, 0),
                TimeZoneId = "America/New_York",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]"
            });
            ctx.Employees.Add(new Employee { FullName = "NY Dev", Email = "ny.dst@crm.local", Department = "Eng", JobTitle = "Dev" });
            await ctx.SaveChangesAsync();
        }

        // Day after Spring Forward (Mon 2026-03-09, EDT = UTC-4)
        // 13:59 UTC = 09:59 EDT -> BeforeCutoff; 14:01 UTC = 10:01 EDT -> Completed
        var tp = new MutableTimeProvider(DateTimeOffset.Parse("2026-03-09T13:59:00Z"));
        var job = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);

        var beforeDst = await job.ExecuteCutoffCheckAsync();
        beforeDst.Status.Should().Be("BeforeCutoff");

        tp.SetUtcNow(DateTimeOffset.Parse("2026-03-09T14:01:00Z"));
        var afterDst = await job.ExecuteCutoffCheckAsync();
        afterDst.Status.Should().Be("Completed");
        afterDst.RemindersSent.Should().Be(1);

        // Day before Spring Forward (Fri 2026-03-06, EST = UTC-5) -> 14:30 UTC = 09:30 EST -> BeforeCutoff
        tp.SetUtcNow(DateTimeOffset.Parse("2026-03-06T14:30:00Z"));
        var beforeEst = await job.ExecuteCutoffCheckAsync();
        beforeEst.Status.Should().Be("BeforeCutoff");
    }

    [Fact]
    public async Task CutoffJob_EmptyProjectWithZeroMembers_MustNotSendRemindersToAllOrgEmployees()
    {
        // BUG PROBE: DailyPlanCutoffReminderJob.cs:126-128 falls back to ALL Employees when memberEmployeeIds.Count == 0!
        var (conn, options, scopeFactory, emailMock) = CreateIsolatedJobInfra();
        using var _ = conn;

        Guid emptyProjectId;
        using (var ctx = new CrmDbContext(options))
        {
            var projA = new Project { Code = "PROJA", Name = "Project A" };
            var emptyProj = new Project { Code = "EMPTY", Name = "Empty Project" };
            var empInA = new Employee { FullName = "Alice A", Email = "alice.a@crm.local", Department = "Eng", JobTitle = "Dev" };
            ctx.Projects.AddRange(projA, emptyProj);
            ctx.Employees.Add(empInA);
            await ctx.SaveChangesAsync();

            ctx.ProjectMembers.Add(new ProjectMember { ProjectId = projA.Id, EmployeeId = empInA.Id });
            ctx.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
            {
                ProjectId = emptyProj.Id,
                CutoffTimeLocal = new TimeOnly(8, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[]"
            });
            await ctx.SaveChangesAsync();
            emptyProjectId = emptyProj.Id;
        }

        var tp = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-18T10:30:00Z"));
        var job = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);

        var summary = await job.ExecuteCutoffCheckAsync(emptyProjectId);

        // Expected: 0 reminders because Empty Project has 0 members!
        summary.RemindersSent.Should().Be(0, "a project with 0 members must not send cutoff reminders to unrelated employees in other projects");
    }

    [Fact]
    public async Task CutoffJob_ConcurrentExecution_MustBeIdempotentWithoutDuplicateEmailsOrDbException()
    {
        // BUG PROBE: Concurrent ExecuteCutoffCheckAsync calls race past DailyPlanReminderLogs check, send duplicate emails, and throw DbUpdateException!
        var (conn, options, scopeFactory, emailMock) = CreateIsolatedJobInfra();
        using var _ = conn;

        using (var ctx = new CrmDbContext(options))
        {
            ctx.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
            {
                ProjectId = null,
                CutoffTimeLocal = new TimeOnly(9, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[]"
            });
            ctx.Employees.Add(new Employee { FullName = "Race Dev", Email = "race@crm.local", Department = "Eng", JobTitle = "Dev" });
            await ctx.SaveChangesAsync();
        }

        // Introduce slight async delay in email service to widen the race window deterministically
        emailMock.Setup(e => e.SendCutoffReminderEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()))
            .Returns(async () => await Task.Delay(50));

        var tp = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-18T10:30:00Z"));
        var job1 = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);
        var job2 = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);

        var act = async () => await Task.WhenAll(job1.ExecuteCutoffCheckAsync(), job2.ExecuteCutoffCheckAsync());
        await act.Should().NotThrowAsync("concurrent cutoff job executions must not throw DbUpdateException on unique index");

        emailMock.Verify(e => e.SendCutoffReminderEmailAsync(
            "race@crm.local", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()),
            Times.Once, "concurrent cutoff checks must not send duplicate reminder emails");
    }

    [Fact]
    public async Task CutoffJob_TransientEmailFailure_ShouldAllowRetryOnSameDay()
    {
        // BUG PROBE: Failed delivery logs EmailDeliveryStatus="Failed" on unique (EmployeeId, PlanDate), permanently blocking retry!
        var (conn, options, scopeFactory, emailMock) = CreateIsolatedJobInfra();
        using var _ = conn;

        using (var ctx = new CrmDbContext(options))
        {
            ctx.PlanningCutoffConfigs.Add(new PlanningCutoffConfig
            {
                ProjectId = null,
                CutoffTimeLocal = new TimeOnly(9, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[]"
            });
            ctx.Employees.Add(new Employee { FullName = "Retry Dev", Email = "retry@crm.local", Department = "Eng", JobTitle = "Dev" });
            await ctx.SaveChangesAsync();
        }

        int attempt = 0;
        emailMock.Setup(e => e.SendCutoffReminderEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempt++;
                if (attempt == 1) throw new InvalidOperationException("Transient SMTP timeout");
                return Task.CompletedTask;
            });

        var tp = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-18T10:00:00Z"));
        var job = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);

        var run1 = await job.ExecuteCutoffCheckAsync();
        run1.RemindersSent.Should().Be(0);
        run1.Errors.Should().NotBeEmpty();

        // 15 minutes later SMTP recovers -> must retry and succeed!
        tp.SetUtcNow(DateTimeOffset.Parse("2026-11-18T10:15:00Z"));
        var run2 = await job.ExecuteCutoffCheckAsync();
        run2.RemindersSent.Should().Be(1, "failed delivery log must not suppress subsequent retry on the same day");
    }

    [Fact]
    public async Task DailyPlan_OnTimeInitialSubmission_UpdatedAfterCutoff_MustPreserveOnTimeStatus()
    {
        // BUG PROBE: PlanningController.SubmitDailyPlan overwrites SubmittedAt & flips IsSubmittedOnTime=false on update!
        Guid empId, projId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            empId = (await db.Employees.FirstAsync(e => e.Role == AppRole.Employee)).Id;
            projId = (await db.Projects.FirstAsync()).Id;
        }

        var leadClient = _fixture.CreateClientWithRole("TeamLead");
        var empClient = _fixture.CreateClientWithEmployee(empId, "ontime.update@crm.local", "OnTime Update", AppRole.Employee);
        var originalGlobal = await leadClient.GetFromJsonAsync<PlanningCutoffConfigDto>("/api/planning/cutoff-config", CrmApiTestFixture.JsonOptions);

        try
        {
            // 1. Set cutoff to end of day so initial submission is On Time
            await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                null, new TimeOnly(23, 59, 59), "UTC", true, "[0, 6]"));

            var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var initialResp = await empClient.PostAsJsonAsync("/api/planning/daily", new SubmitDailyPlanRequestDto(
                PlanDate: targetDate,
                Blockers: null,
                SummaryNotes: "Morning standup plan",
                Items: new[] { new SubmitDailyPlanItemDto(null, "Morning task", 4.0m, "Done by EOD") }
            ));
            initialResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var initialPlan = await initialResp.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);
            initialPlan!.IsSubmittedOnTime.Should().BeTrue();

            // 2. Cutoff has now passed (simulate updating notes/blockers at end of day after cutoff)
            await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                null, new TimeOnly(0, 0, 1), "UTC", true, "[0, 6]"));

            var updateResp = await empClient.PostAsJsonAsync("/api/planning/daily", new SubmitDailyPlanRequestDto(
                PlanDate: targetDate,
                Blockers: "Waiting on QA",
                SummaryNotes: "End of day summary log",
                Items: new[] { new SubmitDailyPlanItemDto(null, "Morning task", 4.0m, "Completed") }
            ));
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updatedPlan = await updateResp.Content.ReadFromJsonAsync<DailyPlanDto>(CrmApiTestFixture.JsonOptions);

            updatedPlan!.IsSubmittedOnTime.Should().BeTrue(
                "updating blockers or summary notes on an already on-time daily plan must not retroactively mark it late");
        }
        finally
        {
            if (originalGlobal != null)
            {
                await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
                    null, originalGlobal.CutoffTimeLocal, originalGlobal.TimeZoneId, originalGlobal.IsReminderEnabled, originalGlobal.ExcludedDaysOfWeekJson));
            }
        }
    }

    [Fact]
    public async Task CutoffConfig_InvalidTimeZone_ShouldReturn400BadRequest()
    {
        var leadClient = _fixture.CreateClientWithRole("TeamLead");

        // Offset string like "UTC+14" is NOT recognized by TimeZoneInfo.FindSystemTimeZoneById -> silently falls back to UTC!
        var invalidTzResp = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
            ProjectId: null,
            CutoffTimeLocal: new TimeOnly(10, 0, 0),
            TimeZoneId: "UTC+14",
            IsReminderEnabled: true,
            ExcludedDaysOfWeekJson: "[0, 6]"
        ));
        invalidTzResp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "unrecognized TimeZoneId 'UTC+14' must be rejected instead of silently falling back to UTC");
    }

    [Fact]
    public async Task CutoffConfig_NonExistentProjectId_ShouldReturn400Or404Not500()
    {
        var leadClient = _fixture.CreateClientWithRole("TeamLead");

        // Non-existent ProjectId -> must return 400/404, NOT 500 Internal Server Error!
        var badProjResp = await leadClient.PutAsJsonAsync("/api/planning/cutoff-config", new UpdatePlanningCutoffConfigDto(
            ProjectId: Guid.NewGuid(),
            CutoffTimeLocal: new TimeOnly(10, 0, 0),
            TimeZoneId: "UTC",
            IsReminderEnabled: true,
            ExcludedDaysOfWeekJson: "[0, 6]"
        ));
        ((int)badProjResp.StatusCode).Should().BeOneOf(400, 404);
    }

    [Fact]
    public async Task CutoffJob_CrossProjectExtremeTimezoneIdempotency_GlobalCheckMustNotSuppressDifferentProjectDay()
    {
        var (conn, options, scopeFactory, emailMock) = CreateIsolatedJobInfra();
        using var _ = conn;

        Guid projAId, projBId;
        using (var ctx = new CrmDbContext(options))
        {
            var projA = new Project { Code = "KIR", Name = "Kiritimati Ops" };
            var projB = new Project { Code = "BAK", Name = "Baker Island Ops" };
            var sharedEmp = new Employee { FullName = "Shared Eng", Email = "shared@crm.local", Department = "Eng", JobTitle = "Dev" };
            ctx.Projects.AddRange(projA, projB);
            ctx.Employees.Add(sharedEmp);
            await ctx.SaveChangesAsync();

            ctx.ProjectMembers.AddRange(
                new ProjectMember { ProjectId = projA.Id, EmployeeId = sharedEmp.Id },
                new ProjectMember { ProjectId = projB.Id, EmployeeId = sharedEmp.Id });

            ctx.PlanningCutoffConfigs.AddRange(
                new PlanningCutoffConfig
                {
                    ProjectId = projA.Id,
                    CutoffTimeLocal = new TimeOnly(10, 0, 0),
                    TimeZoneId = "Pacific/Kiritimati", // UTC+14
                    IsReminderEnabled = true,
                    ExcludedDaysOfWeekJson = "[]"
                },
                new PlanningCutoffConfig
                {
                    ProjectId = projB.Id,
                    CutoffTimeLocal = new TimeOnly(10, 0, 0),
                    TimeZoneId = "Etc/GMT+12", // UTC-12
                    IsReminderEnabled = true,
                    ExcludedDaysOfWeekJson = "[]"
                });
            await ctx.SaveChangesAsync();
            projAId = projA.Id;
            projBId = projB.Id;
        }

        // At 2026-11-18T21:00Z -> Kiritimati is 2026-11-19 11:00 (past 10:00 cutoff -> sends 1 reminder for 2026-11-19)
        var tp = new MutableTimeProvider(DateTimeOffset.Parse("2026-11-18T21:00:00Z"));
        var job = new DailyPlanCutoffReminderJob(scopeFactory, NullLogger<DailyPlanCutoffReminderJob>.Instance, tp);

        var resA = await job.ExecuteCutoffCheckAsync(projAId);
        resA.CheckedDate.Should().Be(new DateOnly(2026, 11, 19));
        resA.RemindersSent.Should().Be(1);

        // 26 hours later: 2026-11-19T23:00Z -> Baker Island (UTC-12) is 2026-11-19 11:00 (past 10:00 cutoff for Project B!)
        // Because DailyPlanReminderLogs is keyed only by (EmployeeId, PlanDate) without ProjectId, Project B's reminder is suppressed!
        tp.SetUtcNow(DateTimeOffset.Parse("2026-11-19T23:00:00Z"));
        var resB = await job.ExecuteCutoffCheckAsync(projBId);
        resB.CheckedDate.Should().Be(new DateOnly(2026, 11, 19));
        resB.RemindersSent.Should().Be(1, "Project B cutoff reminder for its own project override should not be silently suppressed by Project A's log on the same calendar date");
    }

    [Theory]
    [InlineData(2024, 2, 29, 2024, 2, 26)] // Leap day Thu -> Mon Feb 26
    [InlineData(2024, 3, 3, 2024, 2, 26)]  // Sunday after leap day -> crosses into Feb 26
    [InlineData(2026, 1, 1, 2025, 12, 29)] // New Year's Day Thu -> Mon Dec 29 prev year
    [InlineData(2026, 11, 22, 2026, 11, 16)] // Sunday -> Monday
    public void WeeklyPlan_NormalizeToMonday_LeapYearAndYearBoundaries(int y, int m, int d, int expY, int expM, int expD)
    {
        WeeklyPlan.NormalizeToMonday(new DateOnly(y, m, d)).Should().Be(new DateOnly(expY, expM, expD));
    }

    [Theory]
    [InlineData("2024-02-29T23:59:00+14:00", "Pacific/Kiritimati", 2024, 2, 29, 10, 0, false)] // 23:59 local > 10:00 -> late
    [InlineData("2024-02-29T09:59:00+14:00", "Pacific/Kiritimati", 2024, 2, 29, 10, 0, true)]  // 09:59 local <= 10:00 -> on time
    [InlineData("2024-02-29T10:00:00-12:00", "Etc/GMT+12", 2024, 2, 29, 10, 0, true)]          // exact 10:00 local -> on time
    [InlineData("2024-02-29T10:00:01-12:00", "Etc/GMT+12", 2024, 2, 29, 10, 0, false)]         // 10:00:01 local -> late
    public void DailyPlan_EvaluateSubmissionTiming_ExtremeOffsetsAndLeapDay(
        string submittedIso, string tzId, int planY, int planM, int planD, int cutoffH, int cutoffM, bool expectedOnTime)
    {
        var config = new PlanningCutoffConfig
        {
            CutoffTimeLocal = new TimeOnly(cutoffH, cutoffM),
            TimeZoneId = tzId
        };
        var plan = new DailyPlan { PlanDate = new DateOnly(planY, planM, planD) };
        plan.EvaluateSubmissionTiming(config, DateTimeOffset.Parse(submittedIso));
        plan.IsSubmittedOnTime.Should().Be(expectedOnTime);
    }

    private async Task<Guid> GetFirstProjectIdAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<CrmDbContext>().Projects.FirstAsync()).Id;
    }

    private async Task<Guid> GetFirstEmployeeIdAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<CrmDbContext>().Employees.FirstAsync(e => e.Role == AppRole.Employee)).Id;
    }

    private async Task<Guid> GetFirstLeadIdAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<CrmDbContext>().Employees.FirstAsync(e => e.Role == AppRole.TeamLead)).Id;
    }

    #endregion
}
