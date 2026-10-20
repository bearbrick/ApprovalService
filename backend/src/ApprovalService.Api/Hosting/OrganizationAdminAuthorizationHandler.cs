using System.Security.Claims;
using ApprovalService.Domain.Organization;
using ApprovalService.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace ApprovalService.Api.Hosting;

/// <summary>
/// 组织管理权限名称和角色检查条件。
/// </summary>
internal sealed class OrganizationAdminRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// 策略名称。
    /// </summary>
    public const string PolicyName = "OrganizationAdmin";
}

/// <summary>
/// 每次从组织主数据读取当前角色，避免 JWT 中的旧角色继续生效。
/// </summary>
/// <param name="repository">组织仓储。</param>
internal sealed class OrganizationAdminAuthorizationHandler(IOrganizationRepository repository)
    : AuthorizationHandler<OrganizationAdminRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        OrganizationAdminRequirement requirement)
    {
        var personId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(personId) &&
            context.User.FindFirstValue(JwtIdentityTokenService.SessionKindClaim) == "local" &&
            await repository.IsOrganizationAdminAsync(personId))
        {
            context.Succeed(requirement);
        }
    }
}
