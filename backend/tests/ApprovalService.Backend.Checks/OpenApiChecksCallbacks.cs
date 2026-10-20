using System.Net;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
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
                OccurredAt = DateTimeOffset.UtcNow,
            };
            var body = CallbackNotifications.Serialize(notification);
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
            check("重发使用同一事件 ID 与完全相同的请求正文", remote.RequestCount == 2 &&
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
            return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        }
    }
}
