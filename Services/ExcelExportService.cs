using ClosedXML.Excel;
using EArchive.Models;

namespace EArchive.Services;

public sealed class ExcelExportService
{
    public Task ExportAsync(IEnumerable<ArchiveRecord> records, string outputPath, CancellationToken ct = default)
    {
        // ClosedXML يبني المصنف في الذاكرة؛ للتقارير الضخمة جداً استخدم CSV أو مكتبة تدعم الكتابة المتدفقة.
        ct.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("الأرشيف");
        var headers = new[] { "رقم", "العنوان", "المصدر", "الجهة", "ملاحظات", "من تاريخ", "إلى تاريخ",
                              "اسم الملف الأصلي", "المسار النسبي", "الامتداد", "الحجم بالبايت", "تاريخ التسجيل" };
        for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var row = 2;
        foreach (var r in records)
        {
            ct.ThrowIfCancellationRequested();
            sheet.Cell(row,1).Value = r.Id;
            sheet.Cell(row,2).Value = r.Title;
            sheet.Cell(row,3).Value = r.Source;
            sheet.Cell(row,4).Value = r.Destination;
            sheet.Cell(row,5).Value = r.Notes;
            if (r.DateFrom.HasValue) sheet.Cell(row,6).Value = r.DateFrom.Value;
            if (r.DateTo.HasValue) sheet.Cell(row,7).Value = r.DateTo.Value;
            sheet.Cell(row,8).Value = r.OriginalFileName;
            sheet.Cell(row,9).Value = r.StoredRelativePath;
            sheet.Cell(row,10).Value = r.Extension;
            sheet.Cell(row,11).Value = r.FileSize;
            sheet.Cell(row,12).Value = r.CreatedAt;
            row++;
        }
        var used = sheet.RangeUsed();
        if (used is not null) { used.SetAutoFilter(); used.Style.Alignment.WrapText = true; }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(8, 50);
        workbook.SaveAs(outputPath);
        return Task.CompletedTask;
    }
}