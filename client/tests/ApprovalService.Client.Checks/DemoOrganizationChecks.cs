using System.Net;
using System.Net.Http;
using System.Text;
using ApprovalService.Client;
using ApprovalService.Demo.Integration;

namespace ApprovalService.Client.Checks;

/// <summary>
/// 演示组织计划及经 SDK 一键推送的检查。
/// </summary>
internal static class DemoOrganizationChecks
{
    /// <summary>
    /// 验证固定组织、双部门关系和应用级绑定按顺序提交。
    /// </summary>
    /// <param name="check">检查报告。</param>
    /// <returns>组织准备完成。</returns>
    internal static async Task RunAsync(Action<string, bool> check)
    {
        var plan = DemoOrganizationSeed.Build();
        check("演示组织包含七个部门和十九名在职人员", plan.Changes.Departments.Count == 7
            && plan.Changes.People.Count == 19 && plan.Changes.People.All(person => person.IsActive));
        check("李娜可按研发部发起且其主部门仍为生产一部", plan.Changes.Memberships.Count(item =>
            item.PersonId == "demo-person-li-na") == 2 && plan.Changes.Memberships.Any(item =>
            item.PersonId == "demo-person-li-na" && item.DepartmentId == "demo-dept-research" && !item.IsPrimary)
            && plan.Changes.Memberships.Any(item => item.PersonId == "demo-person-li-na" &&
                item.DepartmentId == "demo-dept-production-one" && item.IsPrimary));
        check("业务用户与部门均按同一接入应用建立外部 ID 绑定", plan.Bindings.Count == 26
            && plan.Bindings.Any(item => item.Kind == "person" && item.ExternalId == "wang" &&
                item.EmployeeNumber == "DEMO-WANG" && item.InternalId == null)
            && plan.Bindings.Any(item => item.Kind == "department" && item.ExternalId == "research" &&
                item.InternalId == "demo-dept-research"));

        var seen = new List<string>();
        var handler = new FakeHandler(async request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            seen.Add(path);
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (path.EndsWith("/org/changes", StringComparison.Ordinal))
            {
                check("组织批次通过 SDK 包含双部门关系", body.Contains("demo-dept-research", StringComparison.Ordinal)
                    && body.Contains("demo-person-li-na", StringComparison.Ordinal));
                return Json("{\"applied\":true,\"issues\":[],\"departedPersonIds\":[]}");
            }

            return body.Contains("\"kind\":\"person\"", StringComparison.Ordinal) ?
                Json("{\"personId\":\"demo-person\"}") : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var sdk = new ApprovalServiceClient(new ApprovalServiceClientOptions
        {
            BaseAddress = new Uri("https://approval.example.test/"),
            ApiKey = "demo-key",
        }, handler);
        await DemoOrganizationSeed.ApplyAsync(sdk).ConfigureAwait(false);
        check("一键准备先推送组织，再逐一绑定十九名人员和七个部门", seen.Count == 27
            && seen[0] == "/api/v1/org/changes" && seen.Skip(1).All(path => path == "/api/v1/org/bindings"));
    }

    /// <summary>
    /// 构造 JSON 响应。
    /// </summary>
    /// <param name="body">响应正文。</param>
    /// <returns>HTTP 响应。</returns>
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>
    /// 截获组织推送和绑定请求。
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        /// <summary>
        /// 测试响应回调。
        /// </summary>
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> respond;

        /// <summary>
        /// 创建假 HTTP 处理器。
        /// </summary>
        /// <param name="respond">按请求生成响应。</param>
        internal FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => this.respond = respond;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
