using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Application.Center;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 手写签名的 PNG 完整性、人员归属和嵌入会话限制场景。
/// </summary>
internal static class CenterSignatureChecks
{
    /// <summary>
    /// 用真实 SQLite 与 JWT 路由验证上传、会话隔离及审批事件引用。
    /// </summary>
    /// <param name="backendRoot">后端项目根目录。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>HTTP 场景完成。</returns>
    public static async Task RunHttpAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            };
            await using var server = new ServerFactory(backendRoot, directory, settings);
            using var http = server.CreateClient();
            using var ready = await http.GetAsync(ApiRoutes.HealthReady);
            var tokens = await CenterChecksFixture.SeedAsync(server.Services);
            var png = ValidPng();
            using var anonymous = await UploadAsync(http, null, png, false);
            using var embeddedPersonal = await UploadAsync(http, tokens.Embed, png, true);
            using var uploaded = await UploadAsync(http, tokens.Local, png, true);
            var asset = await uploaded.Content.ReadFromJsonAsync<CenterSignatureResponse>();
            check("真实 JWT 上传拒绝匿名和嵌入个人签名，本地本人可保存 PNG",
                anonymous.StatusCode == HttpStatusCode.Unauthorized &&
                embeddedPersonal.StatusCode == HttpStatusCode.Forbidden &&
                uploaded.StatusCode == HttpStatusCode.OK && asset is { IsPersonal: true });
            using var personal = await GetAsync(http, CenterRoutes.PersonalSignature, tokens.Local);
            var latest = await personal.Content.ReadFromJsonAsync<CenterSignatureResponse>();
            using var stranger = await GetAsync(http, CenterRoutes.SignatureById.Replace("{id}", asset!.Id), tokens.Stranger);
            check("真实 HTTP 个人签名仅本人可读，其他人员不能借 ID 读取",
                personal.StatusCode == HttpStatusCode.OK && latest?.Id == asset.Id &&
                stranger.StatusCode == HttpStatusCode.Forbidden);
            using var malformed = await UploadAsync(http, tokens.Local, [137, 80, 78, 71], false);
            using var oversized = await UploadAsync(http, tokens.Local,
                new byte[SignatureAppService.MaxPngBytes + 1], false);
            check("真实 HTTP 拒绝伪造 PNG 和超过 2 MiB 的正文",
                malformed.StatusCode == HttpStatusCode.BadRequest && oversized.StatusCode == HttpStatusCode.BadRequest);
            using var action = new HttpRequestMessage(HttpMethod.Post,
                CenterRoutes.TaskActions.Replace("{taskId}", "a-todo-task", StringComparison.Ordinal));
            action.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Local);
            action.Content = JsonContent.Create(new CenterTaskActionRequest
            {
                Action = "comment",
                Opinion = "已核对签名",
                SignatureFileId = asset.Id,
            });
            using var commented = await http.SendAsync(action);
            var result = await commented.Content.ReadFromJsonAsync<CenterTaskActionResponse>();
            var historyPath = CenterRoutes.SignatureById.Replace("{id}", asset.Id, StringComparison.Ordinal) +
                "?instanceId=a-todo";
            using var visible = await GetAsync(http, historyPath, tokens.Local);
            using var hidden = await GetAsync(http, historyPath, tokens.Stranger);
            check("任务操作按本人资产验权并冻结事件，历史图片仍按单据参与权限读取",
                commented.StatusCode == HttpStatusCode.OK &&
                result?.Detail.Events.Any(item => item.SignatureFileId == asset.Id) == true &&
                visible.StatusCode == HttpStatusCode.OK && hidden.StatusCode == HttpStatusCode.NotFound);
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 运行独立资产服务和真实 PNG 解码检查。
    /// </summary>
    /// <param name="check">检查记录器。</param>
    /// <returns>全部场景完成。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var png = ValidPng();
        SignaturePngValidator.Validate(png);
        check("完整 RGBA PNG 可作为本人手写签名", true);
        var corrupted = png.ToArray();
        corrupted[^8] ^= 0x01;
        check("签名 PNG 分块 CRC 损坏会被拒绝", ThrowsValidation(() =>
            SignaturePngValidator.Validate(corrupted)));
        check("签名 PNG 截断会被拒绝", ThrowsValidation(() =>
            SignaturePngValidator.Validate(png.AsSpan(0, png.Length - 2))));
        var repository = new MemorySignatureRepository();
        var service = new SignatureAppService(repository, null!, TimeProvider.System);
        var local = new CenterScope { PersonId = "person-a" };
        var embedded = new CenterScope { PersonId = "person-a", ClientAppCode = "sample" };
        var personal = await service.UploadAsync(local, new MemoryStream(png), true);
        check("本人个人签名只保存 SHA 与不可变资产 ID", personal.IsPersonal &&
            personal.Sha256.Length == 64 && personal.Id.Length == 32 &&
            (await service.LatestPersonalAsync(local))?.Id == personal.Id);
        check("跨人员不能借签名 ID 选用他人的资产", await ThrowsUserAsync(ErrorCodes.Forbidden, () =>
            service.EnsureOwnedAsync(new CenterScope { PersonId = "person-b" }, personal.Id)));
        check("嵌入会话不能改动个人签名资产", await ThrowsUserAsync(ErrorCodes.Forbidden, () =>
            service.UploadAsync(embedded, new MemoryStream(png), true)));
        var oneTime = await service.UploadAsync(embedded, new MemoryStream(png), false);
        check("嵌入会话只能创建本人单次签名", !oneTime.IsPersonal &&
            (await repository.FindAsync(oneTime.Id))?.PersonId == embedded.PersonId);
        check("单次签名不会覆盖个人签名", (await service.LatestPersonalAsync(local))?.Id == personal.Id);
        check("超过 2 MiB 的签名请求在入库前被拒绝", await ThrowsUserAsync(ErrorCodes.ValidationFailed, () =>
            service.UploadAsync(local, new MemoryStream(new byte[SignatureAppService.MaxPngBytes + 1]), false)));
    }

    /// <summary>
    /// 构造一个完整的 1×1 RGBA PNG，避免仅用魔数伪造图片。
    /// </summary>
    /// <returns>完整 PNG 字节。</returns>
    private static byte[] ValidPng()
    {
        using var image = new MemoryStream();
        image.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8;
        header[9] = 6;
        Chunk(image, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var encoder = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            encoder.Write([0, 10, 20, 30, 255]);
        }
        Chunk(image, "IDAT"u8, compressed.ToArray());
        Chunk(image, "IEND"u8, []);
        return image.ToArray();
    }

    /// <summary>
    /// 写入一个含标准 CRC 的 PNG 分块。
    /// </summary>
    /// <param name="destination">目标文件流。</param>
    /// <param name="kind">四字节分块类型。</param>
    /// <param name="content">分块数据。</param>
    private static void Chunk(Stream destination, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> content)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)content.Length);
        destination.Write(length);
        destination.Write(kind);
        destination.Write(content);
        var crc = uint.MaxValue;
        foreach (var value in kind.ToArray().Concat(content.ToArray()))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        destination.Write(length);
    }

    /// <summary>
    /// 检查解码器以可修正错误拒绝无效文件。
    /// </summary>
    /// <param name="action">解码动作。</param>
    /// <returns>返回验证错误时为真。</returns>
    private static bool ThrowsValidation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (UserFriendlyException error) { return error.Code == ErrorCodes.ValidationFailed; }
    }

    /// <summary>
    /// 发送原始 PNG 上传请求。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="token">可信 JWT；空表示匿名。</param>
    /// <param name="png">待上传字节。</param>
    /// <param name="personal">是否保存为个人签名。</param>
    /// <returns>HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient http, string? token, byte[] png,
        bool personal)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            CenterRoutes.Signatures + "?personal=" + personal.ToString().ToLowerInvariant());
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        request.Content = new ByteArrayContent(png);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return await http.SendAsync(request);
    }

    /// <summary>
    /// 用可选 JWT 读取签名资产。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="path">签名相对地址。</param>
    /// <param name="token">人员 JWT。</param>
    /// <returns>HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    /// <summary>
    /// 检查异步用例返回指定业务错误。
    /// </summary>
    /// <param name="code">期望错误码。</param>
    /// <param name="action">待调用用例。</param>
    /// <returns>错误码符合预期时为真。</returns>
    private static async Task<bool> ThrowsUserAsync(string code, Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (UserFriendlyException error) { return error.Code == code; }
    }

    /// <summary>
    /// 仅用于本组用例的内存不可变资产仓储。
    /// </summary>
    private sealed class MemorySignatureRepository : ISignatureAssetRepository
    {
        /// <summary>
        /// 已保存资产。
        /// </summary>
        private readonly Dictionary<string, SignatureAssetEntity> assets = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public Task AddAsync(SignatureAssetEntity asset, CancellationToken cancellationToken = default)
        {
            assets.Add(asset.Id, asset);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<SignatureAssetEntity?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(assets.GetValueOrDefault(id));

        /// <inheritdoc />
        public Task<SignatureAssetEntity?> FindLatestPersonalAsync(string personId,
            CancellationToken cancellationToken = default) => Task.FromResult(assets.Values
                .Where(item => item.PersonId == personId && item.IsPersonal)
                .OrderByDescending(item => item.UploadedAt).ThenByDescending(item => item.Id).FirstOrDefault());
    }
}
