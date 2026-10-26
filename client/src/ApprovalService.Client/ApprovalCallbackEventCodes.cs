namespace ApprovalService.Client;

/// <summary>
/// 业务系统接收回调时需要识别的接入测试事件编码。
/// </summary>
public static class ApprovalCallbackEventCodes
{
    /// <summary>
    /// 仅验签确认、不更新业务原单的接入测试回调。
    /// </summary>
    public const string IntegrationTest = "integration.test";
}
