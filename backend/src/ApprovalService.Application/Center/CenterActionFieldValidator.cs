using System.Text.Json;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Center;

/// <summary>
/// 按锁定流程和可信内部组织 ID 校验中心填写的审批人字段。
/// </summary>
internal static class CenterActionFieldValidator
{
    /// <summary>
    /// 只接受当前节点可编辑字段，成功推进时强制其必填项。
    /// </summary>
    /// <param name="definition">审批单锁定的流程定义。</param>
    /// <param name="node">当前执行节点。</param>
    /// <param name="action">待执行命令。</param>
    /// <param name="values">人员提交的字段对象。</param>
    /// <param name="organization">可信组织仓储。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>经验证的独立字段快照。</returns>
    public static async Task<JsonElement?> ValidateAsync(FlowDefinition definition, FlowExecutionNode node,
        string action, JsonElement? values, IOrganizationRepository organization,
        CancellationToken cancellationToken = default)
    {
        var advancing = action is EngineCommandKinds.Approve or EngineCommandKinds.Handle or
            EngineCommandKinds.Acknowledge;
        var editable = definition.Data.Fields.Where(field => field.ApproverInput &&
            string.IsNullOrWhiteSpace(field.Formula) && node.FieldPermissions.Count(permission =>
                permission.FieldKey == field.Key && permission.Access == FlowFieldAccess.Editable) == 1)
            .ToDictionary(field => field.Key, StringComparer.Ordinal);
        if (values is null)
        {
            if (advancing && editable.Values.Any(field => field.Required))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前节点的必填审批人字段不能为空。");
            }
            return null;
        }
        if (values.Value.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批人填写字段必须是对象。");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        OrganizationImportBatch? master = null;
        foreach (var property in values.Value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"审批人填写字段 {property.Name} 重复。");
            }
            if (!editable.TryGetValue(property.Name, out var field))
            {
                throw new UserFriendlyException(ErrorCodes.Forbidden, $"当前节点不能填写字段 {property.Name}。");
            }
            if (field.Type is FlowFieldTypes.DetailTable or FlowFieldTypes.Attachment)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前字段类型尚无安全的中心提交协议。");
            }
            var inputField = new FlowFieldDefinition
            {
                Key = field.Key,
                Name = field.Name,
                Type = field.Type,
                Required = advancing && field.Required,
            };
            var issue = ApprovalDataValidator.Validate(new FlowDataDefinition { Fields = [inputField] },
                values.Value).FirstOrDefault();
            if (issue is not null)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"{issue.Field}：{issue.Message}");
            }
            if (property.Value.ValueKind == JsonValueKind.Null ||
                field.Type is not (FlowFieldTypes.Person or FlowFieldTypes.Department))
            {
                continue;
            }
            master ??= await organization.LoadMasterDataAsync(cancellationToken);
            var id = property.Value.GetString();
            var exists = field.Type == FlowFieldTypes.Person
                ? master.People.Any(person => person.Id == id && person.IsActive)
                : master.Departments.Any(department => department.Id == id);
            if (!exists)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"字段 {field.Key} 引用了不存在或已停用的组织对象。");
            }
        }
        if (advancing && editable.Values.Any(field => field.Required && !seen.Contains(field.Key)))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前节点的必填审批人字段不能为空。");
        }
        return values.Value.Clone();
    }
}
