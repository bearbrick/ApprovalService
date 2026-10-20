using ApprovalService.Application.Organization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Integration;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Organization;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 用真实 SQLite 验证组织导入、来源保护、绑定和离职提醒。
/// </summary>
internal static class OrganizationChecks
{
    /// <summary>
    /// 运行组织场景。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    /// <returns>异步操作。</returns>
    public static async Task RunAsync(Action<string, bool> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "approval-organization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(directory, "organization.db")).Options;
            var factory = new OrganizationContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.ClientApps.Add(new ClientAppEntity
                {
                    Code = "demo",
                    Name = "示例系统",
                    ApiKeyHash = new string('a', 64),
                    SigningSecretCiphertext = "test-ciphertext",
                });
                await db.SaveChangesAsync();
            }

            var repository = new OrganizationRepository(factory);
            var service = new OrganizationAppService(repository);
            var lookup = new OrganizationLookupAppService(repository);
            var invalid = new OrganizationImportBatch
            {
                Departments =
                [
                    new DepartmentEntity { Id = "d1", Code = "D1", Name = "一部", ParentId = "d2" },
                    new DepartmentEntity { Id = "d2", Code = "D2", Name = "二部", ParentId = "d1" },
                ],
                People =
                [
                    new PersonEntity { Id = "p1", EmployeeNumber = "E1", Name = "甲", ManagerId = "missing" },
                    new PersonEntity { Id = "p2", EmployeeNumber = "E1", Name = "乙" },
                ],
            };
            var issues = await service.ValidateImportAsync(invalid);
            check("组织导入一次列出工号重复、上级缺失和部门环",
                issues.Any(item => item.Field == "people[1].employeeNumber") &&
                issues.Any(item => item.Field == "people[0].managerId") &&
                issues.Count(item => item.Message.Contains("环", StringComparison.Ordinal)) == 2);
            invalid.People = null!;
            check("组织导入数组为 null 返回可修正错误",
                await FailsWithCodeAsync(() => service.ValidateImportAsync(invalid), ErrorCodes.ValidationFailed));
            var duplicateCodes = new OrganizationImportBatch
            {
                Departments =
                [
                    new DepartmentEntity { Id = "duplicate-1", Code = "SAME", Name = "甲部" },
                    new DepartmentEntity { Id = "duplicate-2", Code = "SAME", Name = "乙部" },
                    new DepartmentEntity { Id = "long-code", Code = new string('X', 129), Name = "超长编码" },
                ],
            };
            var codeIssues = await service.ValidateImportAsync(duplicateCodes);
            check("组织导入预检拒绝重复部门编码和超数据库列宽的编码",
                codeIssues.Any(item => item.Field == "departments[1].code" && item.Message.Contains("重复", StringComparison.Ordinal)) &&
                codeIssues.Any(item => item.Field == "departments[2].code" && item.Message.Contains("128", StringComparison.Ordinal)));
            var unknownApp = new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "unbound", EmployeeNumber = "E-unbound", Name = "待绑定" }],
                PersonBindings =
                [
                    new ExternalPersonBindingEntity
                    {
                        ClientAppCode = "missing-app",
                        ExternalUserId = "external-unbound",
                        PersonId = "unbound",
                        Method = "import",
                    },
                ],
            };
            check("导入预览拒绝未登记接入应用的外部绑定",
                (await service.ValidateImportAsync(unknownApp)).Any(item =>
                    item.Field == "personBindings[0].clientAppCode"));
            check("正式导入在同一事务拒绝孤儿绑定且不写入人员",
                (await service.ImportAsync(unknownApp, "administrator", DateTimeOffset.UtcNow)).Any(item =>
                    item.Field == "personBindings[0].clientAppCode") &&
                await repository.GetPersonAsync("unbound") is null);

            var batch = new OrganizationImportBatch
            {
                Departments = [new DepartmentEntity { Id = "d1", Code = "D1", Name = "总部" }],
                People =
                [
                    new PersonEntity { Id = "p1", EmployeeNumber = "E1", Name = "甲", Phone = "10001" },
                    new PersonEntity { Id = "p2", EmployeeNumber = "E2", Name = "乙", ManagerId = "p1" },
                ],
                Memberships =
                [
                    new PersonDepartmentEntity { PersonId = "p1", DepartmentId = "d1", IsPrimary = true },
                    new PersonDepartmentEntity { PersonId = "p2", DepartmentId = "d1", IsPrimary = true },
                ],
            };
            var now = new DateTimeOffset(2026, 10, 20, 4, 0, 0, TimeSpan.Zero);
            check("可信组织批次预检查无误", (await service.ImportAsync(batch, "administrator", now)).Count == 0);
            var duplicatePrimary = new OrganizationImportBatch
            {
                Departments = [new DepartmentEntity { Id = "d2", Code = "D2", Name = "分部" }],
                Memberships =
                [
                    new PersonDepartmentEntity { PersonId = "p1", DepartmentId = "d2", IsPrimary = true },
                ],
            };
            check("组织导入与既有关系合并后阻止同一人员多个主部门",
                (await service.ValidateImportAsync(duplicatePrimary)).Any(item =>
                    item.Field == "memberships[0].isPrimary"));
            duplicatePrimary.Memberships.Insert(0,
                new PersonDepartmentEntity { PersonId = "p1", DepartmentId = "d1", IsPrimary = false });
            check("调整主部门时允许同批次取消旧主部门并设置新主部门",
                (await service.ValidateImportAsync(duplicatePrimary)).Count == 0);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("部门人员关系写入并逐项保存来源与操作前后快照",
                    await db.Departments.CountAsync() == 1 && await db.People.CountAsync() == 2 &&
                    await db.PersonDepartments.CountAsync() == 2 && await db.OrganizationChanges.CountAsync() == 5 &&
                    await db.OrganizationChanges.AllAsync(item => item.ActorId == "administrator" &&
                        item.Source == "approvalService" && item.AfterJson != null && item.AfterJson.Length > 0));
            }

            var personId = await service.BindPersonAsync(new PersonBindingInput
            {
                ClientAppCode = "demo",
                ExternalUserId = "external-1",
                EmployeeNumber = "E1",
            }, "administrator", now);
            await service.BindDepartmentAsync(new DepartmentBindingInput
            {
                ClientAppCode = "demo",
                ExternalDepartmentId = "external-d1",
                DepartmentId = "d1",
            }, "administrator", now);
            check("工号自动绑定后业务人员和部门 ID 可解析", personId == "p1" &&
                await lookup.ResolvePersonAsync("demo", "external-1") == "p1" &&
                await lookup.ResolveDepartmentAsync("demo", "external-d1") == "d1");
            check("手机号唯一匹配可手工补绑定", await service.BindPersonAsync(new PersonBindingInput
            {
                ClientAppCode = "demo",
                ExternalUserId = "external-phone",
                Phone = "10001",
                Method = "manual",
            }, "administrator", now) == "p1");
            var snapshot = await lookup.LoadAssigneeSnapshotAsync("p2");
            check("引擎组织快照由可信主数据生成直属上级和主部门",
                snapshot.InitiatingDepartmentId == "d1" &&
                snapshot.People.Single(item => item.Id == "p2").ManagerId == "p1");

            await service.ImportAsync(new OrganizationImportBatch
            {
                People =
                [
                    new PersonEntity { Id = "p6", EmployeeNumber = "E6", Name = "己" },
                    new PersonEntity { Id = "p7", EmployeeNumber = "E7", Name = "庚" },
                ],
            }, "administrator", now);
            var firstManager = new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p6", EmployeeNumber = "E6", Name = "己", ManagerId = "p7" }],
            };
            var secondManager = new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p7", EmployeeNumber = "E7", Name = "庚", ManagerId = "p6" }],
            };
            check("并发预览都基于旧组织结构可先通过",
                (await service.ValidateImportAsync(firstManager)).Count == 0 &&
                (await service.ValidateImportAsync(secondManager)).Count == 0);
            check("正式导入在事务当前快照重新校验并拒绝上级环",
                (await service.ImportAsync(firstManager, "administrator", now)).Count == 0 &&
                (await service.ImportAsync(secondManager, "administrator", now)).Any(item =>
                    item.Field == "people[0].managerId" && item.Message.Contains("环", StringComparison.Ordinal)));
            firstManager.People[0].ManagerId = null;
            check("清除测试上级关系后仍可导入", (await service.ImportAsync(firstManager, "administrator", now)).Count == 0);

            var attemptedOverwrite = new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p1", EmployeeNumber = "E1", Name = "恶意覆盖" }],
            };
            await service.PushChangesAsync(attemptedOverwrite, "demo", now);
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("接入应用推送不能覆盖审批服务可信来源人员",
                    (await db.People.SingleAsync(item => item.Id == "p1")).Name == "甲");
            }

            var external = new OrganizationImportBatch
            {
                People = [new PersonEntity { Id = "p3", EmployeeNumber = "E3", Name = "丙" }],
            };
            check("接入应用可以推送自己来源的新增人员",
                (await service.PushChangesAsync(external, "demo", now)).Issues.Count == 0);
            external.People[0].IsActive = false;
            var departure = await service.PushChangesAsync(external, "demo", now.AddMinutes(1));
            await using (var db = await factory.CreateDbContextAsync())
            {
                check("离职推送生成管理员交接提醒并保留来源变更记录",
                    departure.DepartedPersonIds.SequenceEqual(["p3"]) &&
                    await db.DepartureNotices.AnyAsync(item => item.PersonId == "p3") &&
                    await db.OrganizationChanges.CountAsync(item => item.EntityId == "p3" &&
                        item.Source == "demo") == 2);
            }

            var csv = OrganizationCsvCodec.Template() +
                "person,p4,,\"张,四\",,,E4,,,,,,0,true,,,,,,\r\n";
            check("CSV 模板与引号转义可读取含逗号姓名",
                OrganizationCsvCodec.Parse(csv).People.Single().Name == "张,四");
            var xlsx = OrganizationExcelCodec.Template();
            check("Excel 导入模板可生成并用相同字段解析", xlsx.Length > 100 &&
                OrganizationExcelCodec.Parse(new MemoryStream(xlsx)).People.Count == 0);
            using (var excel = new MemoryStream())
            {
                excel.SaveAs(new[]
                {
                    ExcelRow(("kind", "department"), ("id", "d3"), ("code", "D3"), ("name", "新部门")),
                    ExcelRow(("kind", "person"), ("id", "p5"), ("name", "新人"),
                        ("employeeNumber", "E5"), ("gradeRank", 0), ("isActive", true)),
                    ExcelRow(("kind", "membership"), ("personId", "p5"),
                        ("departmentId", "d3"), ("isPrimary", true)),
                    ExcelRow(("kind", "personBinding"), ("clientAppCode", "demo"),
                        ("externalId", "external-p5"), ("internalId", "p5")),
                    ExcelRow(("kind", "departmentBinding"), ("clientAppCode", "demo"),
                        ("externalId", "external-d3"), ("internalId", "d3")),
                }, excelType: ExcelType.XLSX);
                excel.Position = 0;
                var parsed = OrganizationExcelCodec.Parse(excel);
                check("真实 Excel 行可同时导入部门、人员、所属部门和外部绑定",
                    parsed.Departments.Single().Name == "新部门" &&
                    parsed.People.Single().EmployeeNumber == "E5" &&
                    parsed.Memberships.Single().DepartmentId == "d3" &&
                    parsed.PersonBindings.Single().ExternalUserId == "external-p5" &&
                    parsed.DepartmentBindings.Single().ExternalDepartmentId == "external-d3" &&
                    (await service.ImportAsync(parsed, "administrator", now)).Count == 0 &&
                    await lookup.ResolveBoundPersonAsync("demo", "external-p5") == "p5" &&
                    await lookup.ResolveDepartmentAsync("demo", "external-d3") == "d3");
            }
            check("损坏的 Excel 文件作为可修正输入错误返回",
                await FailsWithCodeAsync(() => Task.FromResult(OrganizationExcelCodec.Parse(
                    new MemoryStream([0, 1, 2, 3]))), ErrorCodes.ValidationFailed));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 核对使用者可修正的错误码。
    /// </summary>
    /// <param name="action">可能失败的操作。</param>
    /// <param name="code">期望错误码。</param>
    /// <returns>符合期望为真。</returns>
    private static async Task<bool> FailsWithCodeAsync(Func<Task> action, string code)
    {
        try
        {
            await action();
            return false;
        }
        catch (UserFriendlyException error)
        {
            return error.Code == code;
        }
    }

    /// <summary>
    /// 建立与组织模板列顺序一致的一行 Excel 数据。
    /// </summary>
    /// <param name="values">需要填入的列及值。</param>
    /// <returns>含全部模板列的行。</returns>
    private static Dictionary<string, object?> ExcelRow(params (string Key, object? Value)[] values)
    {
        var row = OrganizationCsvCodec.Header.Split(',')
            .ToDictionary(header => header, _ => (object?)null, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }

    /// <summary>
    /// 为场景检查创建独立数据库上下文。
    /// </summary>
    /// <param name="options">SQLite 配置。</param>
    private sealed class OrganizationContextFactory(DbContextOptions<ApprovalDbContext> options)
        : IDbContextFactory<ApprovalDbContext>
    {
        /// <inheritdoc />
        public ApprovalDbContext CreateDbContext() => new(options);

        /// <inheritdoc />
        public Task<ApprovalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
