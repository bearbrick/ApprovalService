using System.Globalization;
using System.Text;
using ApprovalService.Application.Printing;
using ApprovalService.Contracts.Protocol;
using ApprovalService.Domain.Shared;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ZXing;
using ZXing.QrCode;

namespace ApprovalService.Infrastructure.Printing;

/// <summary>
/// 单次 PDF 排版器，所有坐标以点计，长行按实际字体宽度换行。
/// </summary>
internal sealed class PrintPageLayout : IDisposable
{
    /// <summary>
    /// 安全打印上下文。
    /// </summary>
    private readonly ApprovalPrintInput input;
    /// <summary>
    /// 当前文档。
    /// </summary>
    private readonly PdfDocument document = new();
    /// <summary>
    /// 嵌入的正文字体。
    /// </summary>
    private readonly XFont font;
    /// <summary>
    /// 使用同一嵌入字体的审批单标题字号。
    /// </summary>
    private readonly XFont titleFont;
    /// <summary>
    /// 当前页面图形。
    /// </summary>
    private XGraphics graphics = null!;
    /// <summary>
    /// 当前页面。
    /// </summary>
    private PdfPage page = null!;
    /// <summary>
    /// 当前垂直位置。
    /// </summary>
    private double y;
    /// <summary>
    /// 正文可用宽度。
    /// </summary>
    private double Width => page.Width.Point - 72;
    /// <summary>
    /// 正文下边界。
    /// </summary>
    private double Bottom => page.Height.Point - 48;

    /// <summary>
    /// 创建独立文档和上传字体。
    /// </summary>
    /// <param name="input">已授权打印快照。</param>
    public PrintPageLayout(ApprovalPrintInput input)
    {
        this.input = input;
        font = new XFont(UploadedFontResolver.Register(input.FontBytes), 9, XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode));
        titleFont = new XFont(UploadedFontResolver.Register(input.FontBytes), 16, XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode));
        document.Info.Title = input.Detail.Instance.Title;
        document.Info.Creator = "ApprovalService";
        NewPage();
    }

    /// <summary>
    /// 开始新页面并绘制固定页眉。
    /// </summary>
    private void NewPage()
    {
        if (document.PageCount >= 500)
        {
            throw new UserFriendlyException(ErrorCodes.ValidationFailed, "审批单内容超过五百页，请减少打印字段或票据后重试。");
        }
        graphics?.Dispose();
        page = document.AddPage();
        page.Size = PageSize.A4;
        page.Orientation = input.Settings.Orientation == "landscape" ? PageOrientation.Landscape : PageOrientation.Portrait;
        graphics = XGraphics.FromPdfPage(page);
        var x = 36d;
        if (input.Settings.ShowLogo && input.Logo is { Length: > 0 })
        {
            DrawImage(input.Logo, new XRect(x, 25, 42, 30));
            x += 48;
        }
        graphics.DrawString(input.CompanyName, font, XBrushes.Black, x, 38);
        graphics.DrawString($"审批编号 {input.Detail.Instance.SerialNo}　　第 {input.PrintNumber} 次打印", font,
            XBrushes.Black, 36, 64);
        if (input.Settings.ShowQrCode && input.DetailUrl.Length > 0)
        {
            var matrix = new QRCodeWriter().encode(input.DetailUrl, BarcodeFormat.QR_CODE, 0, 0);
            var scale = 48d / matrix.Width;
            for (var row = 0; row < matrix.Height; row++)
            {
                for (var column = 0; column < matrix.Width; column++)
                {
                    if (matrix[column, row])
                    {
                        graphics.DrawRectangle(XBrushes.Black, page.Width.Point - 84 + column * scale, 20 + row * scale, scale, scale);
                    }
                }
            }
        }
        if (input.Settings.ShowWatermark)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
            graphics.RotateTransform(-30);
            graphics.DrawString($"{input.PrintedBy}　{input.PrintedAt:yyyy-MM-dd HH:mm}", font, XBrushes.LightGray, -100, 0);
            graphics.Restore(state);
        }
        graphics.DrawLine(XPens.Gray, 36, 74, page.Width.Point - 36, 74);
        y = 88;
    }

    /// <summary>
    /// 确保完整元素能放入当前页面。
    /// </summary>
    /// <param name="height">需要的高度。</param>
    private void Ensure(double height)
    {
        if (y + height > Bottom)
        {
            NewPage();
        }
    }

    /// <summary>
    /// 绘制标题与方框状态。
    /// </summary>
    /// <param name="title">审批标题。</param>
    /// <param name="status">中文状态。</param>
    public void Title(string title, string status)
    {
        foreach (var line in Wrap(title, Width / 2))
        {
            Ensure(24);
            graphics.DrawString(line, titleFont, XBrushes.Black, new XRect(36, y, Width, 22), XStringFormats.TopCenter);
            y += 24;
        }
        graphics.DrawRectangle(XPens.Black, page.Width.Point - 116, y, 80, 20);
        graphics.DrawString(status, font, XBrushes.Black, page.Width.Point - 110, y + 13);
        y += 28;
    }

    /// <summary>
    /// 绘制小节标题。
    /// </summary>
    /// <param name="title">标题。</param>
    public void Section(string title)
    {
        Ensure(42);
        y += 8;
        Paragraph(title);
    }

    /// <summary>
    /// 绘制可以跨页的段落。
    /// </summary>
    /// <param name="text">内容。</param>
    public void Paragraph(string text)
    {
        foreach (var line in Wrap(text, Width - 12))
        {
            Ensure(14);
            graphics.DrawString(line, font, XBrushes.Black, 40, y + 10);
            y += 14;
        }
        y += 4;
    }

    /// <summary>
    /// 绘制跨页重复表头的等宽表格，长单元格也可以跨页。
    /// </summary>
    /// <param name="headers">表头。</param>
    /// <param name="rows">内容行。</param>
    /// <param name="signature">可选末列签名图片。</param>
    public void Table(string[] headers, string[][] rows, byte[]? signature = null)
    {
        if (headers.Length > 8)
        {
            // 列数过多时分成连续列组，避免把中文压成不可读的极窄列。
            for (var offset = 0; offset < headers.Length; offset += 8)
            {
                Table(headers.Skip(offset).Take(8).ToArray(), rows.Select(row => row.Skip(offset).Take(8).ToArray()).ToArray());
            }
            return;
        }
        if (headers.Length == 0)
        {
            return;
        }
        var cellWidth = Width / headers.Length;
        var headerLines = headers.Select(header => Wrap(header, cellWidth - 10)).ToArray();
        var headerHeight = headerLines.Max(lines => lines.Count) * 13 + 8;
        Ensure(headerHeight + 24);
        DrawRow(headerLines, headerHeight, true, cellWidth);
        foreach (var row in rows)
        {
            var lines = row.Select(cell => Wrap(cell, cellWidth - 10)).ToArray();
            var count = lines.Max(column => column.Count);
            var offset = 0;
            while (offset < count)
            {
                var available = (int)((Bottom - y - 8) / 13);
                if (available < (signature is null ? 1 : 3))
                {
                    NewPage();
                    DrawRow(headerLines, headerHeight, true, cellWidth);
                    available = (int)((Bottom - y - 8) / 13);
                }
                var take = Math.Min(count - offset, available);
                var height = Math.Max(take * 13 + 8, signature is null ? 0 : 40);
                DrawRow(lines.Select(column => column.Skip(offset).Take(take).ToList()).ToArray(), height, false, cellWidth);
                if (signature is not null && offset == 0)
                {
                    DrawImage(signature, new XRect(36 + Width - cellWidth + 4, y - height + 4, cellWidth - 8, height - 8));
                }
                offset += take;
            }
        }
        y += 5;
    }

    /// <summary>
    /// 绘制一行已经分段的单元格。
    /// </summary>
    /// <param name="lines">每列文本行。</param>
    /// <param name="height">行高。</param>
    /// <param name="header">是否灰底表头。</param>
    /// <param name="cellWidth">列宽。</param>
    private void DrawRow(List<string>[] lines, double height, bool header, double cellWidth)
    {
        for (var column = 0; column < lines.Length; column++)
        {
            var x = 36 + column * cellWidth;
            graphics.DrawRectangle(XPens.Gray, header ? XBrushes.WhiteSmoke : XBrushes.White, x, y, cellWidth, height);
            for (var line = 0; line < lines[column].Count; line++)
            {
                graphics.DrawString(lines[column][line], font, XBrushes.Black, x + 5, y + 13 + line * 13);
            }
        }
        y += height;
    }

    /// <summary>
    /// 按实际字形宽度换行，不丢弃超长文本。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <param name="width">最大宽度。</param>
    /// <returns>至少一行文本。</returns>
    private List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var rune in text.Replace("\r", string.Empty, StringComparison.Ordinal).EnumerateRunes())
        {
            var value = rune.ToString();
            if (!Rune.IsWhiteSpace(rune) && GlyphHelper.GlyphIndexFromCodePoint(rune.Value, font) == 0)
            {
                throw new UserFriendlyException(ErrorCodes.ValidationFailed, "当前打印字体缺少审批单使用的字形，请联系管理员更换完整字体。");
            }
            if (value == "\n" || current.Length > 0 && graphics.MeasureString(current.ToString() + value, font).Width > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (value != "\n")
            {
                current.Append(value);
            }
        }
        lines.Add(current.ToString());
        return lines;
    }

    /// <summary>
    /// 绘制一个保持比例的签名。
    /// </summary>
    /// <param name="bytes">图片。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public void Image(byte[] bytes, double width, double height)
    {
        Ensure(height + 4);
        DrawImage(bytes, new XRect(40, y, width, height));
        y += height + 4;
    }

    /// <summary>
    /// 每页四张票据，独立分页并标注文件名。
    /// </summary>
    /// <param name="receipts">经过授权的票据。</param>
    public void ReceiptPages((string Name, byte[] Bytes)[] receipts)
    {
        for (var index = 0; index < receipts.Length; index++)
        {
            if (index % 4 == 0)
            {
                NewPage();
            }
            var width = Width / 2 - 8;
            var height = (Bottom - 90) / 2 - 20;
            var x = 36 + index % 2 * (width + 16);
            var top = 90 + index % 4 / 2 * (height + 20);
            DrawImage(receipts[index].Bytes, new XRect(x, top, width, height - 20));
            var name = Wrap(receipts[index].Name, width).First();
            graphics.DrawString(name, font, XBrushes.Black, x, top + height - 5);
        }
    }

    /// <summary>
    /// 按图片比例居中缩放，不拉伸签名或票据。
    /// </summary>
    /// <param name="bytes">图片字节。</param>
    /// <param name="box">允许区域。</param>
    private void DrawImage(byte[] bytes, XRect box)
    {
        using var stream = new MemoryStream(bytes);
        using var image = XImage.FromStream(stream);
        var scale = Math.Min(box.Width / image.PixelWidth, box.Height / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;
        graphics.DrawImage(image, box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
    }

    /// <summary>
    /// 补齐总页数并保存文档。
    /// </summary>
    /// <returns>完整 PDF。</returns>
    public byte[] Save()
    {
        graphics.Dispose();
        for (var index = 0; index < document.PageCount; index++)
        {
            var current = document.Pages[index];
            using var footer = XGraphics.FromPdfPage(current, XGraphicsPdfPageOptions.Append);
            var text = $"打印人 {input.PrintedBy}　{input.PrintedAt.ToLocalTime():yyyy-MM-dd HH:mm}　　第 {index + 1} 页 共 {document.PageCount} 页";
            footer.DrawString(text, font, XBrushes.Gray, 36, current.Height.Point - 25);
        }
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        graphics.Dispose();
        document.Dispose();
    }
}
