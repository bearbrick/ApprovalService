using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Runtime;

/// <summary>
/// 把引擎命令和数据库事务连接起来的应用服务。
/// </summary>
public interface IApprovalRuntimeAppService
{
    /// <summary>
    /// 按审批单锁定的流程版本执行命令并持久化结果。
    /// </summary>
    /// <param name="input">命令和已解析的人员、条件快照。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>引擎结果；同一请求重复提交时标记为重放。</returns>
    Task<EngineResult> ExecuteAsync(RuntimeCommandInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验某张审批单的事件链是否被改写。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>顺序及所有校验值均完整为真。</returns>
    Task<bool> VerifyEventChainAsync(string instanceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 应用服务命令输入，外部查询得到的人员与条件结果由调用方显式提供。
/// </summary>
public sealed class RuntimeCommandInput
{
    /// <summary>
    /// 唯一引擎命令。
    /// </summary>
    public EngineCommand Command { get; set; } = new();

    /// <summary>
    /// 纯引擎的环境输入；执行图由持久化的发布版本覆盖。
    /// </summary>
    public EngineContext Context { get; set; } = new();

    /// <summary>
    /// 开始命令的新审批单元数据；其他命令留空。
    /// </summary>
    public InstanceEntity? StartInstance
    {
        get; set;
    }
}
