using System.Net;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用假的业务系统后端检查签名回调、发送日志和稳定事件重发。
/// </summary>
internal static class OpenApiChecksCallbacks
{
    /// <summary>
    /// 覆盖请求原文签名、失败后重发及日志结果。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "callback.db")).Options;
            var factory = new CallbackContextFactory(options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            }).Build();
            var protector = new AesIntegrationSecretProtector(configuration);
            const string secret = "local-test-signing-secret";
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.ClientApps.Add(new ClientAppEntity
                {
                    Code = "demo",
                    Name = "示例业务系统",
                    ApiKeyHash = new string('a', 64),
                    SigningSecretCiphertext = protector.Protect(secret),
                    CallbackUrl = "https://business.invalid/callback",
                });
                db.Flows.Add(new FlowEntity { Id = "flow", Code = "PURCHASE", Name = "采购", PublishedVersionId = "version" });
                db.FlowVersions.Add(new FlowVersionEntity
                {
                    Id = "version",
                    FlowId = "flow",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = "{}",
                    GraphJson = "{}",
                });
                db.Instances.Add(new InstanceEntity
                {
                    Id = "instance",
                    FlowId = "flow",
                    FlowVersionId = "version",
                    FlowCode = "PURCHASE",
                    BusinessKey = "PO-1",
                    ClientAppCode = "demo",
                    Title = "采购",
                    InitiatorId = "E1",
                    SerialNo = "CG-1",
                    Status = "approved",
                    StateJson = "{}",
                });
                await db.SaveChangesAsync();
            }

            using var document = JsonDocument.Parse("{\"amount\":12800}");
            var notification = new CallbackNotification
            {
                EventId = "event-1",
                Event = "instance.approved",
                InstanceId = "instance",
                SerialNo = "CG-1",
                FlowCode = "PURCHASE",
                BusinessKey = "PO-1",
                Status = "approved",
                Data = document.RootElement.Clone(),
                FieldValues = JsonSerializer.SerializeToElement(new { approvedAmount = 12000 }),
                OccurredAt = DateTimeOffset.UtcNow,
            };
            var body = CallbackNotifications.Serialize(notification);
            var source = new InstanceEntity
            {
                Id = "source-instance",
                SerialNo = "CG-2",
                FlowCode = "PURCHASE",
                BusinessKey = "PO-2",
                Status = EngineInstanceStatuses.Approved,
                ApprovalDataJson = "{\"amount\":1}",
            };
            var mappedEvents = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["instanceStarted"] = "instance.started",
                ["instanceApproved"] = "instance.approved",
                ["instanceRejected"] = "instance.rejected",
                ["instanceWithdrawn"] = "instance.withdrawn",
                ["instanceTerminated"] = "instance.terminated",
                ["instanceCancelled"] = "instance.cancelled",
                ["taskCreated"] = "task.created",
                ["taskCompleted"] = "task.completed",
                ["taskCancelled"] = "task.cancelled",
            };
            check("实例与任务的九类回调事件有稳定外部编码，内部事件不外发",
                mappedEvents.All(pair => CallbackNotifications.Create(new EngineEvent
                {
                    Id = "mapped-event",
                    Kind = pair.Key,
                }, source)?.Event == pair.Value) &&
                CallbackNotifications.Create(new EngineEvent { Kind = "gatewayJoined" }, source) is null);
            var startedNotification = CallbackNotifications.Create(new EngineEvent
            {
                Id = "snapshot-started",
                Kind = "instanceStarted",
            }, source)!;
            source.ApprovalDataJson = "{\"amount\":2}";
            check("同一命令立刻完成时开始回调仍保存发起状态和当时数据快照",
                startedNotification.Status == EngineInstanceStatuses.Running &&
                startedNotification.Data.GetProperty("amount").GetInt32() == 1);
            var item = new OutboxItemEntity
            {
                Id = "event-1:callback",
                EventId = "event-1",
                InstanceId = "instance",
                Kind = "callback",
                PayloadJson = body,
                Attempts = 1,
            };
            var remote = new FakeBusinessHandler { Fail = true };
            using var client = new HttpClient(remote);
            var handler = new CallbackOutboxHandler(factory, protector, new SignedHttpOutboundAdapter(client), TimeProvider.System);
            var mismatched = false;
            try
            {
                await handler.DeliverAsync(new OutboxItemEntity
                {
                    EventId = "unexpected-event",
                    InstanceId = item.InstanceId,
                    PayloadJson = item.PayloadJson,
                }, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                mismatched = true;
            }

            check("回调待发项与正文事件身份不一致时拒绝出站", mismatched && remote.RequestCount == 0);
            var failed = false;
            try
            {
                await handler.DeliverAsync(item, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                failed = true;
            }

            check("业务系统返回失败时回调交给持久队列重试", failed);
            remote.Fail = false;
            item.Attempts = 2;
            await handler.DeliverAsync(item, CancellationToken.None);
            check("含审批人填写字段的回调重发保持同一事件 ID 与完全相同正文", remote.RequestCount == 2 &&
                remote.Bodies.All(sent => sent == body) && remote.RequestIds.All(id => id == "event-1"));
            check("回调按原始 UTF-8 正文生成 HMAC-SHA256 签名", remote.Signatures.All(signature =>
                signature == SignedHttpOutboundAdapter.Sign(body, secret)));
            await using (var db = await factory.CreateDbContextAsync())
            {
                var logs = await db.OutboundSendLogs.OrderBy(log => log.Attempt).ToArrayAsync();
                check("每次发送分别留成功与失败记录且不保存正文", logs.Length == 2 &&
                    logs[0].EventId == "event-1" && !logs[0].Succeeded && logs[0].HttpStatus == 503 &&
                    logs[1].Succeeded && logs[1].HttpStatus == 200);
                item.Status = "completed";
                item.CompletedAt = DateTimeOffset.UtcNow;
                db.OutboxItems.Add(item);
                await db.SaveChangesAsync();
            }

            var delivery = new CallbackDeliveryAppService(new CallbackDeliveryRepository(factory), TimeProvider.System);
            await delivery.ResendAsync("event-1");
            await using (var db = await factory.CreateDbContextAsync())
            {
                var queued = await db.OutboxItems.AsNoTracking().SingleAsync();
                check("管理员重发保留原事件 ID 与原始正文", queued.Status == "pending" &&
                    queued.EventId == "event-1" && queued.PayloadJson == body && queued.CompletedAt is null);
            }

            remote.LargeResponse = true;
            var oversized = false;
            try
            {
                await new SignedHttpOutboundAdapter(client).PostAsync("https://business.invalid/callback", body,
                    secret, "size-check");
            }
            catch (InvalidDataException)
            {
                oversized = true;
            }

            check("业务系统异常大响应被限制在 1 MiB 内", oversized);
            remote.LargeResponse = false;
            remote.Redirect = true;
            var redirectRejected = false;
            try
            {
                await new SignedHttpOutboundAdapter(client).PostAsync("https://business.invalid/callback", body,
                    secret, "redirect-check");
            }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TemporaryRedirect)
            {
                redirectRejected = true;
            }

            check("业务系统重定向不能被当作回调成功", redirectRejected);
            remote.Redirect = false;

            await using (var db = await factory.CreateDbContextAsync())
            {
                var app = await db.ClientApps.SingleAsync();
                app.IsEnabled = false;
                await db.SaveChangesAsync();
            }

            var requestsBeforeDisable = remote.RequestCount;
            var disabled = false;
            try
            {
                await handler.DeliverAsync(item, CancellationToken.None);
            }
            catch (UserFriendlyException error) when (error.Code == ErrorCodes.Forbidden)
            {
                disabled = true;
            }

            await using (var db = await factory.CreateDbContextAsync())
            {
                check("停用应用后不发送含审批数据的回调且留下可诊断失败记录", disabled &&
                    remote.RequestCount == requestsBeforeDisable &&
                    await db.OutboundSendLogs.AnyAsync(log => log.EventId == item.EventId &&
                        log.ErrorCode == "CLIENT_APP_DISABLED"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 单场景数据库上下文工厂。
    /// </summary>
    /// <param name="options">SQLite 配置。</param>
    private sealed class CallbackContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// 可先失败再恢复的业务系统 HTTP 端点。
    /// </summary>
    private sealed class FakeBusinessHandler : HttpMessageHandler
    {
        /// <summary>
        /// 本次是否返回不可用。
        /// </summary>
        public bool Fail
        {
            get; set;
        }

        /// <summary>
        /// 本次是否返回超限正文。
        /// </summary>
        public bool LargeResponse
        {
            get; set;
        }

        /// <summary>
        /// 是否返回到其他主机的临时重定向。
        /// </summary>
        public bool Redirect
        {
            get; set;
        }

        /// <summary>
        /// 实际收到的请求次数。
        /// </summary>
        public int RequestCount
        {
            get; private set;
        }

        /// <summary>
        /// 每次收到的原始正文。
        /// </summary>
        public List<string> Bodies { get; } = [];

        /// <summary>
        /// 每次收到的签名。
        /// </summary>
        public List<string> Signatures { get; } = [];

        /// <summary>
        /// 每次收到的请求 ID。
        /// </summary>
        public List<string> RequestIds { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Signatures.Add(request.Headers.GetValues(HeaderNames.Signature).Single());
            RequestIds.Add(request.Headers.GetValues(HeaderNames.RequestId).Single());
            var response = new HttpResponseMessage(Redirect ? HttpStatusCode.TemporaryRedirect :
                Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            if (Redirect)
            {
                response.Headers.Location = new Uri("https://unregistered.invalid/receive");
            }
            if (LargeResponse)
            {
                response.Content = new StringContent(new string('x', SignedHttpOutboundAdapter.MaxResponseBytes + 1));
            }

            return response;
        }
    }
}
