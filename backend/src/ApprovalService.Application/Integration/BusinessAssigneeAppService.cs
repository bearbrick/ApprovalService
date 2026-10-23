using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 仅在引擎实际进入业务取人节点时，向审批单所属应用取得可信人员快照。
/// </summary>
/// <param name="apps">接入应用登记仓储。</param>
/// <param name="client">双向验签的业务出站接口。</param>
/// <param name="organization">当前应用的严格人员绑定查询。</param>
public sealed class BusinessAssigneeAppService(IClientAppRepository apps, IApprovalHookClient client,
    IOrganizationLookupAppService organization)
{
    /// <summary>
    /// 补齐一次实际到达节点的人员；不修改审批单持久状态。
    /// </summary>
    /// <param name="instance">审批单快照。</param>
    /// <param name="requirement">引擎报告的实际命中节点。</param>
    /// <param name="context">待补齐的可信引擎上下文。</param>
    /// <param name="requestId">原审批命令请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public async Task PrepareAsync(InstanceEntity instance, BusinessAssigneeRequirement requirement,
        EngineContext context, string requestId, CancellationToken cancellationToken = default)
    {
        var app = await apps.FindByCodeAsync(instance.ClientAppCode, cancellationToken);
        if (app is null || !app.IsEnabled)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "审批单所属接入应用不存在或已停用。");
        }

        if (string.IsNullOrWhiteSpace(app.AssigneeUrl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前接入应用未配置业务取人接口。");
        }

        var inputs = JsonSerializer.Deserialize<OpenRoutingInputs>(instance.RoutingInputsJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单发起路由快照为空。");
        if (string.IsNullOrWhiteSpace(inputs.InitiatorExternalId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批单缺少当前应用绑定的发起人员 ID。");
        }

        using var data = JsonDocument.Parse(instance.ApprovalDataJson);
        var hookRequestId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            requestId + ":" + requirement.NodeId))).ToLowerInvariant();
        var response = await client.InvokeAsync(app, app.AssigneeUrl, new AssigneeHookRequest
        {
            FlowCode = instance.FlowCode,
            BusinessKey = instance.BusinessKey,
            NodeId = requirement.NodeId,
            SourceValue = requirement.SourceValue,
            Initiator = inputs.InitiatorExternalId,
            Data = data.RootElement,
        }, hookRequestId, cancellationToken);
        AssigneeHookResponse? result;
        try
        {
            result = response.Deserialize<AssigneeHookResponse>(ApprovalJson.Options);
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务系统取人响应格式不正确。");
        }

        if (result?.UserIds is null)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务系统取人响应缺少 userIds 列表。");
        }

        var people = new List<string>();
        foreach (var externalId in result.UserIds)
        {
            if (string.IsNullOrWhiteSpace(externalId))
            {
                throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务系统取人响应含空人员 ID。");
            }

            var personId = await organization.ResolveBoundPersonAsync(app.Code, externalId, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"业务取人返回的人员 {externalId} 未在当前接入应用绑定或已经离职。");
            if (!people.Contains(personId, StringComparer.Ordinal))
            {
                people.Add(personId);
            }
        }

        context.AssigneeSnapshot ??= new();
        context.AssigneeSnapshot.BusinessApiResults[requirement.NodeId] = people;
    }
}
