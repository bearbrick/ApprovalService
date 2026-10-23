using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Integration;

/// <summary>
/// 仅供已认证的管理用例或部署初始化调用的接入应用登记服务。
/// </summary>
/// <param name="repository">接入应用仓储。</param>
/// <param name="protector">签名密钥加密器。</param>
/// <param name="transportPolicy">出站地址的部署级传输策略。</param>
public sealed class ClientAppAppService(IClientAppRepository repository, IIntegrationSecretProtector protector,
    IntegrationTransportPolicy? transportPolicy = null)
{
    /// <summary>
    /// 创建接入应用并只在本次返回新生成的密钥。
    /// </summary>
    /// <param name="input">管理端校验过的登记资料。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>只显示一次的 API Key 与签名密钥。</returns>
    public Task<ClientAppSecretsDto> CreateAsync(CreateClientAppInput input,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(input, null, null, default, cancellationToken);

    /// <summary>
    /// 本地管理员登记接入应用，并在同一事务记录脱敏操作摘要。
    /// </summary>
    /// <param name="input">应用登记资料。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">登记原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只显示一次的新密钥。</returns>
    public Task<ClientAppSecretsDto> CreateAdminAsync(CreateClientAppInput input, string actorId,
        string reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ValidateAdminChange(actorId, reason);
        return CreateCoreAsync(input, actorId, reason.Trim(), occurredAt, cancellationToken);
    }

    /// <summary>
    /// 复用相同输入规则生成密钥，仅管理入口额外持久审计。
    /// </summary>
    /// <param name="input">应用登记资料。</param>
    /// <param name="actorId">可空的管理员人员 ID。</param>
    /// <param name="reason">可空的登记原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只显示一次的新密钥。</returns>
    private async Task<ClientAppSecretsDto> CreateCoreAsync(CreateClientAppInput input, string? actorId,
        string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);
        if (await repository.FindByCodeAsync(input.Code, cancellationToken) is not null)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "接入应用编码已存在。");
        }

        var apiKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var signingSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var entity = new ClientAppEntity
        {
            Code = input.Code,
            Name = input.Name,
            ApiKeyHash = HashKey(apiKey),
            SigningSecretCiphertext = protector.Protect(signingSecret),
            AllowedFlowCodesJson = JsonSerializer.Serialize(input.AllowedFlowCodes.Distinct(StringComparer.Ordinal)),
            CanViewAllTasks = input.CanViewAllTasks,
            CallbackUrl = input.CallbackUrl,
            DataUrl = input.DataUrl,
            ValidationUrl = input.ValidationUrl,
            AssigneeUrl = input.AssigneeUrl,
            ActionUrl = input.ActionUrl,
        };
        if (actorId is null)
        {
            await repository.CreateAsync(entity, cancellationToken);
        }
        else
        {
            await repository.CreateWithAuditAsync(entity, actorId, reason!, occurredAt, cancellationToken);
        }
        return new ClientAppSecretsDto { Code = input.Code, ApiKey = apiKey, SigningSecret = signingSecret };
    }

    /// <summary>
    /// 验证 API Key 并返回已启用的接入应用。
    /// </summary>
    /// <param name="apiKey">请求头中的 Key。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已认证接入应用。</returns>
    public async Task<ClientAppEntity> AuthenticateAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "缺少接入应用 API Key。");
        }

        var app = await repository.FindActiveByKeyHashAsync(HashKey(apiKey), cancellationToken);
        return app ?? throw new UserFriendlyException(ErrorCodes.Unauthorized, "接入应用 API Key 无效。");
    }

    /// <summary>
    /// 读取已启用应用允许嵌入审批中心的网站来源，拒绝损坏或不符合当前部署环境的配置。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="allowLocalHttp">开发环境是否允许本机 HTTP 来源。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>规范化且无重复的来源列表。</returns>
    public async Task<IReadOnlyList<string>> GetAllowedEmbedOriginsAsync(string code, bool allowLocalHttp,
        CancellationToken cancellationToken = default)
    {
        var app = await repository.FindByCodeAsync(code, cancellationToken);
        if (app is null || !app.IsEnabled)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在或已停用。");
        }

        string[] origins;
        try
        {
            origins = JsonSerializer.Deserialize<string[]>(app.AllowedEmbedOriginsJson) ?? [];
        }
        catch (JsonException)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用允许嵌入来源配置无效。");
        }

        if (origins.Length > 20 || origins.Any(origin => !IsSafeEmbedOrigin(origin, allowLocalHttp)))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用允许嵌入来源配置无效。");
        }

        return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// 在构造安全响应头前再次检查持久化来源，防止损坏数据注入 CSP 指令。
    /// </summary>
    /// <param name="origin">待检查的来源。</param>
    /// <param name="allowLocalHttp">是否在开发环境。</param>
    /// <returns>仅含协议、主机和可选端口的可信来源为真。</returns>
    private static bool IsSafeEmbedOrigin(string? origin, bool allowLocalHttp)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 256 ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
            (allowLocalHttp && uri.Scheme == Uri.UriSchemeHttp &&
                (uri.Host == "localhost" || uri.Host == "127.0.0.1"));
    }

    /// <summary>
    /// 供已授权管理用例启用或停用应用，实际状态切换使旧嵌入会话永久失效。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>异步操作。</returns>
    public Task SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default) =>
        SetEnabledCoreAsync(code, enabled, null, null, default, cancellationToken);

    /// <summary>
    /// 本地管理员启停应用并记录原因与操作人员。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    public Task SetEnabledAdminAsync(string code, bool enabled, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ValidateAdminChange(actorId, reason);
        return SetEnabledCoreAsync(code, enabled, actorId, reason.Trim(), occurredAt, cancellationToken);
    }

    /// <summary>
    /// 复用启停规则，只在管理员入口追加事务内审计。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="enabled">目标启用状态。</param>
    /// <param name="actorId">可空的管理员人员 ID。</param>
    /// <param name="reason">可空的变更原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步操作。</returns>
    private async Task SetEnabledCoreAsync(string code, bool enabled, string? actorId, string? reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用编码不能为空。");
        }

        var app = await repository.FindByCodeAsync(code, cancellationToken) ??
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        if (app.IsEnabled == enabled)
        {
            return;
        }

        var changed = actorId is null
            ? await repository.SetEnabledAsync(code, enabled, cancellationToken)
            : await repository.SetEnabledWithAuditAsync(code, enabled, actorId, reason!, occurredAt,
                cancellationToken);
        if (!changed)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "接入应用状态已变化，请刷新后重试。");
        }
    }

    /// <summary>
    /// 供已授权管理用例轮换应用密钥，同时作废既有嵌入会话。
    /// </summary>
    /// <param name="code">接入应用编码。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>只显示一次的新密钥。</returns>
    public Task<ClientAppSecretsDto> RotateSecretsAsync(string code,
        CancellationToken cancellationToken = default) =>
        RotateSecretsCoreAsync(code, null, null, default, cancellationToken);

    /// <summary>
    /// 本地管理员轮换接入应用密钥，并在同一事务记录脱敏审计摘要。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">轮换原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只显示一次的新密钥。</returns>
    public Task<ClientAppSecretsDto> RotateSecretsAdminAsync(string code, string actorId,
        string reason, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ValidateAdminChange(actorId, reason);
        return RotateSecretsCoreAsync(code, actorId, reason.Trim(), occurredAt, cancellationToken);
    }

    /// <summary>
    /// 复用同一密钥轮换逻辑，只在管理员入口追加事务内审计。
    /// </summary>
    /// <param name="code">应用编码。</param>
    /// <param name="actorId">可空的管理员人员 ID。</param>
    /// <param name="reason">可空的轮换原因。</param>
    /// <param name="occurredAt">操作时间。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>只显示一次的新密钥。</returns>
    private async Task<ClientAppSecretsDto> RotateSecretsCoreAsync(string code, string? actorId,
        string? reason, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用编码不能为空。");
        }

        if (await repository.FindByCodeAsync(code, cancellationToken) is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "接入应用不存在。");
        }

        var apiKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var signingSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var entity = new ClientAppEntity
        {
            Code = code,
            ApiKeyHash = HashKey(apiKey),
            SigningSecretCiphertext = protector.Protect(signingSecret),
        };
        var changed = actorId is null
            ? await repository.UpdateSecretsAsync(entity, cancellationToken)
            : await repository.UpdateSecretsWithAuditAsync(entity, actorId, reason!, occurredAt,
                cancellationToken);
        if (!changed)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "接入应用状态已变化，请刷新后重试。");
        }

        return new ClientAppSecretsDto { Code = code, ApiKey = apiKey, SigningSecret = signingSecret };
    }

    /// <summary>
    /// 管理员应用变更必须具有可信操作者及可审阅的原因。
    /// </summary>
    /// <param name="actorId">管理员人员 ID。</param>
    /// <param name="reason">操作原因。</param>
    private static void ValidateAdminChange(string actorId, string reason)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
        }

        if ((reason?.Trim().Length ?? 0) is < 2 or > 200)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "请填写 2 到 200 个字符的变更原因。");
        }
    }

    /// <summary>
    /// 将 API Key 转为只可比较的摘要。
    /// </summary>
    /// <param name="apiKey">原始密钥。</param>
    /// <returns>SHA-256 小写十六进制摘要。</returns>
    public static string HashKey(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    /// <summary>
    /// 校验编码、授权流程及回调地址，避免保存无效配置。
    /// </summary>
    /// <param name="input">待校验配置。</param>
    private void Validate(CreateClientAppInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 128 ||
            input.Code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')) ||
            string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 256 ||
            input.AllowedFlowCodes is null || input.AllowedFlowCodes.Any(string.IsNullOrWhiteSpace))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用编码、名称或流程授权配置不正确。");
        }

        foreach (var address in new[] { input.CallbackUrl, input.DataUrl, input.ValidationUrl,
            input.AssigneeUrl, input.ActionUrl })
        {
            if (address is not null && (!Uri.TryCreate(address, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttps &&
                    !(transportPolicy?.AllowHttpUrls == true && parsed.Scheme == Uri.UriSchemeHttp))))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    "接入应用回调或钩子地址必须是 HTTPS 绝对地址；内网 HTTP 需要部署时显式开启。");
            }
        }
    }
}

/// <summary>
/// 创建接入应用所需的管理端资料。
/// </summary>
public sealed class CreateClientAppInput
{
    /// <summary>
    /// 唯一接入应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 已授权的流程编码。
    /// </summary>
    public IReadOnlyList<string> AllowedFlowCodes { get; set; } = [];

    /// <summary>
    /// 是否允许统计其他接入应用的待办。
    /// </summary>
    public bool CanViewAllTasks
    {
        get; set;
    }

    /// <summary>
    /// 回调地址。
    /// </summary>
    public string? CallbackUrl
    {
        get; set;
    }

    /// <summary>
    /// 取数钩子地址。
    /// </summary>
    public string? DataUrl
    {
        get; set;
    }

    /// <summary>
    /// 校验钩子地址。
    /// </summary>
    public string? ValidationUrl
    {
        get; set;
    }

    /// <summary>
    /// 取人钩子地址。
    /// </summary>
    public string? AssigneeUrl
    {
        get; set;
    }

    /// <summary>
    /// 自动节点动作地址。
    /// </summary>
    public string? ActionUrl
    {
        get; set;
    }
}

/// <summary>
/// 密钥只在创建或轮换时返回，后续不能从数据库反查 API Key。
/// </summary>
public sealed class ClientAppSecretsDto
{
    /// <summary>
    /// 接入应用编码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 本次新建的 API Key。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 本次新建的签名密钥。
    /// </summary>
    public string SigningSecret { get; set; } = string.Empty;
}
