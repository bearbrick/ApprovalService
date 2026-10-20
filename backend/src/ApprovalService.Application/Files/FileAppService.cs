using System.Security.Cryptography;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Files;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Files;

/// <summary>
/// 先校验大小和真实类型，再存内容与元数据；下载始终先检查已认证身份。
/// </summary>
/// <param name="repository">附件元数据仓储。</param>
/// <param name="storageSelector">本地及对象存储选择器。</param>
/// <param name="policy">上传大小、清理和下载地址限制。</param>
/// <param name="timeProvider">可测试的当前时间。</param>
public sealed class FileAppService(IStoredFileRepository repository, IFileStorageSelector storageSelector,
    FilePolicyOptions policy, TimeProvider timeProvider) : IFileAppService
{
    /// <inheritdoc />
    public async Task<StoredFileDto> UploadAsync(UploadFileInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireUser(input.UploaderId);
        if (!input.Content.CanRead || policy.MaxUploadBytes <= 0)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "附件内容或大小限制无效。");
        }

        var safeName = FileTypeDetector.NormalizeName(input.FileName);
        var temporary = Path.Combine(Path.GetTempPath(), "approval-upload-" + Guid.NewGuid().ToString("N"));
        await using var buffer = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 80 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[80 * 1024];
        while (true)
        {
            var read = await input.Content.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length > policy.MaxUploadBytes - read)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                    $"单个附件不能超过 {policy.MaxUploadBytes / 1024 / 1024} MB。");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            digest.AppendData(chunk, 0, read);
        }

        buffer.Position = 0;
        var signature = new byte[12];
        var signatureLength = 0;
        while (signatureLength < signature.Length)
        {
            var read = await buffer.ReadAsync(signature.AsMemory(signatureLength), cancellationToken);
            if (read == 0)
            {
                break;
            }

            signatureLength += read;
        }
        buffer.Position = 0;
        var contentType = FileTypeDetector.DetectContentType(signature.AsSpan(0, signatureLength), safeName, buffer);
        var hash = Convert.ToHexStringLower(digest.GetHashAndReset());
        var now = timeProvider.GetUtcNow();
        var id = Guid.NewGuid().ToString("N");
        var key = $"{now:yyyy/MM}/{id}";
        var storage = storageSelector.Current;
        buffer.Position = 0;
        await storage.WriteAsync(key, buffer, buffer.Length, contentType, cancellationToken);

        var file = new StoredFileEntity
        {
            Id = id,
            FileName = safeName,
            Size = buffer.Length,
            ContentType = contentType,
            Sha256 = hash,
            StorageKind = storage.Kind,
            StorageKey = key,
            UploadedById = input.UploaderId,
            UploadedAt = now,
        };
        try
        {
            await repository.AddAsync(file, cancellationToken);
        }
        catch
        {
            // 数据库拒绝元数据时尽量删除已写入的孤儿内容；删除失败由存储日志揭示。
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        return new StoredFileDto
        {
            Id = id,
            FileName = safeName,
            Size = file.Size,
            ContentType = contentType,
            Sha256 = hash,
        };
    }

    /// <inheritdoc />
    public async Task LinkAsync(string fileId, string instanceId, string userId,
        CancellationToken cancellationToken = default)
    {
        RequireUser(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (!await repository.CanViewInstanceAsync(instanceId, userId, cancellationToken: cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "无权在这张审批单上使用附件。");
        }

        if (!await repository.LinkAsync(fileId, instanceId, userId, timeProvider.GetUtcNow(), cancellationToken))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "附件不存在、已被使用或正在清理。");
        }
    }

    /// <inheritdoc />
    public async Task<FileDownloadDto> DownloadAsync(string fileId, string userId, Stream destination,
        string? clientAppCode = null, CancellationToken cancellationToken = default)
    {
        RequireUser(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(destination);
        var file = await repository.FindAsync(fileId, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "附件不存在。");
        // 未关联文件没有可信的应用归属，嵌入会话必须拒绝，不能仅凭相同人员 ID 跨应用读取。
        var canView = file.InstanceId is null
            ? clientAppCode is null && file.UploadedById == userId && file.CleanupOwner is null
            : await repository.CanViewInstanceAsync(file.InstanceId, userId, clientAppCode, cancellationToken);
        if (!canView)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "无权查看这张审批单的附件。");
        }

        var storage = storageSelector.Resolve(file.StorageKind);
        var directUrl = await storage.CreateDownloadUrlAsync(file.StorageKey,
            policy.DownloadUrlLifetime, cancellationToken);
        if (directUrl is null)
        {
            await storage.CopyToAsync(file.StorageKey, destination, cancellationToken);
        }

        return new FileDownloadDto
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Size,
            DirectUrl = directUrl,
        };
    }

    /// <inheritdoc />
    public async Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var candidates = await repository.FindUnusedAsync(now - policy.UnusedRetention, now,
            FilePolicyOptions.CleanupBatchSize,
            cancellationToken);
        var removed = 0;
        foreach (var file in candidates)
        {
            var owner = Guid.NewGuid().ToString("N");
            if (!await repository.ClaimCleanupAsync(file.Id, now - policy.UnusedRetention, now,
                owner, now.AddMinutes(5), cancellationToken))
            {
                continue;
            }

            // 先抢数据库租约阻止新的关联，再删内容，最后移除元数据；失败可在租约过期后重试。
            await storageSelector.Resolve(file.StorageKind).DeleteAsync(file.StorageKey, cancellationToken);
            if (await repository.DeleteClaimedAsync(file.Id, owner, cancellationToken))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// 阻止未经认证的调用者用自报身份读取或写入附件。
    /// </summary>
    /// <param name="userId">可信身份来源给出的用户 ID。</param>
    private static void RequireUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录后再操作附件。");
        }
    }
}
