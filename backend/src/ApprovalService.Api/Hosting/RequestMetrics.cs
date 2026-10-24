using System.Diagnostics;
using System.Globalization;
using System.Text;
using ApprovalService.Contracts.Protocol;
using Microsoft.AspNetCore.Routing;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 以固定路由模板聚合请求次数和耗时，不保存人员、单号、查询参数或请求正文。
/// </summary>
internal sealed class RequestMetrics
{
    /// <summary>
    /// Prometheus 累计直方图的秒数边界。
    /// </summary>
    private static readonly double[] Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5];

    /// <summary>
    /// 保护有限维度的聚合字典。
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// 每个方法、路由模板和状态码的聚合值。
    /// </summary>
    private readonly Dictionary<(string Method, string Route, int Status), Entry> entries = [];

    /// <summary>
    /// 记录一次 HTTP 请求。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="route">匹配的路由模板，不含实际 ID。</param>
    /// <param name="status">最终 HTTP 状态码。</param>
    /// <param name="seconds">请求耗时，单位秒。</param>
    public void Observe(string method, string route, int status, double seconds)
    {
        lock (gate)
        {
            // 自定义 HTTP 方法不能创建无限新维度。
            var key = (NormalizeMethod(method), route, status);
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                entries.Add(key, entry);
            }

            entry.Count++;
            entry.Seconds += seconds;
            for (var index = 0; index < Boundaries.Length; index++)
            {
                if (seconds <= Boundaries[index])
                {
                    entry.Buckets[index]++;
                }
            }
        }
    }

    /// <summary>
    /// 生成 Prometheus 文本快照。
    /// </summary>
    /// <returns>只含聚合值和固定路由模板的文本。</returns>
    public string Render()
    {
        var output = new StringBuilder();
        output.Append("# HELP approval_http_requests_total 审批服务收到的 HTTP 请求数。\n")
            .Append("# TYPE approval_http_requests_total counter\n")
            .Append("# HELP approval_http_request_duration_seconds HTTP 请求耗时秒数。\n")
            .Append("# TYPE approval_http_request_duration_seconds histogram\n");

        lock (gate)
        {
            foreach (var (key, entry) in entries.OrderBy(item => item.Key.Method, StringComparer.Ordinal)
                         .ThenBy(item => item.Key.Route, StringComparer.Ordinal).ThenBy(item => item.Key.Status))
            {
                var labels = $"method=\"{Escape(key.Method)}\",route=\"{Escape(key.Route)}\",status=\"{key.Status}\"";
                output.Append("approval_http_requests_total{").Append(labels).Append("} ").Append(entry.Count).Append('\n');
                for (var index = 0; index < Boundaries.Length; index++)
                {
                    output.Append("approval_http_request_duration_seconds_bucket{").Append(labels)
                        .Append(",le=\"").Append(Boundaries[index].ToString("G", CultureInfo.InvariantCulture))
                        .Append("\"} ").Append(entry.Buckets[index]).Append('\n');
                }

                output.Append("approval_http_request_duration_seconds_bucket{").Append(labels)
                    .Append(",le=\"+Inf\"} ").Append(entry.Count).Append('\n');
                output.Append("approval_http_request_duration_seconds_sum{").Append(labels).Append("} ")
                    .Append(entry.Seconds.ToString("G17", CultureInfo.InvariantCulture)).Append('\n');
                output.Append("approval_http_request_duration_seconds_count{").Append(labels).Append("} ")
                    .Append(entry.Count).Append('\n');
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// 转义 Prometheus 标签的特殊字符。
    /// </summary>
    /// <param name="value">标签原文。</param>
    /// <returns>安全的标签文本。</returns>
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// 将非标准 HTTP 方法折叠为固定维度，防止输入放大指标内存。
    /// </summary>
    /// <param name="method">请求方法。</param>
    /// <returns>标准方法或 OTHER。</returns>
    private static string NormalizeMethod(string method) => method switch
    {
        "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" => method,
        _ => "OTHER",
    };

    /// <summary>
    /// 一个有限维度桶的累计值。
    /// </summary>
    private sealed class Entry
    {
        /// <summary>
        /// 已观察的请求数。
        /// </summary>
        public long Count
        {
            get; set;
        }

        /// <summary>
        /// 所有请求的耗时之和。
        /// </summary>
        public double Seconds
        {
            get; set;
        }

        /// <summary>
        /// 每个边界内的累计请求数。
        /// </summary>
        public long[] Buckets { get; } = new long[Boundaries.Length];
    }
}

/// <summary>
/// 统计请求的最终状态和耗时，路由只使用服务端定义的模板。
/// </summary>
/// <param name="next">后续中间件。</param>
internal sealed class RequestMetricsMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 执行并记录一条请求；指标抓取自身不计入统计。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <param name="metrics">聚合器。</param>
    /// <returns>请求处理完成。</returns>
    public async Task InvokeAsync(HttpContext http, RequestMetrics metrics)
    {
        if (http.Request.Path == ApiRoutes.Metrics)
        {
            await next(http);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(http);
        }
        finally
        {
            var route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            metrics.Observe(http.Request.Method, route, http.Response.StatusCode, Stopwatch.GetElapsedTime(start).TotalSeconds);
        }
    }
}
