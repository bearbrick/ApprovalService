using System.Security.Cryptography;
using System.Text;
using ApprovalService.Application.Identity;

namespace ApprovalService.Infrastructure.Identity;

/// <summary>
/// 使用随机盐与 PBKDF2-SHA256 保存本地密码。
/// </summary>
internal sealed class Pbkdf2IdentityPasswordService : IIdentityPasswordService
{
    /// <summary>
    /// 与标签服务一致的迭代次数。
    /// </summary>
    private const int Iterations = 210_000;

    /// <summary>
    /// 随机盐长度。
    /// </summary>
    private const int SaltBytes = 16;

    /// <summary>
    /// 派生摘要长度。
    /// </summary>
    private const int HashBytes = 32;

    /// <summary>
    /// 不存在账号时使用的固定摘要，避免跳过昂贵的密码计算。
    /// </summary>
    private readonly PasswordDigest dummy = CreateDigest("missing-account-placeholder");

    /// <inheritdoc />
    public PasswordDigest Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return CreateDigest(password);
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash, string salt)
    {
        try
        {
            var expected = Convert.FromBase64String(hash);
            var saltBytes = Convert.FromBase64String(salt);
            if (expected.Length != HashBytes || saltBytes.Length != SaltBytes)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), saltBytes,
                Iterations, HashAlgorithmName.SHA256, HashBytes);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool VerifyMissing(string password)
    {
        Verify(password, dummy.Hash, dummy.Salt);
        return false;
    }

    /// <summary>
    /// 生成新的随机盐与派生摘要。
    /// </summary>
    /// <param name="password">原始密码。</param>
    /// <returns>Base64 编码的摘要与盐。</returns>
    private static PasswordDigest CreateDigest(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt,
            Iterations, HashAlgorithmName.SHA256, HashBytes);
        return new PasswordDigest
        {
            Hash = Convert.ToBase64String(hash),
            Salt = Convert.ToBase64String(salt),
        };
    }
}
