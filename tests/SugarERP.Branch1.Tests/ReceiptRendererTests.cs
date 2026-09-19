using System.Drawing.Imaging;
using System.Runtime.Versioning;
using SugarERP.Application;
using SugarERP.Domain;
using SugarERP.Infrastructure.Local;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class ReceiptRendererTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ConfiguredPhysicalPrinter_AcceptsTestReceipt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var printerName = Environment.GetEnvironmentVariable("SUGAR_TEST_PRINTER");
        if (string.IsNullOrWhiteSpace(printerName)) return;

        var printer = new WindowsRasterBranchPrinter();
        Assert.Contains(printerName, printer.GetInstalledPrinterNames());
        await printer.PrintTestAsync(printerName, "Sugar ERP - Branch Type 1");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ReceiptPreviews_RenderAsThermalBitmaps()
    {
        if (!OperatingSystem.IsWindows()) return;

        var now = new DateTimeOffset(2026, 9, 11, 10, 30, 0, TimeSpan.Zero);
        var sale = new SaleDetailsSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), ShiftKind.Morning, "260911-0042", now,
            PaymentMethod.Cash, FulfillmentKind.Takeaway, 32500, 2500, 30000, 0, 30000, 0,
            [new SaleLineDetails(
                Guid.NewGuid(), Guid.NewGuid(), "تورتة شوكولاتة", "CAKE-01", "قطعة", 1,
                1, 0, 1, 32500, 2500, 30000)]);
        var customOrder = new CustomOrderSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "SP-20260911-0001", "أحمد محمد", "01000000000",
            "تورتة عيد ميلاد شوكولاتة، مقاس كبير، كتابة: كل سنة وأنت طيب",
            now.AddDays(2), 85000, 30000, 55000, 120000, CustomOrderStatus.Confirmed, 2, now, now, false,
            [new CustomOrderLineSnapshot(Guid.NewGuid(), "تورتة شوكولاتة", "قطعة", 1, 2, 42500, 85000)]);

        using var saleBitmap = WindowsRasterBranchPrinter.RenderSalePreview("فرع الزمالك", sale);
        using var customOrderBitmap = WindowsRasterBranchPrinter.RenderCustomOrderPreview("فرع الزمالك", customOrder);

        Assert.Equal(576, saleBitmap.Width);
        Assert.Equal(576, customOrderBitmap.Width);
        Assert.True(saleBitmap.Height >= 500);
        Assert.True(customOrderBitmap.Height >= 500);
        var raw = WindowsRasterBranchPrinter.ConvertToEscPos(customOrderBitmap);
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1D, 0x76, 0x30, 0x00 }, raw[..6]);
        Assert.True(raw.Length > customOrderBitmap.Height * 70);
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x41, 0x03 }, raw[^4..]);

        var previewDirectory = Environment.GetEnvironmentVariable("SUGAR_RECEIPT_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(previewDirectory)) return;

        Directory.CreateDirectory(previewDirectory);
        saleBitmap.Save(Path.Combine(previewDirectory, "sale-receipt.png"), ImageFormat.Png);
        customOrderBitmap.Save(Path.Combine(previewDirectory, "custom-order-receipt.png"), ImageFormat.Png);
    }
}
