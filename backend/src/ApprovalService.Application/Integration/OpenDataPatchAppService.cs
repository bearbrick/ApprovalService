using System.Text.Json;
using System.Text.Json.Nodes;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 仅允许审批单所属应用修改发布版本显式授权的审批数据字段。
/// </summary>
/// <param name="repository">审批单与锁定版本仓储。</param>
/// <param name="organization">当前应用严格人员绑定。</param>
/// <param name="contexts">更新后可信路由与组织快照。</param>
/// <param name="runtime">唯一引擎命令入口。</param>
/// <param name="queries">更新后的审批单查询。</param>
/// <param name="timeProvider">固定命令时间。</param>
public sealed class OpenDataPatchAppService(IApprovalRuntimeRepository repository,
    IOrganizationLookupAppService organization, OpenStartContextBuilder contexts,
    IApprovalRuntimeAppService runtime, OpenApprovalQueryAppService queries, TimeProvider timeProvider)
{
    /// <summary>
    /// 合并部分字段，校验并重算公式，最后与事件和路由映射原子提交。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="instanceId">所属审批单 ID。</param>
    /// <param name="request">已绑定实际操作人与部分数据。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>更新后的审批单。</returns>
    public async Task<OpenInstanceResponse> PatchAsync(ClientAppEntity app, string instanceId,
        OpenDataPatchRequest request, string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(requestId) || request is null ||
            string.IsNullOrWhiteSpace(request.User) || request.Data.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批数据更新必须包含已绑定操作人和数据对象。");
        }

        var snapshot = await repository.LoadAsync(instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在。");
        var instance = snapshot.Instance;
        if (instance.ClientAppCode != app.Code)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用不能修改其他应用的审批单。");
        }

        var actorId = await organization.ResolveBoundPersonAsync(app.Code, request.User, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.Forbidden, "操作人员未在当前接入应用绑定或已离职。");
        var state = JsonSerializer.Deserialize<EngineState>(instance.StateJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单引擎状态为空。");
        if (state.ProcessedRequestIds.Contains(requestId))
        {
            return await queries.GetInstanceAsync(app, instanceId, cancellationToken);
        }

        if (state.Status != EngineInstanceStatuses.Running)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "只有审批进行中的审批单可以更新业务数据。");
        }

        var definition = JsonSerializer.Deserialize<FlowDefinition>(snapshot.FlowVersion.DefinitionJson,
            ApprovalJson.Options) ?? throw new InvalidOperationException("审批单锁定的流程定义为空。");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(snapshot.FlowVersion.GraphJson,
            ApprovalJson.Options) ?? throw new InvalidOperationException("审批单锁定的执行图为空。");
        var merged = ApplyPatch(definition.Data, instance.ApprovalDataJson, request.Data);
        var prepared = OpenApprovalDataPreparer.Prepare(definition.Data, merged);
        var changes = CreateChanges(definition.Data, instance.ApprovalDataJson, prepared);
        if (changes.ValueKind != JsonValueKind.Object || !changes.EnumerateObject().Any())
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批数据没有发生变化。");
        }

        var inputs = await contexts.RefreshRoutingInputsAsync(app.Code, graph, prepared,
            instance.RoutingInputsJson, cancellationToken);
        var routingJson = JsonSerializer.Serialize(inputs, ApprovalJson.Options);
        // 只修改无追踪的读取快照，供本次路由计算使用；仓储在引擎成功后才原子保存。
        instance.ApprovalDataJson = prepared.GetRawText();
        instance.RoutingInputsJson = routingJson;
        var context = await contexts.BuildContinuationAsync(instance, definition, graph,
            timeProvider.GetUtcNow(), cancellationToken);
        await runtime.ExecuteAsync(new RuntimeCommandInput
        {
            Command = new EngineCommand
            {
                Kind = OperationKinds.UpdateData,
                InstanceId = instanceId,
                RequestId = requestId,
                Operation = new OperationInput
                {
                    ActorId = actorId,
                    IsOwningClientApp = true,
                    DataChanges = changes,
                },
            },
            Context = context,
            UpdatedApprovalDataJson = instance.ApprovalDataJson,
            UpdatedRoutingInputsJson = routingJson,
        }, cancellationToken);
        return await queries.GetInstanceAsync(app, instanceId, cancellationToken);
    }

    /// <summary>
    /// 按发布字段授权合并顶层值，未授权明细表只允许同长度逐行修改显式开放的子字段。
    /// </summary>
    /// <param name="definition">锁定的数据定义。</param>
    /// <param name="previousJson">审批单原数据快照。</param>
    /// <param name="patch">只含变更字段的对象。</param>
    /// <returns>待整体验证与重算公式的数据对象。</returns>
    private static JsonElement ApplyPatch(FlowDataDefinition definition, string previousJson, JsonElement patch)
    {
        var root = JsonNode.Parse(previousJson) as JsonObject
            ?? throw new InvalidOperationException("审批单原数据快照不是对象。");
        var fields = definition.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        foreach (var changed in patch.EnumerateObject())
        {
            if (!fields.TryGetValue(changed.Name, out var field) || field.ApproverInput ||
                !string.IsNullOrWhiteSpace(field.Formula))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"字段 {changed.Name} 不存在或不能由业务系统更新。");
            }

            if (field.BusinessUpdateAllowed)
            {
                root[field.Key] = JsonNode.Parse(changed.Value.GetRawText());
            }
            else if (field.Type == FlowFieldTypes.DetailTable)
            {
                ApplyRowPatch(field, root, changed.Value);
            }
            else
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"字段 {changed.Name} 未开启审批中业务更新。");
            }
        }

        return JsonSerializer.SerializeToElement(root, ApprovalJson.Options);
    }

    /// <summary>
    /// 在行数不变时以索引更新明细子字段，防止借局部更新新增或删除未经授权的行。
    /// </summary>
    /// <param name="table">明细表字段定义。</param>
    /// <param name="root">原数据对象。</param>
    /// <param name="patch">与原行数一致的逐行补丁数组。</param>
    private static void ApplyRowPatch(FlowFieldDefinition table, JsonObject root, JsonElement patch)
    {
        if (root[table.Key] is not JsonArray rows || patch.ValueKind != JsonValueKind.Array ||
            patch.GetArrayLength() != rows.Count)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                $"明细表 {table.Key} 的子字段更新必须按现有行数逐行提交。");
        }

        var children = table.Children.ToDictionary(child => child.Key, StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var rowPatch = patch[index];
            if (rowPatch.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (rowPatch.ValueKind != JsonValueKind.Object || rows[index] is not JsonObject row)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"明细表 {table.Key} 第 {index + 1} 行更新必须是对象或 null。");
            }

            foreach (var changed in rowPatch.EnumerateObject())
            {
                if (!children.TryGetValue(changed.Name, out var child) || !child.BusinessUpdateAllowed ||
                    child.ApproverInput || !string.IsNullOrWhiteSpace(child.Formula))
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                        $"明细字段 {table.Key}.{changed.Name} 未开启审批中业务更新。");
                }

                row[child.Key] = JsonNode.Parse(changed.Value.GetRawText());
            }
        }
    }

    /// <summary>
    /// 只记录有变化的顶层字段，明细表保留该字段的整组改前改后值。
    /// </summary>
    /// <param name="definition">锁定的数据定义。</param>
    /// <param name="previousJson">原数据快照。</param>
    /// <param name="prepared">完成公式重算的新快照。</param>
    /// <returns>字段名到 before/after 值的 JSON 对象。</returns>
    private static JsonElement CreateChanges(FlowDataDefinition definition, string previousJson, JsonElement prepared)
    {
        var before = JsonNode.Parse(previousJson) as JsonObject
            ?? throw new InvalidOperationException("审批单原数据快照不是对象。");
        var after = JsonNode.Parse(prepared.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("审批单更新后的数据快照不是对象。");
        var changes = new JsonObject();
        foreach (var field in definition.Fields)
        {
            if (JsonNode.DeepEquals(before[field.Key], after[field.Key]))
            {
                continue;
            }

            changes[field.Key] = new JsonObject
            {
                ["before"] = before[field.Key]?.DeepClone(),
                ["after"] = after[field.Key]?.DeepClone(),
            };
        }

        return JsonSerializer.SerializeToElement(changes, ApprovalJson.Options);
    }
}
