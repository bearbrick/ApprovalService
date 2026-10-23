using ApprovalService.Application.Files;
using ApprovalService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 把附件原子写入数据目录下按年月分层的本地文件。
/// </summary>
/// <param name="dataOptions">数据目录配置。</param>
public sealed class LocalFileStorage(IOptions<DataOptions> dataOptions) : IFileStorage
{
    /// <inheritdoc />
    public string Kind => "Local";

    /// <inheritdoc />
    public async Task WriteAsync(string key, Stream source, long length, string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".upload-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 80 * 1024, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(output, cancellationToken);
                if (output.Length != length)
                {
                    throw new InvalidDataException("附件写入字节数与校验长度不一致。");
                }

                await output.FlushAsync(cancellationToken);
            }

            // 同目录移动是原子操作；上传中断不会留下可读的半成品。
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <inheritdoc />
    public async Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        await using var input = new FileStream(GetPath(key), FileMode.Open, FileAccess.Read, FileShare.Read,
            80 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(destination, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Uri?> CreateDownloadUrlAsync(string key, TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Uri?>(null);
    }

    /// <summary>
    /// 仅接受服务生成的年月和十六进制 ID，不让数据库中的对象键越出数据目录。
    /// </summary>
    /// <param name="key">元数据中的对象键。</param>
    /// <returns>数据目录内的绝对文件路径。</returns>
    private string GetPath(string key)
    {
        var parts = key.Split('/');
        if (parts.Length != 3 || parts[0].Length != 4 || parts[1].Length != 2 ||
            parts[2].Length != 32 || !parts[0].All(char.IsAsciiDigit) ||
            !parts[1].All(char.IsAsciiDigit) || !parts[2].All(char.IsAsciiHexDigit) ||
            !int.TryParse(parts[1], out var month) || month is < 1 or > 12)
        {
            throw new InvalidDataException("附件存储键格式无效。");
        }

        var directory = dataOptions.Value.Directory;
        var dataRoot = Path.IsPathRooted(directory) ? directory : Path.Combine(AppContext.BaseDirectory, directory);
        return Path.Combine(Path.GetFullPath(dataRoot), "files", parts[0], parts[1], parts[2]);
    }
}
