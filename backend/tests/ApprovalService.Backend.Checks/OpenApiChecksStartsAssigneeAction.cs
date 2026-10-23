using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Integration;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 以真实数据库和假签名业务端验证按需取人及持久自动动作。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 验证严格绑定、事务前取人、提交后动作、失败重试和事件幂等回写。
    /// </summary>
    /// <param name="services">已初始化的服务范围。</param>
    /// <param name="app">测试接入应用。</param>
    /// <param name="hookClient">指向假业务端的双向验签客户端。</param>
    /// <param name="handler">可控制业务响应的假业务端。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunAssigneeActionHooksAsync(IServiceProvider services, ClientAppEntity app,
        IApprovalHookClient hookClient, HookHandler handler, Action<string, bool> check)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        var assignGraph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        assignGraph.Nodes.Single(node => node.Id == "n1").Assignee = new FlowAssignee
        {
            Source = FlowAssigneeSources.BusinessApi,
            Value = "business-reviewers",
        };
        var actionGraph = EngineScenarioChecks.Chain(FlowNodeTypes.Automatic);
        actionGraph.Nodes.Single(node => node.Id == "n1").Automation = new FlowAutomationSettings
        {
            ActionCode = "reserve-budget",
        };
        var data = new FlowDataDefinition
        {
            Fields = [new FlowFieldDefinition { Key = "amount", Name = "金额", Type = FlowFieldTypes.Money }],
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var storedApp = await db.ClientApps.SingleAsync(item => item.Code == app.Code);
            storedApp.AssigneeUrl = "https://business.invalid/assignee";
            storedApp.ActionUrl = "https://business.invalid/action";
            db.Flows.AddRange(
                new FlowEntity { Id = "hook-assign", Code = "HOOK_ASSIGN", Name = "业务取人", PublishedVersionId = "hook-assign-v1" },
                new FlowEntity { Id = "hook-action", Code = "HOOK_ACTION", Name = "自动动作", PublishedVersionId = "hook-action-v1" });
            db.FlowVersions.AddRange(
                new FlowVersionEntity
                {
                    Id = "hook-assign-v1",
                    FlowId = "hook-assign",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
                    {
                        Code = "HOOK_ASSIGN",
                        Name = "业务取人",
                        Data = data,
                    }, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(assignGraph, ApprovalJson.Options),
                },
                new FlowVersionEntity
                {
                    Id = "hook-action-v1",
                    FlowId = "hook-action",
                    Version = 1,
                    SchemaVersion = 1,
                    DefinitionJson = JsonSerializer.Serialize(new FlowDefinition
                    {
                        Code = "HOOK_ACTION",
                        Name = "自动动作",
                        Data = data,
                    }, ApprovalJson.Options),
                    GraphJson = JsonSerializer.Serialize(actionGraph, ApprovalJson.Options),
                });
            await db.SaveChangesAsync();
        }

        var apps = services.GetRequiredService<IClientAppRepository>();
        var organization = services.GetRequiredService<IOrganizationLookupAppService>();
        var repository = services.GetRequiredService<IApprovalRuntimeRepository>();
        var runtime = new ApprovalRuntimeAppService(repository,
            new BusinessAssigneeAppService(apps, hookClient, organization));
        var start = new OpenApprovalStartAppService(
            services.GetRequiredService<FlowCatalogAppService>(),
            services.GetRequiredService<OpenApprovalQueryAppService>(), organization,
            services.GetRequiredService<OpenStartContextBuilder>(), runtime,
            services.GetRequiredService<IApprovalSerialNumberRepository>(),
            services.GetRequiredService<OpenStartSerialOptions>(),
            services.GetRequiredService<IntegrationTransportPolicy>(),
            new BusinessHookAppService(hookClient), TimeProvider.System);
        var request = new OpenStartRequest
        {
            FlowCode = "HOOK_ASSIGN",
            BusinessKey = "HOOK-ASSIGNEE-REJECT",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 13 }),
        };
        handler.Mode = "unbound-assignee";
        UserFriendlyException? unbound = null;
        try
        {
            await start.StartAsync(app, request, "hook-assignee-unbound");
        }
        catch (UserFriendlyException error)
        {
            unbound = error;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            check("业务取人返回全局工号不能绕过本应用绑定，且失败不落审批单",
                unbound?.Code == ErrorCodes.ValidationFailed &&
                !await db.Instances.AnyAsync(item => item.BusinessKey == request.BusinessKey));
        }

        handler.Mode = "success";
        request.BusinessKey = "HOOK-ASSIGNEE-OK";
        var assigned = await start.StartAsync(app, request, "hook-assignee-ok");
        check("实际进入业务取人节点后用已绑定人员建立任务",
            assigned.Tasks.Single().AssigneeId == "approver" &&
            handler.Requests.Any(item => item.Path == "/assignee" && item.IsSigned &&
                item.Body.Contains("\"initiator\":\"alice\"", StringComparison.Ordinal) &&
                item.Body.Contains("\"nodeId\":\"n1\"", StringComparison.Ordinal)));

        var actionRequest = new OpenStartRequest
        {
            FlowCode = "HOOK_ACTION",
            BusinessKey = "HOOK-ACTION-OK",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 13 }),
        };
        var pending = await start.StartAsync(app, actionRequest, "hook-action-start");
        OutboxItemEntity queued;
        await using (var db = await factory.CreateDbContextAsync())
        {
            queued = await db.OutboxItems.AsNoTracking().SingleAsync(item => item.InstanceId == pending.Id &&
                item.Kind == "engineEvent" && item.PayloadJson.Contains("actionPending"));
        }

        check("自动节点先提交等待状态和持久待发事件，尚未调用业务端",
            pending.Status == EngineInstanceStatuses.Running &&
            !handler.Requests.Any(item => item.Path == "/action"));
        var outbox = new EngineEventOutboxHandler(repository, apps, hookClient,
            services.GetRequiredService<OpenStartContextBuilder>(), runtime, TimeProvider.System);
        handler.Mode = "reject-action";
        UserFriendlyException? refused = null;
        try
        {
            await outbox.DeliverAsync(queued, CancellationToken.None);
        }
        catch (UserFriendlyException error)
        {
            refused = error;
        }

        var stillPending = await repository.LoadAsync(pending.Id);
        check("业务动作失败时保留等待令牌供持久队列重试",
            refused?.Code == ErrorCodes.BusinessHookFailed &&
            stillPending?.Instance.Status == EngineInstanceStatuses.Running);

        handler.Mode = "success";
        await outbox.DeliverAsync(queued, CancellationToken.None);
        var afterSuccess = await repository.LoadAsync(pending.Id);
        var requestsBeforeReplay = handler.Requests.Count(item => item.Path == "/action");
        await outbox.DeliverAsync(queued, CancellationToken.None);
        check("自动动作按事件 ID 提交引擎并在重领后不重复发送",
            afterSuccess?.Instance.Status == EngineInstanceStatuses.Approved &&
            handler.Requests.Count(item => item.Path == "/action") == requestsBeforeReplay &&
            handler.Requests.Any(item => item.Path == "/action" && item.IsSigned &&
                item.Body.Contains("\"eventId\":\"" + queued.EventId + "\"", StringComparison.Ordinal)));
    }
}
