using System.Security.Cryptography;
using System.Text;

namespace ApprovalService.Demo.Integration;

/// <summary>
/// 本机业务后端按原始正文生成和校验审批服务约定的 HMAC-SHA256 签名。
/// </summary>
internal static class DemoHmacSignature
{
    /// <summary>
    /// 为原始 UTF-8 正文生成响应签名。
    /// </summary>
    /// <param name="body">原始响应字节。</param>
    /// <param name="secret">接入应用登记的签名密钥。</param>
    /// <returns>小写十六进制签名头。</returns>
    internal static string Sign(byte[] body, string secret)
    {
        if (body == null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("本机业务后端必须配置签名密钥。", nameof(secret));
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(body);
        var value = new char[7 + digest.Length * 2];
        "sha256=".CopyTo(0, value, 0, 7);
        const string hex = "0123456789abcdef";
        for (var index = 0; index < digest.Length; index++)
        {
            value[7 + index * 2] = hex[digest[index] >> 4];
            value[8 + index * 2] = hex[digest[index] & 15];
        }

        return new string(value);
    }

    /// <summary>
    /// 固定时长比较请求签名，正文不得反序列化后再计算。
    /// </summary>
    /// <param name="body">原始请求字节。</param>
    /// <param name="signature">签名头唯一值。</param>
    /// <param name="secret">接入应用登记的签名密钥。</param>
    /// <returns>签名有效时为 true。</returns>
    internal static bool Verify(byte[] body, string? signature, string secret)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(body, secret));
        var actual = Encoding.ASCII.GetBytes(signature ?? string.Empty);
        var difference = expected.Length ^ actual.Length;
        for (var index = 0; index < expected.Length; index++)
        {
            difference |= expected[index] ^ (index < actual.Length ? actual[index] : 0);
        }

        return difference == 0;
    }
}
