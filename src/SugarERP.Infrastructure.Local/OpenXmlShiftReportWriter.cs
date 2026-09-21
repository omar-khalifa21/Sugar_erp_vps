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
        var businessDate = ParseBusinessDate(report.BusinessDate);
        var fileName = $"{SanitizeFileNamePart(report.SiteName)}_{businessDate:yyyy-MM-dd}_{kind}.xlsx";
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
        var businessDate = ParseBusinessDate(report.BusinessDate);
        var netCashMinor = report.CashSalesMinor - report.CashRefundsMinor;
        var netVisaMinor = report.VisaSalesMinor - report.VisaRefundsMinor;

        sheetData.Append(Row(1));
        sheetData.Append(Row(2, 28, Text($"{report.SiteName} — {(report.Kind == ShiftKind.Morning ? "Morning" : "Evening")} Shift", 1)));
        const uint itemHeaderRow = 4;
        sheetData.Append(Row(itemHeaderRow, 25,
            Text("Item", 2),
            Text("Opening", 2),
            Text("Wared", 2),
            Text("Sold", 2),
            Text("Mortaga3", 2),
            Text("Leftover", 2)));

        uint rowIndex = itemHeaderRow + 1;
        foreach (var item in report.Items)
        {
            sheetData.Append(Row(rowIndex++,
                Text(item.Name, 3),
                Quantity(item.OpeningScaled, item.QuantityScale),
                Quantity(item.IncomingScaled, item.QuantityScale),
                Quantity(checked(item.SoldScaled + item.CafeIssuedScaled), item.QuantityScale),
                Quantity(item.KitchenReturnScaled, item.QuantityScale),
                Quantity(item.ActualScaled, item.QuantityScale)));
        }

        var itemLastRow = Math.Max(itemHeaderRow, rowIndex - 1);
        rowIndex++;
        sheetData.Append(Row(rowIndex, 24,
            Text("Date", 8), Date(businessDate.ToDateTime(TimeOnly.MinValue), 9),
            Text("Total Cash", 8), Money(netCashMinor, 10),
            Text("Total Visa", 8), Money(netVisaMinor, 10)));

        var views = ReportViews();
        views.GetFirstChild<SheetView>()!.Pane = new Pane
        {
            VerticalSplit = itemHeaderRow,
            TopLeftCell = $"A{itemHeaderRow + 1}",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen
        };

        return new Worksheet(
            views,
            new SheetFormatProperties { DefaultRowHeight = 20 },
            new Columns(
                new Column { Min = 1, Max = 1, Width = 32, CustomWidth = true },
                new Column { Min = 2, Max = 6, Width = 15, CustomWidth = true }),
            sheetData,
            new AutoFilter { Reference = $"A{itemHeaderRow}:F{itemLastRow}" },
            new PrintOptions { HorizontalCentered = true },
            new PageMargins { Left = 0.25, Right = 0.25, Top = 0.4, Bottom = 0.4, Header = 0.2, Footer = 0.2 },
            new PageSetup { Orientation = OrientationValues.Landscape, FitToWidth = 1, FitToHeight = 0, PaperSize = 9 });
    }

    private static Worksheet BuildMetadataSheet(ShiftReportData report, string fingerprint)
    {
        var data = new SheetData();
        data.Append(Row(1, Text("fingerprint"), Text(fingerprint)));
        data.Append(Row(2, Text("shift_id"), Text(report.ShiftId.ToString("D"))));
        data.Append(Row(3, Text("report_version"), Number(report.ReportVersion)));
        return new Worksheet(data);
    }

    private static SheetViews ReportViews() => new(
        new SheetView { WorkbookViewId = 0, RightToLeft = false, ShowGridLines = false });

    private static Stylesheet BuildStyles()
    {
        var numberingFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "#,##0.0" },
            new NumberingFormat { NumberFormatId = 166, FormatCode = "#,##0.00" },
            new NumberingFormat { NumberFormatId = 167, FormatCode = "#,##0.000" },
            new NumberingFormat { NumberFormatId = 168, FormatCode = "yyyy-mm-dd" },
            new NumberingFormat { NumberFormatId = 169, FormatCode = "#,##0.00;[Red]-#,##0.00" }) { Count = 6 };
        var fonts = new Fonts(
            new Font(new FontSize { Val = 10 }, new Color { Rgb = "FF30242A" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 14 }, new Color { Rgb = "FF6D2944" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 10 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Arial" }),
            new Font(new Bold(), new FontSize { Val = 10 }, new Color { Rgb = "FF6D2944" }, new FontName { Val = "Arial" })) { Count = 4 };
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            SolidFill("FFD94F83"),
            SolidFill("FFFFF5F8")) { Count = 4 };
        var borders = new Borders(
            new Border(),
            new Border(
                new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE8CBD6" } },
                new DiagonalBorder())) { Count = 2 };
        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
            new CellFormat { FontId = 1, FillId = 0, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 2, FillId = 2, BorderId = 1, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 1, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 1, NumberFormatId = 164, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, ReadingOrder = 1U } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 1, NumberFormatId = 165, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, ReadingOrder = 1U } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 1, NumberFormatId = 166, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, ReadingOrder = 1U } },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 1, NumberFormatId = 167, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, ReadingOrder = 1U } },
            new CellFormat { FontId = 3, FillId = 3, BorderId = 0, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center } },
            new CellFormat { FontId = 0, FillId = 3, BorderId = 0, NumberFormatId = 168, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, ReadingOrder = 1U } },
            new CellFormat { FontId = 3, FillId = 3, BorderId = 0, NumberFormatId = 169, ApplyNumberFormat = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right, Vertical = VerticalAlignmentValues.Center, ReadingOrder = 1U } }) { Count = 11 };
        return new Stylesheet(numberingFormats, fonts, fills, borders, cellFormats);
    }

    private static DateOnly ParseBusinessDate(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new BusinessRuleException("INVALID_REPORT_DATE", "تاريخ تقرير الوردية غير صالح.");
        return date;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Trim().Select(character =>
            invalid.Contains(character) || char.IsControl(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray());
        while (sanitized.Contains("__", StringComparison.Ordinal)) sanitized = sanitized.Replace("__", "_", StringComparison.Ordinal);
        sanitized = sanitized.Trim('_', '.');
        if (sanitized.Length > 80) sanitized = sanitized[..80].TrimEnd('_', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "branch" : sanitized;
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

    private static Cell Quantity(long scaled, int scale) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(((decimal)scaled / Math.Max(1, scale)).ToString(CultureInfo.InvariantCulture)),
        StyleIndex = scale <= 1 ? 4U : scale <= 10 ? 5U : scale <= 100 ? 6U : 7U
    };

    private static Cell Money(long minor, uint style = 10) => new()
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
