using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Files;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Center;

/// <summary>
/// 审批中心只读用例，查询身份只能来自已验证的登录会话。
/// </summary>
/// <param name="repository">在数据库边界执行人员和应用过滤的查询仓储。</param>
/// <param name="files">只读取当前详情已授权且关联本审批单的附件元数据。</param>
public sealed class CenterQueryAppService(ICenterQueryRepository repository, IStoredFileRepository files)
{
    /// <summary>
    /// 读取四类列表之一，并按节点权限裁剪摘要。
    /// </summary>
    /// <param name="scope">服务端可信会话范围。</param>
    /// <param name="query">分页和筛选条件。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权的列表页。</returns>
    public async Task<CenterListResponse> ListAsync(CenterScope scope, CenterQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Section is not (CenterSections.Todo or CenterSections.Done or CenterSections.Copy or CenterSections.Started))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批列表分组不正确。");
        }
        if (query.Skip < 0 || query.Take is < 1 or > 100 || query.Search?.Length > 200 ||
            query.FlowCode?.Length > 100 || query.Status?.Length > 40 ||
            (query.From.HasValue && query.To.HasValue && query.From >= query.To))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "查询条件不正确，每页最多一百条，结束时间须晚于开始时间。");
        }
        var page = await repository.ListAsync(scope, query, cancellationToken);
        return new CenterListResponse
        {
            Total = page.Total,
            Skip = query.Skip,
            Take = query.Take,
            Items = page.Items.Select(item => CenterProjection.Summary(item, scope.PersonId)).ToArray(),
        };
    }

    /// <summary>
    /// 读取同一权限范围内的详情，不区分不存在与不可见，以免泄露单据存在性。
    /// </summary>
    /// <param name="scope">服务端可信会话范围。</param>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>可见的业务快照和安全时间线。</returns>
    public async Task<CenterDetailResponse> FindAsync(CenterScope scope, string instanceId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 100)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批单标识不正确。");
        }
        var record = await repository.FindAsync(scope, instanceId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或当前人员无权查看。");
        var detail = CenterProjection.Detail(record, scope.PersonId);
        var attachments = new List<CenterAttachmentResponse>();
        foreach (var fileId in CenterProjection.VisibleAttachmentIds(detail))
        {
            var file = await files.FindAsync(fileId, cancellationToken);
            // 数据快照中的 ID 不足以证明附件归属，下载接口仍会独立验权。
            if (file?.InstanceId != instanceId)
            {
                continue;
            }
            attachments.Add(new CenterAttachmentResponse
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Size = file.Size,
            });
        }
        detail.Attachments = attachments;
        return detail;
    }

    /// <summary>
    /// 拒绝空人员或伪装为无限制范围的空应用字符串。
    /// </summary>
    /// <param name="scope">可信身份范围。</param>
    private static void ValidateScope(CenterScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.PersonId) ||
            (scope.ClientAppCode is not null && string.IsNullOrWhiteSpace(scope.ClientAppCode)))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "登录身份不完整，请重新登录。");
        }
    }
}
