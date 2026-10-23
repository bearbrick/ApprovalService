using System.Globalization;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Api.Endpoints.OpenApi;

/// <summary>
/// 业务系统按本应用已绑定人员读取待办，跨应用范围还需接入应用授权。
/// </summary>
public static class OpenTaskEndpoints
{
    /// <summary>
    /// 登记待办数量与列表接口。
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>原应用，供链式调用。</returns>
    public static WebApplication MapOpenTaskEndpoints(this WebApplication app)
    {
        app.MapGet(OpenApiRoutes.TaskCount, async (string? user, string? scope, HttpContext http,
            ClientAppAppService clients, IOrganizationLookupAppService organization,
            OpenApprovalQueryAppService queries, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            var personId = await ResolvePersonAsync(organization, client.Code, user, cancellationToken);
            return Results.Ok(await queries.CountTasksAsync(client, personId, ParseScope(scope), cancellationToken));
        });
        app.MapGet(OpenApiRoutes.Tasks, async (string? user, string? scope, string? skip, string? take,
            HttpContext http, ClientAppAppService clients, IOrganizationLookupAppService organization,
            OpenApprovalQueryAppService queries, CancellationToken cancellationToken) =>
        {
            var client = await clients.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey].ToString(), cancellationToken);
            var personId = await ResolvePersonAsync(organization, client.Code, user, cancellationToken);
            return Results.Ok(await queries.ListTasksAsync(client, personId, ParseScope(scope),
                ParseNumber(skip, 0, "skip"), ParseNumber(take, 20, "take"), cancellationToken));
        });
        return app;
    }

    /// <summary>
    /// 只接受当前接入应用已确认的人员绑定，不按全局工号猜测身份。
    /// </summary>
    /// <param name="organization">组织身份查询。</param>
    /// <param name="clientAppCode">已认证接入应用。</param>
    /// <param name="user">业务系统自己的人员 ID。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>内部人员 ID。</returns>
    private static async Task<string> ResolvePersonAsync(IOrganizationLookupAppService organization,
        string clientAppCode, string? user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "待办查询必须提供人员 ID。");
        }

        return await organization.ResolveBoundPersonAsync(clientAppCode, user, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用没有该在职人员的绑定，请先维护人员绑定。");
    }

    /// <summary>
    /// 默认仅看本应用，申请全部范围时交由应用服务检查授权。
    /// </summary>
    /// <param name="scope">app 或 all。</param>
    /// <returns>是否请求全部接入应用。</returns>
    private static bool ParseScope(string? scope) => scope switch
    {
        null or "" or "app" => false,
        "all" => true,
        _ => throw new UserFriendlyException(ErrorCodes.ValidationFailed, "待办范围只能是 app 或 all。"),
    };

    /// <summary>
    /// 将分页输入转成整数，格式错误也返回统一可纠正错误。
    /// </summary>
    /// <param name="value">请求参数。</param>
    /// <param name="defaultValue">未传时的默认值。</param>
    /// <param name="name">字段名。</param>
    /// <returns>解析后的整数。</returns>
    private static int ParseNumber(string? value, int defaultValue, string name)
    {
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new UserFriendlyException(ErrorCodes.ValidationFailed, $"待办分页参数 {name} 不是有效的非负整数。");
    }
}
