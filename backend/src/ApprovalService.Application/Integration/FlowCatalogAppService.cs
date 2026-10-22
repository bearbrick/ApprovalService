using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 按接入应用权限公开当前发布的流程及审批数据格式。
/// </summary>
/// <param name="repository">已发布流程仓储。</param>
public sealed class FlowCatalogAppService(IFlowCatalogRepository repository)
{
    /// <summary>
    /// 列出当前应用获授权的发布流程。
    /// </summary>
    /// <param name="app">已认证的接入应用。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>可发起的流程。</returns>
    public async Task<IReadOnlyList<AvailableFlowResponse>> ListAsync(ClientAppEntity app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var published = await repository.ListPublishedAsync(cancellationToken);
        return published.Where(item => IsAllowed(app, item)).Select(item => new AvailableFlowResponse
        {
            Code = item.Flow.Code,
            Name = item.Flow.Name,
            Version = item.Version.Version,
        }).ToArray();
    }

    /// <summary>
    /// 查询一个已授权流程的审批数据定义和样例。
    /// </summary>
    /// <param name="app">已认证的接入应用。</param>
    /// <param name="code">流程编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>数据定义和样例。</returns>
    public async Task<FlowSchemaResponse> GetSchemaAsync(ClientAppEntity app, string code,
        CancellationToken cancellationToken = default)
    {
        var published = await RequirePublishedAsync(app, code, cancellationToken);
        var definition = ReadDefinition(published);
        using var exampleDocument = JsonDocument.Parse(ApprovalDataValidator.CreateExampleJson(definition.Data));
        return new FlowSchemaResponse
        {
            FlowCode = code,
            Version = published.Version.Version,
            Data = definition.Data,
            Example = exampleDocument.RootElement.Clone(),
        };
    }

    /// <summary>
    /// 读取已发布并授权的流程快照，供发起和预览用例复用。
    /// </summary>
    /// <param name="app">已认证的接入应用。</param>
    /// <param name="code">流程编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已授权流程快照。</returns>
    public async Task<PublishedFlowSnapshot> RequirePublishedAsync(ClientAppEntity app, string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "流程编码不能为空。");
        }

        var published = await repository.LoadPublishedAsync(code, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "流程不存在或尚未发布。");
        if (!IsAllowed(app, published))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用没有使用该流程的权限。");
        }

        return published;
    }

    /// <summary>
    /// 按发布时的定义及接入应用授权双重校验流程范围。
    /// </summary>
    /// <param name="app">接入应用。</param>
    /// <param name="published">当前发布版本。</param>
    /// <returns>已授权为真。</returns>
    private static bool IsAllowed(ClientAppEntity app, PublishedFlowSnapshot published)
    {
        var allowedCodes = JsonSerializer.Deserialize<string[]>(app.AllowedFlowCodesJson, ApprovalJson.Options) ?? [];
        if (!allowedCodes.Contains(published.Flow.Code, StringComparer.Ordinal))
        {
            return false;
        }

        var definition = ReadDefinition(published);
        return definition.AllowedClientApps.Count == 0 ||
            definition.AllowedClientApps.Contains(app.Code, StringComparer.Ordinal);
    }

    /// <summary>
    /// 将不可变发布版本还原为审批数据与范围定义。
    /// </summary>
    /// <param name="published">流程版本。</param>
    /// <returns>正式流程定义。</returns>
    private static FlowDefinition ReadDefinition(PublishedFlowSnapshot published) =>
        JsonSerializer.Deserialize<FlowDefinition>(published.Version.DefinitionJson, ApprovalJson.Options)
        ?? throw new InvalidOperationException("已发布流程定义为空。");
}
