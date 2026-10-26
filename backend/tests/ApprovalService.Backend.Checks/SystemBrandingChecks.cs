using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 HTTP、JWT 与 SQLite 验证打印页眉设置的权限、图片和审计。
/// </summary>
internal static class SystemBrandingChecks
{
    /// <summary>
    /// 执行系统信息场景。
    /// </summary>
    /// <param name="backendRoot">后端项目根目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>场景完成。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Identity:SigningKey"] = Convert.ToBase64String(new byte[64]),
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            };
            await using var server = new ServerFactory(backendRoot, directory, settings);
            using var http = server.CreateClient();
            using (var scope = server.Services.CreateScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using (var db = await factory.CreateDbContextAsync())
                {
                    db.People.AddRange(new PersonEntity
                    {
                        Id = "branding-admin",
                        EmployeeNumber = "BA001",
                        Name = "品牌管理员",
                    }, new PersonEntity
                    {
                        Id = "branding-viewer",
                        EmployeeNumber = "BV001",
                        Name = "普通用户",
                    });
                    db.OrganizationRoles.Add(new OrganizationRoleEntity
                    {
                        Code = "organization-admin",
                        Name = "组织管理员",
                    });
                    db.PersonRoles.Add(new PersonRoleEntity
                    {
                        PersonId = "branding-admin",
                        RoleCode = "organization-admin",
                    });
                    await db.SaveChangesAsync();
                }
                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                await identity.ProvisionAccountAsync("branding-admin", "initial-admin-password");
                await identity.ChangePasswordAsync("branding-admin", "initial-admin-password", "normal-admin-password");
                await identity.ProvisionAccountAsync("branding-viewer", "initial-viewer-password");
                await identity.ChangePasswordAsync("branding-viewer", "initial-viewer-password", "normal-viewer-password");
            }

            using var anonymous = await http.GetAsync(ApiRoutes.AdminSystemBranding);
            var viewer = await LoginAsync(http, "BV001", "normal-viewer-password");
            using var forbidden = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminSystemBranding,
                viewer.AccessToken);
            var admin = await LoginAsync(http, "BA001", "normal-admin-password");
            using var initial = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminSystemBranding,
                admin.AccessToken);
            var original = await initial.Content.ReadFromJsonAsync<SystemBrandingResponse>();
            check("系统信息只允许本地组织管理员读取，空库为修订零",
                anonymous.StatusCode == HttpStatusCode.Unauthorized &&
                forbidden.StatusCode == HttpStatusCode.Forbidden && original is
                { Revision: 0, CompanyName: "", LogoSha256: null });

            using var invalid = await SendAsync(http, HttpMethod.Put, ApiRoutes.AdminSystemBranding,
                admin.AccessToken, new UpdateSystemBrandingRequest
                {
                    CompanyName = "测试单位",
                    ExpectedRevision = 0,
                    Reason = "x",
                });
            using var saved = await SendAsync(http, HttpMethod.Put, ApiRoutes.AdminSystemBranding,
                admin.AccessToken, new UpdateSystemBrandingRequest
                {
                    CompanyName = " 测试单位 ",
                    ExpectedRevision = 0,
                    Reason = "用于打印页眉",
                });
            var named = await saved.Content.ReadFromJsonAsync<SystemBrandingResponse>();
            using var stale = await SendAsync(http, HttpMethod.Put, ApiRoutes.AdminSystemBranding,
                admin.AccessToken, new UpdateSystemBrandingRequest
                {
                    CompanyName = "旧页面覆盖",
                    ExpectedRevision = 0,
                    Reason = "并发修改",
                });
            check("名称校验、去空白、乐观并发与中文错误码有效",
                invalid.StatusCode == HttpStatusCode.BadRequest && saved.IsSuccessStatusCode &&
                named is { CompanyName: "测试单位", Revision: 1 } &&
                stale.StatusCode == HttpStatusCode.Conflict);

            var png = ValidPng();
            using var fake = await SendAsync(http, HttpMethod.Post, ApiRoutes.AdminSystemLogo,
                admin.AccessToken, new UploadSystemLogoRequest
                {
                    ContentBase64 = Convert.ToBase64String([137, 80, 78, 71, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]),
                    ExpectedRevision = 1,
                    Reason = "上传品牌图片",
                });
            using var upload = await SendAsync(http, HttpMethod.Post, ApiRoutes.AdminSystemLogo,
                admin.AccessToken, new UploadSystemLogoRequest
                {
                    ContentBase64 = Convert.ToBase64String(png),
                    ExpectedRevision = 1,
                    Reason = "上传品牌图片",
                });
            var withLogo = await upload.Content.ReadFromJsonAsync<SystemBrandingResponse>();
            using var image = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminSystemLogo,
                admin.AccessToken);
            check("伪造 PNG 被拒，完整图片可上传且鉴权下载有安全响应头",
                fake.StatusCode == HttpStatusCode.BadRequest && upload.IsSuccessStatusCode &&
                withLogo is { Revision: 2, LogoSha256: not null } && image.IsSuccessStatusCode &&
                image.Content.Headers.ContentType?.MediaType == "image/png" &&
                image.Headers.TryGetValues("X-Content-Type-Options", out var values) &&
                values.Contains("nosniff") &&
                (await image.Content.ReadAsByteArrayAsync()).SequenceEqual(png));

            using var forbiddenImage = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminSystemLogo,
                viewer.AccessToken);
            using var removed = await SendAsync(http, HttpMethod.Delete, ApiRoutes.AdminSystemLogo,
                admin.AccessToken, new DeleteSystemLogoRequest
                {
                    ExpectedRevision = 2,
                    Reason = "换品牌图片",
                });
            var withoutLogo = await removed.Content.ReadFromJsonAsync<SystemBrandingResponse>();
            using var missing = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminSystemLogo,
                admin.AccessToken);
            check("图片读取也查权限，删除后返回 404",
                forbiddenImage.StatusCode == HttpStatusCode.Forbidden &&
                removed.IsSuccessStatusCode && withoutLogo is { Revision: 3, LogoSha256: null } &&
                missing.StatusCode == HttpStatusCode.NotFound);

            using var audit = await SendAsync(http, HttpMethod.Get, ApiRoutes.AdminAudit,
                admin.AccessToken);
            var entries = await audit.Content.ReadFromJsonAsync<AdminAuditResponse[]>();
            check("名称、上传和删除都在操作日志中保留操作者和原因，不含图片字节",
                entries?.Count(item => item.Target == "systemBranding:global" &&
                    item.ActorId == "branding-admin" && !string.IsNullOrWhiteSpace(item.Reason)) == 3 &&
                !(await audit.Content.ReadAsStringAsync()).Contains(Convert.ToBase64String(png),
                    StringComparison.Ordinal));
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 通过真实登录接口获取访问令牌。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="number">人员工号。</param>
    /// <param name="password">本地密码。</param>
    /// <returns>身份令牌。</returns>
    private static async Task<IdentityTokensResponse> LoginAsync(HttpClient http, string number, string password)
    {
        using var response = await http.PostAsJsonAsync(ApiRoutes.IdentityLogin,
            new IdentityLoginRequest(number, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!;
    }

    /// <summary>
    /// 发送带人员 JWT 的管理请求。
    /// </summary>
    /// <param name="http">测试客户端。</param>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">管理路由。</param>
    /// <param name="token">人员访问令牌。</param>
    /// <param name="body">可选请求体。</param>
    /// <returns>未释放的 HTTP 响应。</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method,
        string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request);
    }

    /// <summary>
    /// 构造完整的 1×1 RGBA PNG。
    /// </summary>
    /// <returns>可校验的 PNG 字节。</returns>
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
    /// 写入带 CRC 的 PNG 分块。
    /// </summary>
    /// <param name="destination">目标流。</param>
    /// <param name="kind">分块种类。</param>
    /// <param name="content">分块内容。</param>
    private static void Chunk(Stream destination, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> content)
    {
        Span<byte> length = stackalloc byte[4];
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
}
