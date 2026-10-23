using System.Security.Cryptography;
using System.Text;
using ApprovalService.Client.Internal;

namespace ApprovalService.Client;

/// <summary>
/// 审批服务向业务系统投递的已验签事件快照。
/// </summary>
public sealed class ApprovalCallback
{
    /// <summary>
    /// 稳定事件 ID，可用于业务系统去重。
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// 事件编码。
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// 审批单 ID。
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 审批编号。
    /// </summary>
    public string SerialNo { get; set; } = string.Empty;

    /// <summary>
    /// 流程编码。
    /// </summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>
    /// 业务单号。
    /// </summary>
    public string BusinessKey { get; set; } = string.Empty;

    /// <summary>
    /// 相关任务 ID，事件没有任务时为空。
    /// </summary>
    public string? TaskId
    {
        get; set;
    }

    /// <summary>
    /// 事件发生时的审批状态。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 事件发生时的审批数据快照。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// 本次审批人填写字段。
    /// </summary>
    public Dictionary<string, object?>? FieldValues
    {
        get; set;
    }

    /// <summary>
    /// 成功自动动作的独立结果。
    /// </summary>
    public Dictionary<string, object?>? ActionResults
    {
        get; set;
    }

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
}

/// <summary>
/// 业务系统回调入口使用的原文验签与解析器。
/// </summary>
public static class ApprovalCallbackVerifier
{
    /// <summary>
    /// 先验证原始 UTF-8 正文的 HMAC-SHA256，再解析事件。
    /// </summary>
    /// <param name="body">HTTP 请求的原始正文，不要重新序列化。</param>
    /// <param name="signature">X-Approval-Signature 头的唯一值。</param>
    /// <param name="signingSecret">接入应用登记的回调签名密钥。</param>
    /// <returns>已验签的事件快照。</returns>
    public static ApprovalCallback VerifyAndParse(byte[] body, string signature, string signingSecret)
    {
        if (body == null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        if (string.IsNullOrEmpty(signingSecret))
        {
            throw new ArgumentException("回调签名密钥不能为空。", nameof(signingSecret));
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var digest = hmac.ComputeHash(body);
        var expected = new byte[7 + digest.Length * 2];
        Encoding.ASCII.GetBytes("sha256=").CopyTo(expected, 0);
        const string hex = "0123456789abcdef";
        for (var index = 0; index < digest.Length; index++)
        {
            expected[7 + index * 2] = (byte)hex[digest[index] >> 4];
            expected[8 + index * 2] = (byte)hex[digest[index] & 15];
        }

        var actual = Encoding.ASCII.GetBytes(signature ?? string.Empty);
        var difference = expected.Length ^ actual.Length;
        // 逐字节比较固定长度的预期摘要，避免按首个不同字符提前返回。
        for (var index = 0; index < expected.Length; index++)
        {
            difference |= expected[index] ^ (index < actual.Length ? actual[index] : 0);
        }

        if (difference != 0)
        {
            throw new ApprovalServiceException(ApprovalErrorCodes.Unauthorized, "回调签名无效。");
        }

        try
        {
            var row = OpenApiJson.Object(JsonText.Parse(new UTF8Encoding(false, true).GetString(body)), "callback");
            return new ApprovalCallback
            {
                EventId = OpenApiJson.String(row, "eventId"),
                Event = OpenApiJson.String(row, "event"),
                InstanceId = OpenApiJson.String(row, "instanceId"),
                SerialNo = OpenApiJson.String(row, "serialNo"),
                FlowCode = OpenApiJson.String(row, "flowCode"),
                BusinessKey = OpenApiJson.String(row, "businessKey"),
                TaskId = OpenApiJson.OptionalString(row, "taskId"),
                Status = OpenApiJson.String(row, "status"),
                Data = OpenApiJson.Object(OpenApiJson.Field(row, "data"), "data"),
                FieldValues = OpenApiJson.OptionalObject(row, "fieldValues"),
                ActionResults = OpenApiJson.OptionalObject(row, "actionResults"),
                OccurredAt = OpenApiJson.Date(row, "occurredAt"),
            };
        }
        catch (FormatException ex)
        {
            throw new ApprovalServiceException(ApprovalErrorCodes.InvalidJson, "回调正文不是合法的 UTF-8 JSON。", innerException: ex);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ApprovalServiceException(ApprovalErrorCodes.InvalidJson, "回调正文不是合法的 UTF-8 JSON。", innerException: ex);
        }
    }
}
