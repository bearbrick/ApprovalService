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
        var existing = await repository.LoadMasterDataAsync(cancellationToken);
        return OrganizationImportRules.Validate(batch, existing.Departments, existing.People);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationImportIssue>> ImportAsync(OrganizationImportBatch batch,
        string actorId, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var issues = await ValidateImportAsync(batch, cancellationToken);
        if (issues.Count == 0)
        {
            await repository.SaveTrustedImportAsync(batch, actorId, occurredAt, cancellationToken);
        }

        return issues;
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

        var issues = await ValidateImportAsync(batch, cancellationToken);
        if (issues.Count > 0)
        {
            return new OrganizationPushResult { Issues = issues };
        }

        var departed = await repository.SavePushedChangesAsync(batch, clientAppCode,
            "client:" + clientAppCode, occurredAt, cancellationToken);
        return new OrganizationPushResult { DepartedPersonIds = departed };
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
    public Task BindDepartmentAsync(DepartmentBindingInput input, string actorId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ClientAppCode) || string.IsNullOrWhiteSpace(input.ExternalDepartmentId) ||
            string.IsNullOrWhiteSpace(input.DepartmentId))
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "部门绑定的应用、外部 ID 和内部 ID 不能为空。");
        }

        return repository.BindDepartmentAsync(new ExternalDepartmentBindingEntity
        {
            ClientAppCode = input.ClientAppCode,
            ExternalDepartmentId = input.ExternalDepartmentId,
            DepartmentId = input.DepartmentId,
            Method = input.Method,
        }, actorId, occurredAt, cancellationToken);
    }
}
