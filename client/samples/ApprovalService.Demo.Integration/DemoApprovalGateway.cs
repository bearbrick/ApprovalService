using ApprovalService.Client;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 示例业务系统调用审批服务的薄对接层；界面只传自己的业务单据。
/// </summary>
public sealed class DemoApprovalGateway
{
    /// <summary>
    /// 已配置接入应用 Key 的 SDK 客户端。
    /// </summary>
    private readonly ApprovalServiceClient client;

    /// <summary>
    /// 示例系统自己的业务单据文件。
    /// </summary>
    private readonly DemoDocumentStore store;

    /// <summary>
    /// 创建对接层。
    /// </summary>
    /// <param name="client">已配置接入应用的 SDK 客户端。</param>
    /// <param name="store">独立的业务数据文件。</param>
    public DemoApprovalGateway(ApprovalServiceClient client, DemoDocumentStore store)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// 先保存报销原单，再向审批服务发送快照；重试沿用相同请求 ID。
    /// </summary>
    /// <param name="claim">费用报销单。</param>
    /// <param name="requestId">网络重试时不变的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批单快照。</returns>
    public async Task<ApprovalInstance> SubmitExpenseAsync(ExpenseClaim claim, string requestId,
        CancellationToken cancellationToken = default)
    {
        if (claim == null)
        {
            throw new ArgumentNullException(nameof(claim));
        }

        store.SaveExpense(claim);
        var instance = await client.StartAsync(new StartApprovalRequest
        {
            FlowCode = "EXPENSE",
            BusinessKey = claim.BusinessKey,
            Title = claim.Title,
            Initiator = claim.Initiator,
            InitiatingDepartment = claim.Department,
            Data = claim.ToApprovalData(),
        }, requestId, cancellationToken).ConfigureAwait(false);
        store.RecordStart(instance);
        return instance;
    }

    /// <summary>
    /// 先保存采购原单，再向审批服务发送快照。
    /// </summary>
    /// <param name="purchase">采购申请单。</param>
    /// <param name="requestId">网络重试时不变的请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批单快照。</returns>
    public async Task<ApprovalInstance> SubmitPurchaseAsync(PurchaseRequest purchase, string requestId,
        CancellationToken cancellationToken = default)
    {
        if (purchase == null)
        {
            throw new ArgumentNullException(nameof(purchase));
        }

        store.SavePurchase(purchase);
        var instance = await client.StartAsync(new StartApprovalRequest
        {
            FlowCode = "PURCHASE_REQUEST",
            BusinessKey = purchase.BusinessKey,
            Title = purchase.Title,
            Initiator = purchase.Initiator,
            InitiatingDepartment = purchase.Department,
            Data = purchase.ToApprovalData(),
        }, requestId, cancellationToken).ConfigureAwait(false);
        store.RecordStart(instance);
        return instance;
    }

    /// <summary>
    /// 没有本机业务后端时，打开原单后主动同步审批状态。
    /// </summary>
    /// <param name="instanceId">原单保存的审批单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最新审批单快照。</returns>
    public async Task<ApprovalInstance> RefreshAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await client.GetInstanceAsync(instanceId, cancellationToken).ConfigureAwait(false);
        store.UpdateFromQuery(instance);
        return instance;
    }

    /// <summary>
    /// 由已绑定的原发起人在业务系统撤回原单。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="initiator">本应用绑定的原发起人 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤回后的审批单快照。</returns>
    public async Task<ApprovalInstance> WithdrawAsync(string instanceId, string initiator,
        CancellationToken cancellationToken = default)
    {
        var instance = await client.WithdrawAsync(instanceId, initiator, cancellationToken).ConfigureAwait(false);
        store.UpdateFromQuery(instance);
        return instance;
    }

    /// <summary>
    /// 费用报销补充或退回后从业务原单重新提交完整数据。
    /// </summary>
    /// <param name="businessKey">报销业务单号。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复后的审批单。</returns>
    public async Task<ApprovalInstance> ResubmitExpenseAsync(string businessKey, string requestId,
        CancellationToken cancellationToken = default)
    {
        var claim = store.FindExpense(businessKey) ?? throw new InvalidOperationException("费用报销原单不存在。");
        var instanceId = claim.ApprovalInstanceId ?? throw new InvalidOperationException("费用报销原单尚未发起审批。");
        if (instanceId.Length == 0)
        {
            throw new InvalidOperationException("费用报销原单尚未发起审批。");
        }

        var instance = await client.ResubmitAsync(instanceId, new ApprovalResubmitRequest
        {
            User = claim.Initiator,
            Data = claim.ToApprovalData(),
        }, requestId, cancellationToken).ConfigureAwait(false);
        store.UpdateFromQuery(instance);
        return instance;
    }

    /// <summary>
    /// 采购申请退回后从业务原单重新提交完整数据。
    /// </summary>
    /// <param name="businessKey">采购业务单号。</param>
    /// <param name="requestId">稳定请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复后的审批单。</returns>
    public async Task<ApprovalInstance> ResubmitPurchaseAsync(string businessKey, string requestId,
        CancellationToken cancellationToken = default)
    {
        var purchase = store.FindPurchase(businessKey) ?? throw new InvalidOperationException("采购原单不存在。");
        var instanceId = purchase.ApprovalInstanceId ?? throw new InvalidOperationException("采购原单尚未发起审批。");
        if (instanceId.Length == 0)
        {
            throw new InvalidOperationException("采购原单尚未发起审批。");
        }

        var instance = await client.ResubmitAsync(instanceId, new ApprovalResubmitRequest
        {
            User = purchase.Initiator,
            Data = purchase.ToApprovalData(),
        }, requestId, cancellationToken).ConfigureAwait(false);
        store.UpdateFromQuery(instance);
        return instance;
    }

    /// <summary>
    /// 供示例客户端绘制当前人的待办角标。
    /// </summary>
    /// <param name="user">当前业务用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本应用范围的待办数量。</returns>
    public Task<ApprovalTaskCount> GetTaskCountAsync(string user, CancellationToken cancellationToken = default) =>
        client.GetTaskCountAsync(user, cancellationToken: cancellationToken);

    /// <summary>
    /// 供示例客户端自己绘制待办列表。
    /// </summary>
    /// <param name="user">当前业务用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前页待办。</returns>
    public Task<IReadOnlyList<ApprovalTask>> GetTasksAsync(string user, CancellationToken cancellationToken = default) =>
        client.GetTasksAsync(user, cancellationToken: cancellationToken);

    /// <summary>
    /// 用当前业务用户 ID 提交自绘审批窗口的操作。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="action">审批操作和意见。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作后的审批单。</returns>
    public async Task<ApprovalInstance> ActOnTaskAsync(string taskId, ApprovalTaskActionRequest action,
        CancellationToken cancellationToken = default)
    {
        var instance = await client.ActOnTaskAsync(taskId, action, cancellationToken).ConfigureAwait(false);
        store.UpdateFromQuery(instance);
        return instance;
    }
}
