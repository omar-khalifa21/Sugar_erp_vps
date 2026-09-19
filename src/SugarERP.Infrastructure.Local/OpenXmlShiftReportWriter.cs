using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SugarERP.Application;
using SugarERP.Domain;

namespace SugarERP.Infrastructure.Local;

public sealed class OpenXmlShiftReportWriter : IShiftReportWriter
{
    private const string MetadataSheetName = "_metadata";

    public async Task<ShiftReportWriteResult> WriteAsync(
        ShiftReportData report,
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        if (report.ShiftId == Guid.Empty || report.ReportVersion < 1)
            throw new BusinessRuleException("INVALID_REPORT", "بيانات تقرير الوردية غير صالحة.");
        if (string.IsNullOrWhiteSpace(exportDirectory))
            throw new BusinessRuleException("EXPORT_DIRECTORY_REQUIRED", "اختر مجلد حفظ تقارير Excel.");

        var directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(exportDirectory.Trim()));
        Directory.CreateDirectory(directory);
        var kind = report.Kind == ShiftKind.Morning ? "morning" : "evening";
        var fileName = $"shift-{report.BusinessDate}-{kind}-{report.ShiftId.ToString("N")[..8]}-v{report.ReportVersion}.xlsx";
        var targetPath = Path.Combine(directory, fileName);
        var fingerprint = BuildFingerprint(report);
        if (File.Exists(targetPath))
            return await ValidateExistingAsync(targetPath, fingerprint, cancellationToken);

        var tempPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await Task.Run(() => BuildWorkbook(tempPath, report, fingerprint), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(tempPath, targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                File.Delete(tempPath);
                return await ValidateExistingAsync(targetPath, fingerprint, cancellationToken);
            }
            var bytes = new FileInfo(targetPath).Length;
            var sha256 = await HashFileAsync(targetPath, cancellationToken);
            return new ShiftReportWriteResult(targetPath, sha256, bytes, false);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private static void BuildWorkbook(string path, ShiftReportData report, string fingerprint)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = BuildStyles();
        stylesPart.Stylesheet.Save();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        var reportPart = workbookPart.AddNewPart<WorksheetPart>();
        reportPart.Worksheet = BuildReportSheet(report);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(reportPart),
            SheetId = 1,
            Name = "تقرير الوردية"
        });

        var metadataPart = workbookPart.AddNewPart<WorksheetPart>();
        metadataPart.Worksheet = BuildMetadataSheet(report, fingerprint);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(metadataPart),
            SheetId = 2,
            Name = MetadataSheetName,
            State = SheetStateValues.VeryHidden
        });

        workbookPart.Workbook.CalculationProperties = new CalculationProperties
        {
            CalculationMode = CalculateModeValues.Auto,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true
        };
        workbookPart.Workbook.Save();
    }

    private static Worksheet BuildReportSheet(ShiftReportData report)
    {
        var sheetData = new SheetData();
        var hasWaste = report.Items.Any(item => item.WasteScaled != 0);
        var hasAdjustments = report.Items.Any(item => item.AdjustmentScaled != 0);
        var varianceItems = report.Items.Where(item => item.DifferenceScaled != 0).ToArray();
        var totalRefundsMinor = report.CashRefundsMinor + report.VisaRefundsMinor;
        var netSalesMinor = report.CashSalesMinor + report.VisaSalesMinor - totalRefundsMinor;

        sheetData.Append(Row(1));
        sheetData.Append(Row(2, 30, Text("تقرير الوردية", 1)));
        sheetData.Append(Row(3, 34,
            Text("الفرع", 4), Text(report.SiteName, 9),
            Text("تاريخ العمل", 4), Text(report.BusinessDate, 9),
            Text("الوردية", 4), Text(report.Kind == ShiftKind.Morning ? "صباحية" : "مسائية", 9)));
        sheetData.Append(Row(4,
            Text("وقت الفتح", 4), Date(report.OpenedAtUtc.LocalDateTime, 6),
            Text("وقت الإغلاق", 4), Date(report.ClosedAtUtc.LocalDateTime, 6),
            Text("الإيصالات", 4), Number(report.ReceiptCount, 9)));
        sheetData.Append(Row(5));
        sheetData.Append(Row(6, 24, Text("المبيعات والصندوق", 2)));
        sheetData.Append(Row(7, Text("المبيعات", 3), Text("ج.م", 3), Text("الصندوق", 3), Text("ج.م", 3)));
        sheetData.Append(Row(8, Text("نقدي", 4), Money(report.CashSalesMinor), Text("العهدة الافتتاحية", 4), Money(report.OpeningCashMinor)));
        sheetData.Append(Row(9, Text("فيزا", 4), Money(report.VisaSalesMinor), Text("النقدية المتوقعة", 4), Money(report.ExpectedCashMinor)));
        sheetData.Append(Row(10, Text("المرتجعات", 4), Money(totalRefundsMinor), Text("النقدية الفعلية", 4), Money(report.ActualCashMinor)));
        sheetData.Append(Row(11, Text("صافي المبيعات", 10), Money(netSalesMinor, 11), Text("فرق النقدية", 10), Money(report.DifferenceMinor, report.DifferenceMinor == 0 ? 11U : 7U)));
        sheetData.Append(Row(13, 24, Text("حركة الأصناف", 2)));

        var itemHeaders = new List<Cell>
        {
            Text("الصنف", 3), Text("الوحدة", 3), Text("افتتاحية", 3), Text("وارد", 3),
            Text("مباع", 3), Text("طلبات عملاء", 3), Text("مرتجع عميل", 3), Text("مرتجع للمطبخ", 3)
        };
        if (hasWaste) itemHeaders.Add(Text("هالك", 3));
        if (hasAdjustments) itemHeaders.Add(Text("تسوية", 3));
        itemHeaders.Add(Text("المتبقي", 3));
        const uint itemHeaderRow = 14;
        sheetData.Append(Row(itemHeaderRow, 25, itemHeaders.ToArray()));

        uint rowIndex = itemHeaderRow + 1;
        foreach (var item in report.Items)
        {
            var cells = new List<Cell>
            {
                Text(item.Name, 14), Text(item.Unit, 14),
                Quantity(item.OpeningScaled, item.QuantityScale, 15),
                Quantity(item.IncomingScaled, item.QuantityScale, 15),
                Quantity(item.SoldScaled, item.QuantityScale, 15),
                Quantity(item.CafeIssuedScaled, item.QuantityScale, 15),
                Quantity(item.CustomerRestockScaled, item.QuantityScale, 15),
                Quantity(item.KitchenReturnScaled, item.QuantityScale, 15)
            };
            if (hasWaste) cells.Add(Quantity(item.WasteScaled, item.QuantityScale, 15));
            if (hasAdjustments) cells.Add(Quantity(item.AdjustmentScaled, item.QuantityScale, 15));
            cells.Add(Quantity(item.ExpectedScaled, item.QuantityScale, 16));
            sheetData.Append(Row(rowIndex++, cells.ToArray()));
        }

        var itemLastRow = Math.Max(itemHeaderRow, rowIndex - 1);
        if (varianceItems.Length > 0)
        {
            rowIndex++;
            sheetData.Append(Row(rowIndex++, 24, Text("فروق تحتاج مراجعة", 2)));
            sheetData.Append(Row(rowIndex++, Text("الصنف", 3), Text("المتبقي المحسوب", 3), Text("العد المسجل", 3), Text("الفرق", 3)));
            foreach (var item in varianceItems)
            {
                sheetData.Append(Row(rowIndex++,
                    Text(item.Name, 14),
                    Quantity(item.ExpectedScaled, item.QuantityScale, 15),
                    Quantity(item.ActualScaled, item.QuantityScale, 15),
                    Quantity(item.DifferenceScaled, item.QuantityScale, 13)));
            }
        }

        if (report.PendingHoldCount > 0 || report.PendingReturnCount > 0)
        {
            rowIndex++;
            sheetData.Append(Row(rowIndex++, 24, Text("متابعة مطلوبة", 2)));
            if (report.PendingHoldCount > 0)
                sheetData.Append(Row(rowIndex++, Text("طلبات وارد تحت المراجعة", 4), Number(report.PendingHoldCount, 12)));
            if (report.PendingReturnCount > 0)
                sheetData.Append(Row(rowIndex++, Text("مرتجعات مطبخ بانتظار الإقرار", 4), Number(report.PendingReturnCount, 12)));
        }

        rowIndex++;
        sheetData.Append(Row(rowIndex, Text("نسخة الإغلاق الأصلية — أي تصحيح لاحق يظهر كسجل جديد ولا يغيّر هذا التقرير.", 8)));

        var views = RightToLeftViews();
        views.GetFirstChild<SheetView>()!.Pane = new Pane
        {
            VerticalSplit = itemHeaderRow,
            TopLeftCell = $"A{itemHeaderRow + 1}",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen
        };

        var columnCount = itemHeaders.Count;
        var lastColumn = ColumnName(columnCount);
        return new Worksheet(
            views,
            new SheetFormatProperties { DefaultRowHeight = 20 },
            new Columns(
                new Column { Min = 1, Max = 1, Width = 30, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 14, CustomWidth = true },
                new Column { Min = 3, Max = (uint)columnCount, Width = 15, CustomWidth = true }),
            sheetData,
            new AutoFilter { Reference = $"A{itemHeaderRow}:{lastColumn}{itemLastRow}" },
            new PrintOptions { HorizontalCentered = true },
            new PageMargins { Left = 0.25, Right = 0.25, Top = 0.4, Bottom = 0.4, Header = 0.2, Footer = 0.2 },
            new PageSetup { Orientation = OrientationValues.Landscape, FitToWidth = 1, FitToHeight = 0 });
    }

    private static Worksheet BuildMetadataSheet(ShiftReportData report, string fingerprint)
    {
        var data = new SheetData();
        data.Append(Row(1, Text("fingerprint"), Text(fingerprint)));
        data.Append(Row(2, Text("shift_id"), Text(report.ShiftId.ToString("D"))));
        data.Append(Row(3, Text("report_version"), Number(report.ReportVersion)));
        return new Worksheet(data);
    }

    private static SheetViews RightToLeftViews() => new(
        new SheetView { WorkbookViewId = 0, RightToLeft = true, ShowGridLines = false });

    private static Stylesheet BuildStyles()
    {
        var numberingFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00;[Red]-#,##0.00" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "dd/mm/yyyy hh:mm" }) { Count = 2 };
        var fonts = new Fonts(
            new Font(new FontSize { Val = 10 }, new Color { Rgb = "FF30242A" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 18 }, new Color { Rgb = "FF6D2944" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 10 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 10 }, new Color { Rgb = "FF6D2944" }, new FontName { Val = "Arial" }),
            new Font(new Italic(), new FontSize { Val = 9 }, new Color { Rgb = "FF806070" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 10 }, new Color { Rgb = "FFC9364E" }, new FontName { Val = "Arial" })) { Count = 6 };
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            SolidFill("FFD94F83"),
            SolidFill("FFFFF5F8"),
            SolidFill("FFF8C8D8"),
            SolidFill("FFFFE4E8")) { Count = 6 };
        var borders = new Borders(
            new Border(),
            new Border(
                new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
                new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
                new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
                new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
                new DiagonalBorder())) { Count = 2 };
        var plainBorder = new Border(
            new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
            new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
            new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
            new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
            new DiagonalBorder());
        borders.Append(plainBorder);
        borders.Count = 3;
        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
            new CellFormat { FontId = 1, FillId = 0, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 2, FillId = 2, BorderId = 1, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true } },
            new CellFormat { FontId = 2, FillId = 2, BorderId = 1, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true } },
            new CellFormat { FontId = 3, FillId = 0, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 164, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 165, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 5, FillId = 5, BorderId = 0, NumberFormatId = 164, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 4, FillId = 0, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, WrapText = false } },
            new CellFormat { FontId = 0, FillId = 3, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, WrapText = true } },
            new CellFormat { FontId = 3, FillId = 4, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 3, FillId = 4, BorderId = 0, NumberFormatId = 164, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 5, FillId = 5, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 5, FillId = 5, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 2, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 2, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 3, FillId = 3, BorderId = 2, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center } }) { Count = 17 };
        return new Stylesheet(numberingFormats, fonts, fills, borders, cellFormats);
    }

    private static Fill SolidFill(string rgb) => new(new PatternFill(
        new ForegroundColor { Rgb = rgb },
        new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid });

    private static Row Row(uint index, params Cell[] cells) => Row(index, 20, cells);

    private static Row Row(uint index, double height, params Cell[] cells)
    {
        var row = new Row { RowIndex = index, Height = height, CustomHeight = true };
        for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            cells[cellIndex].CellReference = $"{ColumnName(cellIndex + 1)}{index}";
        row.Append(cells);
        return row;
    }

    private static string ColumnName(int columnNumber)
    {
        var name = string.Empty;
        while (columnNumber > 0)
        {
            columnNumber--;
            name = (char)('A' + columnNumber % 26) + name;
            columnNumber /= 26;
        }
        return name;
    }

    private static Cell Text(string value, uint style = 0) => new()
    {
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }),
        StyleIndex = style
    };

    private static Cell Number(long value, uint style = 0) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    private static Cell Quantity(long scaled, int scale, uint style = 3) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(((decimal)scaled / Math.Max(1, scale)).ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    private static Cell Money(long minor, uint style = 5) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(((decimal)minor / 100m).ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    private static Cell Date(DateTime value, uint style) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    private static string BuildFingerprint(ShiftReportData report)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static async Task<ShiftReportWriteResult> ValidateExistingAsync(
        string path,
        string expectedFingerprint,
        CancellationToken cancellationToken)
    {
        var actualFingerprint = await Task.Run(() => ReadFingerprint(path), cancellationToken);
        if (!string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new BusinessRuleException("IMMUTABLE_REPORT_CONFLICT", "يوجد ملف مختلف باسم تقرير هذه الوردية. لم يتم استبداله؛ اختر مجلداً آخر أو راجع الملف الموجود.");
        var hash = await HashFileAsync(path, cancellationToken);
        return new ShiftReportWriteResult(path, hash, new FileInfo(path).Length, true);
    }

    private static string? ReadFingerprint(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook is not { } workbook) return null;
        var sheet = workbook.GetFirstChild<Sheets>()?.Elements<Sheet>().SingleOrDefault(value => value.Name?.Value == MetadataSheetName);
        if (sheet?.Id?.Value is null) return null;
        var part = workbookPart.GetPartById(sheet.Id.Value) as WorksheetPart;
        if (part?.Worksheet is not { } worksheet) return null;
        var row = worksheet.GetFirstChild<SheetData>()?.Elements<Row>().FirstOrDefault();
        return row?.Elements<Cell>().Skip(1).FirstOrDefault()?.InlineString?.Text?.Text;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(bytes);
    }
}
