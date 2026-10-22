using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 无外部钩子的真实 HTTP、SQLite 本机性能基线。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 预热后逐单测量发起、待办数量和同意，报告各自 P95。
    /// </summary>
    /// <param name="http">进程内 HTTP 客户端。</param>
    /// <param name="apiKey">本应用的 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunPerformanceAsync(HttpClient http, string apiKey, Action<string, bool> check)
    {
        const int warmup = 4;
        const int samples = 20;
        var startTimes = new List<double>(samples);
        var countTimes = new List<double>(samples);
        var actionTimes = new List<double>(samples);
        for (var index = 0; index < warmup + samples; index++)
        {
            using var startRequest = Request(new OpenStartRequest
            {
                FlowCode = "PURCHASE",
                BusinessKey = "PERF-" + index,
                Initiator = "alice",
                Data = JsonSerializer.SerializeToElement(new { amount = 1 }),
            }, apiKey, "perf-start-" + index);
            var timer = Stopwatch.StartNew();
            using var startResponse = await http.SendAsync(startRequest);
            timer.Stop();
            if (index >= warmup)
            {
                startTimes.Add(timer.Elapsed.TotalMilliseconds);
            }

            var started = await startResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
            if (startResponse.StatusCode != HttpStatusCode.OK || started?.Tasks.Count != 1)
            {
                throw new InvalidOperationException("性能基线发起请求失败，不能计算有效耗时。");
            }

            using var countRequest = new HttpRequestMessage(HttpMethod.Get,
                OpenApiRoutes.TaskCount + "?user=bob");
            countRequest.Headers.Add(HeaderNames.ApiKey, apiKey);
            timer.Restart();
            using var countResponse = await http.SendAsync(countRequest);
            timer.Stop();
            if (index >= warmup)
            {
                countTimes.Add(timer.Elapsed.TotalMilliseconds);
            }

            if (countResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException("性能基线待办数量请求失败，不能计算有效耗时。");
            }

            using var actionRequest = ActionRequest(started.Tasks.Single().Id,
                new OpenTaskActionRequest { User = "bob", Action = EngineCommandKinds.Approve },
                apiKey, "perf-approve-" + index);
            timer.Restart();
            using var actionResponse = await http.SendAsync(actionRequest);
            timer.Stop();
            if (index >= warmup)
            {
                actionTimes.Add(timer.Elapsed.TotalMilliseconds);
            }

            if (actionResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException("性能基线审批请求失败，不能计算有效耗时。");
            }
        }

        var startP95 = Percentile95(startTimes);
        var countP95 = Percentile95(countTimes);
        var actionP95 = Percentile95(actionTimes);
        Console.WriteLine($"本机无钩子 HTTP+SQLite 基线（预热 {warmup}，样本 {samples}）：" +
            $"发起 P95 {startP95:F1} ms，审批 P95 {actionP95:F1} ms，待办数量 P95 {countP95:F1} ms");
        check("本机发起 P95 不超过 300 毫秒", startP95 <= 300);
        check("本机审批 P95 不超过 300 毫秒", actionP95 <= 300);
        check("本机待办数量 P95 不超过 50 毫秒", countP95 <= 50);
    }

    /// <summary>
    /// 使用最近秩法取排序后的第九十五百分位样本。
    /// </summary>
    /// <param name="times">单次请求的毫秒数。</param>
    /// <returns>第九十五百分位毫秒数。</returns>
    private static double Percentile95(List<double> times)
    {
        times.Sort();
        return times[(int)Math.Ceiling(times.Count * 0.95) - 1];
    }
}
