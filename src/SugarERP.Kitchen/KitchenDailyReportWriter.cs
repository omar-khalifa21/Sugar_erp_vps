using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;

namespace SugarERP.Kitchen;

public sealed record KitchenReportResult(string Path, string Sha256);

public sealed class KitchenDailyReportWriter(KitchenStore store)
{
    public async Task<KitchenReportResult> WriteLatestCountAsync(string directory)
    {
        await using var db = store.Open();
        var latest = await db.IngredientMovements.AsNoTracking().Where(x => x.Kind == "COUNT").OrderByDescending(x => x.OccurredAtUtc).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("لا يوجد جرد مكتمل لإنشاء التقرير.");
        var countId = latest.ShipmentId;
        var counts = await db.IngredientMovements.AsNoTracking().Where(x => x.Kind == "COUNT" && x.ShipmentId == countId).ToListAsync();
        var itemIds = counts.Select(x => x.IngredientItemId).ToArray();
        var balances = await db.Ingredients.AsNoTracking().Where(x => itemIds.Contains(x.ItemId)).ToDictionaryAsync(x => x.ItemId);
        var start = latest.OccurredAtUtc.Date;
        var movements = await db.IngredientMovements.AsNoTracking().Where(x => x.OccurredAtUtc >= start && x.OccurredAtUtc <= latest.OccurredAtUtc).OrderBy(x => x.OccurredAtUtc).ToListAsync();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"kitchen-{latest.OccurredAtUtc:yyyy-MM-dd}-{countId.ToString("N")[..8]}.xlsx");
        if (!File.Exists(path)) Build(path, latest.OccurredAtUtc, countId, counts, balances, movements);
        await using var stream = File.OpenRead(path); return new(path, Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant());
    }

    private static void Build(string path, DateTimeOffset closedAt, Guid countId, IReadOnlyList<KitchenIngredientMovement> counts,
        IReadOnlyDictionary<Guid, KitchenIngredientBalance> balances, IReadOnlyList<KitchenIngredientMovement> movements)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart(); workbook.Workbook = new Workbook(); var sheets = workbook.Workbook.AppendChild(new Sheets());
        AddSheet(workbook, sheets, "الجرد", [
            RowOf("تقرير جرد المطبخ"), RowOf("وقت الإقفال", closedAt.ToString("O")), RowOf("معرف الجرد", countId.ToString("D")),
            RowOf("الخامة", "الرصيد الفعلي", "الوحدة", "تعديل الجرد"),
            .. counts.OrderBy(x => balances[x.IngredientItemId].Name).Select(x => RowOf(balances[x.IngredientItemId].Name, ((decimal)balances[x.IngredientItemId].QuantityScaled / balances[x.IngredientItemId].QuantityScale).ToString("0.###"), balances[x.IngredientItemId].Unit, ((decimal)x.DeltaScaled / balances[x.IngredientItemId].QuantityScale).ToString("0.###"))) ]);
        AddSheet(workbook, sheets, "الحركات", [RowOf("الوقت", "النوع", "الخامة", "التغير", "السبب"), .. movements.Select(x => { var item = balances.GetValueOrDefault(x.IngredientItemId); return RowOf(x.OccurredAtUtc.ToString("O"), x.Kind, item?.Name ?? x.IngredientItemId.ToString(), item is null ? x.DeltaScaled.ToString() : ((decimal)x.DeltaScaled / item.QuantityScale).ToString("0.###"), x.Reason); })]);
        workbook.Workbook.Save();
    }
    private static void AddSheet(WorkbookPart workbook, Sheets sheets, string name, IEnumerable<Row> rows) { var part = workbook.AddNewPart<WorksheetPart>(); var data = new SheetData(); foreach (var row in rows) data.Append(row); part.Worksheet = new Worksheet(data); sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)sheets.Count() + 1, Name = name }); }
    private static Row RowOf(params string[] values) => new(values.Select(value => new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? "")) }));
}
