using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Notifications;
using ApprovalService.Domain.Notifications;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用本机假 SMTP 核对事件邮件正文与需登录的审批中心地址。
/// </summary>
internal static class NotificationRulesChecksSmtp
{
    /// <summary>
    /// 投递由真实审批事件规划出的邮件并核对收到的字节。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var instance = new InstanceEntity
        {
            Id = "instance-42",
            SerialNo = "SP-202610-000042",
            Title = "采购申请",
            InitiatorId = "requester",
        };
        var occurredAt = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
        var result = new EngineResult
        {
            State = new EngineState
            {
                Tasks = [new EngineTask
                {
                    Id = "task-42", NodeId = "approval", AssigneeId = "approver",
                    Status = EngineTaskStatuses.Pending,
                }],
            },
            Events = [new EngineEvent
            {
                Id = "event-42", Kind = "taskCreated", TaskId = "task-42", OccurredAt = occurredAt,
            }],
        };
        var mailItem = new RuntimeEventNotificationPlanner().Plan(instance, new(), result, [],
            new Dictionary<string, string?> { ["approver"] = "approver@example.test" },
            new HashSet<string>(StringComparer.Ordinal) { "approver" })
            .Single(item => JsonSerializer.Deserialize<NotificationMessage>(item.PayloadJson)?.ChannelCode ==
                NotificationChannelCodes.Email);
        var message = JsonSerializer.Deserialize<NotificationMessage>(mailItem.PayloadJson)!;
        await using var smtp = new CaptureSmtpServer();
        var channel = new SmtpNotificationChannel(Options.Create(new SmtpNotificationOptions
        {
            Host = "127.0.0.1",
            Port = smtp.Port,
            EnableSsl = false,
            FromAddress = "approvals@example.test",
            PublicBaseUrl = "https://approval.example.test",
        }));
        await channel.SendAsync(message, CancellationToken.None);
        var raw = await smtp.WaitForMessageAsync();
        var body = DecodeBody(raw);
        check("事件邮件经假 SMTP 含审批摘要与需登录的打开地址",
            raw.Contains("X-Approval-Notification-Id: " + message.Id, StringComparison.OrdinalIgnoreCase) &&
            body.Contains("SP-202610-000042", StringComparison.Ordinal) &&
            body.Contains("采购申请", StringComparison.Ordinal) &&
            body.Contains("https://approval.example.test/m/approvals/instance-42", StringComparison.Ordinal) &&
            !body.Contains("token=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解码 SMTP 数据中的 UTF-8 正文。
    /// </summary>
    /// <param name="raw">SMTP DATA 原文。</param>
    /// <returns>邮件纯文本正文。</returns>
    private static string DecodeBody(string raw)
    {
        var parts = raw.Split(["\r\n\r\n", "\n\n"], 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return string.Empty;
        }

        return parts[0].Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace("\r", string.Empty,
                StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal)))
            : parts[1];
    }

    /// <summary>
    /// 在回环地址接受一封邮件的最小 SMTP 服务。
    /// </summary>
    private sealed class CaptureSmtpServer : IAsyncDisposable
    {
        /// <summary>
        /// 监听随机本地端口。
        /// </summary>
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        /// <summary>
        /// 取消正在等待的连接。
        /// </summary>
        private readonly CancellationTokenSource stop = new();

        /// <summary>
        /// SMTP 对话任务。
        /// </summary>
        private readonly Task<string> receipt;

        /// <summary>
        /// 启动本地 SMTP 服务。
        /// </summary>
        public CaptureSmtpServer()
        {
            listener.Start();
            receipt = CaptureAsync();
        }

        /// <summary>
        /// 动态分配的端口。
        /// </summary>
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>
        /// 等待邮件正文完整接收。
        /// </summary>
        /// <returns>SMTP DATA 原文。</returns>
        public Task<string> WaitForMessageAsync() => receipt;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try
            {
                await receipt;
            }
            catch (OperationCanceledException)
            {
                // 测试提前退出时结束等待。
            }

            stop.Dispose();
        }

        /// <summary>
        /// 完成 SMTP 基本对话并返回数据块。
        /// </summary>
        /// <returns>SMTP DATA 原文。</returns>
        private async Task<string> CaptureAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };
            await writer.WriteLineAsync("220 fake-smtp ready");
            var data = new StringBuilder();
            var transcript = new List<string>();
            var receiving = false;
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                transcript.Add(line);
                if (receiving)
                {
                    if (line == ".")
                    {
                        await writer.WriteLineAsync("250 stored");
                        return data.ToString();
                    }

                    data.AppendLine(line);
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
                    await writer.WriteLineAsync("250 recipient ok");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    receiving = true;
                    await writer.WriteLineAsync("354 send data");
                }
            }

            throw new InvalidOperationException("假 SMTP 连接结束前未收到邮件正文：" +
                string.Join(" | ", transcript));
        }
    }
}
