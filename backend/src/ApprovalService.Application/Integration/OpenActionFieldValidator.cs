using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 仅允许人工任务提交当前节点显式可写的顶层审批人字段。
/// </summary>
public static class OpenActionFieldValidator
{
    /// <summary>
    /// 校验字段授权、类型及本应用人员部门绑定，返回独立的审计快照。
    /// </summary>
    /// <param name="appCode">已认证接入应用编码。</param>
    /// <param name="definition">审批单锁定的流程定义。</param>
    /// <param name="node">正在操作的执行图节点。</param>
    /// <param name="values">请求提交的字段对象。</param>
    /// <param name="organization">本应用既有组织绑定查询。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>通过校验的独立字段快照；未提交时为空。</returns>
    public static async Task<JsonElement?> ValidateAsync(string appCode, FlowDefinition definition,
        FlowExecutionNode node, JsonElement? values, IOrganizationLookupAppService organization,
        CancellationToken cancellationToken = default)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Value.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批人填写字段必须是对象。");
        }

        var fields = definition.Data.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in values.Value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"审批人填写字段 {property.Name} 重复。");
            }

            if (!fields.TryGetValue(property.Name, out var field))
            {
                var detailChild = definition.Data.Fields.Any(table => table.Type == FlowFieldTypes.DetailTable &&
                    table.Children.Any(child => property.Name == table.Key + "." + child.Key));
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, detailChild
                    ? "明细子字段缺少行索引协议，暂不能经任务操作提交。"
                    : $"审批人填写字段 {property.Name} 未在流程中定义。");
            }

            if (field.Type == FlowFieldTypes.DetailTable)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "明细表审批人字段缺少行索引协议，暂不能经任务操作提交。");
            }

            var permissions = node.FieldPermissions.Where(permission => permission.FieldKey == field.Key).ToArray();
            if (!field.ApproverInput || !string.IsNullOrWhiteSpace(field.Formula) ||
                permissions.Length != 1 || permissions[0].Access != FlowFieldAccess.Editable)
            {
                throw new UserFriendlyException(ErrorCodes.Forbidden,
                    $"当前节点不能填写字段 {field.Key}。");
            }

            if (field.Type == FlowFieldTypes.Attachment)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "附件归属校验尚未启用，暂不能提交附件字段。");
            }

            // 复用发起数据验证器的类型和日期规则，避免两套不同的字段格式。
            var inputField = new FlowFieldDefinition
            {
                Key = field.Key,
                Name = field.Name,
                Type = field.Type,
                Required = field.Required,
            };
            var issue = ApprovalDataValidator.Validate(new FlowDataDefinition { Fields = [inputField] },
                values.Value).FirstOrDefault();
            if (issue is not null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"{issue.Field}：{issue.Message}");
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (field.Type == FlowFieldTypes.Person &&
                await organization.ResolveBoundPersonAsync(appCode, property.Value.GetString()!,
                    cancellationToken) is null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"人员字段 {field.Key} 未在当前应用绑定或人员已离职。");
            }

            if (field.Type == FlowFieldTypes.Department &&
                await organization.ResolveBoundDepartmentAsync(appCode, property.Value.GetString()!,
                    cancellationToken) is null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"部门字段 {field.Key} 未在当前应用绑定或部门已不存在。");
            }
        }

        return values.Value.Clone();
    }
}
