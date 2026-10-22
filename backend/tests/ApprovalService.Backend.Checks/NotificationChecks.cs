using System.Net;
using System.Net.Sockets;
using System.Text;
using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Notifications;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 和本机假 SMTP 服务验证消息发送、读取及持久重试。
/// </summary>
internal static class NotificationChecks
{
    /// <summary>
    /// 运行独立于业务事件的渠道场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-notifications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var smtp = new FakeSmtpServer();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContextFactory<ApprovalDbContext>(options =>
                options.UseSqlite("Data Source=" + Path.Combine(directory, "notifications.db")));
            services.AddSingleton<OutboxQueue>();
            services.AddSingleton<ScheduledJobQueue>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<INotificationAppService, NotificationAppService>();
            services.AddScoped<INotificationChannel, InAppNotificationChannel>();
            services.AddScoped<INotificationChannel, SmtpNotificationChannel>();
            services.AddScoped<IOutboxHandler, NotificationOutboxHandler>();
            services.Configure<SmtpNotificationOptions>(options =>
            {
                options.Host = "127.0.0.1";
                options.Port = smtp.Port;
                options.EnableSsl = false;
                options.FromAddress = "approvals@example.test";
                options.PublicBaseUrl = "https://approval.example.test";
            });
            await using var provider = services.BuildServiceProvider();
            await using (var db = await provider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>()
                .CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            var now = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
            using var scope = provider.CreateScope();
            var app = scope.ServiceProvider.GetRequiredService<INotificationAppService>();
            var inbox = new NotificationMessage
            {
                Id = "a-inapp",
                ChannelCode = NotificationChannelCodes.InApp,
                RecipientId = "alice",
                Subject = "待办提醒",
                Body = "有一条待处理审批。",
                TargetPath = "/m/approvals/instance-1",
                CreatedAt = now,
            };
            var email = new NotificationMessage
            {
                Id = "b-email",
                ChannelCode = NotificationChannelCodes.Email,
                RecipientId = "alice",
                RecipientAddress = "alice@example.test",
                Subject = "Approval needed",
                Body = "Review the request.",
                TargetPath = "/m/approvals/instance-1",
                CreatedAt = now,
            };
            await app.QueueAsync(inbox);
            await app.QueueAsync(email);
            await app.QueueAsync(inbox);
            await using (var db = await provider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>()
                .CreateDbContextAsync())
            {
                check("相同通知 ID 幂等入队且邮件未在数据库事务内发送",
                    await db.OutboxItems.CountAsync() == 2 && smtp.Attempts == 0);
            }

            var worker = new DurableJobWorker(provider.GetRequiredService<OutboxQueue>(),
                provider.GetRequiredService<ScheduledJobQueue>(), provider.GetRequiredService<IServiceScopeFactory>());
            await worker.RunOnceAsync(now);
            check("站内消息由待发执行器持久投递并按人员隔离读取",
                (await app.ListAsync("alice", 10)).Single().Id == inbox.Id &&
                (await app.ListAsync("bob", 10)).Count == 0);
            check("其他人员不能把收件箱消息标记已读",
                !await app.MarkReadAsync(inbox.Id, "bob", now.AddMinutes(1)) &&
                await app.MarkReadAsync(inbox.Id, "alice", now.AddMinutes(1)) &&
                (await app.ListAsync("alice", 10)).Single().ReadAt is not null);
            await worker.RunOnceAsync(now);
            DateTimeOffset retryAt;
            await using (var db = await provider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>()
                .CreateDbContextAsync())
            {
                var queued = await db.OutboxItems.SingleAsync(item => item.Id == email.Id);
                retryAt = queued.AvailableAt;
                check("假 SMTP 首次暂时失败时待发项保留并安排退避重试",
                    smtp.Attempts == 1 && queued.Status == JobStatuses.Pending &&
                    queued.Attempts == 1 && retryAt > now);
            }

            // 模拟服务进程重启：重新创建执行器，仍由数据库中的待发项恢复重试。
            var restartedWorker = new DurableJobWorker(provider.GetRequiredService<OutboxQueue>(),
                provider.GetRequiredService<ScheduledJobQueue>(), provider.GetRequiredService<IServiceScopeFactory>());
            await restartedWorker.RunOnceAsync(retryAt.AddSeconds(1));
            await using (var db = await provider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>()
                .CreateDbContextAsync())
            {
                var sent = await db.OutboxItems.SingleAsync(item => item.Id == email.Id);
                check("邮件重试成功后完成待发项并含需登录的站内链接",
                    smtp.Attempts == 2 && sent.Status == JobStatuses.Completed &&
                    smtp.Data?.Contains("X-Approval-Notification-Id: b-email", StringComparison.OrdinalIgnoreCase) == true &&
                    smtp.DecodedBody?.Contains("https://approval.example.test/m/approvals/instance-1",
                        StringComparison.Ordinal) == true);
            }

            var duplicateChannel = new InAppNotificationChannel(scope.ServiceProvider
                .GetRequiredService<INotificationRepository>());
            await duplicateChannel.SendAsync(inbox, CancellationToken.None);
            await using (var db = await provider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>()
                .CreateDbContextAsync())
            {
                check("站内消息重复投递不会产生第二条收件记录",
                    await db.InAppNotifications.CountAsync(item => item.Id == inbox.Id) == 1);
            }

            var conflict = false;
            try
            {
                await app.QueueAsync(new NotificationMessage
                {
                    Id = inbox.Id,
                    ChannelCode = NotificationChannelCodes.InApp,
                    RecipientId = "bob",
                    Subject = inbox.Subject,
                    Body = inbox.Body,
                    TargetPath = inbox.TargetPath,
                    CreatedAt = now,
                });
            }
            catch (ApprovalService.Domain.Shared.UserFriendlyException)
            {
                conflict = true;
            }

            check("重复通知 ID 不能改写收件人", conflict && (await app.ListAsync("bob", 10)).Count == 0);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 仅处理本地两次连接的极简 SMTP 测试服务；首次拒收、第二次接收。
    /// </summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        /// <summary>
        /// 监听回环地址。
        /// </summary>
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        /// <summary>
        /// 停止后台接受连接的信号。
        /// </summary>
        private readonly CancellationTokenSource stop = new();

        /// <summary>
        /// 后台协议处理任务。
        /// </summary>
        private readonly Task loop;

        /// <summary>
        /// 创建并启动假 SMTP 服务。
        /// </summary>
        public FakeSmtpServer()
        {
            listener.Start();
            loop = AcceptAsync();
        }

        /// <summary>
        /// 动态分配的本机端口。
        /// </summary>
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>
        /// 已处理连接数。
        /// </summary>
        public int Attempts
        {
            get; private set;
        }

        /// <summary>
        /// 成功接收的邮件原文。
        /// </summary>
        public string? Data
        {
            get; private set;
        }

        /// <summary>
        /// 成功接收的纯文本正文。
        /// </summary>
        public string? DecodedBody
        {
            get; private set;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // 关闭监听时没有剩余连接。
            }
            finally
            {
                stop.Dispose();
            }
        }

        /// <summary>
        /// 最多接收两次连接，每次独立完成 SMTP 对话。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async Task AcceptAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                Attempts++;
                await HandleAsync(client, Attempts == 1);
            }
        }

        /// <summary>
        /// 处理 EHLO、信封、DATA 与 QUIT 的最小协议子集。
        /// </summary>
        /// <param name="client">当前连接。</param>
        /// <param name="reject">首次连接是否临时拒收。</param>
        /// <returns>异步操作。</returns>
        private async Task HandleAsync(TcpClient client, bool reject)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };
            await writer.WriteLineAsync("220 fake-smtp ready");
            var data = new StringBuilder();
            var receiving = false;
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                if (receiving)
                {
                    if (line == ".")
                    {
                        Data = data.ToString();
                        DecodedBody = DecodeBody(Data);
                        receiving = false;
                        await writer.WriteLineAsync("250 stored");
                    }
                    else
                    {
                        data.AppendLine(line);
                    }
                }
                else if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 fake-smtp");
                }
                else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 sender ok");
                }
                else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync(reject ? "451 try later" : "250 recipient ok");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    receiving = true;
                    await writer.WriteLineAsync("354 send data");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
            }
        }

        /// <summary>
        /// 解析测试邮件的 Base64 正文，以检查登录链接而不依赖外部 MIME 库。
        /// </summary>
        /// <param name="raw">SMTP DATA 原文。</param>
        /// <returns>解码后的正文。</returns>
        private static string DecodeBody(string raw)
        {
            var parts = raw.Split(["\r\n\r\n", "\n\n"], 2, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                return string.Empty;
            }

            return parts[0].Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal)))
                : parts[1];
        }
    }
}
