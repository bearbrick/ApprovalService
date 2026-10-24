using System.Net;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 使用真实 HTTP 和 SQLite 检查接入应用附件在发起事务中的归属、回滚与并发竞争。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 检查应用上传、未关联下载及发起时的原子附件关联。
    /// </summary>
    /// <param name="services">服务器服务提供器。</param>
    /// <param name="http">真实路由测试客户端。</param>
    /// <param name="demoKey">示例应用密钥。</param>
    /// <param name="otherKey">其他应用密钥。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunFilesAsync(IServiceProvider services, HttpClient http, string demoKey,
        string otherKey, Action<string, bool> check)
    {
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        var file = await UploadAsync(http, demoKey, bytes);
        check("接入应用可用 API Key 上传有应用归属的附件", file is { ContentType: "image/png" });
        var fileId = file!.Id;
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();

        using (var ownRequest = DownloadRequest(fileId, demoKey))
        using (var ownResponse = await http.SendAsync(ownRequest))
        using (var otherRequest = DownloadRequest(fileId, otherKey))
        using (var otherResponse = await http.SendAsync(otherRequest))
        {
            check("未关联附件只允许上传它的应用下载",
                ownResponse.StatusCode == HttpStatusCode.OK &&
                (await ownResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes) &&
                otherResponse.StatusCode == HttpStatusCode.Forbidden);
        }

        using (var invalidRequest = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Files + "?fileName=invalid.png")
        {
            Content = new ByteArrayContent(bytes),
        })
        {
            invalidRequest.Headers.Add(HeaderNames.ApiKey, "invalid-key");
            using var invalidResponse = await http.SendAsync(invalidRequest);
            check("无效 API Key 不会退回到人员会话上传", invalidResponse.StatusCode == HttpStatusCode.Unauthorized);
        }

        var crossApp = FileStart("FILE-CROSS", fileId);
        using (var request = Request(crossApp, otherKey, "file-cross-app"))
        using (var response = await http.SendAsync(request))
        {
            check("其他应用不能在发起时关联已上传文件", response.StatusCode == HttpStatusCode.BadRequest);
        }

        var invalid = FileStart("FILE-ROLLBACK", fileId, "missing-file-id");
        using (var request = Request(invalid, demoKey, "file-partial-rollback"))
        using (var response = await http.SendAsync(request))
        {
            check("一张审批单引用不存在文件时整笔拒绝", response.StatusCode == HttpStatusCode.BadRequest);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var stored = await db.StoredFiles.AsNoTracking().SingleAsync(item => item.Id == fileId);
            check("部分附件关联失败会回滚已写实例、事件和先前文件",
                stored.InstanceId is null && stored.LinkedAt is null &&
                !await db.Instances.AsNoTracking().AnyAsync(item => item.BusinessKey == "FILE-ROLLBACK") &&
                !await db.Instances.AsNoTracking().AnyAsync(item => item.BusinessKey == "FILE-CROSS"));
        }

        var valid = FileStart("FILE-LINKED", fileId, fileId);
        using var linkedRequest = Request(valid, demoKey, "file-valid");
        using var linkedResponse = await http.SendAsync(linkedRequest);
        var linked = linkedResponse.StatusCode == HttpStatusCode.OK
            ? JsonSerializer.Deserialize<OpenInstanceResponse>(await linkedResponse.Content.ReadAsStringAsync(),
                ApprovalJson.Options) : null;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stored = await db.StoredFiles.AsNoTracking().SingleAsync(item => item.Id == fileId);
            check("重复字段引用同一附件仅关联一次且随审批单提交",
                linked is not null && stored.InstanceId == linked.Id && stored.LinkedAt is not null &&
                linked.Data.GetProperty("proof").GetArrayLength() == 2 &&
                linked.Data.GetProperty("items")[0].GetProperty("receipt").GetString() == fileId);
        }

        using (var request = Request(FileStart("FILE-REUSE", fileId), demoKey, "file-reuse"))
        using (var response = await http.SendAsync(request))
        {
            check("同一附件不能被第二张审批单抢用", response.StatusCode == HttpStatusCode.BadRequest);
        }

        var competing = (await UploadAsync(http, demoKey, bytes))!.Id;
        var contenders = await Task.WhenAll(
            StartStatusAsync(http, FileStart("FILE-RACE-A", competing), demoKey, "file-race-a"),
            StartStatusAsync(http, FileStart("FILE-RACE-B", competing), demoKey, "file-race-b"));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var stored = await db.StoredFiles.AsNoTracking().SingleAsync(item => item.Id == competing);
            var instances = await db.Instances.AsNoTracking().Where(item =>
                item.BusinessKey == "FILE-RACE-A" || item.BusinessKey == "FILE-RACE-B").ToArrayAsync();
            check("并发争用同一文件至多一张审批单成功且归属不被覆盖",
                contenders.Count(status => status == HttpStatusCode.OK) == 1 &&
                contenders.Count(status => status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict) == 1 &&
                instances.Length == 1 && stored.InstanceId == instances[0].Id);
        }
    }

    /// <summary>
    /// 通过已认证应用上传一张小 PNG。
    /// </summary>
    /// <param name="http">真实路由测试客户端。</param>
    /// <param name="apiKey">上传应用密钥。</param>
    /// <param name="bytes">文件字节。</param>
    /// <returns>上传信息；非成功状态为空。</returns>
    private static async Task<FileUploadResponse?> UploadAsync(HttpClient http, string apiKey, byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Files + "?fileName=receipt.png")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        using var response = await http.SendAsync(request);
        return response.StatusCode == HttpStatusCode.OK
            ? JsonSerializer.Deserialize<FileUploadResponse>(await response.Content.ReadAsStringAsync(), ApprovalJson.Options)
            : null;
    }

    /// <summary>
    /// 构造带接入应用密钥的附件下载请求。
    /// </summary>
    /// <param name="id">文件 ID。</param>
    /// <param name="apiKey">调用应用密钥。</param>
    /// <returns>下载请求。</returns>
    private static HttpRequestMessage DownloadRequest(string id, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.FileById.Replace("{id}", id));
        request.Headers.Add(HeaderNames.ApiKey, apiKey);
        return request;
    }

    /// <summary>
    /// 构造引用顶层和明细附件的发起请求。
    /// </summary>
    /// <param name="businessKey">本次业务单号。</param>
    /// <param name="fileIds">顶层附件 ID，可含重复以检查去重。</param>
    /// <returns>发起请求。</returns>
    private static OpenStartRequest FileStart(string businessKey, params string[] fileIds) => new()
    {
        FlowCode = "PURCHASE",
        BusinessKey = businessKey,
        Initiator = "alice",
        Data = JsonSerializer.SerializeToElement(new
        {
            amount = 3,
            proof = fileIds,
            items = new[] { new { receipt = fileIds[0] } },
        }),
    };

    /// <summary>
    /// 发起一个竞争请求并在响应释放前取状态。
    /// </summary>
    /// <param name="http">真实路由测试客户端。</param>
    /// <param name="input">发起输入。</param>
    /// <param name="apiKey">调用应用密钥。</param>
    /// <param name="requestId">本次唯一请求 ID。</param>
    /// <returns>HTTP 状态。</returns>
    private static async Task<HttpStatusCode> StartStatusAsync(HttpClient http, OpenStartRequest input,
        string apiKey, string requestId)
    {
        using var request = Request(input, apiKey, requestId);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }
}
