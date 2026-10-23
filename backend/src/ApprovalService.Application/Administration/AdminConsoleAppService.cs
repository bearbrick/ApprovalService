using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Administration;

/// <summary>
/// 管理后台的安全读取用例；HTTP 边界负责在调用前核对本地组织管理员权限。
/// </summary>
/// <param name="organization">组织主数据仓储。</param>
/// <param name="clientApps">接入应用仓储。</param>
public sealed class AdminConsoleAppService(IOrganizationRepository organization, IClientAppRepository clientApps)
{
    /// <summary>
    /// 读取部门、人员及所属关系并仅映射管理列表需要的字段。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>组织目录摘要。</returns>
    public async Task<AdminDirectoryResponse> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var master = await organization.LoadMasterDataAsync(cancellationToken);
        var departments = master.Departments.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var people = master.People.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var memberships = master.Memberships
            .GroupBy(item => item.PersonId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        return new AdminDirectoryResponse
        {
            Departments = master.Departments.OrderBy(item => item.Code, StringComparer.Ordinal)
                .Select(item => new AdminDepartmentResponse
                {
                    Id = item.Id,
                    Code = item.Code,
                    Name = item.Name,
                    ParentName = item.ParentId is not null && departments.TryGetValue(item.ParentId, out var parent)
                        ? parent.Name : null,
                    ManagerName = item.HeadPersonId is not null && people.TryGetValue(item.HeadPersonId, out var head)
                        ? head.Name : null,
                    Source = item.TrustedSource,
                }).ToArray(),
            People = master.People.OrderBy(item => item.EmployeeNumber, StringComparer.Ordinal)
                .Select(item => new AdminPersonResponse
                {
                    Id = item.Id,
                    EmployeeNumber = item.EmployeeNumber,
                    Name = item.Name,
                    Departments = memberships.TryGetValue(item.Id, out var entries)
                        ? entries.Where(entry => departments.ContainsKey(entry.DepartmentId))
                            .Select(entry => departments[entry.DepartmentId].Name).ToArray() : [],
                    Position = item.Position,
                    IsActive = item.IsActive,
                }).ToArray(),
        };
    }

    /// <summary>
    /// 读取接入应用配置摘要，永不读取响应所不需要的密钥。
    /// </summary>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>应用安全摘要列表。</returns>
    public async Task<IReadOnlyList<AdminClientAppResponse>> GetClientAppsAsync(
        CancellationToken cancellationToken = default)
    {
        var apps = await clientApps.ListAsync(cancellationToken);
        return apps.OrderBy(item => item.Code, StringComparer.Ordinal)
            .Select(item => new AdminClientAppResponse
            {
                Code = item.Code,
                Name = item.Name,
                AllowedFlowCodes = JsonSerializer.Deserialize<string[]>(item.AllowedFlowCodesJson) ?? [],
                IsEnabled = item.IsEnabled,
            }).ToArray();
    }

    /// <summary>
    /// 读取指定应用当前允许嵌入审批中心的网站来源。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>完整来源列表。</returns>
    public async Task<AdminEmbedOriginsResponse> GetEmbedOriginsAsync(string code,
        CancellationToken cancellationToken = default)
    {
        var app = await clientApps.FindByCodeAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        return new AdminEmbedOriginsResponse
        {
            Code = app.Code,
            Origins = JsonSerializer.Deserialize<string[]>(app.AllowedEmbedOriginsJson) ?? [],
        };
    }

    /// <summary>
    /// 保存完整可信来源列表并在同一事务留下操作者和原因。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="input">完整来源列表与变更原因。</param>
    /// <param name="actorId">当前本地管理员人员 ID。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>规范化后的来源列表。</returns>
    public async Task<AdminEmbedOriginsResponse> SetEmbedOriginsAsync(string code,
        UpdateAdminEmbedOriginsRequest input, string actorId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var origins = ValidateOrigins(input.Origins);
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请填写 2 到 200 个字符的变更原因。");
        }

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
        }

        var app = await clientApps.FindByCodeAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        var json = JsonSerializer.Serialize(origins);
        if (!await clientApps.UpdateAllowedEmbedOriginsAsync(app.Code, app.AllowedEmbedOriginsJson,
            json, actorId, reason, occurredAt, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "接入应用设置已变化，请刷新后重试。");
        }

        return new AdminEmbedOriginsResponse { Code = app.Code, Origins = origins };
    }

    /// <summary>
    /// 只保留完整 HTTPS 来源，开发时允许本机 HTTP；拒绝路径和通配符。
    /// </summary>
    /// <param name="origins">调用方提交的来源列表。</param>
    /// <returns>去重后的规范来源。</returns>
    private static IReadOnlyList<string> ValidateOrigins(IReadOnlyList<string>? origins)
    {
        if (origins is null || origins.Count > 20)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "允许嵌入来源最多 20 项。");
        }

        var normalized = new List<string>();
        foreach (var origin in origins)
        {
            var candidate = origin?.Trim() ?? string.Empty;
            if (candidate.Length is < 8 or > 256 || candidate.Contains('*') ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)))) ||
                uri.UserInfo.Length != 0 ||
                !string.Equals(candidate, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "允许嵌入来源须为完整 HTTPS 网站来源；本机开发可用 HTTP localhost，不能包含路径或通配符。");
            }

            if (!normalized.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(uri.GetLeftPart(UriPartial.Authority));
            }
        }

        return normalized;
    }
}
