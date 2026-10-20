namespace ApprovalService.Application.Integration;

/// <summary>
/// 接入应用签名密钥的落库加密接口。
/// </summary>
public interface IIntegrationSecretProtector
{
    /// <summary>
    /// 加密明文签名密钥。
    /// </summary>
    /// <param name="secret">签名密钥明文。</param>
    /// <returns>可持久化的加密密文。</returns>
    string Protect(string secret);

    /// <summary>
    /// 仅在发送回调或钩子时解密签名密钥。
    /// </summary>
    /// <param name="ciphertext">已保存密文。</param>
    /// <returns>签名密钥明文。</returns>
    string Unprotect(string ciphertext);
}
