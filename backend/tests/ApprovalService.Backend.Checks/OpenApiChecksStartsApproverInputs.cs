using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApprovalService.Contracts;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Engine.Execution;
using ApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 审批人填写字段的节点授权、格式与事件回调留痕场景。
/// </summary>
internal static partial class OpenApiChecksStarts
{
    /// <summary>
    /// 通过真实 HTTP 验证只有本节点可写的顶层字段能进入审计和回调。
    /// </summary>
    /// <param name="services">已迁移的检查服务。</param>
    /// <param name="http">真实 HTTP 客户端。</param>
    /// <param name="apiKey">示例应用 API Key。</param>
    /// <param name="check">检查记录器。</param>
    /// <returns>异步操作。</returns>
    private static async Task RunApproverInputsAsync(IServiceProvider services, HttpClient http, string apiKey,
        Action<string, bool> check)
    {
        var graph = EngineScenarioChecks.Chain(FlowNodeTypes.Approval);
        var node = graph.Nodes.Single(item => item.Id == "n1");
        node.Assignee = new FlowAssignee { Source = FlowAssigneeSources.Member, Value = "approver" };
        node.FieldPermissions =
        [
            new FlowFieldPermission { FieldKey = "approvedAmount", Access = FlowFieldAccess.Editable },
            new FlowFieldPermission { FieldKey = "personInput", Access = FlowFieldAccess.Editable },
        ];
        var definition = new FlowDefinition
        {
            Code = "INPUT",
            Name = "审批人填写",
            Data = new FlowDataDefinition
            {
                Fields =
                [
                    new FlowFieldDefinition { Key = "amount", Name = "申请金额", Type = FlowFieldTypes.Money },
                    new FlowFieldDefinition { Key = "approvedAmount", Name = "核定金额", Type = FlowFieldTypes.Money,
                        ApproverInput = true, Required = true },
                    new FlowFieldDefinition { Key = "personInput", Name = "经办人", Type = FlowFieldTypes.Person,
                        ApproverInput = true },
                    new FlowFieldDefinition { Key = "secretNote", Name = "内部备注", Type = FlowFieldTypes.Text,
                        ApproverInput = true },
                    new FlowFieldDefinition { Key = "items", Name = "明细", Type = FlowFieldTypes.DetailTable,
                        Children = [new FlowFieldDefinition { Key = "comment", Name = "备注", ApproverInput = true }] },
                ],
            },
        };
        var factory = services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Flows.Add(new FlowEntity
            {
                Id = "input-flow",
                Code = "INPUT",
                Name = "审批人填写",
                PublishedVersionId = "input-version",
            });
            db.FlowVersions.Add(new FlowVersionEntity
            {
                Id = "input-version",
                FlowId = "input-flow",
                Version = 1,
                SchemaVersion = 1,
                DefinitionJson = JsonSerializer.Serialize(definition, ApprovalJson.Options),
                GraphJson = JsonSerializer.Serialize(graph, ApprovalJson.Options),
            });
            await db.SaveChangesAsync();
        }

        using var startRequest = Request(new OpenStartRequest
        {
            FlowCode = "INPUT",
            BusinessKey = "INPUT-1",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 15 }),
        }, apiKey, "input-start");
        using var startResponse = await http.SendAsync(startRequest);
        var started = await startResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        check("审批人填写字段不接受业务系统在发起时预填",
            startResponse.StatusCode == HttpStatusCode.OK && started is not null &&
            !started.Data.TryGetProperty("approvedAmount", out _));
        var taskId = started!.Tasks.Single().Id;

        using var businessFieldRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new { amount = 99 }),
        }, apiKey, "input-business-field");
        using var businessFieldResponse = await http.SendAsync(businessFieldRequest);
        check("审批人不能借填写字段修改业务系统原始字段",
            businessFieldResponse.StatusCode == HttpStatusCode.Forbidden);

        using var hiddenFieldRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new { secretNote = "不可见" }),
        }, apiKey, "input-hidden-field");
        using var hiddenFieldResponse = await http.SendAsync(hiddenFieldRequest);
        check("审批人不能填写当前节点未显式授权的字段",
            hiddenFieldResponse.StatusCode == HttpStatusCode.Forbidden);

        using var wrongTypeRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new { approvedAmount = "错误" }),
        }, apiKey, "input-wrong-type");
        using var wrongTypeResponse = await http.SendAsync(wrongTypeRequest);
        check("审批人填写金额仍按流程字段类型校验", wrongTypeResponse.StatusCode == HttpStatusCode.BadRequest);

        using var foreignPersonRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new { personInput = "E001" }),
        }, apiKey, "input-unbound-person");
        using var foreignPersonResponse = await http.SendAsync(foreignPersonRequest);
        check("审批人填写人员字段不能绕过本应用既有绑定",
            foreignPersonResponse.StatusCode == HttpStatusCode.BadRequest);

        using var detailRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new Dictionary<string, string>
            {
                ["items.comment"] = "缺行号",
            }),
        }, apiKey, "input-detail-field");
        using var detailResponse = await http.SendAsync(detailRequest);
        check("明细子字段缺行索引协议时明确拒绝", detailResponse.StatusCode == HttpStatusCode.BadRequest);

        using var missingRequiredRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
        }, apiKey, "input-required-missing");
        using var missingRequiredResponse = await http.SendAsync(missingRequiredRequest);
        check("成功推进时必须提交当前节点的必填审批人字段",
            missingRequiredResponse.StatusCode == HttpStatusCode.BadRequest);

        using var partialRequiredRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new { personInput = "alice" }),
        }, apiKey, "input-required-partial");
        using var partialRequiredResponse = await http.SendAsync(partialRequiredRequest);
        check("成功推进时提供其他字段也不能省略必填字段",
            partialRequiredResponse.StatusCode == HttpStatusCode.BadRequest);

        using var draftRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = "saveDraft",
            Opinion = "稍后填写",
        }, apiKey, "input-required-draft");
        using var draftResponse = await http.SendAsync(draftRequest);
        check("暂存意见无需填写必填审批人字段", draftResponse.StatusCode == HttpStatusCode.OK);

        using var validRequest = ActionRequest(taskId, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Approve,
            FieldValues = JsonSerializer.SerializeToElement(new { approvedAmount = 12.5m, personInput = "alice" }),
        }, apiKey, "input-valid");
        using var validResponse = await http.SendAsync(validRequest);
        var completed = await validResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        var audited = completed?.Events.Any(item => item.Event == "taskCompleted" &&
            item.Detail.TryGetProperty("audit", out var audit) &&
            audit.TryGetProperty("fieldValues", out var fields) &&
            fields.GetProperty("approvedAmount").GetDecimal() == 12.5m) == true;
        check("审批人填写值进入历史操作审计而不改业务数据快照",
            validResponse.StatusCode == HttpStatusCode.OK && audited && completed is not null &&
            !completed.Data.TryGetProperty("approvedAmount", out _));
        check("审批人成功推进后查询返回独立当前填写值", completed is not null &&
            completed.ApproverFieldValues.GetProperty("approvedAmount").GetDecimal() == 12.5m &&
            completed.ApproverFieldValues.GetProperty("personInput").GetString() == "alice");

        using var secondStartRequest = Request(new OpenStartRequest
        {
            FlowCode = "INPUT",
            BusinessKey = "INPUT-2",
            Initiator = "alice",
            Data = JsonSerializer.SerializeToElement(new { amount = 20 }),
        }, apiKey, "input-start-reject");
        using var secondStartResponse = await http.SendAsync(secondStartRequest);
        var secondInstance = await secondStartResponse.Content.ReadFromJsonAsync<OpenInstanceResponse>(ApprovalJson.Options);
        using var rejectInvalidRequest = ActionRequest(secondInstance!.Tasks.Single().Id, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Reject,
            Opinion = "不通过",
            FieldValues = JsonSerializer.SerializeToElement(new { approvedAmount = "格式错误" }),
        }, apiKey, "input-reject-invalid");
        using var rejectInvalidResponse = await http.SendAsync(rejectInvalidRequest);
        check("拒绝时若提交字段仍校验类型", rejectInvalidResponse.StatusCode == HttpStatusCode.BadRequest);
        using var rejectRequest = ActionRequest(secondInstance!.Tasks.Single().Id, new OpenTaskActionRequest
        {
            User = "bob",
            Action = EngineCommandKinds.Reject,
            Opinion = "不通过",
        }, apiKey, "input-required-reject");
        using var rejectResponse = await http.SendAsync(rejectRequest);
        check("拒绝审批无需填写必填审批人字段",
            secondStartResponse.StatusCode == HttpStatusCode.OK && rejectResponse.StatusCode == HttpStatusCode.OK);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var notifications = await db.OutboxItems.AsNoTracking().Where(item =>
                item.InstanceId == started.Id && item.Kind == "callback").ToArrayAsync();
            var decision = notifications.Select(item => JsonDocument.Parse(item.PayloadJson)).ToArray();
            try
            {
                check("任务完成与审批结果回调带独立填写值且重发正文已持久固定",
                    decision.Any(item => item.RootElement.GetProperty("event").GetString() == "task.completed" &&
                        item.RootElement.GetProperty("fieldValues").GetProperty("approvedAmount").GetDecimal() == 12.5m) &&
                    decision.Any(item => item.RootElement.GetProperty("event").GetString() == "instance.approved" &&
                        item.RootElement.GetProperty("fieldValues").GetProperty("approvedAmount").GetDecimal() == 12.5m) &&
                    decision.All(item => !item.RootElement.GetProperty("data").TryGetProperty("approvedAmount", out _)));
            }
            finally
            {
                foreach (var item in decision)
                {
                    item.Dispose();
                }
            }
        }
    }
}
