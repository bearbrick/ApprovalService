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
/// 用真实 JWT 和 SQLite 检查个人订阅与管理员发送时间的 HTTP 权限边界。
/// </summary>
internal static class NotificationRulesChecksEndpoints
{
    /// <summary>
    /// 执行通知设置的认证和隔离场景。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
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
            await using var factory = new ServerFactory(backendRoot, directory, settings);
            using var client = factory.CreateClient();
            string adminToken;
            string userToken;
            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using (var db = await dbFactory.CreateDbContextAsync())
                {
                    db.People.AddRange(
                        new PersonEntity { Id = "admin", EmployeeNumber = "ADMIN-N", Name = "管理员" },
                        new PersonEntity { Id = "user", EmployeeNumber = "USER-N", Name = "审批人" });
                    db.OrganizationRoles.Add(new OrganizationRoleEntity
                    {
                        Code = "organization-admin",
                        Name = "组织管理员",
                    });
                    db.PersonRoles.Add(new PersonRoleEntity
                    {
                        PersonId = "admin",
                        RoleCode = "organization-admin",
                    });
                    await db.SaveChangesAsync();
                }

                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                await identity.ProvisionAccountAsync("admin", "temporary-admin-123");
                await identity.ProvisionAccountAsync("user", "temporary-user-123");
                await identity.ChangePasswordAsync("admin", "temporary-admin-123", "configured-admin-123");
                await identity.ChangePasswordAsync("user", "temporary-user-123", "configured-user-123");
                adminToken = (await identity.LoginAsync("ADMIN-N", "configured-admin-123")).AccessToken;
                userToken = (await identity.LoginAsync("USER-N", "configured-user-123")).AccessToken;
            }

            using (var anonymous = await client.GetAsync(ApiRoutes.NotificationPreferences))
            {
                check("未登录不能读取个人提醒设置", anonymous.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var update = Request(HttpMethod.Put,
                ApiRoutes.NotificationPreferenceByCategory.Replace("{category}", "newTask"), userToken,
                new UpdateNotificationPreferenceRequest(false, true)))
            using (var response = await client.SendAsync(update))
            {
                var value = await response.Content.ReadFromJsonAsync<NotificationPreferenceResponse>();
                check("人员只能用 JWT 身份设置自己的提醒渠道",
                    response.StatusCode == HttpStatusCode.OK && value?.Category == "newTask" &&
                    value.EmailEnabled == false && value.InAppEnabled);
            }

            using (var read = Request(HttpMethod.Get, ApiRoutes.NotificationPreferences, userToken))
            using (var response = await client.SendAsync(read))
            {
                var values = await response.Content.ReadFromJsonAsync<NotificationPreferenceResponse[]>();
                check("个人提醒偏好经真实 HTTP 持久读取",
                    response.StatusCode == HttpStatusCode.OK &&
                    values?.Single(item => item.Category == "newTask").EmailEnabled == false);
            }

            using (var read = Request(HttpMethod.Get, ApiRoutes.NotificationPreferences, adminToken))
            using (var response = await client.SendAsync(read))
            {
                var values = await response.Content.ReadFromJsonAsync<NotificationPreferenceResponse[]>();
                check("个人偏好按认证人员隔离，管理员不会读取他人的设置",
                    response.StatusCode == HttpStatusCode.OK &&
                    values?.Single(item => item.Category == "newTask").EmailEnabled == true);
            }

            using (var denied = Request(HttpMethod.Put, ApiRoutes.AdminDailyDigest, userToken,
                new UpdateDailyDigestSettingsRequest("08:30")))
            using (var response = await client.SendAsync(denied))
            {
                check("普通人员不能更改每日汇总的全局发送时间", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var update = Request(HttpMethod.Put, ApiRoutes.AdminDailyDigest, adminToken,
                new UpdateDailyDigestSettingsRequest("08:30")))
            using (var response = await client.SendAsync(update))
            {
                var value = await response.Content.ReadFromJsonAsync<DailyDigestSettingsResponse>();
                check("当前组织管理员可设置每日汇总的当地时刻",
                    response.StatusCode == HttpStatusCode.OK && value?.LocalTime == "08:30");
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 建立只以 JWT 身份授权的请求。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">契约地址。</param>
    /// <param name="token">有效人员访问令牌。</param>
    /// <param name="body">可选 JSON 输入。</param>
    /// <returns>待发送请求。</returns>
    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
