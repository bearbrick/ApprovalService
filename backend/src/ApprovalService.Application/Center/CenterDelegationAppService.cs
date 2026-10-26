using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Center;

/// <summary>
/// 管理个人委托设置，并逐项把尚未处理的任务交给生效代理人。
/// </summary>
/// <param name="rules">持久委托规则。</param>
/// <param name="people">可信组织人员。</param>
/// <param name="tasks">审批任务归属查询。</param>
/// <param name="contexts">可信组织与路由上下文。</param>
/// <param name="runtime">统一引擎命令入口。</param>
/// <param name="clock">服务端 UTC 时间。</param>
public sealed class CenterDelegationAppService(IDelegationRuleRepository rules, IOrganizationRepository people,
    IApprovalOpenQueryRepository tasks, OpenStartContextBuilder contexts, IApprovalRuntimeAppService runtime,
    TimeProvider clock)
{
    /// <summary>
    /// 按身份范围读取委托规则。
    /// </summary>
    /// <param name="actorId">已认证操作人。</param>
    /// <param name="principalId">委托人。</param>
    /// <param name="isAdministrator">已由服务端认证的组织管理员权限。</param>
    /// <param name="isLocal">本地账号会话。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>规则列表。</returns>
    public async Task<IReadOnlyList<CenterDelegationResponse>> ListAsync(string actorId, string principalId,
        bool isAdministrator, bool isLocal, CancellationToken cancellationToken = default)
    {
        Authorize(actorId, principalId, isAdministrator, isLocal);
        return (await rules.ListAsync(principalId, cancellationToken)).Select(Map).ToArray();
    }

    /// <summary>
    /// 在服务端串行化事务中新增一项时间和流程范围均有效的规则。
    /// </summary>
    /// <param name="actorId">已认证操作人。</param>
    /// <param name="principalId">委托人。</param>
    /// <param name="isAdministrator">已核实组织管理员权限。</param>
    /// <param name="isLocal">本地账号会话。</param>
    /// <param name="input">规则输入。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>保存后的规则。</returns>
    public async Task<CenterDelegationResponse> CreateAsync(string actorId, string principalId,
        bool isAdministrator, bool isLocal, CenterDelegationRequest input,
        CancellationToken cancellationToken = default)
    {
        Authorize(actorId, principalId, isAdministrator, isLocal);
        if (input is null || input.FlowCodes is null || string.IsNullOrWhiteSpace(input.DelegatePersonId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "委托设置格式不正确。");
        }
        if (await people.GetPersonAsync(principalId, cancellationToken) is not { IsActive: true } ||
            await people.GetPersonAsync(input.DelegatePersonId, cancellationToken) is not { IsActive: true })
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "委托人或代理人不存在或已停用。");
        }
        var rule = new DelegationRuleEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            PrincipalId = principalId,
            DelegateId = input.DelegatePersonId,
            StartsAtUtc = input.StartsAtUtc,
            EndsAtUtc = input.EndsAtUtc,
            FlowCodes = input.FlowCodes,
        };
        DelegationRules.Validate(rule, []);
        await rules.CreateAsync(rule, cancellationToken);
        return Map(rule);
    }

    /// <summary>
    /// 删除规则仅影响今后产生的任务，既有任务保持原代理关系。
    /// </summary>
    /// <param name="actorId">已认证操作人。</param>
    /// <param name="principalId">委托人。</param>
    /// <param name="isAdministrator">已核实组织管理员权限。</param>
    /// <param name="isLocal">本地账号会话。</param>
    /// <param name="id">规则 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public async Task DeleteAsync(string actorId, string principalId, bool isAdministrator, bool isLocal,
        string id, CancellationToken cancellationToken = default)
    {
        Authorize(actorId, principalId, isAdministrator, isLocal);
        if (string.IsNullOrWhiteSpace(id) || !await rules.DeleteAsync(principalId, id, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "委托设置不存在。");
        }
    }

    /// <summary>
    /// 逐项独立处理已有任务；原任务完成、范围失效或并发改变均作为单项错误返回。
    /// </summary>
    /// <param name="actorId">真实已认证操作人。</param>
    /// <param name="principalId">任务原审批人。</param>
    /// <param name="isAdministrator">已核实组织管理员权限。</param>
    /// <param name="isLocal">本地账号会话。</param>
    /// <param name="input">任务 ID 与管理员原因。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>与输入同顺序的逐项结果。</returns>
    public async Task<IReadOnlyList<CenterDelegateTaskResult>> DelegateTasksAsync(string actorId,
        string principalId, bool isAdministrator, bool isLocal, CenterDelegateTasksRequest input,
        string requestId, CancellationToken cancellationToken = default)
    {
        Authorize(actorId, principalId, isAdministrator, isLocal);
        if (input?.TaskIds is null || input.TaskIds.Count is < 1 or > 100 ||
            input.TaskIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 200) ||
            input.TaskIds.Distinct(StringComparer.Ordinal).Count() != input.TaskIds.Count ||
            string.IsNullOrWhiteSpace(requestId) ||
            (isAdministrator && string.IsNullOrWhiteSpace(input.Reason)))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "待转委托任务或管理员原因不正确。");
        }
        var results = new List<CenterDelegateTaskResult>();
        for (var index = 0; index < input.TaskIds.Count; index++)
        {
            var taskId = input.TaskIds[index];
            try
            {
                await DelegateOneAsync(actorId, principalId, isAdministrator, input.Reason, taskId,
                    requestId + ":" + index, cancellationToken);
                results.Add(new CenterDelegateTaskResult { TaskId = taskId, Succeeded = true });
            }
            catch (UserFriendlyException error)
            {
                results.Add(new CenterDelegateTaskResult
                {
                    TaskId = taskId,
                    Code = error.Code,
                    Message = error.Message,
                });
            }
        }
        return results;
    }

    /// <summary>
    /// 使用当前规则和真实操作人执行单项引擎命令。
    /// </summary>
    /// <param name="actorId">真实操作人。</param>
    /// <param name="principalId">原审批人。</param>
    /// <param name="isAdministrator">已核实管理员权限。</param>
    /// <param name="reason">管理员原因。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    private async Task DelegateOneAsync(string actorId, string principalId, bool isAdministrator, string? reason,
        string taskId, string requestId, CancellationToken cancellationToken)
    {
        var record = await tasks.LoadTaskAsync(taskId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批任务不存在。");
        if (record.Task.AssigneeId != principalId)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只能转委托该人员尚未处理的原任务。");
        }
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(record.FlowVersion.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单执行图为空。");
        var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单流程定义为空。");
        var context = await contexts.BuildContinuationAsync(record.Instance, definition, graph,
            clock.GetUtcNow(), cancellationToken);
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = OperationKinds.DelegateExisting,
                InstanceId = record.Instance.Id,
                TaskId = taskId,
                RequestId = requestId,
                Operation = new OperationInput
                {
                    ActorId = actorId,
                    IsAdministrator = isAdministrator,
                    Reason = reason,
                },
            },
            Context = context,
        }, cancellationToken);
    }

    /// <summary>
    /// 应用层再次检查身份，阻止普通人员借其他入口修改他人设置。
    /// </summary>
    /// <param name="actorId">操作人。</param>
    /// <param name="principalId">委托人。</param>
    /// <param name="isAdministrator">服务端核实的管理员权限。</param>
    /// <param name="isLocal">本地会话。</param>
    private static void Authorize(string actorId, string principalId, bool isAdministrator, bool isLocal)
    {
        if (!isLocal || string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(principalId) ||
            (!isAdministrator && actorId != principalId))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "只有本人或组织管理员可以维护委托设置。");
        }
    }

    /// <summary>
    /// 将领域规则转换为人员接口响应。
    /// </summary>
    /// <param name="rule">规则实体。</param>
    /// <returns>不含内部存储 JSON 的响应。</returns>
    private static CenterDelegationResponse Map(DelegationRuleEntity rule) => new()
    {
        Id = rule.Id,
        PrincipalPersonId = rule.PrincipalId,
        DelegatePersonId = rule.DelegateId,
        StartsAtUtc = rule.StartsAtUtc,
        EndsAtUtc = rule.EndsAtUtc,
        FlowCodes = rule.FlowCodes,
    };
}
