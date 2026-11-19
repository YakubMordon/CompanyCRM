using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyCRM.Tests;

public class SignalRSerializationIntegrationTests : IClassFixture<CrmApiTestFixture>
{
    private readonly CrmApiTestFixture _fixture;

    public SignalRSerializationIntegrationTests(CrmApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class SignalRTestSubscriber : IAsyncDisposable
    {
        private readonly WebSocket _ws;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _receiveTask;

        public ConcurrentQueue<(string Target, JsonElement Payload)> Events { get; } = new();

        private SignalRTestSubscriber(WebSocket ws)
        {
            _ws = ws;
            _receiveTask = Task.Run(ReceiveLoopAsync);
        }

        public static async Task<SignalRTestSubscriber> ConnectAsync(CrmApiTestFixture fixture, string jwtToken)
        {
            // Ensure TestServer is initialized before creating WebSocketClient
            _ = fixture.CreateClient();
            var wsClient = fixture.Server.CreateWebSocketClient();
            var uri = new Uri($"ws://localhost/hubs/crm?access_token={Uri.EscapeDataString(jwtToken)}");
            var ws = await wsClient.ConnectAsync(uri, CancellationToken.None);

            // SignalR JSON protocol handshake (record separator 0x1E)
            var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\x1e");
            await ws.SendAsync(handshake, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

            var buffer = new byte[4096];
            var sb = new StringBuilder();
            while (true)
            {
                var res = await ws.ReceiveAsync(buffer, CancellationToken.None);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                if (res.EndOfMessage && sb.ToString().Contains('\x1e'))
                {
                    break;
                }
            }

            var handshakeResp = sb.ToString().Split('\x1e', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            handshakeResp.Should().NotBeNull();
            using var doc = JsonDocument.Parse(handshakeResp!);
            doc.RootElement.TryGetProperty("error", out _).Should().BeFalse($"SignalR handshake failed: {handshakeResp}");

            return new SignalRTestSubscriber(ws);
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
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    pending.Append(Encoding.UTF8.GetString(ms.ToArray()));
                    var text = pending.ToString();
                    var lastSep = text.LastIndexOf('\x1e');
                    if (lastSep < 0) continue;

                    var completeChunk = text[..lastSep];
                    pending.Clear();
                    if (lastSep + 1 < text.Length)
                    {
                        pending.Append(text[(lastSep + 1)..]);
                    }

                    foreach (var frame in completeChunk.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
                    {
                        using var doc = JsonDocument.Parse(frame);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("type", out var typeProp) && typeProp.GetInt32() == 1 &&
                            root.TryGetProperty("target", out var targetProp) &&
                            root.TryGetProperty("arguments", out var argsProp) &&
                            argsProp.ValueKind == JsonValueKind.Array &&
                            argsProp.GetArrayLength() > 0)
                        {
                            Events.Enqueue((targetProp.GetString()!, argsProp[0].Clone()));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (WebSocketException)
            {
                // Socket closed during teardown
            }
        }

        public async Task<JsonElement> WaitForEventAsync(string target, Func<JsonElement, bool> predicate, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                foreach (var (evtTarget, payload) in Events)
                {
                    if (evtTarget == target && predicate(payload))
                    {
                        return payload;
                    }
                }
                await Task.Delay(25);
            }

            throw new TimeoutException($"Timed out waiting for SignalR event '{target}'. Captured events: {Events.Count}");
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                }
                catch
                {
                    // Ignore close errors during test teardown
                }
            }
            try
            {
                await _receiveTask;
            }
            catch
            {
                // Ignore
            }
            _ws.Dispose();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task WorkItem_CreateAndTransitionStatus_BroadcastsStringEnumsOverSignalR()
    {
        var client = _fixture.CreateClient();
        var authResp = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        authResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await authResp.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        Guid projectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            projectId = (await db.Projects.FirstAsync()).Id;
        }

        await using var subscriber = await SignalRTestSubscriber.ConnectAsync(_fixture, auth.Token);

        var createResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            ProjectId: projectId,
            Title: $"SignalR Enum Test {Guid.NewGuid():N}",
            Description: "Verifying string enum wire serialization over SignalR",
            Type: WorkItemType.Bug,
            Priority: WorkItemPriority.High
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);
        created.Should().NotBeNull();

        var createdPayload = await subscriber.WaitForEventAsync(
            "WorkItemUpdated",
            el => el.TryGetProperty("id", out var idProp) && idProp.GetGuid() == created!.Id);

        createdPayload.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        createdPayload.GetProperty("status").GetString().Should().Be("Backlog");

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/workitems/{created!.Id}/status",
            new KanbanTransitionDto(WorkItemStatus.InProgress, 1));
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var transitionedPayload = await subscriber.WaitForEventAsync(
            "WorkItemUpdated",
            el => el.TryGetProperty("id", out var idProp) &&
                  idProp.GetGuid() == created.Id &&
                  el.TryGetProperty("status", out var st) &&
                  st.ValueKind == JsonValueKind.String &&
                  st.GetString() == "InProgress");

        transitionedPayload.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        transitionedPayload.GetProperty("status").GetString().Should().Be("InProgress");
        transitionedPayload.GetProperty("type").ValueKind.Should().Be(JsonValueKind.String);
        transitionedPayload.GetProperty("type").GetString().Should().Be("Bug");
        transitionedPayload.GetProperty("priority").ValueKind.Should().Be(JsonValueKind.String);
        transitionedPayload.GetProperty("priority").GetString().Should().Be("High");
        transitionedPayload.GetProperty("reviewStatus").ValueKind.Should().Be(JsonValueKind.String);
        transitionedPayload.GetProperty("reviewStatus").GetString().Should().Be("NotRequired");
        transitionedPayload.GetProperty("externalSource").ValueKind.Should().Be(JsonValueKind.String);
        transitionedPayload.GetProperty("externalSource").GetString().Should().Be("None");
        transitionedPayload.GetProperty("lastSyncOrigin").ValueKind.Should().Be(JsonValueKind.String);
        transitionedPayload.GetProperty("lastSyncOrigin").GetString().Should().Be("LocalCrm");
    }

    [Fact]
    public async Task ReviewApprove_BroadcastsStringReviewStatusInReviewQueueUpdatedAndWorkItemUpdated()
    {
        Guid projectId;
        Guid reviewerId;
        Guid creatorId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            projectId = (await db.Projects.FirstAsync()).Id;
            reviewerId = (await db.Employees.FirstAsync(e => e.Role == AppRole.TeamLead)).Id;
            creatorId = (await db.Employees.FirstAsync(e => e.Role == AppRole.Employee)).Id;
        }

        var creatorClient = _fixture.CreateClientWithEmployee(creatorId, "creator.signalr@crm.local", "Creator", AppRole.Employee);
        var reviewerClient = _fixture.CreateClientWithEmployee(reviewerId, "reviewer.signalr@crm.local", "Reviewer", AppRole.TeamLead);
        var reviewerToken = reviewerClient.DefaultRequestHeaders.Authorization!.Parameter!;

        await using var subscriber = await SignalRTestSubscriber.ConnectAsync(_fixture, reviewerToken);

        var createResp = await creatorClient.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            ProjectId: projectId,
            Title: $"SignalR Review Item {Guid.NewGuid():N}",
            ReviewerId: reviewerId
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var assignedReviewEvent = await subscriber.WaitForEventAsync(
            "ReviewQueueUpdated",
            el => el.TryGetProperty("workItemId", out var idProp) &&
                  idProp.GetGuid() == created!.Id &&
                  el.TryGetProperty("action", out var actProp) &&
                  actProp.GetString() == "Assigned");

        assignedReviewEvent.GetProperty("reviewStatus").ValueKind.Should().Be(JsonValueKind.String);
        assignedReviewEvent.GetProperty("reviewStatus").GetString().Should().Be("Pending");

        var approveResp = await reviewerClient.PostAsJsonAsync(
            $"/api/reviews/{created!.Id}/approve",
            new ApproveReviewRequestDto(WorkItemStatus.Todo, "Approved via SignalR test"));
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedReviewEvent = await subscriber.WaitForEventAsync(
            "ReviewQueueUpdated",
            el => el.TryGetProperty("workItemId", out var idProp) &&
                  idProp.GetGuid() == created.Id &&
                  el.TryGetProperty("action", out var actProp) &&
                  actProp.GetString() == "Approved");

        approvedReviewEvent.GetProperty("reviewStatus").ValueKind.Should().Be(JsonValueKind.String);
        approvedReviewEvent.GetProperty("reviewStatus").GetString().Should().Be("Approved");

        var approvedWorkItemEvent = await subscriber.WaitForEventAsync(
            "WorkItemUpdated",
            el => el.TryGetProperty("id", out var idProp) &&
                  idProp.GetGuid() == created.Id &&
                  el.TryGetProperty("reviewStatus", out var rsProp) &&
                  rsProp.ValueKind == JsonValueKind.String &&
                  rsProp.GetString() == "Approved");

        approvedWorkItemEvent.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        approvedWorkItemEvent.GetProperty("status").GetString().Should().Be("Todo");
        approvedWorkItemEvent.GetProperty("reviewStatus").ValueKind.Should().Be(JsonValueKind.String);
        approvedWorkItemEvent.GetProperty("reviewStatus").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task SyncWebhookPushAndConflictResolution_BroadcastRealProjectIdAndWorkItemUpdated()
    {
        var client = _fixture.CreateClient();
        var authResp = await client.PostAsync("/api/auth/switch-persona/TeamLead", null);
        authResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await authResp.Content.ReadFromJsonAsync<AuthResponseDto>(CrmApiTestFixture.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        Guid syncProjectId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var proj = await db.Projects.FirstAsync(p => p.IsSyncEnabled && p.ExternalSource == ExternalSourceType.AzureDevOps);
            syncProjectId = proj.Id;
        }

        await using var subscriber = await SignalRTestSubscriber.ConnectAsync(_fixture, auth.Token);

        // 1. Inbound novel webhook -> SyncStatusChanged("WorkItemImported", real projectId) + WorkItemUpdated
        var uniqueExtId = Random.Shared.Next(800000, 999999);
        var uniqueTitle = $"Unique Webhook Import {Guid.NewGuid():N}";
        var adoPayload = $$"""
        {
            "subscriptionId": "sub-signalr-1",
            "eventType": "workitem.created",
            "resource": {
                "id": {{uniqueExtId}},
                "url": "https://dev.azure.com/org/proj/_workitems/edit/{{uniqueExtId}}",
                "fields": {
                    "System.Title": "{{uniqueTitle}}",
                    "System.Description": "Imported over webhook",
                    "System.State": "Active",
                    "System.WorkItemType": "Task"
                }
            }
        }
        """;

        var webhookResp = await client.PostAsync(
            "/api/webhooks/ado",
            new StringContent(adoPayload, Encoding.UTF8, "application/json"));
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var webhookResult = await webhookResp.Content.ReadFromJsonAsync<WebhookProcessingResultDto>(CrmApiTestFixture.JsonOptions);
        webhookResult.Should().NotBeNull();
        webhookResult!.Status.Should().Be("Applied");
        webhookResult.WorkItemId.Should().NotBeNull();

        var importSyncEvt = await subscriber.WaitForEventAsync(
            "SyncStatusChanged",
            el => el.TryGetProperty("workItemId", out var wiProp) &&
                  wiProp.ValueKind == JsonValueKind.String &&
                  wiProp.GetGuid() == webhookResult.WorkItemId!.Value);

        importSyncEvt.GetProperty("projectId").GetGuid().Should().Be(syncProjectId);
        importSyncEvt.GetProperty("projectId").GetGuid().Should().NotBe(Guid.Empty);
        importSyncEvt.GetProperty("status").GetString().Should().Be("WorkItemImported");

        var importWiEvt = await subscriber.WaitForEventAsync(
            "WorkItemUpdated",
            el => el.TryGetProperty("id", out var idProp) && idProp.GetGuid() == webhookResult.WorkItemId!.Value);

        importWiEvt.GetProperty("externalSource").ValueKind.Should().Be(JsonValueKind.String);
        importWiEvt.GetProperty("externalSource").GetString().Should().Be("AzureDevOps");
        importWiEvt.GetProperty("lastSyncOrigin").ValueKind.Should().Be(JsonValueKind.String);
        importWiEvt.GetProperty("lastSyncOrigin").GetString().Should().Be("ExternalWebhook");

        // 2. Push local work item -> SyncStatusChanged("WorkItemPushed", real projectId) + WorkItemUpdated
        var createResp = await client.PostAsJsonAsync("/api/workitems", new CreateWorkItemDto(
            ProjectId: syncProjectId,
            Title: $"Push SignalR Test {Guid.NewGuid():N}"
        ));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var localCreated = await createResp.Content.ReadFromJsonAsync<WorkItemDto>(CrmApiTestFixture.JsonOptions);

        var pushResp = await client.PostAsync($"/api/sync/workitems/{localCreated!.Id}/push", null);
        pushResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var pushSyncEvt = await subscriber.WaitForEventAsync(
            "SyncStatusChanged",
            el => el.TryGetProperty("workItemId", out var wiProp) &&
                  wiProp.ValueKind == JsonValueKind.String &&
                  wiProp.GetGuid() == localCreated.Id &&
                  el.TryGetProperty("status", out var stProp) &&
                  stProp.GetString() == "WorkItemPushed");

        pushSyncEvt.GetProperty("projectId").GetGuid().Should().Be(syncProjectId);

        var pushWiEvt = await subscriber.WaitForEventAsync(
            "WorkItemUpdated",
            el => el.TryGetProperty("id", out var idProp) &&
                  idProp.GetGuid() == localCreated.Id &&
                  el.TryGetProperty("externalId", out var extProp) &&
                  extProp.ValueKind == JsonValueKind.String);
        pushWiEvt.GetProperty("externalSource").ValueKind.Should().Be(JsonValueKind.String);
        pushWiEvt.GetProperty("externalSource").GetString().Should().Be("AzureDevOps");

        // 3. Conflict resolution (KeepBoth) -> SyncStatusChanged(real projectId, newItem.Id) + WorkItemUpdated(newItem.Id)
        Guid conflictId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var conflict = new SyncConflict
            {
                Id = Guid.NewGuid(),
                ProjectId = syncProjectId,
                LocalWorkItemId = localCreated.Id,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = $"ADO-SIG-{Random.Shared.Next(1000, 9999)}",
                ExternalTitle = $"KeepBoth SignalR Title {Guid.NewGuid():N}",
                ExternalDescription = "Conflict keep-both desc",
                ExternalStatus = "Active",
                SimilarityScore = 0.92,
                ConflictType = SyncConflictType.DuplicateCandidate,
                Status = ConflictStatus.Pending,
                ResolutionAction = ConflictResolutionAction.Unresolved,
                DetectedAt = DateTimeOffset.UtcNow
            };
            db.SyncConflicts.Add(conflict);
            await db.SaveChangesAsync();
            conflictId = conflict.Id;
        }

        var resolveResp = await client.PostAsJsonAsync(
            $"/api/sync/conflicts/{conflictId}/resolve",
            new ResolveConflictRequestDto(ConflictResolutionAction.KeepBoth, "Keep both via SignalR test"));
        resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolveResult = await resolveResp.Content.ReadFromJsonAsync<ConflictResolutionResultDto>(CrmApiTestFixture.JsonOptions);
        resolveResult.Should().NotBeNull();
        resolveResult!.ResolvedWorkItemId.Should().NotBeNull();
        resolveResult.ResolvedWorkItemId.Should().NotBe(localCreated.Id);

        var resolveSyncEvt = await subscriber.WaitForEventAsync(
            "SyncStatusChanged",
            el => el.TryGetProperty("workItemId", out var wiProp) &&
                  wiProp.ValueKind == JsonValueKind.String &&
                  wiProp.GetGuid() == resolveResult.ResolvedWorkItemId!.Value);

        resolveSyncEvt.GetProperty("projectId").GetGuid().Should().Be(syncProjectId);
        resolveSyncEvt.GetProperty("status").GetString().Should().Be("SyncCompleted");

        var resolveWiEvt = await subscriber.WaitForEventAsync(
            "WorkItemUpdated",
            el => el.TryGetProperty("id", out var idProp) && idProp.GetGuid() == resolveResult.ResolvedWorkItemId!.Value);
        resolveWiEvt.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        resolveWiEvt.GetProperty("status").GetString().Should().Be("InProgress");
    }
}
