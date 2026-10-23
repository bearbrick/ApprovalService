using System.Security.Claims;
using System.Text;
using ApprovalService.Api.Hosting;
using ApprovalService.Application.Integration;
using ApprovalService.Application.Organization;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;
using ApprovalService.Infrastructure.Organization;

namespace ApprovalService.Api.Endpoints;

/// <summary>
/// 组织导入、接入应用推送与外部标识绑定路由。
/// </summary>
internal static class OrganizationEndpoints
{
    /// <summary>
    /// 导入文件最大字节数。
    /// </summary>
    private const long MaximumFileBytes = 5 * 1024 * 1024;

    /// <summary>
    /// 登记组织管理与开放接口，并分别应用人员或接入应用鉴权。
    /// </summary>
    /// <param name="app">路由构造器。</param>
    /// <returns>原路由构造器。</returns>
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.OrgChanges, async (HttpContext http, OrganizationBatchRequest request,
            ClientAppAppService apps, IOrganizationAppService service, CancellationToken cancellationToken) =>
        {
            var client = await apps.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey], cancellationToken);
            var result = await service.PushChangesAsync(ToBatch(request), client.Code, DateTimeOffset.UtcNow,
                cancellationToken);
            return new OrganizationBatchResponse(result.Issues.Count == 0, ToErrors(result.Issues),
                result.DepartedPersonIds);
        });
        app.MapPost(ApiRoutes.OrgBindings, async (HttpContext http, OrganizationBindingRequest request,
            ClientAppAppService apps, IOrganizationAppService service, CancellationToken cancellationToken) =>
        {
            var client = await apps.AuthenticateAsync(http.Request.Headers[HeaderNames.ApiKey], cancellationToken);
            if (request.Kind == "person" && !string.IsNullOrWhiteSpace(request.InternalId))
            {
                throw new UserFriendlyException(ErrorCodes.Forbidden,
                    "接入应用不能直接指定内部人员 ID，请按本应用人员工号或手机号匹配。");
            }

            return await BindAsync(service, request, client.Code, "client:" + client.Code, "push",
                cancellationToken);
        });
        app.MapPost(ApiRoutes.AdminOrgBindings, async (HttpContext http, OrganizationBindingRequest request,
            IOrganizationAppService service, CancellationToken cancellationToken) =>
        {
            var clientCode = request.ClientAppCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(clientCode))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "缺少接入应用编码。");
            }

            return await BindAsync(service, request, clientCode, PersonId(http), "manual", cancellationToken);
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        app.MapPost(ApiRoutes.OrgImport, async (HttpContext http, IOrganizationAppService service,
            CancellationToken cancellationToken) =>
        {
            var form = await http.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? throw new UserFriendlyException(
                ErrorCodes.ValidationFailed, "请选择 CSV 或 XLSX 组织文件。");
            if (file.Length == 0 || file.Length > MaximumFileBytes)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织文件必须非空且不超过 5 MB。");
            }

            await using var stream = file.OpenReadStream();
            OrganizationImportBatch batch;
            switch (Path.GetExtension(file.FileName).ToLowerInvariant())
            {
                case ".csv":
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
                    {
                        try
                        {
                            batch = OrganizationCsvCodec.Parse(await reader.ReadToEndAsync(cancellationToken));
                        }
                        catch (DecoderFallbackException)
                        {
                            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                                "组织 CSV 文件不是有效的 UTF-8 编码。");
                        }
                    }

                    break;
                case ".xlsx":
                    batch = OrganizationExcelCodec.Parse(stream);
                    break;
                default:
                    throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织文件只支持 CSV 或 XLSX。");
            }

            var preview = http.Request.Query["preview"] == "true";
            var issues = preview
                ? await service.ValidateImportAsync(batch, cancellationToken)
                : await service.ImportAsync(batch, PersonId(http), DateTimeOffset.UtcNow, cancellationToken);
            return new OrganizationBatchResponse(!preview && issues.Count == 0, ToErrors(issues), []);
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName).DisableAntiforgery();
        app.MapGet(ApiRoutes.OrgTemplate, (HttpContext http) =>
        {
            var format = http.Request.Query["format"].ToString();
            return format switch
            {
                "csv" => Results.File(Encoding.UTF8.GetBytes(OrganizationCsvCodec.Template()),
                    "text/csv; charset=utf-8", "organization-template.csv"),
                "xlsx" or "" => Results.File(OrganizationExcelCodec.Template(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "organization-template.xlsx"),
                _ => throw new UserFriendlyException(ErrorCodes.ValidationFailed, "模板格式只支持 csv 或 xlsx。"),
            };
        }).RequireAuthorization(OrganizationAdminRequirement.PolicyName);
        return app;
    }

    /// <summary>
    /// 把开放请求映射为领域批次，先拒绝缺失数组以免变成程序错误。
    /// </summary>
    /// <param name="request">请求体。</param>
    /// <returns>领域批次。</returns>
    private static OrganizationImportBatch ToBatch(OrganizationBatchRequest request)
    {
        if (request.Departments is null || request.People is null || request.Memberships is null ||
            request.Departments.Any(item => item is null) ||
            request.People.Any(item => item is null) ||
            request.Memberships.Any(item => item is null))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed,
                "组织部门、人员、所属部门列表及列表项不能为空。");
        }

        return new OrganizationImportBatch
        {
            Departments = request.Departments.Select(item => new DepartmentEntity
            {
                Id = item.Id,
                Code = item.Code,
                Name = item.Name,
                ParentId = item.ParentId,
                HeadPersonId = item.HeadPersonId,
            }).ToList(),
            People = request.People.Select(item => new PersonEntity
            {
                Id = item.Id,
                EmployeeNumber = item.EmployeeNumber,
                Name = item.Name,
                Phone = item.Phone,
                Email = item.Email,
                ManagerId = item.ManagerId,
                Position = item.Position,
                Grade = item.Grade,
                GradeRank = item.GradeRank,
                IsActive = item.IsActive,
            }).ToList(),
            Memberships = request.Memberships.Select(item => new PersonDepartmentEntity
            {
                PersonId = item.PersonId,
                DepartmentId = item.DepartmentId,
                IsPrimary = item.IsPrimary,
            }).ToList(),
        };
    }

    /// <summary>
    /// 绑定外部 ID；接入应用编码和操作者都来自认证上下文。
    /// </summary>
    /// <param name="service">组织应用服务。</param>
    /// <param name="request">绑定请求。</param>
    /// <param name="clientCode">已验证的接入应用编码。</param>
    /// <param name="actorId">认证操作者。</param>
    /// <param name="method">绑定来源。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>绑定结果。</returns>
    private static async Task<IResult> BindAsync(IOrganizationAppService service,
        OrganizationBindingRequest request, string clientCode, string actorId, string method,
        CancellationToken cancellationToken)
    {
        if (request.Kind == "person")
        {
            var personId = await service.BindPersonAsync(new PersonBindingInput
            {
                ClientAppCode = clientCode,
                ExternalUserId = request.ExternalId,
                PersonId = request.InternalId,
                EmployeeNumber = request.EmployeeNumber,
                Phone = request.Phone,
                Method = method,
            }, actorId, DateTimeOffset.UtcNow, cancellationToken);
            return Results.Ok(new
            {
                personId
            });
        }

        if (request.Kind == "department")
        {
            await service.BindDepartmentAsync(new DepartmentBindingInput
            {
                ClientAppCode = clientCode,
                ExternalDepartmentId = request.ExternalId,
                DepartmentId = request.InternalId ?? string.Empty,
                Method = method,
            }, actorId, DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        }

        throw new UserFriendlyException(ErrorCodes.ValidationFailed, "绑定类型必须是 person 或 department。");
    }

    /// <summary>
    /// 逐项校验问题映射到统一错误模型。
    /// </summary>
    /// <param name="issues">领域校验问题。</param>
    /// <returns>开放接口问题列表。</returns>
    private static IReadOnlyList<ApiFieldError> ToErrors(IReadOnlyList<OrganizationImportIssue> issues) =>
        issues.Select(item => new ApiFieldError(item.Field, ErrorCodes.ValidationFailed, item.Message)).ToArray();

    /// <summary>
    /// 读取经过 JWT 验证的内部人员 ID。
    /// </summary>
    /// <param name="http">请求上下文。</param>
    /// <returns>内部人员 ID。</returns>
    private static string PersonId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UserFriendlyException(ErrorCodes.Unauthorized, "请先登录。");
}
