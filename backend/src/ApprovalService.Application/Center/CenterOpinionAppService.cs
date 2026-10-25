using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Center;

/// <summary>
/// 使用已验证人员会话管理个人常用意见。
/// </summary>
/// <param name="repository">个人意见仓储。</param>
public sealed class CenterOpinionAppService(ICenterOpinionRepository repository)
{
    /// <summary>
    /// 读取本人意见；嵌入会话可以只读使用。
    /// </summary>
    /// <param name="scope">从 JWT 提取的可信人员范围。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>当前人员意见。</returns>
    public async Task<IReadOnlyList<CenterOpinionResponse>> ListAsync(CenterScope scope,
        CancellationToken cancellationToken = default) => (await repository.ListAsync(scope.PersonId, cancellationToken))
            .Select(Map).ToArray();

    /// <summary>
    /// 为本地登录人员新增意见。
    /// </summary>
    /// <param name="scope">从 JWT 提取的可信人员范围。</param>
    /// <param name="input">意见正文。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已保存意见。</returns>
    public async Task<CenterOpinionResponse> CreateAsync(CenterScope scope, CenterOpinionRequest input,
        CancellationToken cancellationToken = default)
    {
        RequireLocal(scope);
        var entity = new CenterOpinionEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            PersonId = scope.PersonId,
            Content = Validate(input.Content),
            CreatedAtUtc = DateTime.UtcNow,
        };
        if (!await repository.CreateAsync(entity, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "常用意见最多保存三十条，请先删除不再使用的意见。");
        }
        return Map(entity);
    }

    /// <summary>
    /// 修改本地登录人员拥有的意见。
    /// </summary>
    /// <param name="scope">从 JWT 提取的可信人员范围。</param>
    /// <param name="id">意见标识。</param>
    /// <param name="input">新正文。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已修改意见。</returns>
    public async Task<CenterOpinionResponse> UpdateAsync(CenterScope scope, string id, CenterOpinionRequest input,
        CancellationToken cancellationToken = default)
    {
        RequireLocal(scope);
        var entity = await repository.UpdateAsync(scope.PersonId, id, Validate(input.Content), cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "常用意见不存在。");
        return Map(entity);
    }

    /// <summary>
    /// 删除本地登录人员拥有的意见。
    /// </summary>
    /// <param name="scope">从 JWT 提取的可信人员范围。</param>
    /// <param name="id">意见标识。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>删除完成。</returns>
    public async Task DeleteAsync(CenterScope scope, string id, CancellationToken cancellationToken = default)
    {
        RequireLocal(scope);
        if (!await repository.DeleteAsync(scope.PersonId, id, cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "常用意见不存在。");
        }
    }

    /// <summary>
    /// 禁止嵌入会话管理个人设置。
    /// </summary>
    /// <param name="scope">从 JWT 提取的可信人员范围。</param>
    private static void RequireLocal(CenterScope scope)
    {
        if (scope.ClientAppCode is not null)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "嵌入会话只能读取常用意见。");
        }
    }

    /// <summary>
    /// 校验并整理意见正文。
    /// </summary>
    /// <param name="content">用户提交的正文。</param>
    /// <returns>已校验正文。</returns>
    private static string Validate(string? content)
    {
        var value = content?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "常用意见必须填写，且不能超过五百字。");
        }
        return value;
    }

    /// <summary>
    /// 将内部实体转换为公开响应。
    /// </summary>
    /// <param name="entity">个人意见。</param>
    /// <returns>不含人员标识的响应。</returns>
    private static CenterOpinionResponse Map(CenterOpinionEntity entity) => new()
    {
        Id = entity.Id,
        Content = entity.Content,
        CreatedAtUtc = entity.CreatedAtUtc,
    };
}
