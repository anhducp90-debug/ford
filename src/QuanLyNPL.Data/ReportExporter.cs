using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuanLyNPL.Data;

public static class ReportExporter
{
    public static void ToExcel(ReportTable t, Stream output)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(Sheet(t.Title));
        ws.Cell(1, 1).SetValue(t.Title).Style.Font.SetBold().Font.SetFontSize(14);
        var r = 2;
        foreach (var f in t.Filters) ws.Cell(r++, 1).SetValue(f);
        r++;
        for (var i = 0; i < t.Headers.Count; i++)
            ws.Cell(r, i + 1).SetValue(t.Headers[i]).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray);
        foreach (var row in t.Rows)
        {
            r++;
            for (var i = 0; i < row.Length; i++)
            {
                var cell = ws.Cell(r, i + 1);
                switch (row[i])
                {
                    case decimal d: cell.SetValue(d); cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case null: break;
                    default: cell.SetValue(row[i]!.ToString()); break;
                }
            }
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(output);
    }

    public static void ToPdf(ReportTable t, Stream output)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;   // máy không có font vẫn xuất được (thiếu dấu thay vì lỗi)
        Document.Create(doc => doc.Page(p =>
        {
            p.Size(PageSizes.A4.Landscape()); p.Margin(25); p.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));   // Arial hỗ trợ tiếng Việt trên Windows
            p.Header().Column(col =>
            {
                col.Item().Text(t.Title).Bold().FontSize(15);
                foreach (var f in t.Filters) col.Item().Text(f);
            });
            p.Content().PaddingTop(8).Table(tb =>
            {
                tb.ColumnsDefinition(cd => { foreach (var _ in t.Headers) cd.RelativeColumn(); });
                tb.Header(h => { foreach (var x in t.Headers) h.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(x).Bold(); });
                foreach (var row in t.Rows)
                    foreach (var v in row)
                        tb.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3)
                          .Text(v is decimal d ? d.ToString("#,##0.00") : v?.ToString() ?? "");
            });
            p.Footer().AlignRight().Text(x => { x.Span("Trang "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
        })).GeneratePdf(output);
    }

    static string Sheet(string title) { var s = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()); return s.Length > 31 ? s[..31] : s; }
}
