using ApprovalService.Application.Files;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace ApprovalService.Infrastructure.Files;

/// <summary>
/// 将附件内容放在兼容 S3 的私有对象桶中，下载只签发短时地址。
/// </summary>
public sealed class MinioFileStorage : IFileStorage
{
    /// <summary>
    /// 对象存储连接配置。
    /// </summary>
    private readonly IOptions<FileStorageOptions> options;

    /// <summary>
    /// 首次使用时建立客户端，后续上传下载复用其 HTTP 连接池。
    /// </summary>
    private readonly Lazy<IMinioClient> client;

    /// <summary>
    /// 创建对象存储实现，暂不连接远端。
    /// </summary>
    /// <param name="options">对象存储连接和地址配置。</param>
    public MinioFileStorage(IOptions<FileStorageOptions> options)
    {
        this.options = options;
        client = new Lazy<IMinioClient>(CreateClient);
    }

    /// <inheritdoc />
    public string Kind => "Minio";

    /// <inheritdoc />
    public async Task WriteAsync(string key, Stream source, long length, string contentType,
        CancellationToken cancellationToken = default)
    {
        var args = new PutObjectArgs().WithBucket(Bucket).WithObject(key)
            .WithStreamData(source).WithObjectSize(length).WithContentType(contentType);
        await Client.PutObjectAsync(args, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken = default)
    {
        var args = new GetObjectArgs().WithBucket(Bucket).WithObject(key)
            .WithCallbackStream(async stream => await stream.CopyToAsync(destination, cancellationToken));
        await Client.GetObjectAsync(args, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var args = new RemoveObjectArgs().WithBucket(Bucket).WithObject(key);
        await Client.RemoveObjectAsync(args, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Uri?> CreateDownloadUrlAsync(string key, TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.BrowserAccessible)
        {
            return null;
        }

        var seconds = checked((int)Math.Ceiling(lifetime.TotalSeconds));
        if (seconds is < 1 or > FileStorageOptions.MaxPresignedUrlSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "附件预签名地址有效期必须为 1 秒到 5 分钟。");
        }

        var args = new PresignedGetObjectArgs().WithBucket(Bucket).WithObject(key).WithExpiry(seconds);
        return new Uri(await Client.PresignedGetObjectAsync(args), UriKind.Absolute);
    }

    /// <summary>
    /// 配置验证后读取对象桶名。
    /// </summary>
    private string Bucket => options.Value.Bucket ?? throw new InvalidOperationException("附件对象桶未配置。");

    /// <summary>
    /// 按当前配置构建兼容 S3 的客户端。
    /// </summary>
    private IMinioClient Client => client.Value;

    /// <summary>
    /// 按当前配置构建连接池客户端。
    /// </summary>
    /// <returns>MinIO/S3 客户端。</returns>
    private IMinioClient CreateClient()
    {
        var value = options.Value;
        var endpoint = new Uri(value.Endpoint ?? throw new InvalidOperationException("附件对象存储地址未配置。"));
        return new MinioClient().WithEndpoint(endpoint.Authority)
            .WithCredentials(value.AccessKey, value.SecretKey)
            .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps).Build();
    }
}
