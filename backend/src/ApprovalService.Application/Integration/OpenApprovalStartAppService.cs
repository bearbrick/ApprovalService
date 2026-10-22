using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Application.Organization;
using ApprovalService.Application.Runtime;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 以接入应用权限、严格人员绑定和已发布流程快照发起审批。
/// </summary>
/// <param name="flows">已授权发布流程。</param>
/// <param name="queries">本应用的在途键与详情查询。</param>
/// <param name="organization">严格人员绑定。</param>
/// <param name="contextBuilder">可信引擎输入快照。</param>
/// <param name="runtime">统一引擎命令入口。</param>
/// <param name="serialNumbers">持久流水号分配器。</param>
/// <param name="serialOptions">部署级编号默认格式。</param>
/// <param name="transport">业务原单 URL 的传输策略。</param>
/// <param name="hooks">事务前的业务取数和校验钩子。</param>
/// <param name="timeProvider">固定发生时间。</param>
public sealed class OpenApprovalStartAppService(
    FlowCatalogAppService flows,
    OpenApprovalQueryAppService queries,
    IOrganizationLookupAppService organization,
    OpenStartContextBuilder contextBuilder,
    IApprovalRuntimeAppService runtime,
    IApprovalSerialNumberRepository serialNumbers,
    OpenStartSerialOptions serialOptions,
    IntegrationTransportPolicy transport,
    BusinessHookAppService hooks,
    TimeProvider timeProvider)
{
    /// <summary>
    /// 已有在途审批单返回本应用原单，否则校验快照并在引擎事务中发起。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="request">业务系统发起请求。</param>
    /// <param name="requestId">请求中间件校验后的 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>本应用审批单详情，包含当前待办。</returns>
    public async Task<OpenInstanceResponse> StartAsync(ClientAppEntity app, OpenStartRequest request,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (request is null)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "发起审批的请求体不能为空。");
        }

        Validate(request, requestId);
        var published = await flows.RequirePublishedAsync(app, request.FlowCode, cancellationToken);
        var existing = await queries.FindActiveForStartAsync(app, request.FlowCode, request.BusinessKey, cancellationToken);
        if (existing is not null)
        {
            return await queries.GetInstanceAsync(app, existing.Id, cancellationToken);
        }

        var initiatorId = await organization.ResolveBoundPersonAsync(app.Code, request.Initiator, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "发起人员未在当前接入应用绑定或已经离职，请先维护人员绑定。");
        var submittedById = request.SubmittedBy is null ? null :
            (await organization.ResolveBoundPersonAsync(app.Code, request.SubmittedBy, cancellationToken)
                ?? throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "代发人员未在当前接入应用绑定或已经离职，请先维护人员绑定。"));
        await hooks.FillStartDataAsync(app, request, requestId, cancellationToken);
        Validate(request, requestId);
        var definition = JsonSerializer.Deserialize<FlowDefinition>(published.Version.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("已发布流程定义为空。");
        var graph = JsonSerializer.Deserialize<FlowExecutionGraph>(published.Version.GraphJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("已发布执行图为空。");
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? published.Flow.Name + " · " + request.BusinessKey : request.Title;
        if (title.Length > 256)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批标题过长，请显式传入不超过 256 字的标题。");
        }

        var now = timeProvider.GetUtcNow();
        var data = OpenApprovalDataPreparer.Prepare(definition.Data, request.Data!.Value);
        var context = await contextBuilder.BuildAsync(app.Code, definition, graph, request, initiatorId,
            data, now, cancellationToken);
        await hooks.ValidateAsync(app, new ValidationHookRequest
        {
            Phase = "start",
            FlowCode = request.FlowCode,
            BusinessKey = request.BusinessKey,
            Actor = request.Initiator,
            Data = data,
        }, requestId, cancellationToken);
        var dateBucket = now.ToString(serialOptions.DatePattern, CultureInfo.InvariantCulture);
        ApprovalSerialNumber.Format(serialOptions.Prefix, serialOptions.DatePattern, serialOptions.Digits, 1, now);
        var sequence = await serialNumbers.AllocateAsync(serialOptions.Prefix, dateBucket, cancellationToken);
        var serialNo = ApprovalSerialNumber.Format(serialOptions.Prefix, serialOptions.DatePattern,
            serialOptions.Digits, sequence, now);
        var instanceId = Guid.NewGuid().ToString("N");
        try
        {
            await runtime.ExecuteAsync(new RuntimeCommandInput
            {
                Command = new EngineCommand
                {
                    Kind = EngineCommandKinds.Start,
                    RequestId = requestId,
                    InstanceId = instanceId,
                    Operation = new() { ActorId = submittedById ?? initiatorId },
                },
                Context = context,
                StartInstance = new InstanceEntity
                {
                    Id = instanceId,
                    SerialNo = serialNo,
                    FlowVersionId = published.Version.Id,
                    FlowCode = published.Flow.Code,
                    BusinessKey = request.BusinessKey,
                    Title = title,
                    InitiatorId = initiatorId,
                    InitiatorDepartmentId = context.AssigneeSnapshot?.InitiatingDepartmentId,
                    SubmittedById = submittedById,
                    ClientAppCode = app.Code,
                    ApprovalDataJson = data.GetRawText(),
                    RoutingInputsJson = JsonSerializer.Serialize(new OpenRoutingInputs
                    {
                        InitiatorSelections = context.AssigneeSnapshot?.InitiatorSelections ?? new(StringComparer.Ordinal),
                        DataValues = context.AssigneeSnapshot?.DataValues ?? new(StringComparer.Ordinal),
                    }, ApprovalJson.Options),
                    DetailUrl = request.DetailUrl,
                    IsUrgent = request.IsUrgent,
                },
            }, cancellationToken);
        }
        catch (UserFriendlyException error) when (error.Code == ErrorCodes.Conflict)
        {
            // 并发发起同业务单号由运行时唯一键只保存一张；败者只可读本应用那张。
            var concurrent = await queries.FindActiveForStartAsync(app, request.FlowCode, request.BusinessKey,
                cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            return await queries.GetInstanceAsync(app, concurrent.Id, cancellationToken);
        }

        return await queries.GetInstanceAsync(app, instanceId, cancellationToken);
    }

    /// <summary>
    /// 批量发起时逐张提交，单项业务错误不回滚已成功的审批单。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="request">待发起的审批单列表。</param>
    /// <param name="requestId">批次的稳定请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>顺序与请求一致的逐项结果。</returns>
    public async Task<OpenBatchStartResponse> StartBatchAsync(ClientAppEntity app, OpenBatchStartRequest request,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (request?.Items is null || request.Items.Count is < 1 or > 100 ||
            string.IsNullOrWhiteSpace(requestId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "批量发起必须包含 1 至 100 张审批单。");
        }

        var results = new List<OpenBatchStartItemResponse>(request.Items.Count);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (item is null)
            {
                results.Add(new OpenBatchStartItemResponse(index, null,
                    new OpenBatchStartError(ErrorCodes.ValidationFailed, "批量发起的单项请求不能为空。")));
                continue;
            }

            // 每项使用独立且重试稳定的引擎请求 ID，不能把整个批次共享为一次命令。
            var itemRequestId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                requestId + ":" + index.ToString(CultureInfo.InvariantCulture))));
            try
            {
                var instance = await StartAsync(app, item, itemRequestId, cancellationToken);
                results.Add(new OpenBatchStartItemResponse(index, instance, null));
            }
            catch (UserFriendlyException error)
            {
                results.Add(new OpenBatchStartItemResponse(index, null,
                    new OpenBatchStartError(error.Code, error.Message)));
            }
        }

        return new OpenBatchStartResponse(results);
    }

    /// <summary>
    /// 拒绝过长键、未定义的数据协议及不安全的原单 URL。
    /// </summary>
    /// <param name="request">发起请求。</param>
    /// <param name="requestId">请求 ID。</param>
    private void Validate(OpenStartRequest request, string requestId)
    {
        if (string.IsNullOrWhiteSpace(request.FlowCode) || request.FlowCode.Length > 128 ||
            string.IsNullOrWhiteSpace(request.BusinessKey) || request.BusinessKey.Length > 200 ||
            string.IsNullOrWhiteSpace(request.Initiator) || request.Initiator.Length > 128 ||
            string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64 || request.Title is null ||
            request.Title.Length > 256)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "流程编码、业务单号、发起人、标题或请求 ID 不正确。");
        }

        if (request.SubmittedBy is not null && string.IsNullOrWhiteSpace(request.SubmittedBy))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "代发人员 ID 不能为空。");
        }

        if (request.DetailUrl is not null &&
            (request.DetailUrl.Length > 2048 || !Uri.TryCreate(request.DetailUrl, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttps && !(transport.AllowHttpUrls && url.Scheme == Uri.UriSchemeHttp)) ||
                !string.IsNullOrEmpty(url.UserInfo)))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "业务原单地址须为 HTTPS 绝对地址，可信内网可显式启用 HTTP。");
        }
    }
}
