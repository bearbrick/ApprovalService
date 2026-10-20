using System.Security.Cryptography;
using System.Text;
using ApprovalService.Application.Integration;
using Microsoft.Extensions.Configuration;

namespace ApprovalService.Infrastructure.Integration;

/// <summary>
/// 用部署环境提供的主密钥加密接入应用签名密钥。
/// </summary>
/// <param name="configuration">包含主密钥的部署配置。</param>
public sealed class AesIntegrationSecretProtector(IConfiguration configuration) : IIntegrationSecretProtector
{
    /// <summary>
    /// AES-GCM 随机数长度。
    /// </summary>
    private const int NonceLength = 12;

    /// <summary>
    /// AES-GCM 校验标签长度。
    /// </summary>
    private const int TagLength = 16;

    /// <inheritdoc />
    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var key = LoadKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(key, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        return Convert.ToBase64String(nonce.Concat(tag).Concat(ciphertext).ToArray());
    }

    /// <inheritdoc />
    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length <= NonceLength + TagLength)
        {
            throw new InvalidOperationException("接入应用签名密钥密文格式不正确。");
        }

        var plaintext = new byte[data.Length - NonceLength - TagLength];
        using (var aes = new AesGcm(LoadKey(), TagLength))
        {
            aes.Decrypt(data.AsSpan(0, NonceLength), data.AsSpan(NonceLength + TagLength),
                data.AsSpan(NonceLength, TagLength), plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// 读取部署配置中 256 位的主密钥；缺失时绝不降级为明文保存。
    /// </summary>
    /// <returns>32 字节 AES 主密钥。</returns>
    private byte[] LoadKey()
    {
        var encoded = configuration["ApprovalService:Integration:MasterKey"];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException("缺少 ApprovalService:Integration:MasterKey，不能保存或读取签名密钥。");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException("接入应用加密主密钥必须是 32 字节的 Base64。", error);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException("接入应用加密主密钥必须是 32 字节的 Base64。");
        }

        return key;
    }
}
