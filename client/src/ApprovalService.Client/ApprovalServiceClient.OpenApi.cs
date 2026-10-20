using System.Net.Http;
using ApprovalService.Client.Internal;

namespace ApprovalService.Client;

/// <summary>
/// 审批流客户端的已确认开放接口方法。
/// </summary>
public sealed partial class ApprovalServiceClient
{
    /// <summary>
    /// 查询本接入应用可用的已发布流程。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流程列表。</returns>
    public async Task<IReadOnlyList<AvailableFlow>> GetFlowsAsync(CancellationToken cancellationToken = default) =>
        OpenApiJson.Flows(await SendAsync(HttpMethod.Get, Relative(Protocol.Flows), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// 查询流程发起时的数据定义与 JSON 样例。
    /// </summary>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>数据定义与样例。</returns>
    public async Task<FlowSchema> GetFlowSchemaAsync(string flowCode, CancellationToken cancellationToken = default) =>
        OpenApiJson.Schema(await SendAsync(HttpMethod.Get, ForCode(Protocol.FlowSchema, flowCode), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// 以业务系统已绑定人员身份发起审批。
    /// </summary>
    /// <param name="input">业务单号、人员及数据快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建或复用的审批单。</returns>
    public async Task<ApprovalInstance> StartAsync(StartApprovalRequest input, CancellationToken cancellationToken = default)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var body = new Dictionary<string, object?>
        {
            ["flowCode"] = Required(input.FlowCode, nameof(input.FlowCode)),
            ["businessKey"] = Required(input.BusinessKey, nameof(input.BusinessKey)),
            ["title"] = Required(input.Title, nameof(input.Title)),
            ["initiator"] = Required(input.Initiator, nameof(input.Initiator)),
            ["initiatingDepartment"] = input.InitiatingDepartment,
            ["submittedBy"] = input.SubmittedBy,
            ["data"] = input.Data ?? throw new ArgumentException("发起审批必须提供数据快照。", nameof(input)),
            ["detailUrl"] = input.DetailUrl,
            ["isUrgent"] = input.IsUrgent,
            ["selectedAssignees"] = input.SelectedAssignees,
        };
        return OpenApiJson.Instance(await SendAsync(HttpMethod.Post, Relative(Protocol.Instances), cancellationToken, body).ConfigureAwait(false));
    }

    /// <summary>
    /// 按审批单 ID 查询状态、业务数据和操作记录。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审批单详情。</returns>
    public async Task<ApprovalInstance> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default) =>
        OpenApiJson.Instance(await SendAsync(HttpMethod.Get, ForId(Protocol.Instance, instanceId), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// 按流程编码与业务单号查询本应用的历史审批单，最新的排在前面。
    /// </summary>
    /// <param name="flowCode">流程编码。</param>
    /// <param name="businessKey">业务单号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>历史审批单。</returns>
    public async Task<IReadOnlyList<ApprovalInstance>> FindInstancesAsync(string flowCode, string businessKey,
        CancellationToken cancellationToken = default)
    {
        var path = Relative(Protocol.Instances) + "?flowCode=" + Encode(flowCode, nameof(flowCode))
            + "&businessKey=" + Encode(businessKey, nameof(businessKey));
        return OpenApiJson.Instances(await SendAsync(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// 查询已绑定人员的待办数量。
    /// </summary>
    /// <param name="user">本应用已绑定的业务人员 ID。</param>
    /// <param name="allApps">是否申请已授权的跨应用待办范围。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待处理、加急和超时数量。</returns>
    public async Task<ApprovalTaskCount> GetTaskCountAsync(string user, bool allApps = false,
        CancellationToken cancellationToken = default)
    {
        var path = Relative(Protocol.TaskCount) + "?user=" + Encode(user, nameof(user)) + (allApps ? "&scope=all" : "");
        return OpenApiJson.TaskCount(await SendAsync(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// 分页查询已绑定人员的待办。
    /// </summary>
    /// <param name="user">本应用已绑定的业务人员 ID。</param>
    /// <param name="skip">跳过的条数，不能为负。</param>
    /// <param name="take">本页条数，范围为一到一百。</param>
    /// <param name="allApps">是否申请已授权的跨应用待办范围。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待办列表。</returns>
    public async Task<IReadOnlyList<ApprovalTask>> GetTasksAsync(string user, int skip = 0, int take = 20,
        bool allApps = false, CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take < 1 || take > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "待办分页要求 skip 非负、take 为一到一百。");
        }

        var path = Relative(Protocol.Tasks) + "?user=" + Encode(user, nameof(user))
            + "&skip=" + skip.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "&take=" + take.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (allApps ? "&scope=all" : "");
        return OpenApiJson.Tasks(await SendAsync(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// 对人工任务执行审批或协作命令。
    /// </summary>
    /// <param name="taskId">服务返回的完整任务 ID，可能包含斜杠。</param>
    /// <param name="input">操作人、动作和可选目标。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作后的审批单详情。</returns>
    public async Task<ApprovalInstance> ActOnTaskAsync(string taskId, ApprovalTaskActionRequest input,
        CancellationToken cancellationToken = default)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var body = new Dictionary<string, object?>
        {
            ["user"] = Required(input.User, nameof(input.User)),
            ["action"] = Required(input.Action, nameof(input.Action)),
            ["opinion"] = input.Opinion,
            ["targetUser"] = input.TargetUser,
            ["targetNodeId"] = input.TargetNodeId,
            ["relatedId"] = input.RelatedId,
            ["mode"] = input.Mode,
            ["mentionedUsers"] = input.MentionedUsers,
        };
        return OpenApiJson.Instance(await SendAsync(HttpMethod.Post, ForId(Protocol.TaskActions, taskId),
            cancellationToken, body).ConfigureAwait(false));
    }

    /// <summary>
    /// 由发起人撤回在途审批。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="user">本应用已绑定的发起人员 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤回后的审批单详情。</returns>
    public async Task<ApprovalInstance> WithdrawAsync(string instanceId, string user,
        CancellationToken cancellationToken = default) =>
        OpenApiJson.Instance(await SendAsync(HttpMethod.Post, ForId(Protocol.InstanceWithdraw, instanceId),
            cancellationToken, UserBody(user)).ConfigureAwait(false));

    /// <summary>
    /// 由发起人催办当前人工任务。
    /// </summary>
    /// <param name="instanceId">审批单 ID。</param>
    /// <param name="user">本应用已绑定的发起人员 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>催办后的审批单详情。</returns>
    public async Task<ApprovalInstance> RemindAsync(string instanceId, string user,
        CancellationToken cancellationToken = default) =>
        OpenApiJson.Instance(await SendAsync(HttpMethod.Post, ForId(Protocol.InstanceRemind, instanceId),
            cancellationToken, UserBody(user)).ConfigureAwait(false));

    /// <summary>
    /// 构造实例级命令的请求体。
    /// </summary>
    /// <param name="user">本应用已绑定的发起人员 ID。</param>
    /// <returns>JSON 对象。</returns>
    private static Dictionary<string, object?> UserBody(string user) => new() { ["user"] = Required(user, nameof(user)) };

    /// <summary>
    /// 将后端定义的根相对路径拼到配置的服务路径前缀后。
    /// </summary>
    /// <param name="path">权威契约的路径副本。</param>
    /// <returns>不带开头斜杠的路径。</returns>
    private static string Relative(string path) => path.TrimStart('/');

    /// <summary>
    /// 将流程编码替换到路径模板中。
    /// </summary>
    /// <param name="path">路径模板。</param>
    /// <param name="code">流程编码。</param>
    /// <returns>相对于服务地址的路径。</returns>
    private static string ForCode(string path, string code) => Relative(path.Replace("{code}", Encode(code, nameof(code))));

    /// <summary>
    /// 将审批单或任务 ID 替换到路径模板中，任务 ID 内的斜杠也编码。
    /// </summary>
    /// <param name="path">路径模板。</param>
    /// <param name="id">审批单或任务 ID。</param>
    /// <returns>相对于服务地址的路径。</returns>
    private static string ForId(string path, string id) => Relative(path.Replace("{id}", Encode(id, nameof(id))));

    /// <summary>
    /// 校验并编码路径或查询参数。
    /// </summary>
    /// <param name="value">参数值。</param>
    /// <param name="name">参数名。</param>
    /// <returns>百分号编码后的值。</returns>
    private static string Encode(string value, string name) => Uri.EscapeDataString(Required(value, name));

    /// <summary>
    /// 校验必填的业务文本。
    /// </summary>
    /// <param name="value">文本。</param>
    /// <param name="name">参数名。</param>
    /// <returns>原文本。</returns>
    private static string Required(string value, string name) => !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("参数不能为空白。", name);
}
