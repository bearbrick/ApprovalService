using System.Text.Json;
using ApprovalService.Application.Center;
using ApprovalService.Application.Files;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Administration;
using ApprovalService.Domain.Center;
using ApprovalService.Domain.Files;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Commands;
using ApprovalService.Engine.Execution;

namespace ApprovalService.Application.Printing;

/// <summary>
/// 打印按中心权限取得一致快照，先生成成功再原子追加打印审计。
/// </summary>
/// <param name="center">人员与应用授权查询。</param>
/// <param name="runtime">审批单乐观并发提交。</param>
/// <param name="branding">公司名称及 Logo。</param>
/// <param name="fonts">单位上传字体。</param>
/// <param name="printer">PDF 渲染器。</param>
/// <param name="signatures">签名授权读取。</param>
/// <param name="files">已关联文件元数据。</param>
/// <param name="storage">文件内容存储。</param>
/// <param name="organization">人员显示名称。</param>
/// <param name="clock">可信时间。</param>
public sealed class ApprovalPrintAppService(ICenterQueryRepository center, IApprovalRuntimeRepository runtime,
    ISystemBrandingRepository branding, IPrintFontStore fonts, IApprovalPrinter printer,
    SignatureAppService signatures, IStoredFileRepository files, IFileStorageSelector storage,
    IOrganizationRepository organization, TimeProvider clock)
{
    /// <summary>
    /// 为有查看权限的当前人员生成 PDF，并追加同一次数的审计。
    /// </summary>
    /// <param name="scope">可信登录人员和嵌入应用范围。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="detailUrl">管理员配置的线上审批单地址。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已持久记录生成的 PDF。</returns>
    public async Task<byte[]> PrintAsync(CenterScope scope, string id, string detailUrl,
        CancellationToken cancellationToken = default) =>
        await PrintCoreAsync(scope, id, detailUrl, null, null, cancellationToken);

    /// <summary>
    /// 已认证来源应用归档自己的审批单，审计明确记录应用而非冒充发起人。
    /// </summary>
    /// <param name="appCode">认证后的应用编码。</param>
    /// <param name="appName">认证后的应用名称。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="detailUrl">部署公开地址。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>来源应用可归档的 PDF。</returns>
    public async Task<byte[]> PrintForAppAsync(string appCode, string appName, string id, string detailUrl,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await runtime.LoadAsync(id, cancellationToken);
        if (snapshot is null || snapshot.Instance.ClientAppCode != appCode)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或不属于当前接入应用。");
        }
        // 归档使用来源单发起人的字段视图；权限来自上面的应用归属检查，审计主体仍为应用。
        return await PrintCoreAsync(new CenterScope { PersonId = snapshot.Instance.InitiatorId, ClientAppCode = appCode },
            id, detailUrl, "clientApp:" + appCode, "接入应用：" + appName, cancellationToken);
    }

    /// <summary>
    /// 在统一的生成与审计事务路径完成打印。
    /// </summary>
    /// <param name="scope">用于裁剪数据的可信范围。</param>
    /// <param name="id">审批单 ID。</param>
    /// <param name="detailUrl">公开地址。</param>
    /// <param name="appActor">归档应用审计主体；人员打印时为空。</param>
    /// <param name="appName">归档应用显示名称；人员打印时为空。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>已记审计的 PDF。</returns>
    private async Task<byte[]> PrintCoreAsync(CenterScope scope, string id, string detailUrl, string? appActor,
        string? appName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope.PersonId) || string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "打印身份或审批单标识不正确。");
        }
        var record = await center.FindAsync(scope, id, cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.NotFound, "审批单不存在或当前人员无权查看。");
        var font = await fonts.ReadAsync(cancellationToken)
            ?? throw new UserFriendlyException(ErrorCodes.Conflict, "请联系组织管理员先上传具有 PDF 内嵌授权的中文字体。");
        var definition = JsonSerializer.Deserialize<FlowDefinition>(record.FlowVersion.DefinitionJson, ApprovalJson.Options)
            ?? throw new InvalidOperationException("审批单锁定流程版本为空。");
        if (definition.PrintSettings.ShowQrCode && string.IsNullOrWhiteSpace(detailUrl))
        {
            throw new UserFriendlyException(ErrorCodes.Conflict, "请联系管理员配置打印二维码的公开访问地址，或关闭该流程的二维码。");
        }
        var brand = await branding.GetAsync(cancellationToken);
        var department = record.Instance.InitiatorDepartmentId;
        if (!string.IsNullOrWhiteSpace(department))
        {
            var master = await organization.LoadMasterDataAsync(cancellationToken);
            department = master.Departments.FirstOrDefault(item => item.Id == department)?.Name ?? department;
        }
        var input = new ApprovalPrintInput
        {
            Detail = CenterProjection.Detail(record, scope.PersonId),
            Settings = definition.PrintSettings,
            FontBytes = font.Bytes,
            CompanyName = brand?.CompanyName ?? string.Empty,
            DepartmentName = department ?? string.Empty,
            Logo = brand?.LogoBytes,
            PrintedBy = appName ?? (await organization.GetPersonAsync(scope.PersonId, cancellationToken))?.Name ?? scope.PersonId,
            PrintedAt = clock.GetUtcNow(),
            PrintNumber = record.Events.Count(item => item.Kind == PrintingRoutes.PrintedEvent) + 1,
            DetailUrl = detailUrl,
        };
        await LoadAssetsAsync(input, scope, cancellationToken);
        var pdf = printer.Render(input);
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var payload = new EngineEvent
        {
            Id = requestId,
            Kind = PrintingRoutes.PrintedEvent,
            OccurredAt = input.PrintedAt,
            Audit = new OperationAudit { ActorId = appActor ?? scope.PersonId, Opinion = $"第 {input.PrintNumber} 次打印" },
        };
        var entry = new OperationEventEntity
        {
            Id = requestId,
            InstanceId = id,
            RequestId = requestId,
            Kind = PrintingRoutes.PrintedEvent,
            Sequence = record.Instance.LastEventSequence + 1,
            PreviousHash = record.Instance.LastEventHash,
            OccurredAt = input.PrintedAt,
            PayloadJson = JsonSerializer.Serialize(payload, ApprovalJson.Options),
        };
        entry.Hash = OperationHash.Compute(entry);
        var version = record.Instance.Version;
        record.Instance.Version++;
        if (!await runtime.CommitAsync(new RuntimeCommit
        {
            Instance = record.Instance,
            ExpectedVersion = version,
            Tasks = record.Tasks,
            Events = [entry],
        }, cancellationToken))
        {
            // 并发办理或打印会使快照过期，不能交付带重复次数或旧状态的 PDF。
            throw new UserFriendlyException(ErrorCodes.Conflict, "审批单在生成期间发生变化，请重新打印。");
        }
        return pdf;
    }

    /// <summary>
    /// 只从已授权字段引用中读取附件与签名，不访问外部地址。
    /// </summary>
    /// <param name="input">安全详情。</param>
    /// <param name="scope">当前人员。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>资源准备完成。</returns>
    private async Task LoadAssetsAsync(ApprovalPrintInput input, CenterScope scope, CancellationToken cancellationToken)
    {
        var people = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in input.Detail.Events.SelectMany(item => new[] { item.ActorId, item.OnBehalfOfId })
            .Where(id => id is not null).Cast<string>().Distinct(StringComparer.Ordinal))
        {
            people[id] = (await organization.GetPersonAsync(id, cancellationToken))?.Name ?? id;
        }
        input.People = people;
        var signatureBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (input.Settings.ShowSignatures)
        {
            foreach (var id in input.Detail.Events.Select(item => item.SignatureFileId).Where(id => id is not null)
                .Cast<string>().Distinct(StringComparer.Ordinal))
            {
                signatureBytes[id] = await signatures.ReadPngAsync(scope, id, input.Detail.Instance.Id, cancellationToken);
            }
        }
        input.Signatures = signatureBytes;
        var attachments = new List<CenterAttachmentResponse>();
        var receiptBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var id in CenterProjection.VisibleAttachmentIds(input.Detail))
        {
            var file = await files.FindAsync(id, cancellationToken);
            if (file?.InstanceId != input.Detail.Instance.Id)
            {
                continue;
            }
            attachments.Add(new CenterAttachmentResponse
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Size = file.Size
            });
            if (input.Settings.IncludeReceiptPages && file.ContentType is "image/png" or "image/jpeg")
            {
                totalBytes += file.Size;
                if (totalBytes > 80 * 1024 * 1024 || receiptBytes.Count >= 100)
                {
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed, "票据页最多包含一百张 PNG/JPEG 图片，总大小不超过 80 MiB。");
                }
                using var buffer = new MemoryStream();
                await storage.Resolve(file.StorageKind).CopyToAsync(file.StorageKey, buffer, cancellationToken);
                receiptBytes[id] = buffer.ToArray();
            }
        }
        input.Detail.Attachments = attachments;
        input.Receipts = receiptBytes;
    }
}
