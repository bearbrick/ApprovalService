using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApprovalService.Application.Identity;
using ApprovalService.Application.Reporting;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Reporting;
using ApprovalService.Domain.Runtime;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniExcelLibs;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 通过真实 HTTP 验证报表 JWT、部门范围和保密数据脱敏。
/// </summary>
internal static class ReportingEndpointChecks
{
    /// <summary>
    /// 运行报表端点场景。
    /// </summary>
    /// <param name="backendRoot">后端目录。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>完成的异步任务。</returns>
    public static async Task RunAsync(string backendRoot, Action<string, bool> check)
    {
        var directory = TempDataDirectory.Create();
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["ApprovalService:Identity:SigningKey"] = Convert.ToBase64String(new byte[64]),
                ["ApprovalService:Integration:MasterKey"] = Convert.ToBase64String(new byte[32]),
            };
            await using var factory = new ServerFactory(backendRoot, directory, settings);
            using var client = factory.CreateClient();
            var started = DateTimeOffset.UtcNow.AddDays(-2);
            using (var scope = factory.Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
                await using (var db = await dbFactory.CreateDbContextAsync())
                {
                    db.People.AddRange(
                        new PersonEntity { Id = "admin", EmployeeNumber = "RA", Name = "管理员" },
                        new PersonEntity { Id = "head", EmployeeNumber = "RH", Name = "负责人" },
                        new PersonEntity { Id = "other", EmployeeNumber = "RO", Name = "普通人员" });
                    db.Departments.AddRange(
                        new DepartmentEntity { Id = "D1", Code = "D1", Name = "直属", HeadPersonId = "head" },
                        new DepartmentEntity { Id = "D2", Code = "D2", Name = "子部门", ParentId = "D1" });
                    db.OrganizationRoles.Add(new OrganizationRoleEntity
                    {
                        Code = "organization-admin",
                        Name = "组织管理员",
                    });
                    db.PersonRoles.Add(new PersonRoleEntity
                    {
                        PersonId = "admin",
                        RoleCode = "organization-admin",
                    });
                    db.Flows.AddRange(
                        new FlowEntity { Id = "F1", Code = "PUBLIC", Name = "公开" },
                        new FlowEntity { Id = "F2", Code = "SECRET", Name = "保密" });
                    db.FlowVersions.AddRange(
                        new FlowVersionEntity
                        {
                            Id = "V1",
                            FlowId = "F1",
                            Version = 1,
                            DefinitionJson = "{}",
                            GraphJson = "{}",
                        },
                        new FlowVersionEntity
                        {
                            Id = "V2",
                            FlowId = "F2",
                            Version = 1,
                            DefinitionJson = "{}",
                            GraphJson = "{\"isConfidential\":true}",
                        });
                    db.Instances.AddRange(
                        new InstanceEntity
                        {
                            Id = "I1",
                            SerialNo = "REPORT-1",
                            FlowId = "F1",
                            FlowVersionId = "V1",
                            FlowCode = "PUBLIC",
                            BusinessKey = "B1",
                            Title = "公开内容",
                            InitiatorId = "other",
                            InitiatorDepartmentId = "D1",
                            ClientAppCode = "ERP",
                            Status = "approved",
                            StartedAt = started,
                            EndedAt = started.AddHours(2),
                        },
                        new InstanceEntity
                        {
                            Id = "I2",
                            SerialNo = "REPORT-2",
                            FlowId = "F2",
                            FlowVersionId = "V2",
                            FlowCode = "SECRET",
                            BusinessKey = "B2",
                            Title = "秘密内容",
                            InitiatorId = "other",
                            InitiatorDepartmentId = "D2",
                            ClientAppCode = "ERP",
                            Status = "approved",
                            StartedAt = started,
                            EndedAt = started.AddHours(3),
                        });
                    await db.SaveChangesAsync();
                }

                var identity = scope.ServiceProvider.GetRequiredService<IIdentityAppService>();
                foreach (var person in new[] { "admin", "head", "other" })
                {
                    await identity.ProvisionAccountAsync(person, "initial-report-password");
                    await identity.ChangePasswordAsync(person, "initial-report-password", "normal-report-password");
                }
            }

            var query = "?fromUtc=" + Uri.EscapeDataString(started.AddHours(-1).ToString("O")) +
                "&untilUtc=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
            using (var response = await client.GetAsync(ApiRoutes.AdminReportOverview + query))
            {
                check("匿名请求不能读取报表", response.StatusCode == HttpStatusCode.Unauthorized);
            }

            using (var response = await client.GetAsync(ApiRoutes.AdminReportExport + query))
            {
                check("匿名请求不能导出报表", response.StatusCode == HttpStatusCode.Unauthorized);
            }

            var ordinary = await LoginAsync(client, "RO");
            using (var response = await GetAsync(client, ApiRoutes.AdminReportOverview + query, ordinary))
            {
                check("普通本地人员不能读取报表", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportExport + query, ordinary))
            {
                check("普通人员不能导出报表", response.StatusCode == HttpStatusCode.Forbidden);
            }

            var admin = await LoginAsync(client, "RA");
            using (var response = await GetAsync(client, ApiRoutes.AdminReportOverview + query, admin))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminReportOverviewResponse>();
                check("组织管理员概览分开统计公开和保密数量", response.IsSuccessStatusCode &&
                    body?.VisibleStartedCount == 1 && body.ConfidentialStartedCount == 1 &&
                    body.VisibleCompletedCount == 1 && body.ConfidentialCompletedCount == 1);
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportFlows + query, admin))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminReportFlowsResponse>();
                var raw = await response.Content.ReadAsStringAsync();
                check("流程列表不泄露保密流程编码与内容", response.IsSuccessStatusCode &&
                    body?.Items.Single().FlowCode == "PUBLIC" && body.ConfidentialStartedCount == 1 &&
                    !raw.Contains("SECRET", StringComparison.Ordinal) &&
                    !raw.Contains("秘密内容", StringComparison.Ordinal));
            }

            foreach (var route in new[] { ApiRoutes.AdminReportNodes, ApiRoutes.AdminReportPeople })
            {
                using var response = await GetAsync(client, route + query, admin);
                var raw = await response.Content.ReadAsStringAsync();
                check("节点和人员接口不泄露保密流程", response.IsSuccessStatusCode &&
                    !raw.Contains("SECRET", StringComparison.Ordinal) &&
                    !raw.Contains("秘密内容", StringComparison.Ordinal));
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportExport + query, admin))
            {
                var workbook = await response.Content.ReadAsByteArrayAsync();
                var flowRows = ReadSheet(workbook, "流程汇总");
                var nodeRows = ReadSheet(workbook, "节点汇总");
                var peopleRows = ReadSheet(workbook, "人员汇总");
                var confidentialRows = ReadSheet(workbook, "保密匿名总量");
                check("管理员导出真实四表 XLSX 且列名为中文", response.IsSuccessStatusCode &&
                    response.Content.Headers.ContentType?.MediaType == AdminReportExportFile.ContentType &&
                    workbook.Length > 4 && workbook[0] == 'P' && workbook[1] == 'K' &&
                    flowRows.Count == 1 && flowRows[0].ContainsKey("流程编码") &&
                    flowRows[0]["流程编码"]?.ToString() == "PUBLIC" &&
                    confidentialRows.Count == 1 &&
                    confidentialRows[0]["匿名发起量"]?.ToString() == "1" &&
                    confidentialRows[0]["匿名结束量"]?.ToString() == "1");
                check("保密维度不进入导出三张明细汇总表", !ContainsSecret(flowRows) &&
                    !ContainsSecret(nodeRows) && !ContainsSecret(peopleRows) &&
                    confidentialRows[0].Keys.SequenceEqual(new[] { "匿名发起量", "匿名结束量" }));
            }

            var head = await LoginAsync(client, "RH");
            using (var response = await GetAsync(client, ApiRoutes.AdminReportOverview + query, head))
            {
                var body = await response.Content.ReadFromJsonAsync<AdminReportOverviewResponse>();
                check("部门负责人只看到直接负责部门", response.IsSuccessStatusCode &&
                    body?.VisibleStartedCount == 1 && body.ConfidentialStartedCount == 0);
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportExport + query, head))
            {
                var workbook = await response.Content.ReadAsByteArrayAsync();
                var flowRows = ReadSheet(workbook, "流程汇总");
                var confidentialRows = ReadSheet(workbook, "保密匿名总量");
                check("负责人导出只含直属部门且保密数量不跨部门", response.IsSuccessStatusCode &&
                    flowRows.Count == 1 && flowRows[0]["流程编码"]?.ToString() == "PUBLIC" &&
                    confidentialRows[0]["匿名发起量"]?.ToString() == "0");
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportExport + query +
                "&departmentId=D2", head))
            {
                check("负责人不能用导出部门参数扩展到子部门", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportExport + "?fromUtc=invalid", admin))
            {
                check("导出与 JSON 查询采用相同时间校验", response.StatusCode == HttpStatusCode.BadRequest);
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportOverview + query +
                "&departmentId=D2", head))
            {
                check("负责人不能用部门参数扩展到子部门", response.StatusCode == HttpStatusCode.Forbidden);
            }

            using (var response = await GetAsync(client, ApiRoutes.AdminReportOverview + "?fromUtc=invalid", admin))
            {
                var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
                check("报表时间参数错误返回统一业务错误", response.StatusCode == HttpStatusCode.BadRequest &&
                    body?.Code == ErrorCodes.ValidationFailed);
            }

            using (var scope = factory.Services.CreateScope())
            {
                var exporter = scope.ServiceProvider.GetRequiredService<IReportingWorkbookExporter>();
                var oversized = new ReportingQueryDto
                {
                    VisibleMetrics = new ReportingMetrics
                    {
                        Flows = Enumerable.Range(0, 10001).Select(index => new ReportingFlowMetric
                        {
                            FlowCode = "F" + index,
                        }).ToArray(),
                    },
                };
                try
                {
                    exporter.Export(oversized);
                    check("报表汇总超过行数上限时拒绝导出", false);
                }
                catch (ApprovalService.Domain.Shared.UserFriendlyException exception)
                {
                    check("报表汇总超过行数上限时拒绝导出", exception.Code == ErrorCodes.ValidationFailed);
                }

                var formulaMetric = new ReportingQueryDto
                {
                    VisibleMetrics = new ReportingMetrics
                    {
                        Flows = [new ReportingFlowMetric { FlowCode = "=SUM(1,1)" }],
                    },
                };
                var formulaRows = ReadSheet(exporter.Export(formulaMetric), "流程汇总");
                check("导出维度标识按文本转义表格公式", formulaRows.Count == 1 &&
                    formulaRows[0]["流程编码"]?.ToString() == "'=SUM(1,1)");
            }
        }
        finally
        {
            TempDataDirectory.Delete(directory);
        }
    }

    /// <summary>
    /// 从工作簿中读取指定中文工作表的真实单元格。
    /// </summary>
    /// <param name="workbook">XLSX 字节。</param>
    /// <param name="sheetName">工作表名称。</param>
    /// <returns>按表头命名的行。</returns>
    private static IReadOnlyList<IDictionary<string, object>> ReadSheet(byte[] workbook, string sheetName)
    {
        using var stream = new MemoryStream(workbook);
        return stream.Query(useHeaderRow: true, sheetName: sheetName, excelType: ExcelType.XLSX)
            .Cast<IDictionary<string, object>>().ToArray();
    }

    /// <summary>
    /// 检查工作表中的单元格是否出现保密流程标识或原始标题。
    /// </summary>
    /// <param name="rows">工作表行。</param>
    /// <returns>出现保密值时为真。</returns>
    private static bool ContainsSecret(IReadOnlyList<IDictionary<string, object>> rows) =>
        rows.SelectMany(row => row.Values).Any(value =>
            (value?.ToString() ?? string.Empty).Contains("SECRET", StringComparison.Ordinal) ||
            (value?.ToString() ?? string.Empty).Contains("秘密内容", StringComparison.Ordinal));

    /// <summary>
    /// 使用真实身份接口创建本地访问令牌。
    /// </summary>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="employeeNumber">工号。</param>
    /// <returns>访问令牌。</returns>
    private static async Task<string> LoginAsync(HttpClient client, string employeeNumber)
    {
        using var response = await client.PostAsJsonAsync(ApiRoutes.IdentityLogin,
            new IdentityLoginRequest(employeeNumber, "normal-report-password"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdentityTokensResponse>())!.AccessToken;
    }

    /// <summary>
    /// 带已验证人员访问令牌请求报表。
    /// </summary>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="path">报表路径。</param>
    /// <param name="token">人员访问令牌。</param>
    /// <returns>未释放的响应。</returns>
    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
