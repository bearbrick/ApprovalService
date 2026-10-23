using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 将已验签的业务钩子响应转换为可继续处理的审批数据与校验结论。
/// </summary>
/// <param name="client">双向验签的业务系统出站接口。</param>
public sealed class BusinessHookAppService(IApprovalHookClient client)
{
    /// <summary>
    /// 发起请求未附数据时，从当前应用登记的取数接口取得业务快照。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="request">待发起请求；成功后补入已取得的数据及展示字段。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public async Task FillStartDataAsync(ClientAppEntity app, OpenStartRequest request, string requestId,
        CancellationToken cancellationToken = default)
    {
        if (request.Data is { ValueKind: JsonValueKind.Object })
        {
            return;
        }

        if (request.Data is not null && request.Data.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批数据必须是 JSON 对象。");
        }

        if (string.IsNullOrWhiteSpace(app.DataUrl))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "发起请求未传审批数据，当前接入应用也未配置取数接口。");
        }

        var response = await client.InvokeAsync(app, app.DataUrl,
            new DataHookRequest
            {
                FlowCode = request.FlowCode,
                BusinessKey = request.BusinessKey,
                RequestId = requestId,
            }, requestId, cancellationToken);
        DataHookResponse? result;
        try
        {
            result = response.Deserialize<DataHookResponse>(ApprovalJson.Options);
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务系统取数响应格式不正确。");
        }

        if (result?.Data?.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed,
                "业务系统取数响应必须包含 data 对象。");
        }

        request.Data = result.Data.Value.Clone();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            request.Title = result.Title ?? string.Empty;
        }

        request.DetailUrl ??= result.DetailUrl;
    }

    /// <summary>
    /// 仅在业务校验允许后返回；拒绝原因原样提示调用方。
    /// </summary>
    /// <param name="app">已认证接入应用。</param>
    /// <param name="request">发起或人工通过前的校验资料。</param>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public async Task ValidateAsync(ClientAppEntity app, ValidationHookRequest request, string requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(app.ValidationUrl))
        {
            return;
        }

        var response = await client.InvokeAsync(app, app.ValidationUrl, request, requestId, cancellationToken);
        ValidationHookResponse? result;
        try
        {
            result = response.Deserialize<ValidationHookResponse>(ApprovalJson.Options);
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed, "业务系统校验响应格式不正确。");
        }

        if (result?.Allowed is null || (result.Allowed == false && string.IsNullOrWhiteSpace(result.Reason)))
        {
            throw new UserFriendlyException(ErrorCodes.BusinessHookFailed,
                "业务系统校验响应必须包含 allowed，拒绝时还须提供 reason。");
        }

        if (result.Allowed == false)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, result.Reason!);
        }
    }
}
