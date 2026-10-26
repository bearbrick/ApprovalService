using System.Globalization;
using System.Text.Json;
using ApprovalService.Application.Printing;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using ApprovalService.Engine.Expressions;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ZXing;
using ZXing.QrCode;

namespace ApprovalService.Infrastructure.Printing;

/// <summary>
/// 使用上传字体生成 A4 审批凭证，表格分页时重复列名。
/// </summary>
public sealed class PdfApprovalPrinter : IApprovalPrinter
{
    /// <inheritdoc />
    public void ValidateFont(byte[] bytes)
    {
        try
        {
            var font = new XFont(UploadedFontResolver.Register(bytes), 9, XFontStyleEx.Regular);
            if ("中文审批单".Any(character => GlyphHelper.GlyphIndexFromCodePoint(character, font) == 0))
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "字体缺少基本中文字形，请选择完整的中文字体。");
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IndexOutOfRangeException or EndOfStreamException or KeyNotFoundException)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "字体无法解析，请上传有效的中文 TrueType 字体。");
        }
    }

    /// <inheritdoc />
    public byte[] Render(ApprovalPrintInput input)
    {
        using var layout = new PrintPageLayout(input);
        var detail = input.Detail;
        var fields = input.Settings.FieldKeys.Count == 0 ? detail.FieldDefinitions :
            input.Settings.FieldKeys.Select(key => detail.FieldDefinitions.FirstOrDefault(field => field.Key == key))
                .Where(field => field is not null).Cast<CenterFieldDefinitionResponse>().ToArray();
        layout.Title(detail.Instance.Title, Label(detail.Instance.Status));
        layout.Table(["申请人", "所属部门"], [[detail.Instance.InitiatorName, input.DepartmentName]]);
        layout.Table(["业务单号", "来源系统"], [[detail.Instance.BusinessKey, detail.Instance.SourceName]]);
        layout.Table(["提交时间", "流程名称"], [[detail.Instance.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), detail.Instance.FlowName]]);
        var simple = fields.Where(field => field.Type != "detailTable").ToArray();
        for (var index = 0; index < simple.Length; index += 2)
        {
            var left = simple[index];
            var right = index + 1 < simple.Length ? simple[index + 1] : null;
            layout.Table([left.Name, right?.Name ?? string.Empty],
                [[DisplayValue(detail, detail.Data, left), right is null ? string.Empty : DisplayValue(detail, detail.Data, right)]]);
        }
        foreach (var field in fields.Where(field => field.Type == "detailTable"))
        {
            if (!detail.Data.TryGetProperty(field.Key, out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            layout.Section(field.Name);
            layout.Table(field.Children.Select(child => child.Name).ToArray(),
                rows.EnumerateArray().Select(row => field.Children.Select(child => DisplayValue(detail, row, child)).ToArray()).ToArray());
            foreach (var money in field.Children.Where(child => child.Type == "money"))
            {
                var total = rows.EnumerateArray().Sum(row => row.TryGetProperty(money.Key, out var amount) &&
                    amount.TryGetDecimal(out var number) ? number : 0);
                layout.Paragraph($"{money.Name}合计：{total:N2}　大写：{RmbUppercase.Format(total)}");
            }
        }
        layout.Section("审批记录");
        foreach (var item in detail.Events)
        {
            var node = detail.Tasks.FirstOrDefault(task => task.NodeId == item.NodeId)?.NodeName ?? "审批操作";
            var actor = Name(input, item.ActorId);
            if (item.OnBehalfOfId is { } original)
            {
                actor += $"（代{Name(input, original)}）";
            }
            var signature = input.Settings.ShowSignatures && item.SignatureFileId is { } signatureId
                ? input.Signatures.GetValueOrDefault(signatureId) : null;
            layout.Table(["节点", "处理人", "结果", "意见", "时间", "签名"], [[node, actor, Label(item.Kind),
                input.Settings.ShowComments ? item.Opinion ?? string.Empty : "（不显示）",
                item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ""]], signature);
        }
        layout.Section("附件清单");
        layout.Table(["文件名", "类型", "字节数"], detail.Attachments.Select(file =>
            new[] { file.FileName, file.ContentType, file.Size.ToString(CultureInfo.InvariantCulture) }).ToArray());
        if (input.Settings.IncludeReceiptPages)
        {
            layout.ReceiptPages(detail.Attachments.Where(file => input.Receipts.ContainsKey(file.Id))
                .Select(file => (file.FileName, input.Receipts[file.Id])).ToArray());
        }
        return layout.Save();
    }

    /// <summary>
    /// 读取已裁剪数据中的标量值。
    /// </summary>
    /// <param name="data">安全数据。</param>
    /// <param name="key">可见字段编码。</param>
    /// <returns>可打印内容。</returns>
    private static string Value(JsonElement data, string key) => data.TryGetProperty(key, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString() : string.Empty;

    /// <summary>
    /// 附件字段只显示经归属校验的文件名，不打印对象存储标识或跨单引用。
    /// </summary>
    /// <param name="detail">已授权详情。</param>
    /// <param name="data">本层数据。</param>
    /// <param name="field">可见字段。</param>
    /// <returns>显示内容。</returns>
    private static string DisplayValue(CenterDetailResponse detail, JsonElement data, CenterFieldDefinitionResponse field)
    {
        if (field.Type != "attachment" || !data.TryGetProperty(field.Key, out var value))
        {
            return Value(data, field.Key);
        }
        var ids = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString()).ToArray() : [value.GetString()];
        return string.Join("、", detail.Attachments.Where(file => ids.Contains(file.Id)).Select(file => file.FileName));
    }

    /// <summary>
    /// 取得已授权人员名称。
    /// </summary>
    /// <param name="input">打印上下文。</param>
    /// <param name="id">人员 ID。</param>
    /// <returns>名称或原始标识。</returns>
    private static string Name(ApprovalPrintInput input, string? id) => id is null ? "系统" : input.People.GetValueOrDefault(id, id);

    /// <summary>
    /// 将常见状态和操作翻译为纸质凭证用语。
    /// </summary>
    /// <param name="value">状态或事件编码。</param>
    /// <returns>中文名称。</returns>
    private static string Label(string value) => value switch
    {
        "approved" or "taskApproved" => "已同意",
        "rejected" or "taskRejected" => "已拒绝",
        "running" => "审批中",
        "withdrawn" => "已撤回",
        "terminated" => "已终止",
        "cancelled" => "已取消",
        "awaitingInitiator" => "待发起人处理",
        "instanceStarted" => "发起",
        "instanceApproved" => "审批通过",
        "taskCompleted" => "办理完成",
        "printed" => "打印",
        "taskCreated" => "生成待办",
        "taskDelegated" => "转委托",
        "taskTransferred" => "转交",
        _ => value,
    };
}
