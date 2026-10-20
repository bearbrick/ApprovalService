using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Organization;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Application.Organization;

/// <summary>
/// 对组织导入与绑定执行校验、来源边界和审计上下文传递。
/// </summary>
/// <param name="repository">组织仓储。</param>
public sealed class OrganizationAppService(IOrganizationRepository repository) : IOrganizationAppService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationImportIssue>> ValidateImportAsync(OrganizationImportBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Departments is null || batch.People is null || batch.Memberships is null ||
            batch.PersonBindings is null || batch.DepartmentBindings is null)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "组织部门、人员和所属部门列表不能为空。");
        }

        var existing = await repository.LoadMasterDataAsync(cancellationToken);
        var clientAppCodes = await repository.LoadClientAppCodesAsync(cancellationToken);
        return OrganizationImportRules.Validate(batch, existing.Departments, existing.People,
            existing.Memberships, clientAppCodes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationImportIssue>> ImportAsync(OrganizationImportBatch batch,
        string actorId, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var result = await repository.SaveTrustedImportAsync(batch, actorId, occurredAt, cancellationToken);
        return result.Issues;
    }

    /// <inheritdoc />
    public async Task<OrganizationPushResult> PushChangesAsync(OrganizationImportBatch batch,
        string clientAppCode, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientAppCode);
        if (clientAppCode == "approvalService")
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden, "接入应用不能冒充组织主数据来源。");
        }

        var result = await repository.SavePushedChangesAsync(batch, clientAppCode,
            "client:" + clientAppCode, occurredAt, cancellationToken);
        return new OrganizationPushResult
        {
            Issues = result.Issues,
            DepartedPersonIds = result.DepartedPersonIds,
        };
    }

    /// <inheritdoc />
    public async Task<string> BindPersonAsync(PersonBindingInput input, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ClientAppCode) || string.IsNullOrWhiteSpace(input.ExternalUserId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "接入应用编码和业务人员 ID 不能为空。");
        }

        var person = string.IsNullOrWhiteSpace(input.PersonId)
            ? await repository.MatchPersonAsync(input.EmployeeNumber, input.Phone, cancellationToken)
            : await repository.GetPersonAsync(input.PersonId, cancellationToken);
        if (person is null)
        {
            throw new UserFriendlyException(ErrorCodes.NotFound, "找不到唯一匹配的审批服务人员。");
        }

        if (!person.IsActive)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "离职人员不能建立新的业务系统绑定。");
        }

        if (input.Method == "push" && person.TrustedSource != input.ClientAppCode)
        {
            throw new UserFriendlyException(ErrorCodes.Forbidden,
                "接入应用不能自行绑定其他来源的人员，请由组织管理员确认绑定。");
        }

        await repository.BindPersonAsync(new ExternalPersonBindingEntity
        {
            ClientAppCode = input.ClientAppCode,
            ExternalUserId = input.ExternalUserId,
            PersonId = person.Id,
            Method = input.Method,
        }, actorId, occurredAt, cancellationToken);
        return person.Id;
    }

    /// <inheritdoc />
    public async Task BindDepartmentAsync(DepartmentBindingInput input, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ClientAppCode) || string.IsNullOrWhiteSpace(input.ExternalDepartmentId) ||
            string.IsNullOrWhiteSpace(input.DepartmentId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "部门绑定的应用、外部 ID 和内部 ID 不能为空。");
        }

        if (input.Method == "push")
        {
            var master = await repository.LoadMasterDataAsync(cancellationToken);
            var department = master.Departments.SingleOrDefault(item => item.Id == input.DepartmentId);
            if (department is null)
            {
                throw new UserFriendlyException(ErrorCodes.NotFound, "部门不存在。");
            }

            if (department.TrustedSource != input.ClientAppCode)
            {
                throw new UserFriendlyException(ErrorCodes.Forbidden,
                    "接入应用不能自行绑定其他来源的部门，请由组织管理员确认绑定。");
            }
        }

        await repository.BindDepartmentAsync(new ExternalDepartmentBindingEntity
        {
            ClientAppCode = input.ClientAppCode,
            ExternalDepartmentId = input.ExternalDepartmentId,
            DepartmentId = input.DepartmentId,
            Method = input.Method,
        }, actorId, occurredAt, cancellationToken);
    }
}
