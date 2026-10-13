namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// GET /api/v1/info 的响应：业务系统接入时先调它确认地址和版本。
/// </summary>
/// <param name="Name">服务名称，固定为 ApprovalService。</param>
/// <param name="Version">服务程序的版本，如 0.1.0。</param>
/// <param name="ApiVersion">开放接口的版本，如 v1。</param>
public sealed record ServiceInfoResponse(string Name, string Version, string ApiVersion);
