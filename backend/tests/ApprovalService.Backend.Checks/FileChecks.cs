using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using ApprovalService.Application.Files;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Files;
using ApprovalService.Infrastructure.Options;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 和本地文件检查附件内容、元数据、权限及到期清理。
/// </summary>
internal static class FileChecks
{
    /// <summary>
    /// 运行附件场景；提供 MinIO 环境变量时增加真实对象存储检查。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "files.db")).Options;
            var factory = new FileContextFactory(options);
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.EnsureCreatedAsync();
            }

            var repository = new StoredFileRepository(factory);
            var local = new LocalFileStorage(Options.Create(new DataOptions { Directory = directory }));
            var storageOptions = Options.Create(new FileStorageOptions());
            var selector = new FileStorageSelector([local, new MinioFileStorage(storageOptions)], storageOptions);
            var service = new FileAppService(repository, selector, new FilePolicyOptions(), TimeProvider.System);
            var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
            var uploaded = await service.UploadAsync(Input(png, "../票据.png", "requester"));
            await using (var context = await factory.CreateDbContextAsync())
            {
                var metadata = await context.StoredFiles.SingleAsync();
                check("附件数据库只保存元数据且本地目录保存原始字节",
                    metadata.FileName == "票据.png" && metadata.Size == png.Length &&
                    metadata.StorageKind == "Local" && metadata.Sha256 == uploaded.Sha256 &&
                    File.Exists(Path.Combine(directory, "files", metadata.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
            }

            await using var output = new MemoryStream();
            var download = await service.DownloadAsync(uploaded.Id, "requester", output);
            check("上传者可下载未关联文件且内容与媒体类型一致",
                download.DirectUrl is null && download.ContentType == "image/png" && output.ToArray().SequenceEqual(png));
            check("其他用户不能读取尚未关联的附件",
                await ThrowsCodeAsync(() => service.DownloadAsync(uploaded.Id, "intruder", new MemoryStream()),
                    ErrorCodes.Forbidden));
            check("附件根据文件签名而不是扩展名识别类型",
                await ThrowsCodeAsync(() => service.UploadAsync(Input(png, "假票据.pdf", "requester")),
                    ErrorCodes.ValidationFailed));
            foreach (var (extension, part, expectedType) in new[]
            {
                ("docx", "word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
                ("xlsx", "xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                ("pptx", "ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            })
            {
                var office = await service.UploadAsync(Input(OfficeBytes(part), "审批附件." + extension, "requester"));
                check("Office " + extension + " 根据压缩包主部件识别并可下载", office.ContentType == expectedType);
            }

            check("伪装成 Word 的普通 ZIP 不作为 DOCX 接受",
                await ThrowsCodeAsync(() => service.UploadAsync(Input(OfficeBytes("xl/workbook.xml"),
                    "伪装.docx", "requester")), ErrorCodes.ValidationFailed));
            var smallService = new FileAppService(repository, selector,
                new FilePolicyOptions { MaxUploadBytes = 5 }, TimeProvider.System);
            check("超过可配置大小限制的上传在写存储前拒绝",
                await ThrowsCodeAsync(() => smallService.UploadAsync(Input(png, "票据.png", "requester")),
                    ErrorCodes.ValidationFailed));

            await SeedInstanceAsync(factory);
            await service.LinkAsync(uploaded.Id, "instance", "requester");
            check("非审批单可见用户不能在关联后下载",
                await ThrowsCodeAsync(() => service.DownloadAsync(uploaded.Id, "intruder", new MemoryStream()),
                    ErrorCodes.Forbidden));
            await using var reviewerOutput = new MemoryStream();
            await service.DownloadAsync(uploaded.Id, "reviewer", reviewerOutput);
            check("已分配的审批人能够读取其审批单附件", reviewerOutput.ToArray().SequenceEqual(png));

            var orphan = await service.UploadAsync(Input(png, "临时.png", "requester"));
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.StoredFiles.Where(item => item.Id == orphan.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UploadedAt,
                        DateTimeOffset.UtcNow.AddHours(-25)));
            }

            check("超过二十四小时的未引用文件被清理，关联文件保留",
                await service.CleanupUnusedAsync() == 1 && await repository.FindAsync(orphan.Id) is null &&
                await repository.FindAsync(uploaded.Id) is not null);
            check("服务公开下载接口在身份模块未接入时默认拒绝",
                await ApiRejectsAnonymousAsync(backendRoot));
            check("可信身份的下载接口能完整发送本地文件并关闭临时流",
                await ApiStreamsAuthenticatedFileAsync(backendRoot, png));
            await CheckMigrationAsync(check);

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPROVAL_TEST_MINIO")))
            {
                await CheckMinioAsync(png, check);
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 构造小文件上传输入。
    /// </summary>
    /// <param name="bytes">文件内容。</param>
    /// <param name="name">显示名。</param>
    /// <param name="userId">测试身份。</param>
    /// <returns>上传输入。</returns>
    private static UploadFileInput Input(byte[] bytes, string name, string userId) => new()
    {
        Content = new MemoryStream(bytes),
        FileName = name,
        UploaderId = userId,
    };

    /// <summary>
    /// 建立最小 Office ZIP 结构以检查主部件识别，不解压用户输入。
    /// </summary>
    /// <param name="mainPart">文档类型对应的主部件路径。</param>
    /// <returns>ZIP 文件字节。</returns>
    private static byte[] OfficeBytes(string mainPart)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("[Content_Types].xml");
            archive.CreateEntry(mainPart);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// 用最小流程与审批单设置发起人和审批人可见性。
    /// </summary>
    /// <param name="factory">测试数据库工厂。</param>
    /// <returns>异步操作。</returns>
    private static async Task SeedInstanceAsync(FileContextFactory factory)
    {
        await using var context = await factory.CreateDbContextAsync();
        context.Flows.Add(new FlowEntity { Id = "flow", Code = "FILE", Name = "附件测试" });
        context.FlowVersions.Add(new FlowVersionEntity
        {
            Id = "version",
            FlowId = "flow",
            Version = 1,
            DefinitionJson = "{}",
            GraphJson = "{}",
        });
        context.Instances.Add(new InstanceEntity
        {
            Id = "instance",
            SerialNo = "FILE-1",
            FlowId = "flow",
            FlowVersionId = "version",
            FlowCode = "FILE",
            BusinessKey = "1",
            Title = "测试",
            InitiatorId = "requester",
            ClientAppCode = "demo",
            Status = "running",
        });
        context.ApprovalTasks.Add(new ApprovalTaskEntity
        {
            Id = "task",
            InstanceId = "instance",
            NodeId = "approval",
            AssigneeId = "reviewer",
            Status = "pending",
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 检查用户可修正的拒绝错误码。
    /// </summary>
    /// <param name="operation">预期失败的操作。</param>
    /// <param name="code">预期错误码。</param>
    /// <returns>错误码相同为真。</returns>
    private static async Task<bool> ThrowsCodeAsync(Func<Task> operation, string code)
    {
        try
        {
            await operation();
            return false;
        }
        catch (UserFriendlyException exception)
        {
            return exception.Code == code;
        }
    }

    /// <summary>
    /// 进程内启动服务，验证公开路径没有自报用户 ID 的绕过方式。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <returns>上传下载均拒绝匿名调用为真。</returns>
    private static async Task<bool> ApiRejectsAnonymousAsync(string backendRoot)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var factory = new ServerFactory(backendRoot, directory);
            using var http = factory.CreateClient();
            using var upload = await http.PostAsync(ApiRoutes.Files + "?fileName=a.png",
                new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]));
            using var download = await http.GetAsync(ApiRoutes.Files + "/any?userId=requester");
            return upload.StatusCode == HttpStatusCode.Unauthorized && download.StatusCode == HttpStatusCode.Unauthorized;
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 测试宿主模拟已验证身份，检查 HTTP 文件响应的完整字节和流生命周期。
    /// </summary>
    /// <param name="backendRoot">后端根目录。</param>
    /// <param name="bytes">期望下载的文件字节。</param>
    /// <returns>收到完整文件且成功返回为真。</returns>
    private static async Task<bool> ApiStreamsAuthenticatedFileAsync(string backendRoot, byte[] bytes)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            await using var baseline = new ServerFactory(backendRoot, directory);
            await using var authenticated = baseline.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FileIdentityStartupFilter>()));
            using var http = authenticated.CreateClient();
            using var scope = authenticated.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IFileAppService>();
            var uploaded = await service.UploadAsync(Input(bytes, "票据.png", "requester"));
            using var response = await http.GetAsync(ApiRoutes.Files + "/" + uploaded.Id);
            return response.StatusCode == HttpStatusCode.OK &&
                (await response.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes) &&
                response.Content.Headers.ContentType?.MediaType == "image/png";
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 环境指定现有 MinIO 测试桶时，真实检查上传、转发、短时地址和删除。
    /// </summary>
    /// <param name="png">测试文件内容。</param>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckMinioAsync(byte[] png, Action<string, bool> check)
    {
        var options = Options.Create(new FileStorageOptions
        {
            Provider = "Minio",
            Endpoint = Environment.GetEnvironmentVariable("APPROVAL_TEST_MINIO"),
            Bucket = Environment.GetEnvironmentVariable("APPROVAL_TEST_MINIO_BUCKET"),
            AccessKey = Environment.GetEnvironmentVariable("APPROVAL_TEST_MINIO_ACCESS_KEY"),
            SecretKey = Environment.GetEnvironmentVariable("APPROVAL_TEST_MINIO_SECRET_KEY"),
            BrowserAccessible = true,
        });
        var storage = new MinioFileStorage(options);
        var key = $"{DateTimeOffset.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
        try
        {
            await storage.WriteAsync(key, new MemoryStream(png), png.Length, "image/png");
            await using var output = new MemoryStream();
            await storage.CopyToAsync(key, output);
            var url = await storage.CreateDownloadUrlAsync(key, TimeSpan.FromMinutes(5));
            check("真实 MinIO 可以上传、服务转发并签发短时下载地址",
                output.ToArray().SequenceEqual(png) && url is { IsAbsoluteUri: true });
        }
        finally
        {
            await storage.DeleteAsync(key);
        }
    }

    /// <summary>
    /// 真实 SQLite 迁移、对象存储暂时失败、重跑继续与幂等检查。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task CheckMigrationAsync(Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var options = new DbContextOptionsBuilder<SqliteApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "migration.db")).Options;
            var factory = new MigrationContextFactory(options);
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.MigrateAsync();
            }

            var repository = new StoredFileRepository(factory);
            var local = new LocalFileStorage(Options.Create(new DataOptions { Directory = directory }));
            var remote = new TestObjectStorage { FailNextWrite = true };
            var selector = new FileStorageSelector([local, remote], Options.Create(new FileStorageOptions()));
            var service = new FileAppService(repository, selector, new FilePolicyOptions(), TimeProvider.System);
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
            var uploaded = await service.UploadAsync(Input(bytes, "迁移.png", "requester"));
            var before = (await repository.FindAsync(uploaded.Id))!;
            var wrongConfiguration = new LocalToMinioMigration(factory, selector,
                Options.Create(new FileStorageOptions { Provider = "Local" }),
                Options.Create(new DataOptions { Directory = directory }));
            var configurationRejected = false;
            try
            {
                await wrongConfiguration.MigrateAsync();
            }
            catch (InvalidOperationException)
            {
                configurationRejected = true;
            }

            check("迁移命令未配置 MinIO 目标时明确拒绝", configurationRejected);
            var migration = new LocalToMinioMigration(factory, selector,
                Options.Create(new FileStorageOptions { Provider = "Minio" }),
                Options.Create(new DataOptions { Directory = directory }));
            var failed = false;
            try
            {
                await migration.MigrateAsync();
            }
            catch (IOException)
            {
                failed = true;
            }

            check("对象存储暂时失败时本地文件和元数据保持可恢复",
                failed && (await repository.FindAsync(uploaded.Id))!.StorageKind == "Local" &&
                File.Exists(Path.Combine(directory, "files", before.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
            var moved = await migration.MigrateAsync();
            check("恢复后迁移校验内容并切换元数据、删除本地副本",
                moved == 1 && (await repository.FindAsync(uploaded.Id))!.StorageKind == "Minio" &&
                remote.Bytes.SequenceEqual(bytes) &&
                !File.Exists(Path.Combine(directory, "files", before.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
            check("迁移命令重复运行保持幂等", await migration.MigrateAsync() == 0);

            var competing = await service.UploadAsync(Input(bytes, "并发.png", "requester"));
            remote.OnWriteAsync = async () =>
            {
                await using var context = await factory.CreateDbContextAsync();
                await context.StoredFiles.Where(item => item.Id == competing.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.StorageKind, "Minio"));
            };
            var conflict = false;
            try
            {
                await migration.MigrateAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                conflict = true;
            }

            check("并行迁移获胜者已切换元数据时失败者不删除对象内容",
                conflict && (await repository.FindAsync(competing.Id))!.StorageKind == "Minio" &&
                remote.Bytes.SequenceEqual(bytes));
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 为每个仓储调用建立新的 SQLite 上下文。
    /// </summary>
    /// <param name="options">共享数据库连接配置。</param>
    private sealed class FileContextFactory(DbContextOptions<ApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <summary>
        /// 以异步调用形式创建独立上下文。
        /// </summary>
        /// <param name="cancellationToken">取消信号。</param>
        /// <returns>新的数据库上下文。</returns>
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    /// <summary>
    /// 为迁移检查创建具有 SQLite 迁移程序集的上下文。
    /// </summary>
    /// <param name="options">SQLite 数据库配置。</param>
    private sealed class MigrationContextFactory(DbContextOptions<SqliteApprovalDbContext> options) : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new SqliteApprovalDbContext(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    /// <summary>
    /// 用内存对象模拟迁移目标并支持一次可恢复的写入失败。
    /// </summary>
    private sealed class TestObjectStorage : IFileStorage
    {
        /// <inheritdoc />
        public string Kind => "Minio";

        /// <summary>
        /// 下一次上传是否抛出临时错误。
        /// </summary>
        public bool FailNextWrite
        {
            get; set;
        }

        /// <summary>
        /// 模拟对象写入后、元数据更新前的并行操作。
        /// </summary>
        public Func<Task>? OnWriteAsync
        {
            get; set;
        }

        /// <summary>
        /// 目标存储中的内容。
        /// </summary>
        public byte[] Bytes { get; private set; } = [];

        /// <inheritdoc />
        public async Task WriteAsync(string key, Stream source, long length, string contentType,
            CancellationToken cancellationToken = default)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("模拟对象存储暂时不可用。");
            }

            await using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            Bytes = buffer.ToArray();
            if (OnWriteAsync is not null)
            {
                await OnWriteAsync();
            }
        }

        /// <inheritdoc />
        public Task CopyToAsync(string key, Stream destination, CancellationToken cancellationToken = default)
        {
            return destination.WriteAsync(Bytes, cancellationToken).AsTask();
        }

        /// <inheritdoc />
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Bytes = [];
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<Uri?> CreateDownloadUrlAsync(string key, TimeSpan lifetime,
            CancellationToken cancellationToken = default) => Task.FromResult<Uri?>(null);
    }

    /// <summary>
    /// 仅检查程序使用的可信身份中间件，模拟后续 JWT 验证结果。
    /// </summary>
    private sealed class FileIdentityStartupFilter : IStartupFilter
    {
        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, continuePipeline) =>
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "requester")], "file-check"));
                    await continuePipeline();
                });
                next(app);
            };
        }
    }
}
