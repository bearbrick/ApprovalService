using System.Text.Json;
using ApprovalService.Application.Integration;
using ApprovalService.Contracts.Flows;
using ApprovalService.Domain.Shared;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 发起数据准备场景：服务端公式、审批人字段、附件与逐字段错误。
/// </summary>
internal static class OpenApiChecksData
{
    /// <summary>
    /// 验证业务系统不能自造公式结果或提前注入审批人值。
    /// </summary>
    /// <param name="check">检查结果记录器。</param>
    public static void Run(Action<string, bool> check)
    {
        var definition = new FlowDataDefinition
        {
            Fields =
            [
                new FlowFieldDefinition { Key = "base", Name = "税前金额", Type = FlowFieldTypes.Money, Required = true },
                new FlowFieldDefinition { Key = "tax", Name = "税额", Type = FlowFieldTypes.Money, Required = true },
                new FlowFieldDefinition { Key = "total", Name = "合计", Type = FlowFieldTypes.Money,
                    Formula = "${base} + ${tax}" },
                new FlowFieldDefinition { Key = "auditNote", Name = "审核备注", ApproverInput = true },
                new FlowFieldDefinition { Key = "proof", Name = "附件", Type = FlowFieldTypes.Attachment },
                new FlowFieldDefinition
                {
                    Key = "items", Name = "明细", Type = FlowFieldTypes.DetailTable,
                    Children =
                    [
                        new FlowFieldDefinition { Key = "qty", Name = "数量", Type = FlowFieldTypes.Number },
                        new FlowFieldDefinition { Key = "price", Name = "单价", Type = FlowFieldTypes.Money },
                        new FlowFieldDefinition { Key = "lineAmount", Name = "行金额", Type = FlowFieldTypes.Money,
                            Formula = "${items.qty} * ${items.price}" },
                    ],
                },
            ],
        };
        using var input = JsonDocument.Parse("{" +
            "\"base\":100,\"tax\":20,\"total\":999,\"auditNote\":\"伪造\"," +
            "\"items\":[{\"qty\":2,\"price\":3,\"lineAmount\":999}]}");
        var prepared = OpenApprovalDataPreparer.Prepare(definition, input.RootElement);
        check("发起时服务端重算总额与明细公式且清除调用方伪造的审批人字段",
            prepared.GetProperty("total").GetDecimal() == 120m &&
            prepared.GetProperty("items")[0].GetProperty("lineAmount").GetDecimal() == 6m &&
            !prepared.TryGetProperty("auditNote", out _));

        using var missing = JsonDocument.Parse("{\"tax\":20}");
        check("缺少必传业务字段时返回可定位的校验错误",
            Rejects(definition, missing.RootElement, "data.base"));
        using var attached = JsonDocument.Parse("{\"base\":100,\"tax\":20,\"proof\":[\"file-1\"]}");
        check("附件归属未接通时发起拒绝未验证的文件 ID",
            Rejects(definition, attached.RootElement, "data.proof"));

        var cycle = new FlowDataDefinition
        {
            Fields =
            [
                new FlowFieldDefinition { Key = "a", Formula = "${b} + 1" },
                new FlowFieldDefinition { Key = "b", Formula = "${a} + 1" },
            ],
        };
        using var empty = JsonDocument.Parse("{}");
        check("循环公式在执行前被拒绝而不进入审批条件", Rejects(cycle, empty.RootElement, "循环依赖"));
    }

    /// <summary>
    /// 判断准备数据是否以指定中文定位信息拒绝。
    /// </summary>
    /// <param name="definition">审批数据定义。</param>
    /// <param name="data">输入快照。</param>
    /// <param name="location">应出现在错误中的字段或原因。</param>
    /// <returns>拒绝且定位准确时为真。</returns>
    private static bool Rejects(FlowDataDefinition definition, JsonElement data, string location)
    {
        try
        {
            OpenApprovalDataPreparer.Prepare(definition, data);
            return false;
        }
        catch (UserFriendlyException error)
        {
            return error.Code == "VALIDATION_FAILED" && error.Message.Contains(location, StringComparison.Ordinal);
        }
    }
}
