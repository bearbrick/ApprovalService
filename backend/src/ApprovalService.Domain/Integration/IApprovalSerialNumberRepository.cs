namespace ApprovalService.Domain.Integration;

/// <summary>
/// 在多进程和三种数据库上安全分配审批编号流水号。
/// </summary>
public interface IApprovalSerialNumberRepository
{
    /// <summary>
    /// 为前缀和日期桶分配下一正整数；失败的发起可以留空号。
    /// </summary>
    /// <param name="prefix">审批编号前缀。</param>
    /// <param name="dateBucket">按配置格式生成的日期桶。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本次独占的流水号。</returns>
    Task<long> AllocateAsync(string prefix, string dateBucket, CancellationToken cancellationToken = default);
}
