using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Definitions;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 只允许本服务兼容 BPMN 文件进入草稿，并显式导出指定来源。
/// </summary>
/// <param name="management">已有草稿写入和范围授权用例。</param>
/// <param name="repository">流程管理读取与身份快照。</param>
/// <param name="transfer">同事务复核权限的版本导出仓储。</param>
public sealed class FlowTransferAppService(FlowManagementAppService management,
    IFlowManagementRepository repository, IFlowTransferRepository transfer)
{
    /// <summary>
    /// 校验文件兼容性、同编码冲突和可能的发布问题，不保存内容。
    /// </summary>
    /// <param name="request">BPMN 文件内容。</param>
    /// <param name="actorId">当前内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只读预检结果。</returns>
    public async Task<AdminFlowImportPreviewResponse> PreviewImportAsync(AdminFlowImportPreviewRequest request,
        string actorId, bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var definition = ImportCompatible(request.BpmnXml);
        var existing = await repository.LoadAsync(definition.Code, cancellationToken);
        var canOverwrite = existing is not null && FlowManagementAccessRules.CanManage(identity.IsActive,
            isLocalSession, identity.IsOrganizationAdmin, identity.IsFlowAdmin,
            identity.DirectDepartmentIds, existing.Flow);
        var compilation = FlowCompiler.Compile(definition);
        return new AdminFlowImportPreviewResponse
        {
            Code = definition.Code,
            Name = definition.Name,
            NodeCount = definition.Canvas.Nodes.Count,
            HasCodeConflict = existing is not null,
            CanOverwrite = canOverwrite,
            ExistingDraftRevision = canOverwrite ? existing?.Draft?.Revision ?? 0 : null,
            PublishIssues = compilation.Issues.Select(issue => new AdminFlowImportIssue
            {
                ElementId = issue.ElementId,
                Message = issue.Message,
            }).ToArray(),
        };
    }

    /// <summary>
    /// 再次预检兼容文件；无目标时创建新草稿，有显式目标与修订时替换草稿。
    /// </summary>
    /// <param name="request">文件和写入前提。</param>
    /// <param name="actorId">当前内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>保存后的草稿详情。</returns>
    public async Task<AdminFlowResponse> ImportAsync(AdminFlowImportRequest request, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var definition = ImportCompatible(request.BpmnXml);
        if (request.TargetCode is null)
        {
            if (request.ExpectedRevision is not null)
            {
                throw Validation("新建流程不能填写覆盖修订号。");
            }

            if (await repository.LoadAsync(definition.Code, cancellationToken) is not null)
            {
                throw new UserFriendlyException(ErrorCodes.Conflict,
                    "同编码流程已存在；请显式指定目标和预期草稿修订后再覆盖。");
            }

            return await management.CreateAsync(new AdminFlowCreateRequest
            {
                Definition = definition,
                Group = request.Group,
                OwnerDepartmentId = request.OwnerDepartmentId,
                AdministratorId = request.AdministratorId,
                Reason = request.Reason,
            }, actorId, isLocalSession, cancellationToken);
        }

        if (request.TargetCode != definition.Code || request.ExpectedRevision is null)
        {
            throw Validation("覆盖已有流程须指定与文件相同的目标编码和预期草稿修订。");
        }

        var current = await management.GetAsync(request.TargetCode, actorId, isLocalSession,
            cancellationToken);
        return await management.SaveDraftAsync(request.TargetCode, new AdminFlowDraftRequest
        {
            ExpectedRevision = request.ExpectedRevision.Value,
            Definition = definition,
            Group = request.Group ?? current.Group,
            OwnerDepartmentId = request.OwnerDepartmentId ?? current.OwnerDepartmentId,
            AdministratorId = request.AdministratorId ?? current.AdministratorId,
            Reason = request.Reason,
        }, actorId, isLocalSession, cancellationToken);
    }

    /// <summary>
    /// 在当前管理权限下导出草稿或指定不可变发布版本。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="source">draft 或 published。</param>
    /// <param name="version">发布版本号；草稿时须留空。</param>
    /// <param name="actorId">当前内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>完整兼容 BPMN XML。</returns>
    public async Task<string> ExportAsync(string code, string? source, int? version, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        if (source != AdminFlowTransferSources.Draft && source != AdminFlowTransferSources.Published ||
            source == AdminFlowTransferSources.Draft && version is not null ||
            source == AdminFlowTransferSources.Published && version is null or <= 0)
        {
            throw Validation("导出须选择 draft 或 published；发布版须指定正整数版本号。");
        }

        var json = await transfer.LoadDefinitionAsync(code, source, version, actorId, isLocalSession,
            cancellationToken);
        var definition = JsonSerializer.Deserialize<FlowDefinition>(json, ApprovalJson.Options)
            ?? throw new InvalidOperationException("流程定义 JSON 为空。");
        try
        {
            return BpmnFlowCodec.Export(definition);
        }
        catch (ArgumentException exception)
        {
            throw Validation("当前草稿包含无法导出的节点或连线：" + exception.Message);
        }
    }

    /// <summary>
    /// 核对当前人员在职、本地会话及管理角色。
    /// </summary>
    /// <param name="actorId">内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前身份快照。</returns>
    private async Task<FlowManagerIdentity> RequireIdentityAsync(string actorId, bool isLocalSession,
        CancellationToken cancellationToken)
    {
        var identity = await repository.LoadIdentityAsync(actorId, cancellationToken);
        if (!isLocalSession || !identity.IsActive ||
            !(identity.IsOrganizationAdmin || identity.IsFlowAdmin))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "当前账号不能管理流程。");
        }

        return identity;
    }

    /// <summary>
    /// 解析完整本服务扩展并把不兼容文件转为可纠正错误。
    /// </summary>
    /// <param name="xml">BPMN XML。</param>
    /// <returns>文件内完整流程定义。</returns>
    private static FlowDefinition ImportCompatible(string xml)
    {
        try
        {
            return BpmnFlowCodec.Import(xml);
        }
        catch (FormatException exception)
        {
            throw Validation(exception.Message);
        }
    }

    /// <summary>
    /// 创建统一请求错误。
    /// </summary>
    /// <param name="message">中文说明。</param>
    /// <returns>用户可修正错误。</returns>
    private static UserFriendlyException Validation(string message) => new(ErrorCodes.ValidationFailed, message);
}
