namespace ApprovalService.Client.Internal;

/// <summary>
/// 让 HTTP 内容释放自身时不关闭调用方提供的上传流。
/// </summary>
internal sealed class NonClosingReadStream : Stream
{
    /// <summary>
    /// 调用方拥有并负责释放的原始流。
    /// </summary>
    private readonly Stream source;

    /// <summary>
    /// 包装可读流，但不接管其所有权。
    /// </summary>
    /// <param name="source">调用方的原始流。</param>
    public NonClosingReadStream(Stream source) => this.source = source;

    /// <inheritdoc />
    public override bool CanRead => source.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => source.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => source.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    /// <inheritdoc />
    public override void Flush() => source.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken) => source.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("上传流只读。");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("上传流只读。");
}
