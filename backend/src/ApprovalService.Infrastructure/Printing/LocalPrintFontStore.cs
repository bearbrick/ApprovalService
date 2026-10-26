using System.Text.Json;
using ApprovalService.Application.Printing;
using ApprovalService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ApprovalService.Infrastructure.Printing;

/// <summary>
/// 把字体和元数据作为一个文件原子替换，存放在部署数据目录并随之备份。
/// </summary>
/// <param name="options">部署数据目录。</param>
public sealed class LocalPrintFontStore(IOptions<DataOptions> options) : IPrintFontStore
{
    /// <summary>
    /// 只由服务端固定的存储路径，用户文件名不参与路径拼接。
    /// </summary>
    private readonly string path = Path.Combine(Path.GetFullPath(options.Value.Directory, AppContext.BaseDirectory), "printing", "font.json");

    /// <inheritdoc />
    public async Task<PrintFontAsset?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        // 允许原子重命名替换，使正在生成的文档继续使用已经读到的完整版本。
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<PrintFontAsset>(stream, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteAsync(PrintFontAsset asset, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, asset, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
