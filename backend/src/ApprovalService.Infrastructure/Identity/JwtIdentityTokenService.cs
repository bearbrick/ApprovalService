using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using ApprovalService.Application.Identity;
using ApprovalService.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApprovalService.Infrastructure.Identity;

/// <summary>
/// 使用 HS256 签发供网页和 App 使用的短期访问令牌。
/// </summary>
public sealed class JwtIdentityTokenService : IIdentityAccessTokenService
{
    /// <summary>
    /// 本服务专用的签发者和受众。
    /// </summary>
    public const string Issuer = "ApprovalService";

    /// <summary>
    /// 账号令牌版本的声明名称。
    /// </summary>
    public const string TokenVersionClaim = "tokenVersion";

    /// <summary>
    /// 首次登录改密码标记的声明名称。
    /// </summary>
    public const string MustChangePasswordClaim = "mustChangePassword";

    /// <summary>
    /// 签名密钥文件名称。
    /// </summary>
    private const string KeyFileName = "identity-signing.key";

    /// <summary>
    /// JWT 签名密钥。
    /// </summary>
    private readonly SymmetricSecurityKey key;

    /// <summary>
    /// 从配置或数据目录获得签名密钥。
    /// </summary>
    /// <param name="options">身份配置。</param>
    /// <param name="data">数据目录配置。</param>
    public JwtIdentityTokenService(IOptions<IdentityTokenOptions> options, IOptions<DataOptions> data)
    {
        key = new SymmetricSecurityKey(ResolveKey(options.Value, data.Value));
    }

    /// <summary>
    /// API 层 JWT Bearer 校验使用的签发者、受众和算法限制。
    /// </summary>
    public TokenValidationParameters ValidationParameters => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Issuer,
        IssuerSigningKey = key,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };

    /// <inheritdoc />
    public string Issue(string personId, long tokenVersion, bool mustChangePassword,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, personId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(TokenVersionClaim, tokenVersion.ToString(CultureInfo.InvariantCulture)),
                new Claim(MustChangePasswordClaim, mustChangePassword ? "true" : "false"),
            ]),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });

    /// <summary>
    /// 优先使用部署配置，否则在数据目录中稳定保存随机密钥。
    /// </summary>
    /// <param name="options">身份配置。</param>
    /// <param name="data">数据目录配置。</param>
    /// <returns>至少 32 字节的密钥。</returns>
    private static byte[] ResolveKey(IdentityTokenOptions options, DataOptions data)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            try
            {
                return RequireLength(Convert.FromBase64String(options.SigningKey));
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("ApprovalService:Identity:SigningKey 必须是 Base64 文本。");
            }
        }

        var directory = Path.IsPathRooted(data.Directory)
            ? data.Directory
            : Path.Combine(AppContext.BaseDirectory, data.Directory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, KeyFileName);
        if (!File.Exists(path))
        {
            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            try
            {
                // 多实例同时首次启动时只有一个能创建，输掉的一方读取已创建的文件。
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(generated);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        return RequireLength(Convert.FromBase64String(File.ReadAllText(path).Trim()));
    }

    /// <summary>
    /// 拒绝弱签名密钥。
    /// </summary>
    /// <param name="bytes">候选密钥。</param>
    /// <returns>足够长的原密钥。</returns>
    private static byte[] RequireLength(byte[] bytes) => bytes.Length >= 32
        ? bytes
        : throw new InvalidOperationException("ApprovalService:Identity:SigningKey 至少需要 32 字节。");
}
