using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

public class AdjustmentTests
{
    // 10 bộ 110/56 → VAI 20 (áo 10 + quần 10, định mức 1)
    static (Fixture f, IssueService iss, OrderAdjustmentService adj, long o, long bom) Setup()
    {
        var f = new Fixture();
        var b = f.Bom.CreateBom(f.Prod, "B");
        f.Bom.AddVersion(b, new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Cust, f.Prod, "DH1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) });
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        return (f, new IssueService(f.C, f.Orders, f.Stock), new OrderAdjustmentService(f.C, f.Orders, f.Stock), o, b);
    }

    [Fact]
    public void Preview_ShowsDelta_WithoutSaving()
    {
        var (f, _, adj, o, _) = Setup();
        var p = adj.Preview(o, new[] { new OrderLine("110/56", 15) });
        var d = p.Deltas.Single();
        Assert.Equal((20m, 30m, 10m), (d.Before, d.After, d.Diff));
        Assert.True(p.CanApply);
        Assert.Equal(20m, f.Orders.CalculateDemand(o).TotalByMaterial["VAI"]);   // chưa lưu
    }

    [Fact]
    public void Apply_RequiresReason_SavesHistoryAndNewSnapshot()
    {
        var (f, _, adj, o, _) = Setup();
        Assert.Throws<BusinessException>(() => adj.Apply(o, new[] { new OrderLine("110/56", 15) }, " "));
        adj.Apply(o, new[] { new OrderLine("110/56", 15) }, "Khách tăng đơn");
        Assert.Equal(30m, f.Orders.CalculateDemand(o).TotalByMaterial["VAI"]);
        var h = adj.History(o);
        Assert.Equal(2, h.Count); Assert.Equal("Lập đơn", h[0].Reason); Assert.Equal("Khách tăng đơn", h[1].Reason);
        Assert.Equal(1L, Db.Scalar(f.C, "SELECT COUNT(*) FROM audit_log WHERE entity='order' AND reason='Khách tăng đơn'"));
    }

    [Fact]
    public void IncreaseAfterIssue_NextDraftSuggestsOnlyTheIncrease()
    {
        var (f, iss, adj, o, _) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d1, "VAI", 20m); f.Stock.Confirm(d1);
        adj.Apply(o, new[] { new OrderLine("110/56", 15) }, "Tăng đơn");
        var d2 = iss.CreateIssueDraft(o, "X2");
        Assert.Equal("10", Db.Scalar(f.C, "SELECT qty_suggested FROM stock_doc_line WHERE doc_id=" + d2));
        Assert.Equal(80m, f.Stock.OnHand("VAI"));                                // phiếu đã xác nhận không bị hồi tố
    }

    [Fact]
    public void ReduceBelowIssued_WarnsAndNextDraftSuggestsZero()
    {
        var (f, iss, adj, o, _) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d1, "VAI", 20m); f.Stock.Confirm(d1);
        var p = adj.Preview(o, new[] { new OrderLine("110/56", 5) });   // nhu cầu mới 10 < đã cấp 20
        Assert.Contains(p.Warnings, w => w.Contains("vượt nhu cầu mới"));
        adj.Apply(o, new[] { new OrderLine("110/56", 5) }, "Giảm đơn");
        var d2 = iss.CreateIssueDraft(o, "X2");
        Assert.Equal("0", Db.Scalar(f.C, "SELECT qty_suggested FROM stock_doc_line WHERE doc_id=" + d2));
        iss.SetActual(d2, "VAI", 0m); f.Stock.Confirm(d2);                           // kho không cấp thêm
        Assert.Equal(10m, f.Stock.CarryClosing(o, f.Cat.MaterialId("VAI"), "ALL"));   // dư 10, chờ xử lý trả/giữ/chuyển đơn
    }

    [Fact]
    public void Blocked_WhenDraftIssueExists_UntilDraftDeleted()
    {
        var (f, iss, adj, o, _) = Setup();
        var d = iss.CreateIssueDraft(o, "X1");
        Assert.False(adj.Preview(o, new[] { new OrderLine("110/56", 15) }).CanApply);
        Assert.Throws<BusinessException>(() => adj.Apply(o, new[] { new OrderLine("110/56", 15) }, "x"));
        f.Stock.DeleteDraft(d, "Làm lại");
        adj.Apply(o, new[] { new OrderLine("110/56", 15) }, "Tăng đơn");
        Assert.Equal(1L, Db.Scalar(f.C, "SELECT COUNT(*) FROM audit_log WHERE entity='stock_doc_draft'"));
    }

    [Fact]
    public void ConfirmedDoc_CannotBeDeleted()
    {
        var (f, _, _, _, _) = Setup();
        var d = f.Stock.CreateDraft("IN", "N2", new[] { ("VAI", 1m) }); f.Stock.Confirm(d);
        Assert.Throws<BusinessException>(() => f.Stock.DeleteDraft(d, "x"));
    }

    [Fact]
    public void Validation_BadSize_Negative_Duplicate()
    {
        var (_, _, adj, o, _) = Setup();
        Assert.Throws<BusinessException>(() => adj.Apply(o, new[] { new OrderLine("NOPE", 1) }, "x"));
        Assert.Throws<BusinessException>(() => adj.Apply(o, new[] { new OrderLine("110/56", -1) }, "x"));
        Assert.Throws<BusinessException>(() => adj.Apply(o, new[] { new OrderLine("110/56", 1), new OrderLine("110/56", 2) }, "x"));
    }

    [Fact]
    public void RebindBom_AppliesNewVersionToRemainingDemand_WithPreview()
    {
        var (f, iss, adj, o, bom) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d1, "VAI", 20m); f.Stock.Confirm(d1);
        f.Bom.AddVersion(bom, new DateTime(2026, 3, 1), new[] { new BomLineInput("VAI", "*", 1.5m) });
        var same = new[] { new OrderLine("110/56", 10) };
        Assert.Equal(0m, adj.Preview(o, same, rebindBom: false).Deltas.Single().Diff);
        var p = adj.Preview(o, same, rebindBom: true, asOf: new DateTime(2026, 4, 1));
        Assert.Equal(10m, p.Deltas.Single().Diff);                                // 30 − 20
        adj.Apply(o, same, "Đổi BOM", rebindBom: true, asOf: new DateTime(2026, 4, 1));
        var d2 = iss.CreateIssueDraft(o, "X2");
        Assert.Equal("10", Db.Scalar(f.C, "SELECT qty_suggested FROM stock_doc_line WHERE doc_id=" + d2));
        Assert.Contains("v2", adj.History(o).Last().BomSummary);
    }

    [Fact]
    public void ReportRowsCanDifferFromHistory_ButDoNotChangeConfirmedDocs()
    {
        var (f, iss, adj, o, _) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d1, "VAI", 20m); f.Stock.Confirm(d1);
        adj.Apply(o, new[] { new OrderLine("110/56", 1) }, "Giảm mạnh");
        Assert.Equal("20", Db.Scalar(f.C, "SELECT qty FROM stock_doc_line WHERE doc_id=" + d1));
    }
}

public class ReportTests
{
    static (Fixture f, long o) Setup()
    {
        var f = new Fixture();
        var b = f.Bom.CreateBom(f.Prod, "B");
        f.Bom.AddVersion(b, new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m), new BomLineInput("CHI", "*", 0.5m, 10m, "AO") });
        var o = f.Orders.CreateOrder(f.Cust, f.Prod, "DH1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) });
        return (f, o);
    }
    static ReportService Rep(Fixture f) => new(f.C, f.Orders, f.Stock);

    [Fact]
    public void CurrentStock_HasOnHandReservedAvailable()
    {
        var (f, o) = Setup();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        f.Stock.Reserve(o, "VAI", 30m);
        var row = Rep(f).CurrentStock(new ReportFilter(MaterialCode: "VAI")).Rows.Single();
        Assert.Equal(new object?[] { "VAI", "Vải chính", "KH1", "m", 100m, 30m, 70m }, row);
    }

    [Fact]
    public void InOutStock_Reconciles_ByPeriod()
    {
        var (f, o) = Setup();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        f.Stock.Confirm(f.Stock.CreateDraft("OUT", "X1", new[] { ("VAI", 30m) }));
        f.Stock.Confirm(f.Stock.CreateDraft("RETURN", "R1", new[] { ("VAI", 5m) }));
        var cs = new StockCountService(f.C, f.Stock);
        cs.Confirm(cs.Create("K1", new[] { ("VAI", 70m) }));            // sổ 75 → đếm 70 → −5
        var today = DateTime.Today;
        var r = Rep(f).InOutStock(new ReportFilter(today, today, MaterialCode: "VAI")).Rows.Single();
        Assert.Equal(new object?[] { "VAI", "Vải chính", "KH1", "m", 0m, 100m, 30m, 5m, -5m, 70m }, r);
        var later = Rep(f).InOutStock(new ReportFilter(today.AddDays(1), today.AddDays(2), MaterialCode: "VAI")).Rows.Single();
        Assert.Equal(70m, later[4]);   // đầu kỳ = tồn trước kỳ
        Assert.Equal(70m, later[9]);
    }

    [Fact]
    public void OrderDemand_SummaryAndDrilldown_MatchEachOther()
    {
        var (f, o) = Setup();
        var rep = Rep(f);
        var sum = rep.OrderDemand(o).Rows.ToDictionary(r => (string)r[0]!, r => (decimal)r[2]!);
        var det = rep.OrderDemandDetail(o).Rows;
        Assert.Equal(20m, sum["VAI"]); Assert.Equal(5.5m, sum["CHI"]);        // 10 × 0.5 × 1.1
        Assert.Equal(sum["VAI"], det.Where(r => (string)r[0]! == "VAI").Sum(r => (decimal)r[4]!));
        Assert.All(det, r => Assert.Equal("B", r[1]));
    }

    [Fact]
    public void Availability_AndShortageOnlyFilter()
    {
        var (f, o) = Setup();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m), ("CHI", 2m) }));
        var rep = Rep(f);
        var all = rep.OrderAvailability(o);
        Assert.Equal(2, all.Rows.Count);
        Assert.Contains("THIẾU NPL", all.Filters[0]);
        var short1 = rep.OrderAvailability(o, onlyShortage: true).Rows.Single();
        Assert.Equal("CHI", short1[0]); Assert.Equal(3.5m, short1[6]); Assert.Equal("THIẾU", short1[7]);
    }

    [Fact]
    public void Fulfillment_ListsStatusPerOrder_IncompleteWhenNoBom()
    {
        var (f, o) = Setup();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m), ("CHI", 100m) }));
        // đơn thứ hai không có BOM hiệu lực (ngày lập đơn trước phiên bản BOM)
        f.Orders.CreateOrder(f.Cust, f.Prod, "DH0", new DateTime(2025, 1, 1), new[] { new OrderLine("110/56", 1) });
        var rep = Rep(f).Fulfillment();
        Assert.Equal("ĐỦ NPL", rep.Rows.Single(r => (string)r[0]! == "DH1")[4]);
        Assert.Equal("CHƯA ĐỦ DỮ LIỆU", rep.Rows.Single(r => (string)r[0]! == "DH0")[4]);
        Assert.Single(Rep(f).Fulfillment(new ReportFilter(OnlyShortage: true)).Rows.Where(_ => false).DefaultIfEmpty());
    }

    [Fact]
    public void TransactionHistory_FiltersByMaterialOrderAndType()
    {
        var (f, o) = Setup();
        var iss = new IssueService(f.C, f.Orders, f.Stock);
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m), ("CHI", 100m) }));
        var d = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d, "VAI", 15m); iss.SetActual(d, "CHI", 5.5m); f.Stock.Confirm(d);
        var bu = iss.CreateSupplement(d, "B1", new[] { ("VAI", 5m) }); f.Stock.Confirm(bu);
        var rep = Rep(f);
        Assert.Equal(3, rep.TransactionHistory(new ReportFilter(MaterialCode: "VAI")).Rows.Count);
        Assert.Equal(2, rep.TransactionHistory(new ReportFilter(OrderId: o, MaterialCode: "VAI")).Rows.Count);
        Assert.Contains(rep.TransactionHistory(new ReportFilter(MaterialCode: "VAI")).Rows, r => (string)r[2]! == "Xuất bù");
        Assert.Empty(rep.TransactionHistory(new ReportFilter(From: DateTime.Today.AddDays(1))).Rows);
    }

    [Fact]
    public void ConfigHistory_ListsVersionsWithReason()
    {
        var (f, o) = Setup();
        new OrderAdjustmentService(f.C, f.Orders, f.Stock).Apply(o, new[] { new OrderLine("110/56", 12) }, "Tăng đơn");
        var rows = Rep(f).ConfigHistory(o).Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Tăng đơn", rows[1][2]);
    }

    [Fact]
    public void Export_Excel_And_Pdf_ProduceValidFiles()
    {
        var (f, o) = Setup();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        var t = Rep(f).CurrentStock();
        var xl = new MemoryStream(); ReportExporter.ToExcel(t, xl);
        Assert.True(xl.Length > 1000);
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(xl.ToArray())))
            Assert.Equal(100m, (decimal)wb.Worksheets.First().Cell(6, 5).GetDouble());   // dòng 1 tiêu đề, 3 header, dữ liệu từ dòng 4 theo mã: CHI, KHOA, VAI
        var pdf = new MemoryStream(); ReportExporter.ToPdf(t, pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.ToArray(), 0, 4));
    }
}
