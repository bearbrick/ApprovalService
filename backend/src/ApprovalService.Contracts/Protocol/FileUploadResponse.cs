namespace ApprovalService.Contracts.Protocol;

/// <summary>
/// 上传成功后返回可在业务审批数据中引用的文件 ID 和校验信息。
/// </summary>
/// <param name="Id">附件 ID。</param>
/// <param name="FileName">安全显示名。</param>
/// <param name="Size">文件字节数。</param>
/// <param name="ContentType">按文件内容识别的媒体类型。</param>
/// <param name="Sha256">内容的 SHA-256 小写十六进制值。</param>
public sealed record FileUploadResponse(string Id, string FileName, long Size, string ContentType, string Sha256);
