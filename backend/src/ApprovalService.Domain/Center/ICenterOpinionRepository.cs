namespace ApprovalService.Domain.Center;

/// <summary>
/// 仅以可信人员标识访问个人常用意见的持久仓储。
/// </summary>
public interface ICenterOpinionRepository
{
    /// <summary>
    /// 读取当前人员的常用意见。
    /// </summary>
    /// <param name="personId">可信内部人员标识。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>按创建时间排序的个人意见。</returns>
    Task<IReadOnlyList<CenterOpinionEntity>> ListAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在同一事务内校验个人条数并新增意见。
    /// </summary>
    /// <param name="entity">待保存的个人意见。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否仍有可用名额。</returns>
    Task<bool> CreateAsync(CenterOpinionEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 只修改当前人员拥有的意见。
    /// </summary>
    /// <param name="personId">可信内部人员标识。</param>
    /// <param name="id">意见标识。</param>
    /// <param name="content">已校验的正文。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>修改后的意见；无权访问或不存在时为空。</returns>
    Task<CenterOpinionEntity?> UpdateAsync(string personId, string id, string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 只删除当前人员拥有的意见。
    /// </summary>
    /// <param name="personId">可信内部人员标识。</param>
    /// <param name="id">意见标识。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>是否找到并删除。</returns>
    Task<bool> DeleteAsync(string personId, string id, CancellationToken cancellationToken = default);
}
