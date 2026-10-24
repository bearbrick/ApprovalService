using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Definitions;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 本地流程管理员的草稿编辑与双重并发发布用例。
/// </summary>
/// <param name="repository">流程持久化与当前组织范围核对。</param>
/// <param name="timeProvider">统一操作时间。</param>
public sealed class FlowManagementAppService(IFlowManagementRepository repository, TimeProvider timeProvider)
{
    /// <summary>
    /// 只列出当前人员可管理的流程摘要。
    /// </summary>
    /// <param name="actorId">已认证的内部人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>有权限的流程摘要。</returns>
    public async Task<IReadOnlyList<AdminFlowResponse>> ListAsync(string actorId, bool isLocalSession,
        CancellationToken cancellationToken = default)
    {
        var identity = await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var flows = await repository.ListAsync(cancellationToken);
        return flows.Where(item => CanManage(identity, isLocalSession, item.Flow))
            .Select(item => Response(item, false)).ToArray();
    }

    /// <summary>
    /// 读取本人的流程草稿和当前发布版指针。
    /// </summary>
    /// <param name="code">流程编码。</param>
    /// <param name="actorId">已认证的内部人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>可编辑流程详情。</returns>
    public async Task<AdminFlowResponse> GetAsync(string code, string actorId, bool isLocalSession,
        CancellationToken cancellationToken = default)
    {
        var identity = await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var snapshot = await repository.LoadAsync(code, cancellationToken) ?? throw NotFound();
        if (!CanManage(identity, isLocalSession, snapshot.Flow))
        {
            throw NotFound();
        }

        return Response(snapshot, true);
    }

    /// <summary>
    /// 创建流程及第一个可编辑草稿。
    /// </summary>
    /// <param name="request">流程定义和元数据。</param>
    /// <param name="actorId">已认证的内部人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>创建后的流程详情。</returns>
    public async Task<AdminFlowResponse> CreateAsync(AdminFlowCreateRequest request, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var definition = RequireDefinition(request.Definition, null);
        ValidateMetadata(request.Group, request.OwnerDepartmentId, request.AdministratorId, request.Reason);
        var input = Write(definition, request.Group, request.OwnerDepartmentId, request.AdministratorId,
            request.Reason, actorId, isLocalSession);
        input.FlowId = Guid.NewGuid().ToString("N");
        return Response(await repository.CreateAsync(input, cancellationToken), true);
    }

    /// <summary>
    /// 使用预期修订号替换草稿和可编辑元数据。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="request">草稿及并发修订。</param>
    /// <param name="actorId">已认证的内部人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>保存后的流程详情。</returns>
    public async Task<AdminFlowResponse> SaveDraftAsync(string code, AdminFlowDraftRequest request,
        string actorId, bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var definition = RequireDefinition(request.Definition, code);
        ValidateMetadata(request.Group, request.OwnerDepartmentId, request.AdministratorId, request.Reason);
        if (request.ExpectedRevision < 0)
        {
            throw Validation("草稿预期修订号不能为负数。");
        }

        var input = Write(definition, request.Group, request.OwnerDepartmentId, request.AdministratorId,
            request.Reason, actorId, isLocalSession);
        input.ExpectedRevision = request.ExpectedRevision;
        return Response(await repository.ReplaceDraftAsync(input, cancellationToken), true);
    }

    /// <summary>
    /// 编译完整执行图，按草稿修订及当前发布版原子追加新版本。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="request">双重并发前提及发布原因。</param>
    /// <param name="actorId">已认证的内部人员 ID。</param>
    /// <param name="isLocalSession">是否为本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>发布后的流程详情。</returns>
    public async Task<AdminFlowResponse> PublishAsync(string code, AdminFlowPublishRequest request,
        string actorId, bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identity = await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        var snapshot = await repository.LoadAsync(code, cancellationToken) ?? throw NotFound();
        if (!CanManage(identity, isLocalSession, snapshot.Flow))
        {
            throw NotFound();
        }

        ValidateReason(request.Reason);
        var draft = snapshot.Draft ?? throw Conflict("流程草稿不存在，无法发布。");
        if (request.ExpectedRevision != draft.Revision ||
            request.ExpectedPublishedVersionId != snapshot.Flow.PublishedVersionId)
        {
            throw Conflict("流程草稿或当前发布版已变化，请刷新后重试。");
        }

        var definition = JsonSerializer.Deserialize<FlowDefinition>(draft.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("流程草稿 JSON 为空。");
        var compiled = FlowCompiler.Compile(definition);
        if (compiled.Graph is null)
        {
            var details = string.Join("；", compiled.Issues.Take(4).Select(item => item.Message));
            throw Validation("流程发布检查未通过：" + details);
        }

        var input = new FlowPublishWrite
        {
            Code = code,
            SchemaVersion = definition.SchemaVersion,
            ExpectedRevision = request.ExpectedRevision,
            ExpectedPublishedVersionId = request.ExpectedPublishedVersionId,
            DefinitionJson = draft.DefinitionJson,
            GraphJson = JsonSerializer.Serialize(compiled.Graph, ApprovalJson.Options),
            ContentSha256 = Fingerprint(draft.DefinitionJson),
            ActorId = actorId,
            IsLocalSession = isLocalSession,
            Reason = request.Reason.Trim(),
            VersionId = Guid.NewGuid().ToString("N"),
            AuditId = Guid.NewGuid().ToString("N"),
            OccurredAt = timeProvider.GetUtcNow(),
        };
        return Response(await repository.PublishAsync(input, cancellationToken), true);
    }

    /// <summary>
    /// 按预期状态停用或恢复新审批单发起，不影响已锁定版本的在途单。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="request">目标状态、预期状态和原因。</param>
    /// <param name="actorId">已认证内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>状态变更后的流程详情。</returns>
    public async Task<AdminFlowResponse> SetEnabledAsync(string code, AdminFlowEnabledRequest request,
        string actorId, bool isLocalSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireIdentityAsync(actorId, isLocalSession, cancellationToken);
        ValidateReason(request.Reason);
        var snapshot = await repository.SetEnabledAsync(new FlowEnabledWrite
        {
            Code = code,
            ExpectedEnabled = request.ExpectedEnabled,
            Enabled = request.Enabled,
            ActorId = actorId,
            IsLocalSession = isLocalSession,
            Reason = request.Reason.Trim(),
            AuditId = Guid.NewGuid().ToString("N"),
            OccurredAt = timeProvider.GetUtcNow(),
        }, cancellationToken);
        return Response(snapshot, true);
    }

    /// <summary>
    /// 从当前组织主数据判断当前会话至少可管理一个范围。
    /// </summary>
    /// <param name="actorId">已认证人员。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前身份。</returns>
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
    /// 根据当前身份判断直接部门管理范围。
    /// </summary>
    /// <param name="identity">当前组织身份。</param>
    /// <param name="isLocalSession">是否本地会话。</param>
    /// <param name="flow">目标流程。</param>
    /// <returns>有权限时为真。</returns>
    private static bool CanManage(FlowManagerIdentity identity, bool isLocalSession, FlowEntity flow) =>
        FlowManagementAccessRules.CanManage(identity.IsActive, isLocalSession, identity.IsOrganizationAdmin,
            identity.IsFlowAdmin, identity.DirectDepartmentIds, flow);

    /// <summary>
    /// 要求草稿定义具备稳定编码和名称，允许尚未完成的画布稍后编辑。
    /// </summary>
    /// <param name="definition">请求中的流程定义。</param>
    /// <param name="expectedCode">修改时既有稳定编码。</param>
    /// <returns>通过基础校验的定义。</returns>
    private static FlowDefinition RequireDefinition(FlowDefinition? definition, string? expectedCode)
    {
        if (definition is null || definition.SchemaVersion != FlowSchema.CurrentVersion ||
            string.IsNullOrWhiteSpace(definition.Code) || definition.Code.Length > 128 ||
            string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 256 ||
            definition.Canvas is null || definition.Data is null ||
            (expectedCode is not null && definition.Code != expectedCode))
        {
            throw Validation("流程草稿格式、编码或名称不正确；修改流程时编码不能改变。");
        }

        return definition;
    }

    /// <summary>
    /// 校验可编辑元数据长度和必填操作原因。
    /// </summary>
    /// <param name="group">流程分组。</param>
    /// <param name="ownerDepartmentId">直接所属部门 ID。</param>
    /// <param name="administratorId">指定管理员内部 ID。</param>
    /// <param name="reason">操作原因。</param>
    private static void ValidateMetadata(string? group, string? ownerDepartmentId, string? administratorId,
        string reason)
    {
        ValidateReason(reason);
        if (group?.Length > 128 || ownerDepartmentId?.Length > 64 || administratorId?.Length > 64 ||
            ownerDepartmentId == string.Empty || administratorId == string.Empty)
        {
            throw Validation("流程分组、所属部门或管理员 ID 格式不正确。");
        }
    }

    /// <summary>
    /// 管理写入均要求可审计的简短原因。
    /// </summary>
    /// <param name="reason">操作原因。</param>
    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 2 or > 200)
        {
            throw Validation("请填写 2 到 200 字的操作原因。");
        }
    }

    /// <summary>
    /// 组装数据库写入所需的可信草稿快照。
    /// </summary>
    /// <param name="definition">流程定义。</param>
    /// <param name="group">流程分组。</param>
    /// <param name="ownerDepartmentId">直接所属部门。</param>
    /// <param name="administratorId">运行期指定管理员。</param>
    /// <param name="reason">操作原因。</param>
    /// <param name="actorId">操作人员。</param>
    /// <param name="isLocalSession">是否本地会话。</param>
    /// <returns>持久化写入模型。</returns>
    private FlowDraftWrite Write(FlowDefinition definition, string? group, string? ownerDepartmentId,
        string? administratorId, string reason, string actorId, bool isLocalSession)
    {
        var json = JsonSerializer.Serialize(definition, ApprovalJson.Options);
        return new FlowDraftWrite
        {
            Code = definition.Code,
            Name = definition.Name,
            Group = group,
            OwnerDepartmentId = ownerDepartmentId,
            AdministratorId = administratorId,
            DefinitionJson = json,
            ContentSha256 = Fingerprint(json),
            ActorId = actorId,
            IsLocalSession = isLocalSession,
            Reason = reason.Trim(),
            OccurredAt = timeProvider.GetUtcNow(),
            AuditId = Guid.NewGuid().ToString("N"),
        };
    }

    /// <summary>
    /// 将内部快照映射为管理接口响应，列表不读取草稿正文。
    /// </summary>
    /// <param name="snapshot">数据库快照。</param>
    /// <param name="includeDefinition">是否包含完整草稿。</param>
    /// <returns>安全管理视图。</returns>
    internal static AdminFlowResponse Response(ManagedFlowSnapshot snapshot, bool includeDefinition) => new()
    {
        Code = snapshot.Flow.Code,
        Name = snapshot.Flow.Name,
        Group = snapshot.Flow.Group,
        OwnerDepartmentId = snapshot.Flow.OwnerDepartmentId,
        AdministratorId = snapshot.Flow.AdministratorId,
        IsEnabled = snapshot.Flow.IsEnabled,
        DraftRevision = snapshot.Draft?.Revision ?? 0,
        Draft = includeDefinition && snapshot.Draft is not null
            ? JsonSerializer.Deserialize<FlowDefinition>(snapshot.Draft.DefinitionJson, ApprovalJson.Options) : null,
        PublishedVersionId = snapshot.Published?.Id,
        PublishedVersion = snapshot.Published?.Version,
    };

    /// <summary>
    /// 对审计中的流程 JSON 计算小写十六进制 SHA-256。
    /// </summary>
    /// <param name="json">完整流程 JSON。</param>
    /// <returns>固定长度摘要。</returns>
    private static string Fingerprint(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    /// <summary>
    /// 生成可修正的请求错误。
    /// </summary>
    /// <param name="message">中文说明。</param>
    /// <returns>统一校验错误。</returns>
    private static UserFriendlyException Validation(string message) => new(ErrorCodes.ValidationFailed, message);

    /// <summary>
    /// 生成流程不存在错误。
    /// </summary>
    /// <returns>统一未找到错误。</returns>
    private static UserFriendlyException NotFound() => new(ErrorCodes.NotFound, "流程不存在。");

    /// <summary>
    /// 生成草稿或发布指针冲突错误。
    /// </summary>
    /// <param name="message">中文说明。</param>
    /// <returns>统一冲突错误。</returns>
    private static UserFriendlyException Conflict(string message) => new(ErrorCodes.Conflict, message);
}
