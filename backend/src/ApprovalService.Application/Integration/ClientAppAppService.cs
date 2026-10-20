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
public sealed class ClientAppAppService(IClientAppRepository repository, IIntegrationSecretProtector protector)
{
    /// <summary>
    /// 创建接入应用并只在本次返回新生成的密钥。
    /// </summary>
    /// <param name="input">管理端校验过的登记资料。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>只显示一次的 API Key 与签名密钥。</returns>
    public async Task<ClientAppSecretsDto> CreateAsync(CreateClientAppInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);
        if (await repository.FindByCodeAsync(input.Code, cancellationToken) is not null)
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "接入应用编码已存在。");
        }

        var apiKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var signingSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await repository.CreateAsync(new ClientAppEntity
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
        }, cancellationToken);
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
    private static void Validate(CreateClientAppInput input)
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
                parsed.Scheme is not ("http" or "https")))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用回调或钩子地址必须是 HTTP 绝对地址。");
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
/// 密钥只在创建时返回，后续不能从数据库反查 API Key。
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
