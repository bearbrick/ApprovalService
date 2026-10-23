using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Jobs;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 以真实 HTTP 和 SQLite 检查自动动作人工重试的流程范围权限。
/// </summary>
internal static class ActionRetryEndpointChecks
{
    /// <summary>
    /// 验证认证、按流程授权和原事件重排。
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
            string flowAdminToken;
            string organizationAdminToken;
            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using (var db = await dbFactory.CreateDbContextAsync())
                {
                    db.People.AddRange(
                        new PersonEntity { Id = "flow-admin", EmployeeNumber = "FLOW-ADMIN", Name = "流程管理员" },
                        new PersonEntity { Id = "organization-admin", EmployeeNumber = "ORG-ADMIN", Name = "组织管理员" });
                    db.OrganizationRoles.Add(new OrganizationRoleEntity
                    {
                        Code = "organization-admin",
                        Name = "组织管理员",
                    });
                    db.PersonRoles.Add(new PersonRoleEntity
                    {
                        PersonId = "organization-admin",
                        RoleCode = "organization-admin",
                    });
                    db.Flows.AddRange(
                        new FlowEntity
                        {
                            Id = "flow-one",
                            Code = "FLOW_ONE",
                            Name = "第一流程",
                            AdministratorId = "FLOW-ADMIN"
                        },
                        new FlowEntity
                        {
                            Id = "flow-two",
                            Code = "FLOW_TWO",
                            Name = "第二流程",
                            AdministratorId = "ORG-ADMIN"
                        });
                    db.FlowVersions.AddRange(
                        new FlowVersionEntity { Id = "version-one", FlowId = "flow-one", Version = 1 },
                        new FlowVersionEntity { Id = "version-two", FlowId = "flow-two", Version = 1 });
                    db.Instances.AddRange(
                        new InstanceEntity
                        {
                            Id = "instance-one",
                            FlowId = "flow-one",
                            FlowCode = "FLOW_ONE",
                            FlowVersionId = "version-one",
                            SerialNo = "ACTION-ONE",
                            BusinessKey = "ONE",
                            Title = "第一审批"
                        },
                        new InstanceEntity
                        {
                            Id = "instance-two",
                            FlowId = "flow-two",
                            FlowCode = "FLOW_TWO",
                            FlowVersionId = "version-two",
                            SerialNo = "ACTION-TWO",
                            BusinessKey = "TWO",
                            Title = "第二审批"
                        });
                    db.OutboxItems.AddRange(ActionItem("instance-one", "first/event"),
                        ActionItem("instance-two", "second/event"));
                    await db.SaveChangesAsync();
                }

                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                await identity.ProvisionAccountAsync("flow-admin", "temporary-password-123");
                await identity.ChangePasswordAsync("flow-admin", "temporary-password-123", "current-password-123");
                flowAdminToken = (await identity.LoginAsync("FLOW-ADMIN", "current-password-123")).AccessToken;
                await identity.ProvisionAccountAsync("organization-admin", "temporary-password-456");
                await identity.ChangePasswordAsync("organization-admin", "temporary-password-456",
                    "current-password-456");
                organizationAdminToken = (await identity.LoginAsync("ORG-ADMIN", "current-password-456"))
                    .AccessToken;
            }

            using (var anonymous = await RetryAsync(client, "first/event", null))
            {
                check("人工动作重试不接受匿名或单独 API Key", anonymous.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var otherFlow = await RetryAsync(client, "second/event", flowAdminToken))
            {
                check("流程管理员不能重试其他流程的自动动作", otherFlow.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var organizationOnly = await RetryAsync(client, "first/event", organizationAdminToken))
            {
                check("组织管理员角色不自动获得其他流程的动作重试权",
                    organizationOnly.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var allowed = await RetryAsync(client, "first/event", flowAdminToken))
            {
                check("所属流程管理员以原事件 ID 人工重试", allowed.StatusCode == HttpStatusCode.NoContent);
            }

            using (var repeated = await RetryAsync(client, "first/event", flowAdminToken))
            {
                check("已重排的动作不能同时再次重排", repeated.StatusCode == HttpStatusCode.Conflict);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var first = await db.OutboxItems.AsNoTracking().SingleAsync(item => item.EventId == "first/event");
                var second = await db.OutboxItems.AsNoTracking().SingleAsync(item => item.EventId == "second/event");
                check("人工重试保留原事件且跨流程拒绝未改队列", first.Id == "first/event:dispatch" &&
                    first.Attempts >= 9 && second.Status == JobStatuses.NeedsAttention && second.Attempts == 10);
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 构造已达上限的原业务动作待发项。
    /// </summary>
    /// <param name="instanceId">所属审批单。</param>
    /// <param name="eventId">原事件 ID。</param>
    /// <returns>持久待发项。</returns>
    private static OutboxItemEntity ActionItem(string instanceId, string eventId) => new()
    {
        Id = eventId + ":dispatch",
        InstanceId = instanceId,
        EventId = eventId,
        Kind = "engineEvent",
        PayloadJson = JsonSerializer.Serialize(new
        {
            kind = "actionPending"
        }),
        Status = JobStatuses.NeedsAttention,
        Attempts = 10,
        AvailableAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// 向管理路由发送携带可选本地令牌的人工重试请求。
    /// </summary>
    /// <param name="client">进程内 HTTP 客户端。</param>
    /// <param name="eventId">原事件 ID。</param>
    /// <param name="token">本地 JWT，匿名时为空。</param>
    /// <returns>尚未释放的响应。</returns>
    private static async Task<HttpResponseMessage> RetryAsync(HttpClient client, string eventId, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.AdminActionRetry)
        {
            Content = JsonContent.Create(new AdminActionRetryRequest(eventId)),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }
}
