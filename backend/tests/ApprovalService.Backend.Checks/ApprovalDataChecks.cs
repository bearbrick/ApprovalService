using System.Text.Json;
using ApprovalService.Contracts.Flows;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Runtime;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Expressions;

namespace ApprovalService.Backend.Checks;

/// <summary>
/// 审批数据、受限表达式、人民币大写和编号规则的场景检查。
/// </summary>
internal static class ApprovalDataChecks
{
    /// <summary>
    /// 运行审批数据相关场景。
    /// </summary>
    /// <param name="check">记录检查结果。</param>
    public static void Run(Action<string, bool> check)
    {
        using var document = JsonDocument.Parse("""
            {"amount":12.5,"items":[{"qty":2,"amount":10},{"qty":3,"amount":20}],"start":"2026-10-01","end":"2026-10-04"}
            """);
        var data = document.RootElement;
        check("表达式按优先级求四则运算", Equals(ApprovalExpression.Evaluate("2 + 3 * 4", data), 14m));
        check("表达式读取快照并对明细列求和及平均值",
            Equals(ApprovalExpression.Evaluate("SUM(${items.amount}) + AVG(${items.amount})", data), 45m));
        check("表达式支持最大最小值、比较、且或",
            Equals(ApprovalExpression.Evaluate("MAX(${items.qty}) == 3 AND MIN(${items.qty}) < 3 OR FALSE", data), true));
        check("ANY 支持集合非空真值和集合包含指定值",
            Equals(ApprovalExpression.Evaluate("ANY(${items.qty}, 3)", data), true)
            && Equals(ApprovalExpression.Evaluate("ANY(${items.qty}, 5)", data), false));
        check("IF 仅计算命中的分支，日期差按日计算",
            Equals(ApprovalExpression.Evaluate("IF(${amount} > 10, DATEDIFF(${start}, ${end}), 1 / 0)", data), 3m));
        check("发起人上下文可参与条件且不从快照取值",
            Equals(ApprovalExpression.Evaluate("${initiator.level} >= 3", data,
                new Dictionary<string, object?> { ["initiator.level"] = 4 }), true));
        check("表达式字段引用返回去重路径",
            ApprovalExpression.Validate("${items.amount} + ${items.amount}").SequenceEqual(["items.amount"]));
        check("超长、深嵌套和未知函数会给中文可纠正错误",
            Fails(() => ApprovalExpression.Validate(new string('1', ApprovalExpression.MaxLength + 1)))
            && Fails(() => ApprovalExpression.Validate(new string('(', ApprovalExpression.MaxDepth + 1) + "1" + new string(')', ApprovalExpression.MaxDepth + 1)))
            && Fails(() => ApprovalExpression.Validate("EXEC('bad')")));
        check("除零和缺字段在求值时明确失败",
            Fails(() => ApprovalExpression.Evaluate("1 / 0", data))
            && Fails(() => ApprovalExpression.Evaluate("${missing}", data)));

        var definition = new FlowDataDefinition
        {
            Fields =
            [
                new FlowFieldDefinition { Key = "reason", Name = "事由", Type = FlowFieldTypes.Text, Required = true },
                new FlowFieldDefinition { Key = "total", Name = "总额", Type = FlowFieldTypes.Money, Formula = "SUM(${items.amount})" },
                new FlowFieldDefinition { Key = "audit", Name = "核定金额", Type = FlowFieldTypes.Money, ApproverInput = true, Required = true },
                new FlowFieldDefinition
                {
                    Key = "items", Name = "明细", Type = FlowFieldTypes.DetailTable, Required = true,
                    Children =
                    [
                        new FlowFieldDefinition { Key = "qty", Name = "数量", Type = FlowFieldTypes.Number, Required = true },
                        new FlowFieldDefinition { Key = "date", Name = "日期", Type = FlowFieldTypes.Date, Required = true },
                    ],
                },
            ],
        };
        using var invalid = JsonDocument.Parse("""{"items":[{"qty":"two","date":"not-a-date"},{}]}""");
        var issues = ApprovalDataValidator.Validate(definition, invalid.RootElement);
        check("快照缺失和明细子字段类型错误返回精确路径",
            issues.Select(issue => issue.Field).OrderBy(path => path, StringComparer.Ordinal)
                .SequenceEqual(new[] { "data.items[0].date", "data.items[0].qty", "data.items[1].date", "data.items[1].qty", "data.reason" }));
        check("计算字段和审批人填写字段不要求业务系统发起时提交",
            issues.All(issue => issue.Field is not ("data.total" or "data.audit")));
        using var emptyRequired = JsonDocument.Parse("""{"reason":" ","items":[]}""");
        check("必传文本不能只含空格，必传明细至少有一行",
            ApprovalDataValidator.Validate(definition, emptyRequired.RootElement)
                .Select(issue => issue.Field).OrderBy(path => path, StringComparer.Ordinal)
                .SequenceEqual(new[] { "data.items", "data.reason" }));
        var example = ApprovalDataValidator.CreateExampleJson(definition);
        using var sample = JsonDocument.Parse(example);
        check("同一数据定义生成可校验的 JSON 样例",
            ApprovalDataValidator.Validate(definition, sample.RootElement).Count == 0
            && !sample.RootElement.TryGetProperty("total", out _)
            && !sample.RootElement.TryGetProperty("audit", out _));
        check("人民币金额大写处理角分、跨组零和负数",
            RmbUppercase.Format(1334.50m) == "壹仟叁佰叁拾肆元伍角整"
            && RmbUppercase.Format(10001.01m) == "壹万零壹元零壹分"
            && RmbUppercase.Format(-0.05m) == "负零元伍分");
        check("审批编号由调用方提供日期和流水号，并校验位数",
            ApprovalSerialNumber.Format("SP", "yyyyMM", 4, 7, new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.FromHours(8))) == "SP-202610-0007"
            && NumberFails(() => ApprovalSerialNumber.Format("SP", "yyyyMM", 2, 100, DateTimeOffset.UnixEpoch)));
    }

    /// <summary>
    /// 判断表达式是否抛出中文错误。
    /// </summary>
    /// <param name="action">待执行表达式。</param>
    /// <returns>是否出现预期错误。</returns>
    private static bool Fails(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ApprovalExpressionException error)
        {
            return error.Message.Any(character => character >= '\u4e00' && character <= '\u9fff');
        }
    }

    /// <summary>
    /// 判断编号规则是否拒绝非法配置。
    /// </summary>
    /// <param name="action">待执行的编号操作。</param>
    /// <returns>是否抛出参数错误。</returns>
    private static bool NumberFails(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (UserFriendlyException error) when (error.Code == ErrorCodes.ValidationFailed)
        {
            return true;
        }
    }
}
