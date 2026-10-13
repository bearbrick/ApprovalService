using ApprovalService.Contracts.Protocol;

namespace ApprovalService.Application.Services;

/// <summary>
/// 服务自身的信息：业务系统接入时先调它确认地址和版本。
/// </summary>
public interface IServiceInfoAppService
{
    /// <summary>
    /// 取服务名称和版本。
    /// </summary>
    /// <returns>服务信息。</returns>
    ServiceInfoResponse Get();
}
