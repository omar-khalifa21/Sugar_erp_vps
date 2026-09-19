using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

/// <summary>
/// Renders Arabic receipts and shift summaries to a bitmap before handing the
/// page to the Windows print spooler. Accounting is always committed before
/// this side effect is attempted; failures remain retryable in side_effect_jobs.
/// </summary>
public sealed class WindowsRasterBranchPrinter : IBranchPrinter
{
    private const int PaperWidthPixels = 576;
    private const int Padding = 12;
    private static readonly string[] VirtualPrinterTerms = ["pdf", "xps", "onenote", "fax", "document writer"];

    public IReadOnlyList<string> GetInstalledPrinterNames()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return GetWindowsPrinterNames();
    }

    [SupportedOSPlatform("windows")]
    public Task PrintTestAsync(string printerName, string siteName, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("الطباعة المباشرة متاحة على Windows فقط.");
        return RunPrintAsync(printerName, () => RenderTest(siteName), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    public Task PrintSaleAsync(
        string printerName,
        string siteName,
        SaleDetailsSnapshot sale,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("الطباعة المباشرة متاحة على Windows فقط.");
        return RunPrintAsync(printerName, () => RenderSale(siteName, sale), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    public Task PrintCustomOrderAsync(
        string printerName,
        string siteName,
        CustomOrderSnapshot order,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("الطباعة المباشرة متاحة على Windows فقط.");
        return RunPrintAsync(printerName, () => RenderCustomOrder(siteName, order), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    public Task PrintShiftReportAsync(
        string printerName,
        ShiftReportData report,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("الطباعة المباشرة متاحة على Windows فقط.");
        return RunPrintAsync(printerName, () => RenderShift(report), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static Task RunPrintAsync(string printerName, Func<Bitmap> render, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("الطباعة المباشرة متاحة على Windows فقط.");
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = render();
            PrintWindowsBitmap(ResolveWindowsPrinter(printerName), bitmap);
        }, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static string ResolveWindowsPrinter(string? printerName)
    {
        if (!string.IsNullOrWhiteSpace(printerName)) return printerName.Trim();
        var settings = new PrinterSettings();
        if (settings.IsDefaultPrinter && settings.IsValid && !string.IsNullOrWhiteSpace(settings.PrinterName)
            && !IsVirtualPrinter(settings.PrinterName))
            return settings.PrinterName;
        var firstPhysical = GetWindowsPrinterNames().FirstOrDefault();
        if (firstPhysical is not null) return firstPhysical;
        throw new InvalidOperationException("اختر طابعة الإيصالات من الإعدادات أولاً.");
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GetWindowsPrinterNames() => PrinterSettings.InstalledPrinters
        .Cast<string>()
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Where(value => !IsVirtualPrinter(value))
        .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static bool IsVirtualPrinter(string value) =>
        VirtualPrinterTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows")]
    private static void PrintWindowsBitmap(string printerName, Bitmap bitmap)
    {
        var bytes = ConvertToEscPos(bitmap);
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            throw new InvalidOperationException("تعذر فتح طابعة الإيصالات.");
        try
        {
            var document = new RawDocumentInfo { DocumentName = "Sugar Receipt", DataType = "RAW" };
            if (StartDocPrinter(printer, 1, ref document) == 0)
                throw new InvalidOperationException("تعذر بدء مهمة الطباعة.");
            try
            {
                if (!StartPagePrinter(printer)) throw new InvalidOperationException("تعذر بدء صفحة الطباعة.");
                try
                {
                    if (!WritePrinter(printer, bytes, bytes.Length, out var written) || written != bytes.Length)
                        throw new InvalidOperationException("تعذر إرسال بيانات الإيصال إلى الطابعة.");
                }
                finally { EndPagePrinter(printer); }
            }
            finally { EndDocPrinter(printer); }
        }
        finally { ClosePrinter(printer); }
    }

    [SupportedOSPlatform("windows")]
    internal static byte[] ConvertToEscPos(Bitmap bitmap)
    {
        var bytesPerRow = (bitmap.Width + 7) / 8;
        var result = new List<byte>(bitmap.Height * bytesPerRow + 32);
        result.AddRange([0x1B, 0x40, 0x1D, 0x76, 0x30, 0x00]);
        result.Add((byte)(bytesPerRow & 0xFF));
        result.Add((byte)((bytesPerRow >> 8) & 0xFF));
        result.Add((byte)(bitmap.Height & 0xFF));
        result.Add((byte)((bitmap.Height >> 8) & 0xFF));

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
            for (var byteColumn = 0; byteColumn < bytesPerRow; byteColumn++)
            {
                byte packed = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = byteColumn * 8 + bit;
                    if (x >= bitmap.Width) continue;
                    var offset = y * data.Stride + x * 3;
                    if (Marshal.ReadByte(data.Scan0, offset + 2) < 128) packed |= (byte)(0x80 >> bit);
                }
                result.Add(packed);
            }
        }
        finally { bitmap.UnlockBits(data); }
        result.AddRange([0x0A, 0x0A, 0x0A, 0x1D, 0x56, 0x41, 0x03]);
        return result.ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap RenderTest(string siteName)
    {
        var lines = new List<ReceiptLine>
        {
            ReceiptLine.Center("Sugar", LineStyle.Brand),
            ReceiptLine.Center(string.IsNullOrWhiteSpace(siteName) ? "فرع نوع ١" : siteName, LineStyle.Heading),
            ReceiptLine.Separator(),
            ReceiptLine.Center("اختبار طابعة ناجح", LineStyle.Heading),
            ReceiptLine.RightAligned($"التاريخ: {DateTime.Now:yyyy-MM-dd HH:mm}"),
            ReceiptLine.RightAligned("إذا كانت العربية واضحة فالطابعة جاهزة."),
            ReceiptLine.Separator(),
            ReceiptLine.Center("شكراً لكم", LineStyle.Heading)
        };
        return Render(lines);
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap RenderSale(string siteName, SaleDetailsSnapshot sale)
    {
        var shiftText = sale.ShiftKind == ShiftKind.Morning ? "صباحية" : "مسائية";
        var lines = new List<ReceiptLine>
        {
            ReceiptLine.Center("Sugar", LineStyle.Brand),
            ReceiptLine.Center(ReceiptSiteName(siteName), LineStyle.Normal),
            ReceiptLine.Center($"رقم الإيصال: \u200E#{sale.ReceiptNumber}\u200E", LineStyle.Heading),
            ReceiptLine.Separator(),
            ReceiptLine.RightAligned($"{sale.OccurredAtUtc.ToLocalTime():dd/MM/yyyy}  الوردية: {shiftText}"),
            ReceiptLine.RightAligned(FulfillmentText(sale.Fulfillment)),
            ReceiptLine.Separator(),
            ReceiptLine.Columns("الصنف", "الكمية", "الإجمالي", true)
        };
        foreach (var item in sale.Lines)
        {
            var effective = item.OriginalQuantityScaled - item.RefundedQuantityScaled;
            lines.Add(ReceiptLine.Columns(
                item.ItemName,
                Quantity(effective, item.QuantityScale, item.Unit),
                Number(item.EffectiveLineTotalMinor)));
        }
        lines.Add(ReceiptLine.Separator());
        lines.Add(ReceiptLine.Columns("المجموع", string.Empty, Money(sale.SubtotalMinor)));
        if (sale.DiscountMinor > 0)
            lines.Add(ReceiptLine.Columns("الخصم", string.Empty, $"- {Money(sale.DiscountMinor)}"));
        if (sale.RefundedTotalMinor > 0)
            lines.Add(ReceiptLine.Columns("المسترجع", string.Empty, $"- {Money(sale.RefundedTotalMinor)}"));
        lines.Add(ReceiptLine.Columns("الإجمالي", string.Empty, Money(Math.Max(0, sale.EffectiveTotalMinor - sale.TipMinor)), true));
        lines.Add(ReceiptLine.Columns("طريقة الدفع", string.Empty, PaymentText(sale.PaymentMethod)));
        lines.Add(ReceiptLine.Separator());
        lines.Add(ReceiptLine.Center("شكراً لزيارتكم", LineStyle.Heading));
        return Render(lines);
    }

    [SupportedOSPlatform("windows")]
    internal static Bitmap RenderSalePreview(string siteName, SaleDetailsSnapshot sale) => RenderSale(siteName, sale);

    [SupportedOSPlatform("windows")]
    private static Bitmap RenderCustomOrder(string siteName, CustomOrderSnapshot order)
    {
        var lines = new List<ReceiptLine>
        {
            ReceiptLine.Center("Sugar", LineStyle.Brand),
            ReceiptLine.Center(ReceiptSiteName(siteName), LineStyle.Normal),
            ReceiptLine.Center($"فاتورة: \u200E#{order.OrderNumber}\u200E", LineStyle.Heading),
            ReceiptLine.Separator(),
            ReceiptLine.RightAligned($"إلى: {order.CustomerName}"),
            ReceiptLine.RightAligned($"تاريخ الفاتورة: {order.UpdatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"),
            ReceiptLine.RightAligned($"موعد التسليم: {order.DueAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"),
            ReceiptLine.Separator(),
            ReceiptLine.Columns("الصنف", "الكمية", "الإجمالي", true)
        };
        foreach (var item in order.Lines ?? [])
            lines.Add(ReceiptLine.Columns(
                item.ItemName,
                Quantity(item.QuantityScaled, item.QuantityScale, item.Unit),
                Number(item.LineTotalMinor)));
        if (!string.IsNullOrWhiteSpace(order.Description))
        {
            lines.Add(ReceiptLine.Separator());
            lines.Add(ReceiptLine.RightAligned($"ملاحظات: {order.Description}"));
        }
        var previousBalanceMinor = Math.Max(0, order.CustomerBalanceMinor - order.RemainingMinor);
        lines.AddRange([
            ReceiptLine.Separator(),
            ReceiptLine.Columns("إجمالي الفاتورة", string.Empty, Money(order.TotalMinor), true),
            ReceiptLine.Columns("المدفوع لهذه الفاتورة", string.Empty, Money(order.PaidMinor)),
            ReceiptLine.Columns("متبقي هذه الفاتورة", string.Empty, Money(order.RemainingMinor)),
            ReceiptLine.Columns("متبقي من فواتير سابقة", string.Empty, Money(previousBalanceMinor)),
            ReceiptLine.Columns("إجمالي المستحق اليوم", string.Empty, Money(order.CustomerBalanceMinor), true),
            ReceiptLine.Columns("الحالة", string.Empty, CustomOrderStatusText(order.Status)),
            ReceiptLine.Separator(),
            ReceiptLine.Center("شكراً لزيارتكم", LineStyle.Heading)
        ]);
        return Render(lines);
    }

    [SupportedOSPlatform("windows")]
    internal static Bitmap RenderCustomOrderPreview(string siteName, CustomOrderSnapshot order) => RenderCustomOrder(siteName, order);

    private static string ReceiptSiteName(string siteName) => siteName
        .Replace("— عرض محلي", string.Empty, StringComparison.Ordinal)
        .Replace("عرض محلي", string.Empty, StringComparison.Ordinal)
        .Trim().TrimEnd('—', '-', '–').Trim();

    [SupportedOSPlatform("windows")]
    private static Bitmap RenderShift(ShiftReportData report)
    {
        var shiftText = report.Kind == ShiftKind.Morning ? "صباحية" : "مسائية";
        var lines = new List<ReceiptLine>
        {
            ReceiptLine.Center("Sugar", LineStyle.Brand),
            ReceiptLine.Center(report.SiteName, LineStyle.Heading),
            ReceiptLine.Center($"ملخص وردية {shiftText}", LineStyle.Heading),
            ReceiptLine.RightAligned($"التاريخ: {report.BusinessDate}"),
            ReceiptLine.RightAligned($"من {report.OpenedAtUtc.ToLocalTime():HH:mm} إلى {report.ClosedAtUtc.ToLocalTime():HH:mm}"),
            ReceiptLine.Separator(),
            ReceiptLine.Columns("نقدي", string.Empty, Money(report.CashSalesMinor - report.CashRefundsMinor)),
            ReceiptLine.Columns("فيزا", string.Empty, Money(report.VisaSalesMinor - report.VisaRefundsMinor)),
            ReceiptLine.Columns("النقدية المتوقعة", string.Empty, Money(report.ExpectedCashMinor)),
            ReceiptLine.Columns("النقدية الفعلية", string.Empty, Money(report.ActualCashMinor)),
            ReceiptLine.Columns("فرق النقدية", string.Empty, Money(report.DifferenceMinor), true),
            ReceiptLine.RightAligned($"عدد الإيصالات: {report.ReceiptCount}"),
            ReceiptLine.Separator(),
            ReceiptLine.StockColumns("الصنف", "افتتاحية", "وارد", "فعلي", true)
        };
        foreach (var item in report.Items)
        {
            lines.Add(ReceiptLine.StockColumns(
                item.Name,
                QuantityNumber(item.OpeningScaled, item.QuantityScale),
                QuantityNumber(item.IncomingScaled, item.QuantityScale),
                QuantityNumber(item.ActualScaled, item.QuantityScale)));
            if (item.ActualScaled != item.ExpectedScaled)
                lines.Add(ReceiptLine.RightAligned(
                    $"فرق {item.Name}: متوقع {Quantity(item.ExpectedScaled, item.QuantityScale, item.Unit)}، فعلي {Quantity(item.ActualScaled, item.QuantityScale, item.Unit)}"));
        }
        if (report.PendingHoldCount > 0)
            lines.Add(ReceiptLine.RightAligned($"طلبات وارد تحت المراجعة: {report.PendingHoldCount}"));
        if (report.PendingReturnCount > 0)
            lines.Add(ReceiptLine.RightAligned($"مرتجعات مطبخ معلقة: {report.PendingReturnCount}"));
        lines.Add(ReceiptLine.Separator());
        lines.Add(ReceiptLine.Center("التقرير الكامل محفوظ بصيغة Excel", LineStyle.Normal));
        return Render(lines);
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap Render(IReadOnlyList<ReceiptLine> lines)
    {
        var height = Padding * 2 + lines.Sum(value => value.Style switch
        {
            LineStyle.Brand => 72,
            LineStyle.Heading => 42,
            LineStyle.Separator => 24,
            _ => 34
        }) + 100;
        var bitmap = new Bitmap(PaperWidthPixels, Math.Max(260, height), PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brandFont = BrandFont();
        using var headingFont = Font(16, FontStyle.Bold);
        using var normalFont = Font(13, FontStyle.Regular);
        using var boldFont = Font(13, FontStyle.Bold);
        using var tableFont = Font(10, FontStyle.Regular);
        using var tableBoldFont = Font(10, FontStyle.Bold);
        using var rtl = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.DirectionRightToLeft,
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        using var center = new StringFormat(rtl) { Alignment = StringAlignment.Center };
        using var left = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        float y = Padding;
        foreach (var line in lines)
        {
            var lineHeight = line.Style switch
            {
                LineStyle.Brand => 72,
                LineStyle.Heading => 42,
                LineStyle.Separator => 24,
                _ => 34
            };
            if (line.Style == LineStyle.Separator)
            {
                graphics.FillRectangle(Brushes.Black, Padding, y + lineHeight / 2f, PaperWidthPixels - Padding * 2, 2);
                y += lineHeight;
                continue;
            }
            var font = line.Style switch
            {
                LineStyle.Brand => brandFont,
                LineStyle.Heading => headingFont,
                _ when line.Bold => boldFont,
                _ => normalFont
            };
            if (line.Fourth is not null)
            {
                // The stock table is RTL: item name gets the widest cell; numeric cells stay compact.
                const float numericWidth = 84;
                const float gap = 5;
                var nameWidth = PaperWidthPixels - Padding * 2 - numericWidth * 3 - gap * 3;
                var tableRowFont = line.Bold ? tableBoldFont : tableFont;
                graphics.DrawString(line.Right, tableRowFont, Brushes.Black,
                    new RectangleF(Padding + numericWidth * 3 + gap * 3, y, nameWidth, lineHeight), rtl);
                graphics.DrawString(line.Middle ?? string.Empty, tableRowFont, Brushes.Black,
                    new RectangleF(Padding + numericWidth * 2 + gap * 2, y, numericWidth, lineHeight), center);
                graphics.DrawString(line.Left ?? string.Empty, tableRowFont, Brushes.Black,
                    new RectangleF(Padding + numericWidth + gap, y, numericWidth, lineHeight), center);
                graphics.DrawString(line.Fourth, tableRowFont, Brushes.Black,
                    new RectangleF(Padding, y, numericWidth, lineHeight), center);
            }
            else if (line.Middle is null && line.Left is null)
            {
                graphics.DrawString(line.Right, font, Brushes.Black,
                    new RectangleF(Padding, y, PaperWidthPixels - Padding * 2, lineHeight),
                    line.Centered ? center : rtl);
            }
            else
            {
                const float valueWidth = 138;
                const float quantityWidth = 132;
                var itemWidth = PaperWidthPixels - Padding * 2 - valueWidth - quantityWidth;
                graphics.DrawString(line.Right, font, Brushes.Black,
                    new RectangleF(Padding + valueWidth + quantityWidth, y, itemWidth, lineHeight), rtl);
                graphics.DrawString(line.Middle ?? string.Empty, font, Brushes.Black,
                    new RectangleF(Padding + valueWidth, y, quantityWidth, lineHeight), center);
                graphics.DrawString(line.Left ?? string.Empty, font, Brushes.Black,
                    new RectangleF(Padding, y, valueWidth, lineHeight), left);
            }
            y += lineHeight;
        }
        graphics.FillRectangle(Brushes.White, 0, y + 1, PaperWidthPixels, bitmap.Height - (int)y - 1);
        return Binarize(bitmap);
    }

    [SupportedOSPlatform("windows")]
    private static Font Font(float size, FontStyle style)
    {
        foreach (var family in new[] { "Segoe UI", "Tahoma", "Arial" })
        {
            try { return new Font(family, size, style, GraphicsUnit.Point); }
            catch (ArgumentException) { }
        }
        return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
    }

    [SupportedOSPlatform("windows")]
    private static Font BrandFont()
    {
        string[] preferred = ["Cooper Black", "Gabriola", "Brush Script MT", "Georgia"];
        using var installed = new InstalledFontCollection();
        foreach (var family in preferred)
            if (installed.Families.Any(value => value.Name.Equals(family, StringComparison.OrdinalIgnoreCase)))
                return new Font(family, 34f, FontStyle.Bold, GraphicsUnit.Point);
        return Font(34f, FontStyle.Bold);
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap Binarize(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, source.Width, source.Height);
        var sourceData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var resultData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var sourceOffset = y * sourceData.Stride + x * 3;
                var red = System.Runtime.InteropServices.Marshal.ReadByte(sourceData.Scan0, sourceOffset + 2);
                var color = red < 200 ? Color.Black : Color.White;
                var resultOffset = y * resultData.Stride + x * 3;
                System.Runtime.InteropServices.Marshal.WriteByte(resultData.Scan0, resultOffset, color.B);
                System.Runtime.InteropServices.Marshal.WriteByte(resultData.Scan0, resultOffset + 1, color.G);
                System.Runtime.InteropServices.Marshal.WriteByte(resultData.Scan0, resultOffset + 2, color.R);
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
            source.Dispose();
        }
        return result;
    }

    private static string Quantity(long scaled, int scale, string unit) =>
        $"{((decimal)scaled / scale).ToString(scale == 1 ? "0" : "0.###", CultureInfo.InvariantCulture)} {unit}";

    private static string QuantityNumber(long scaled, int scale) =>
        ((decimal)scaled / scale).ToString(scale == 1 ? "0" : "0.###", CultureInfo.InvariantCulture);

    private static string Money(long minor) => $"{minor / 100m:0.00} ج.م";
    private static string Number(long minor) => $"{minor / 100m:0.00}";
    private static string PaymentText(PaymentMethod method) => method == PaymentMethod.Cash ? "نقدي" : "فيزا";
    private static string FulfillmentText(FulfillmentKind kind) => kind == FulfillmentKind.Table ? "طاولة" : "تيك أواي";
    private static string CustomOrderStatusText(CustomOrderStatus status) => status switch
    {
        CustomOrderStatus.New => "جديد",
        CustomOrderStatus.Confirmed => "مؤكد",
        CustomOrderStatus.Ready => "جاهز",
        CustomOrderStatus.Delivered => "تم التسليم",
        _ => "ملغي"
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RawDocumentInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref RawDocumentInfo info);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr printer, byte[] bytes, int count, out int written);

    private enum LineStyle { Normal, Heading, Brand, Separator }

    private sealed record ReceiptLine(
        string Right,
        string? Middle,
        string? Left,
        bool Centered,
        bool Bold,
        LineStyle Style,
        string? Fourth = null)
    {
        public static ReceiptLine RightAligned(string text) => new(text, null, null, false, false, LineStyle.Normal);
        public static ReceiptLine Center(string text, LineStyle style) => new(text, null, null, true, style != LineStyle.Normal, style);
        public static ReceiptLine Separator() => new(string.Empty, null, null, false, false, LineStyle.Separator);
        public static ReceiptLine Columns(string right, string middle, string left, bool bold = false) =>
            new(right, middle, left, false, bold, LineStyle.Normal);
        public static ReceiptLine StockColumns(string name, string opening, string incoming, string actual, bool bold = false) =>
            new(name, opening, incoming, false, bold, LineStyle.Normal, actual);
    }
}
