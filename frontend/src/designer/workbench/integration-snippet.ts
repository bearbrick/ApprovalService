/**
 * 生成对任意 UTF-16 字符都安全的 C# 普通字符串字面量。
 * @param value 不可信的流程编码。
 * @returns 用 ASCII 和 Unicode 转义组成的字面量。
 */
function csharpStringLiteral(value: string): string {
  let literal = '"'
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index)
    if (code === 0x22) literal += '\\"'
    else if (code === 0x5c) literal += '\\\\'
    else if (code >= 0x20 && code <= 0x7e) literal += value[index]
    else literal += `\\u${code.toString(16).padStart(4, '0')}`
  }
  return literal + '"'
}

/**
 * 给业务系统生成引用现有无依赖 SDK 的最小发起示例。
 * @param flowCode 已授权且已发布的流程编码。
 * @returns 不包含明文密钥、业务单据或界面框架的 C# 源码。
 */
export function buildCSharpStartSnippet(flowCode: string): string {
  if (typeof flowCode !== 'string' || !flowCode.trim() || flowCode.length > 128) {
    throw new Error('请选择有效的已发布流程编码。')
  }

  // 每个非 ASCII 码元都单独转义，Unicode 行分隔符也不会破坏生成源码。
  const codeLiteral = csharpStringLiteral(flowCode)
  return `using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ApprovalService.Client;

/// <summary>
/// 业务系统把自己的单据快照交给审批服务；单据本身仍由业务系统保存。
/// </summary>
public static class ApprovalWorkflowExample
{
    /// <summary>
    /// 以当前应用已绑定的人员身份发起一次审批。
    /// </summary>
    /// <param name="businessKey">业务系统自己的稳定单号。</param>
    /// <param name="title">审批标题。</param>
    /// <param name="initiatorExternalId">当前应用已绑定的人员 ID。</param>
    /// <param name="data">业务系统提供的审批字段快照。</param>
    /// <param name="requestId">网络重试时保持不变的请求 ID。</param>
    /// <returns>审批服务的审批单 ID。</returns>
    public static async Task<string> StartAsync(string businessKey, string title,
        string initiatorExternalId, Dictionary<string, object?> data, string requestId)
    {
        var serviceUrl = Environment.GetEnvironmentVariable("APPROVAL_SERVICE_URL");
        var apiKey = Environment.GetEnvironmentVariable("APPROVAL_API_KEY");
        if (string.IsNullOrWhiteSpace(serviceUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("请先配置审批服务地址和接入应用 API Key。");
        }

        using (var client = new ApprovalServiceClient(new ApprovalServiceClientOptions
        {
            BaseAddress = new Uri(serviceUrl, UriKind.Absolute),
            ApiKey = apiKey,
        }))
        {
            var approval = await client.StartAsync(new StartApprovalRequest
            {
                FlowCode = ${codeLiteral},
                BusinessKey = businessKey,
                Title = title,
                Initiator = initiatorExternalId,
                Data = data,
            }, requestId).ConfigureAwait(false);
            return approval.Id;
        }
    }
}
`
}
