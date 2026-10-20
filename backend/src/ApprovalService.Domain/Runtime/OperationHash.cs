using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ApprovalService.Domain.Runtime;

/// <summary>
/// 为只追加的操作记录计算链式校验值。
/// </summary>
public static class OperationHash
{
    /// <summary>
    /// 根据稳定字段计算本条记录的 SHA-256 十六进制值。
    /// </summary>
    /// <param name="entry">待校验记录；Hash 自身不参与计算。</param>
    /// <returns>小写十六进制校验值。</returns>
    public static string Compute(OperationEventEntity entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // 字段以换行符隔开，避免拼接歧义；JSON 使用上层统一的序列化格式。
        var payload = string.Join('\n',
            entry.InstanceId,
            entry.Sequence.ToString(CultureInfo.InvariantCulture),
            entry.RequestId,
            entry.Kind,
            entry.PayloadJson,
            entry.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            entry.PreviousHash);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// 从第一条记录开始验证顺序、上条校验值和当前内容。
    /// </summary>
    /// <param name="entries">按序读取的操作记录。</param>
    /// <returns>每条记录都完整且未被改写为真。</returns>
    public static bool Verify(IReadOnlyList<OperationEventEntity> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var previousHash = string.Empty;
        var instanceId = entries.Count == 0 ? string.Empty : entries[0].InstanceId;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.InstanceId != instanceId ||
                entry.Sequence != index + 1 ||
                entry.PreviousHash != previousHash ||
                !string.Equals(entry.Hash, Compute(entry), StringComparison.Ordinal))
            {
                return false;
            }

            previousHash = entry.Hash;
        }

        return true;
    }

    /// <summary>
    /// 同时核对审批单上的最终锚点，检测链尾记录被删除的情况。
    /// </summary>
    /// <param name="entries">按序读取的操作记录。</param>
    /// <param name="lastSequence">审批单持久化的最后事件序号。</param>
    /// <param name="lastHash">审批单持久化的最后事件校验值。</param>
    /// <returns>每条记录和最终锚点都完整且未被改写为真。</returns>
    public static bool Verify(IReadOnlyList<OperationEventEntity> entries, long lastSequence, string lastHash)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(lastHash);
        if (lastSequence < 0 || entries.Count != lastSequence || !Verify(entries))
        {
            return false;
        }

        var actualHash = entries.Count == 0 ? string.Empty : entries[^1].Hash;
        return string.Equals(actualHash, lastHash, StringComparison.Ordinal);
    }
}
