using System.Net;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 以假业务系统验证发起前取数、校验及双向原文签名。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 钩子失败时不落部分审批，成功时保存服务端取得的数据。
    /// </summary>
    /// <param name="services">已初始化真实数据库的服务集合。</param>
    /// <param name="apiKey">已创建应用的 API Key。</param>
    /// <param name="secret">该应用签名密钥。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunHooksAsync(IServiceProvider services, string apiKey, string secret,
        Action<string, bool> check)
    {
        var app = await services.GetRequiredService<ClientAppAppService>().AuthenticateAsync(apiKey);
        app.DataUrl = "https://business.invalid/data";
        app.ValidationUrl = "https://business.invalid/validate";
        var handler = new HookHandler(secret);
        using var http = new HttpClient(handler);
        var hookClient = new ApprovalHookClient(new SignedHttpOutboundAdapter(http),
            services.GetRequiredService<IIntegrationSecretProtector>());
        var start = new OpenApprovalStartAppService(
            services.GetRequiredService<FlowCatalogAppService>(),
            services.GetRequiredService<OpenApprovalQueryAppService>(),
            services.GetRequiredService<IOrganizationLookupAppService>(),
            services.GetRequiredService<OpenStartContextBuilder>(),
            services.GetRequiredService<IApprovalRuntimeAppService>(),
            services.GetRequiredService<IApprovalSerialNumberRepository>(),
            services.GetRequiredService<OpenStartSerialOptions>(),
            services.GetRequiredService<IntegrationTransportPolicy>(),
            new BusinessHookAppService(hookClient), TimeProvider.System);
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();

        handler.Mode = "bad-signature";
        var badSignature = await FailureAsync(start, app, "HOOK-SIGN", "hook-sign");
        await using (var db = await factory.CreateDbContextAsync())
        {
            check("取数响应未通过原文字节签名时拒绝且不创建审批单",
                badSignature?.Code == ErrorCodes.BusinessHookFailed &&
                !await db.Instances.AnyAsync(item => item.BusinessKey == "HOOK-SIGN"));
        }

        handler.Mode = "bad-request-id";
        var badRequestId = await FailureAsync(start, app, "HOOK-REQUEST", "hook-request");
        check("业务系统未回显相同请求 ID 时拒绝取数", badRequestId?.Code == ErrorCodes.BusinessHookFailed);

        handler.Mode = "reject";
        var rejected = await FailureAsync(start, app, "HOOK-REJECT", "hook-reject");
        await using (var db = await factory.CreateDbContextAsync())
        {
            check("业务校验拒绝提示业务原因且不保存部分审批",
                rejected?.Code == ErrorCodes.ValidationFailed && rejected.Message == "预算不足" &&
                !await db.Instances.AnyAsync(item => item.BusinessKey == "HOOK-REJECT"));
        }

        handler.Mode = "success";
        var created = await start.StartAsync(app, Request("HOOK-OK"), "hook-success");
        check("签名取数与业务校验通过后保存取数快照和标题",
            created.BusinessKey == "HOOK-OK" && created.Title == "业务取数采购" &&
            created.Data.GetProperty("amount").GetDecimal() == 13 &&
            handler.Requests.Any(item => item.Path == "/data" && item.Body.Contains("\"requestId\":\"hook-success\"", StringComparison.Ordinal)) &&
            handler.Requests.Any(item => item.Path == "/validate" &&
                item.Body.Contains("\"phase\":\"start\"", StringComparison.Ordinal) &&
                item.Body.Contains("\"actor\":\"alice\"", StringComparison.Ordinal)) &&
            handler.Requests.All(item => item.IsSigned));

        var actions = new OpenTaskActionAppService(
            services.GetRequiredService<IApprovalOpenQueryRepository>(),
            services.GetRequiredService<IOrganizationLookupAppService>(),
            services.GetRequiredService<OpenStartContextBuilder>(),
            services.GetRequiredService<IApprovalRuntimeAppService>(),
            services.GetRequiredService<OpenApprovalQueryAppService>(),
            new BusinessHookAppService(hookClient), TimeProvider.System);
        var action = new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve };
        handler.Mode = "reject";
        UserFriendlyException? approvalDenied = null;
        try
        {
            await actions.ExecuteAsync(app, created.Tasks.Single().Id, action, "hook-approve-reject");
        }
        catch (UserFriendlyException error)
        {
            approvalDenied = error;
        }

        var stillPending = await services.GetRequiredService<OpenApprovalQueryAppService>()
            .GetInstanceAsync(app, created.Id);
        check("节点同意前校验拒绝时原任务保持待办",
            approvalDenied?.Code == ErrorCodes.ValidationFailed &&
            stillPending.Tasks.Single().Status == "pending" &&
            handler.Requests.Last().Body.Contains("\"phase\":\"approve\"", StringComparison.Ordinal));

        handler.Mode = "success";
        var approved = await actions.ExecuteAsync(app, created.Tasks.Single().Id, action, "hook-approve-ok");
        check("节点同意前校验通过后才由统一引擎提交审批",
            approved.Status == EngineInstanceStatuses.Approved &&
            handler.Requests.Last().Body.Contains("\"taskId\"", StringComparison.Ordinal));

        await RunAssigneeActionHooksAsync(services, app, hookClient, handler, check);
    }

    /// <summary>
    /// 构造仅传单号、由取数钩子补数据的发起请求。
    /// </summary>
    /// <param name="businessKey">业务单号。</param>
    /// <returns>发起请求。</returns>
    private static OpenStartRequest Request(string businessKey) => new()
    {
        FlowCode = "PURCHASE",
        BusinessKey = businessKey,
        Initiator = "alice",
    };

    /// <summary>
    /// 返回业务钩子可纠正错误供场景检查。
    /// </summary>
    /// <param name="start">开放发起服务。</param>
    /// <param name="app">已认证应用。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <returns>预期的用户错误；未报错则为空。</returns>
    private static async Task<UserFriendlyException?> FailureAsync(OpenApprovalStartAppService start,
        ClientAppEntity app, string businessKey, string requestId)
    {
        try
        {
            await start.StartAsync(app, Request(businessKey), requestId);
            return null;
        }
        catch (UserFriendlyException error)
        {
            return error;
        }
    }

    /// <summary>
    /// 模拟在登记地址上双向验签的业务端。
    /// </summary>
    /// <param name="secret">仅测试用签名密钥。</param>
    private sealed class HookHandler(string secret) : HttpMessageHandler
    {
        /// <summary>
        /// 控制响应签名、请求 ID 和业务校验结果。
        /// </summary>
        public string Mode { get; set; } = "success";

        /// <summary>
        /// 每次收到的路径、正文与请求签名结论。
        /// </summary>
        public List<(string Path, string Body, bool IsSigned)> Requests { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            var signature = request.Headers.GetValues(HeaderNames.Signature).Single();
            Requests.Add((path, body, signature == SignedHttpOutboundAdapter.Sign(body, secret)));
            var requestId = request.Headers.GetValues(HeaderNames.RequestId).Single();
            var responseBody = path switch
            {
                "/data" => "{\"data\":{\"amount\":13},\"title\":\"业务取数采购\"}",
                "/assignee" when Mode == "unbound-assignee" => "{\"userIds\":[\"E002\"]}",
                "/assignee" => "{\"userIds\":[\"bob\"]}",
                "/action" when Mode == "reject-action" => "{\"success\":false}",
                "/action" => "{\"success\":true,\"result\":{\"reservationId\":\"R-1\"}}",
                _ when Mode == "reject" => "{\"allowed\":false,\"reason\":\"预算不足\"}",
                _ => "{\"allowed\":true}",
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(responseBody)),
            };
            response.Headers.TryAddWithoutValidation(HeaderNames.Signature,
                Mode == "bad-signature" ? "sha256=" + new string('0', 64) :
                    SignedHttpOutboundAdapter.Sign(responseBody, secret));
            response.Headers.TryAddWithoutValidation(HeaderNames.RequestId,
                Mode == "bad-request-id" ? "another-request" : requestId);
            return response;
        }
    }
}
