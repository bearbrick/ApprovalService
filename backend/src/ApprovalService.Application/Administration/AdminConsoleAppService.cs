using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;

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
}
