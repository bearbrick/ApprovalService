using System.Security.Cryptography;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Identity;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Identity;

/// <summary>
/// 执行账号登录、令牌轮换、密码修改和一次性嵌入交换。
/// </summary>
/// <param name="repository">账号与令牌的原子仓储。</param>
/// <param name="passwords">密码摘要服务。</param>
/// <param name="accessTokens">访问令牌签发服务。</param>
/// <param name="clock">可替换的时间来源。</param>
public sealed class IdentityAppService(IIdentityRepository repository, IIdentityPasswordService passwords,
    IIdentityAccessTokenService accessTokens, TimeProvider clock) : IIdentityAppService
{
    /// <summary>
    /// 访问令牌有效期。
    /// </summary>
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 刷新令牌有效期。
    /// </summary>
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// 嵌入令牌有效期。
    /// </summary>
    private static readonly TimeSpan EmbedLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 连续错误密码达到此次数后暂时锁定账号。
    /// </summary>
    private const int MaxFailedLogins = 5;

    /// <summary>
    /// 账号因连续失败而锁定的时长。
    /// </summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public async Task ProvisionAccountAsync(string personId, string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(temporaryPassword) ||
            temporaryPassword.Length is < 10 or > 128)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "人员 ID 和 10 到 128 位临时密码不能为空。");
        }

        var snapshot = await repository.FindByPersonIdAsync(personId, cancellationToken);
        if (snapshot is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "人员不存在。");
        }

        if (!snapshot.Person.IsActive)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "离职人员不能开通账号。");
        }

        if (snapshot.Account is not null)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "账号已开通。");
        }

        var digest = passwords.Hash(temporaryPassword);
        await repository.CreateAccountAsync(new LocalAccountEntity
        {
            PersonId = personId,
            PasswordHash = digest.Hash,
            PasswordSalt = digest.Salt,
            MustChangePassword = true,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IdentityTokensDto> LoginAsync(string employeeNumber, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber) || string.IsNullOrEmpty(password))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请输入工号和密码。");
        }

        var snapshot = await repository.FindByEmployeeNumberAsync(employeeNumber.Trim(), cancellationToken);
        if (snapshot?.Account is null)
        {
            passwords.VerifyMissing(password);
            throw InvalidCredentials();
        }

        var account = snapshot.Account;
        var now = clock.GetUtcNow();
        if (account.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            passwords.VerifyMissing(password);
            throw InvalidCredentials();
        }

        if (!passwords.Verify(password, account.PasswordHash, account.PasswordSalt))
        {
            await repository.RecordFailedLoginAsync(snapshot.Person.Id, now, MaxFailedLogins,
                LockoutDuration, cancellationToken);
            throw InvalidCredentials();
        }

        EnsureEnabled(snapshot);
        if (!await repository.ResetFailedLoginsAsync(snapshot.Person.Id, now, cancellationToken))
        {
            throw InvalidCredentials();
        }
        return await IssueTokensAsync(snapshot, null, null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IdentityTokensDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw InvalidToken();
        }

        var consumed = await repository.ConsumeRefreshTokenAsync(HashToken(refreshToken), clock.GetUtcNow(),
            cancellationToken);
        if (consumed is null)
        {
            throw InvalidToken();
        }

        var snapshot = await repository.FindByPersonIdAsync(consumed.PersonId, cancellationToken);
        if (snapshot?.Account is null || !snapshot.Person.IsActive || !snapshot.Account.IsEnabled ||
            snapshot.Account.TokenVersion != consumed.TokenVersion)
        {
            throw InvalidToken();
        }

        if (consumed.ClientAppCode is not null &&
            (string.IsNullOrWhiteSpace(consumed.ExternalUserId) ||
                !await repository.IsEmbedSubjectActiveAsync(consumed.ClientAppCode, consumed.ExternalUserId,
                    consumed.PersonId, cancellationToken)))
        {
            throw InvalidToken();
        }

        return await IssueTokensAsync(snapshot, null, consumed.ClientAppCode, consumed.ExternalUserId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(string personId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrEmpty(currentPassword))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "人员和当前密码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 10 or > 128)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "新密码长度应为 10 到 128 个字符。");
        }

        var snapshot = await repository.FindByPersonIdAsync(personId, cancellationToken);
        if (snapshot?.Account is null)
        {
            throw InvalidCredentials();
        }

        EnsureEnabled(snapshot);
        if (!passwords.Verify(currentPassword, snapshot.Account.PasswordHash, snapshot.Account.PasswordSalt))
        {
            throw InvalidCredentials();
        }

        if (passwords.Verify(newPassword, snapshot.Account.PasswordHash, snapshot.Account.PasswordSalt))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "新密码不能与当前密码相同。");
        }

        var digest = passwords.Hash(newPassword);
        var changed = await repository.ChangePasswordAsync(personId, snapshot.Account.TokenVersion, digest.Hash,
            digest.Salt, mustChangePassword: false, cancellationToken);
        if (!changed)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "账号刚被修改，请重新登录后重试。");
        }
    }

    /// <inheritdoc />
    public async Task RevokeAllAsync(string personId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "人员 ID 不能为空。");
        }

        var version = await repository.RevokeAllTokensAsync(personId, cancellationToken);
        if (version is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "账号不存在。");
        }
    }

    /// <inheritdoc />
    public async Task<EmbedTokenDto> CreateEmbedTokenAsync(string clientAppCode, string externalUserId,
        string personId, string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientAppCode) || string.IsNullOrWhiteSpace(externalUserId) ||
            string.IsNullOrWhiteSpace(personId) ||
            !IsLocalPath(targetPath))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用、人员或打开路径不正确。");
        }

        var snapshot = await repository.FindByPersonIdAsync(personId, cancellationToken);
        if (snapshot?.Account is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "人员账号不存在。");
        }

        EnsureEnabled(snapshot);
        if (!await repository.IsEmbedSubjectActiveAsync(clientAppCode, externalUserId, personId,
            cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用的人员绑定已失效。");
        }

        var raw = NewToken();
        var expiresAt = clock.GetUtcNow().Add(EmbedLifetime);
        await repository.AddEmbedTokenAsync(new EmbedTokenEntity
        {
            TokenHash = HashToken(raw),
            ClientAppCode = clientAppCode,
            ExternalUserId = externalUserId,
            PersonId = personId,
            TargetPath = targetPath,
            ExpiresAt = expiresAt,
        }, cancellationToken);
        return new EmbedTokenDto { Token = raw, ExpiresAt = expiresAt, TargetPath = targetPath };
    }

    /// <inheritdoc />
    public async Task<IdentityTokensDto> ExchangeEmbedTokenAsync(string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw InvalidToken();
        }

        var consumed = await repository.ConsumeEmbedTokenAsync(HashToken(rawToken), clock.GetUtcNow(),
            cancellationToken);
        if (consumed is null)
        {
            throw InvalidToken();
        }

        var snapshot = await repository.FindByPersonIdAsync(consumed.PersonId, cancellationToken);
        if (snapshot?.Account is null || !snapshot.Person.IsActive || !snapshot.Account.IsEnabled)
        {
            throw InvalidToken();
        }

        if (string.IsNullOrWhiteSpace(consumed.ExternalUserId) ||
            !await repository.IsEmbedSubjectActiveAsync(consumed.ClientAppCode, consumed.ExternalUserId,
                consumed.PersonId, cancellationToken))
        {
            throw InvalidToken();
        }

        return await IssueTokensAsync(snapshot, consumed.TargetPath, consumed.ClientAppCode,
            consumed.ExternalUserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAccessPrincipalAsync(string personId, long tokenVersion, string? sessionKind,
        string? clientAppCode, string? externalUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId))
        {
            return false;
        }

        var snapshot = await repository.FindByPersonIdAsync(personId, cancellationToken);
        if (snapshot?.Account is not { IsEnabled: true } account || !snapshot.Person.IsActive ||
            account.TokenVersion != tokenVersion)
        {
            return false;
        }

        return sessionKind switch
        {
            IdentitySessionKinds.Local => clientAppCode is null && externalUserId is null,
            IdentitySessionKinds.Embed => !string.IsNullOrWhiteSpace(clientAppCode) &&
                !string.IsNullOrWhiteSpace(externalUserId) &&
                await repository.IsEmbedSubjectActiveAsync(clientAppCode, externalUserId, personId,
                    cancellationToken),
            _ => false,
        };
    }

    /// <summary>
    /// 签发访问令牌并只保存新刷新令牌的摘要。
    /// </summary>
    /// <param name="snapshot">已通过账号状态检查的快照。</param>
    /// <param name="targetPath">嵌入交换的目标页面。</param>
    /// <param name="clientAppCode">嵌入会话的接入应用；本地登录为空。</param>
    /// <param name="externalUserId">嵌入会话的外部人员 ID；本地登录为空。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>原始访问与刷新令牌。</returns>
    private async Task<IdentityTokensDto> IssueTokensAsync(IdentityAccountSnapshot snapshot, string? targetPath,
        string? clientAppCode, string? externalUserId, CancellationToken cancellationToken)
    {
        var account = snapshot.Account ?? throw new InvalidOperationException("令牌签发时账号为空。");
        var now = clock.GetUtcNow();
        var accessExpiresAt = now.Add(AccessLifetime);
        var refreshExpiresAt = now.Add(RefreshLifetime);
        var rawRefresh = NewToken();
        await repository.AddRefreshTokenAsync(new RefreshTokenEntity
        {
            TokenHash = HashToken(rawRefresh),
            PersonId = snapshot.Person.Id,
            TokenVersion = account.TokenVersion,
            ClientAppCode = clientAppCode,
            ExternalUserId = externalUserId,
            ExpiresAt = refreshExpiresAt,
        }, cancellationToken);
        return new IdentityTokensDto
        {
            PersonId = snapshot.Person.Id,
            AccessToken = accessTokens.Issue(snapshot.Person.Id, account.TokenVersion,
                account.MustChangePassword, clientAppCode, externalUserId, now, accessExpiresAt),
            AccessExpiresAt = accessExpiresAt,
            RefreshToken = rawRefresh,
            RefreshExpiresAt = refreshExpiresAt,
            MustChangePassword = account.MustChangePassword,
            TargetPath = targetPath,
        };
    }

    /// <summary>
    /// 判断页面路径为本站路由且不会重定向到外站。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <returns>可接受为真。</returns>
    private static bool IsLocalPath(string? path) => path is { Length: > 1 and <= 1024 } &&
        (path.StartsWith("#/", StringComparison.Ordinal) ||
            (path.StartsWith("/", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal))) &&
        !path.Contains('\\') && !path.Any(char.IsControl);

    /// <summary>
    /// 为持久化令牌生成 URL 安全的摘要。
    /// </summary>
    /// <param name="raw">原始令牌。</param>
    /// <returns>小写十六进制 SHA-256。</returns>
    private static string HashToken(string raw) => Convert.ToHexStringLower(
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    /// <summary>
    /// 生成 256 位随机令牌，数据库中仅保存摘要。
    /// </summary>
    /// <returns>URL 安全令牌。</returns>
    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// 检查在职且账号启用。
    /// </summary>
    /// <param name="snapshot">人员和账号。</param>
    private static void EnsureEnabled(IdentityAccountSnapshot snapshot)
    {
        if (!snapshot.Person.IsActive || snapshot.Account?.IsEnabled != true)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "账号已停用，请联系管理员。");
        }
    }

    /// <summary>
    /// 统一错误文字，不让登录方分辨工号不存在与密码错误。
    /// </summary>
    /// <returns>未授权异常。</returns>
    private static UserFriendlyException InvalidCredentials() =>
        new(ErrorCodes.Unauthorized, "工号或密码不正确。");

    /// <summary>
    /// 拒绝已过期、已消费或被撤销的令牌。
    /// </summary>
    /// <returns>未授权异常。</returns>
    private static UserFriendlyException InvalidToken() =>
        new(ErrorCodes.Unauthorized, "令牌无效或已过期，请重新登录。");
}
