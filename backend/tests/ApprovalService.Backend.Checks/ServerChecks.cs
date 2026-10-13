using System.Net;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;

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
        }
        finally
        {
            TempDataDirectory.Delete(data);
        }
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
