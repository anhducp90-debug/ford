using Microsoft.Data.Sqlite;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuanLyNPL.Data;

/// <summary>In chứng từ kho (phiếu nhập, phiếu xuất/cấp phát, trả, xuất bù, chuyển) ra PDF khổ A4 có chỗ ký.</summary>
public static class VoucherPdf
{
    public static string Title(DocRow d) => d.Kind == TransferService.Kind ? (d.Type == "OUT" ? "PHIẾU XUẤT KHO (CHUYỂN NPL)" : "PHIẾU NHẬP KHO (NHẬN CHUYỂN NPL)") : d.Type switch
    {
        "IN" => "PHIẾU NHẬP KHO", "RETURN" => "PHIẾU NHẬP KHO (TRẢ NPL)",
        "OUT" => d.RefDocId is not null ? (d.SubKind == "BU_HAO_HUT" ? "PHIẾU XUẤT KHO (BÙ HAO HỤT)" : "PHIẾU XUẤT KHO (BÙ THIẾU)") : d.Scope is not null ? "PHIẾU XUẤT KHO (CẤP PHÁT NPL)" : "PHIẾU XUẤT KHO",
        "ADJUST_UP" => "PHIẾU ĐIỀU CHỈNH TĂNG", "ADJUST_DOWN" => "PHIẾU ĐIỀU CHỈNH GIẢM", _ => "CHỨNG TỪ KHO"
    };

    static string N(decimal d) => d.ToString("#,##0.####", new System.Globalization.NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." });

    public static void Generate(SqliteConnection c, long docId, Stream output)
    {
        var q = new Queries(c);
        var d = q.Docs().FirstOrDefault(x => x.Id == docId) ?? throw new BusinessException("Phiếu không tồn tại.");
        var lines = q.DocLines(docId);
        var company = new CompanyInfo(c);
        var cust = Db.Query(c, "SELECT k.code||' – '||k.name FROM stock_doc s JOIN customer k ON k.id=s.customer_id WHERE s.id=$i", r => r.GetString(0), null, ("$i", docId)).FirstOrDefault() ?? "";
        var product = d.OrderId is long oid ? Db.Query(c, "SELECT p.code||' – '||p.name FROM \"order\" o JOIN product p ON p.id=o.product_id WHERE o.id=$o", r => r.GetString(0), null, ("$o", oid)).FirstOrDefault() : null;
        var refNo = d.RefDocId is long rid ? q.Docs().FirstOrDefault(x => x.Id == rid)?.DocNo : null;
        var isIssue = d.Type == "OUT" && d.Scope is not null && d.RefDocId is null;
        var qtyText = isIssue ? IssueService.Describe(c, docId) : "";
        var date = d.ConfirmedAt ?? d.CreatedAt;
        var isIn = d.Type is "IN" or "RETURN" or "ADJUST_UP";
        string Scope(string? s) => "BOM " + IssueScopes.Label(s);

        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        Document.Create(doc => doc.Page(p =>
        {
            p.Size(PageSizes.A4); p.Margin(28); p.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));
            p.Header().Row(r =>
            {
                r.RelativeItem().Column(col =>
                {
                    col.Item().Text(company.Name.Length > 0 ? company.Name : "(Tên đơn vị – khai báo ở Quản trị)").Bold();
                    if (company.Address.Length > 0) col.Item().Text(company.Address);
                });
                r.ConstantItem(170).AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text($"Số: {d.DocNo}").Bold();
                    col.Item().AlignRight().Text($"Ngày {date:dd} tháng {date:MM} năm {date:yyyy}");
                    if (d.Status == "DRAFT") col.Item().AlignRight().Text("BẢN NHÁP – CHƯA XÁC NHẬN").FontColor(Colors.Red.Medium).Bold();
                });
            });
            p.Content().PaddingTop(14).Column(col =>
            {
                col.Item().AlignCenter().Text(Title(d)).FontSize(16).Bold();
                col.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(110); cd.RelativeColumn(); cd.ConstantColumn(90); cd.RelativeColumn(); });
                    void Pair(string k, string? v) { t.Cell().PaddingVertical(2).Text(k).FontColor(Colors.Grey.Darken2); t.Cell().PaddingVertical(2).Text(v ?? "").SemiBold(); }
                    Pair("Khách hàng:", cust); Pair("Đơn hàng:", d.OrderNo.Length > 0 ? d.OrderNo + (product is null ? "" : $" ({product})") : "");
                    Pair("Nơi xuất:", d.FromPlace); Pair("Nơi nhận:", d.ToPlace);
                    if (isIssue || d.Scope is not null) { Pair("Loại BOM:", Scope(d.Scope)); Pair("", ""); }
                    if (refNo is not null) { Pair("Phiếu gốc:", refNo); Pair("", ""); }
                    Pair("Ghi chú:", d.Note); Pair("", "");
                });
                if (qtyText.Length > 0)
                {
                    col.Item().PaddingTop(8).Text("Số lượng sản phẩm cấp NPL:").SemiBold();
                    col.Item().Text(qtyText);
                }
                col.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(32); cd.ConstantColumn(95); cd.RelativeColumn(); cd.ConstantColumn(45);
                        if (isIssue) cd.ConstantColumn(75);
                        cd.ConstantColumn(85);
                    });
                    void H(string x) => t.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4).AlignCenter().Text(x).Bold();
                    H("STT"); H("Mã NPL"); H("Tên nguyên phụ liệu"); H("ĐVT"); if (isIssue) H("Đề xuất"); H(isIssue ? "Thực cấp" : "Số lượng");
                    var i = 0;
                    foreach (var l in lines)
                    {
                        i++;
                        t.Cell().Border(0.5f).Padding(4).AlignCenter().Text(i.ToString());
                        t.Cell().Border(0.5f).Padding(4).Text(l.Material);
                        t.Cell().Border(0.5f).Padding(4).Text(l.MaterialName);
                        t.Cell().Border(0.5f).Padding(4).AlignCenter().Text(l.Unit);
                        if (isIssue) t.Cell().Border(0.5f).Padding(4).AlignRight().Text(l.Suggested is decimal s ? N(s) : "");
                        t.Cell().Border(0.5f).Padding(4).AlignRight().Text(l.Entered ? N(l.Qty) : "");
                    }
                });
                col.Item().PaddingTop(4).Text($"Tổng số dòng: {lines.Count}").Italic().FontSize(9);
                col.Item().PaddingTop(26).Row(r =>
                {
                    foreach (var who in new[] { "Người lập phiếu", isIn ? "Người giao hàng" : "Thủ kho", isIn ? "Thủ kho" : "Người nhận hàng", "Phụ trách bộ phận" })
                        r.RelativeItem().Column(cc =>
                        {
                            cc.Item().AlignCenter().Text(who).Bold();
                            cc.Item().AlignCenter().Text("(Ký, ghi rõ họ tên)").Italic().FontSize(8.5f);
                        });
                });
            });
            p.Footer().AlignRight().Text(x => { x.Span("Trang ").FontSize(8); x.CurrentPageNumber().FontSize(8); x.Span(" / ").FontSize(8); x.TotalPages().FontSize(8); });
        })).GeneratePdf(output);
    }
}
