using System.Net;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using Microsoft.Extensions.Options;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 服务端的最小链路：健康检查、服务信息、请求 ID 和链路 ID、统一的错误格式。
/// </summary>
internal static class ServerChecks
{
    /// <summary>
    /// 在进程内启动服务并逐项检查。
    /// </summary>
    /// <param name="backendRoot">backend 目录。</param>
    /// <param name="check">记录检查结果。</param>
    /// <returns>检查做完时完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var data = TempDataDirectory.Create();
        try
        {
            await using var factory = new ServerFactory(backendRoot, data);
            using var http = factory.CreateClient();

            var live = await http.GetAsync(ApiRoutes.HealthLive);
            var ready = await http.GetAsync(ApiRoutes.HealthReady);
            check("存活检查和就绪检查返回 200", live.StatusCode == HttpStatusCode.OK && ready.StatusCode == HttpStatusCode.OK);

            var infoResponse = await http.GetAsync(ApiRoutes.Info);
            var info = JsonSerializer.Deserialize<ServiceInfoResponse>(await infoResponse.Content.ReadAsStringAsync(), ApprovalJson.Options);
            check("服务信息返回名称 ApprovalService、接口版本 v1 和程序版本",
                infoResponse.StatusCode == HttpStatusCode.OK && info is { Name: "ApprovalService", ApiVersion: "v1" } && info.Version.Length > 0);
            check("每个响应都带请求 ID 和链路 ID",
                infoResponse.Headers.Contains(HeaderNames.RequestId) && infoResponse.Headers.Contains(HeaderNames.TraceId));

            using var withId = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Info);
            withId.Headers.Add(HeaderNames.RequestId, "demo-req.001");
            var echoed = await http.SendAsync(withId);
            using var badId = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Info);
            badId.Headers.TryAddWithoutValidation(HeaderNames.RequestId, "含 空格 的 ID");
            var replaced = await http.SendAsync(badId);
            check("调用方传的请求 ID 原样返回；不合规的换成服务生成的",
                echoed.Headers.GetValues(HeaderNames.RequestId).Single() == "demo-req.001"
                && replaced.Headers.GetValues(HeaderNames.RequestId).Single() is { Length: 32 } generated && generated.All(char.IsAsciiHexDigitLower));

            var missing = await http.GetAsync(ApiRoutes.OpenApiPrefix + "/no-such-api");
            var error = await ReadErrorAsync(missing);
            check("不存在的地址返回 404 和统一格式的错误体（code、message、traceId 和响应头一致）",
                missing.StatusCode == HttpStatusCode.NotFound && error is { Code: ErrorCodes.NotFound }
                && error.TraceId == missing.Headers.GetValues(HeaderNames.TraceId).Single() && error.Message.Length > 0);
            check("错误体里的中文直接输出，不转成 \\u 转义（curl、日志里能直接读）",
                (await missing.Content.ReadAsStringAsync()).Contains("接口地址不存在", StringComparison.Ordinal));

            var wrongMethod = await http.PostAsync(ApiRoutes.Info, null);
            check("方法不对返回 405 和统一格式的错误体",
                wrongMethod.StatusCode == HttpStatusCode.MethodNotAllowed && (await ReadErrorAsync(wrongMethod))?.Code == ErrorCodes.NotFound);

            check("未知数据库提供程序在启动时拒绝，诊断指出 Provider 且不回显连接串",
                await RejectsInvalidConfigurationAsync(backendRoot, data,
                    new Dictionary<string, string?>
                    {
                        ["ApprovalService:Data:Provider"] = "Unknown",
                        ["ApprovalService:Data:ConnectionString"] = "example-sensitive-marker",
                    }, "ApprovalService:Data:Provider", "example-sensitive-marker"));
            check("空数据目录在启动时拒绝，并指出 Directory 配置键",
                await RejectsInvalidConfigurationAsync(backendRoot, data,
                    new Dictionary<string, string?> { ["ApprovalService:Data:Directory"] = " " }, "ApprovalService:Data:Directory"));
            check("外部数据库缺少连接串时在启动时拒绝，并指出 ConnectionString 配置键",
                await RejectsInvalidConfigurationAsync(backendRoot, data,
                    new Dictionary<string, string?> { ["ApprovalService:Data:Provider"] = "SqlServer" }, "ApprovalService:Data:ConnectionString"));
            check("外部数据库不可连接时服务仍存活但就绪检查返回 503",
                await ReportsUnavailableDatabaseAsync(backendRoot, data, "SqlServer")
                && await ReportsUnavailableDatabaseAsync(backendRoot, data, "PostgreSql"));
        }
        finally
        {
            TempDataDirectory.Delete(data);
        }
    }

    /// <summary>
    /// 确认错误数据配置在服务监听请求前被拒绝，且诊断包含对应配置键。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="dataDirectory">临时数据目录。</param>
    /// <param name="settings">覆盖默认值的错误配置。</param>
    /// <param name="expectedKey">应出现在诊断中的配置键。</param>
    /// <param name="forbiddenText">不得出现在诊断中的文本；不需要检查时为 null。</param>
    /// <returns>启动被拒绝且诊断正确时为 true。</returns>
    private static async Task<bool> RejectsInvalidConfigurationAsync(
        string backendRoot,
        string dataDirectory,
        IReadOnlyDictionary<string, string?> settings,
        string expectedKey,
        string? forbiddenText = null)
    {
        var quietSettings = new Dictionary<string, string?>(settings)
        {
            ["Logging:LogLevel:Microsoft.Extensions.Hosting.Internal.Host"] = "None",
        };
        await using var factory = new ServerFactory(backendRoot, dataDirectory, quietSettings);
        try
        {
            using var client = factory.CreateClient();
            // 启动校验应在这里抛出；请求只用于确保惰性启动已经发生。
            await client.GetAsync(ApiRoutes.HealthReady);
            return false;
        }
        catch (OptionsValidationException error)
        {
            return error.Failures.Any(failure => failure.Contains(expectedKey, StringComparison.Ordinal))
                && (forbiddenText is null || !error.Message.Contains(forbiddenText, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 确认外部数据库不可用时服务仍能启动，但不能报告就绪。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="dataDirectory">临时数据目录。</param>
    /// <param name="provider">数据库提供程序。</param>
    /// <returns>存活检查为 200 且就绪检查为 503 时为真。</returns>
    private static async Task<bool> ReportsUnavailableDatabaseAsync(string backendRoot, string dataDirectory, string provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ApprovalService:Data:Provider"] = provider,
            ["ApprovalService:Data:ConnectionString"] = provider == "SqlServer"
                ? "Server=127.0.0.1,1;Database=approval;User Id=test;Password=test-only;Connect Timeout=1;Encrypt=False"
                : "Host=127.0.0.1;Port=1;Database=approval;Username=test;Password=test-only;Timeout=1",
        };
        await using var factory = new ServerFactory(backendRoot, dataDirectory, settings);
        using var client = factory.CreateClient();
        var live = await client.GetAsync(ApiRoutes.HealthLive);
        var ready = await client.GetAsync(ApiRoutes.HealthReady);
        return live.StatusCode == HttpStatusCode.OK && ready.StatusCode == HttpStatusCode.ServiceUnavailable;
    }

    /// <summary>
    /// 把响应体读成错误体。
    /// </summary>
    /// <param name="response">响应。</param>
    /// <returns>错误体；不是 JSON 时为 null。</returns>
    private static async Task<ApiErrorResponse?> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiErrorResponse>(await response.Content.ReadAsStringAsync(), ApprovalJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
