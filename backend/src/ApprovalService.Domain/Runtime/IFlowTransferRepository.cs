namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 在当前流程管理范围内读取可导出的草稿或不可变发布版。
/// </summary>
public interface IFlowTransferRepository
{
    /// <summary>
    /// 同一事务内核对本地人员权限并读取指定版本的完整定义。
    /// </summary>
    /// <param name="code">稳定流程编码。</param>
    /// <param name="source">draft 或 published。</param>
    /// <param name="version">指定发布版本号；草稿时为空。</param>
    /// <param name="actorId">已认证内部人员 ID。</param>
    /// <param name="isLocalSession">是否本地账号会话。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>完整流程定义 JSON。</returns>
    Task<string> LoadDefinitionAsync(string code, string source, int? version, string actorId,
        bool isLocalSession, CancellationToken cancellationToken = default);
}
