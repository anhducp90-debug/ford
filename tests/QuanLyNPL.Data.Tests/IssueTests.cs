using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

public class IssueTests
{
    // Đơn 10 bộ size 110/56: áo S + quần S, BOM VAI 1/cái → nhu cầu VAI = 20. Nhập 100 VAI.
    static (Fixture f, IssueService iss, long order) Setup(decimal stockIn = 100m)
    {
        var f = new Fixture();
        var b = f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "DH1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) });
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", stockIn) }));
        return (f, new IssueService(f.C, f.Orders, f.Stock), o);
    }
    static decimal Sug(Fixture f, long doc) => Db.D((string)Db.Scalar(f.C, "SELECT qty_suggested FROM stock_doc_line WHERE doc_id=" + doc)!);
    static long Mid(Fixture f, string code) => f.Cat.MaterialId(code);

    [Fact]
    public void Issue_FullFlow_SuggestsDemand_ConfirmsAndBalancesToZero()
    {
        var (f, iss, o) = Setup();
        var d = iss.CreateIssueDraft(o, "X1");
        Assert.Equal(20m, Sug(f, d));
        Assert.Equal(100m, f.Stock.OnHand("VAI"));                   // nháp không đổi tồn
        Assert.Empty(iss.SetActual(d, "VAI", 20m));
        f.Stock.Confirm(d);
        Assert.Equal(80m, f.Stock.OnHand("VAI"));
        Assert.Equal(0m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));
    }

    [Fact]
    public void Confirm_RequiresActualEntered_ButZeroIsAllowedAndKept()
    {
        var (f, iss, o) = Setup();
        var d = iss.CreateIssueDraft(o, "X1");
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(d));  // chưa nhập thực cấp
        iss.SetActual(d, "VAI", 0m);
        f.Stock.Confirm(d);                                          // 0 hợp lệ, không tự thay bằng đề xuất
        Assert.Equal(100m, f.Stock.OnHand("VAI"));
        Assert.Equal(-20m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));
    }

    [Fact]
    public void UnderIssue_CarriesNegative_NextDraftAddsShortfall_SupplementClearsIt()
    {
        var (f, iss, o) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1");
        iss.SetActual(d1, "VAI", 15m); f.Stock.Confirm(d1);
        Assert.Equal(-5m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));
        var d2 = iss.CreateIssueDraft(o, "X2");                      // đơn không đổi → chỉ cần bù 5
        Assert.Equal(5m, Sug(f, d2));
        iss.SetActual(d2, "VAI", 5m); f.Stock.Confirm(d2);
        Assert.Equal(0m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));
        Assert.Equal(80m, f.Stock.OnHand("VAI"));
    }

    [Fact]
    public void OverIssue_CarriesPositive_ReducesFutureDemand()
    {
        var (f, iss, o) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1");
        var w = iss.SetActual(d1, "VAI", 25m);
        Assert.Contains(w, x => x.Contains("vượt đề xuất"));
        f.Stock.Confirm(d1);
        Assert.Equal(5m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));
        // đơn tăng lên 20 bộ → nhu cầu 40; đã tính 20, kỳ mới 20, trừ dư 5 → đề xuất 15
        Db.Exec(f.C, "UPDATE order_line SET sets_qty='20' WHERE order_id=" + o);
        var d2 = iss.CreateIssueDraft(o, "X2");
        Assert.Equal(15m, Sug(f, d2));
    }

    [Fact]
    public void Carry_ReadsLatestClosing_NotSumOfHistory()
    {
        var (f, iss, o) = Setup();
        var d1 = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d1, "VAI", 15m); f.Stock.Confirm(d1);   // -5
        var d2 = iss.CreateIssueDraft(o, "X2"); iss.SetActual(d2, "VAI", 5m); f.Stock.Confirm(d2);    // 0
        Assert.Equal(0m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));   // SUM(closing) sẽ ra -5
    }

    [Fact]
    public void DuplicateDraft_Blocked_AndScopesNotMixed()
    {
        var (f, iss, o) = Setup();
        iss.CreateIssueDraft(o, "X1");
        Assert.Throws<BusinessException>(() => iss.CreateIssueDraft(o, "X2"));
        var (f2, iss2, o2) = Setup();
        var d = iss2.CreateIssueDraft(o2, "Y1", "AO"); iss2.SetActual(d, "VAI", 10m); f2.Stock.Confirm(d);
        Assert.Throws<BusinessException>(() => iss2.CreateIssueDraft(o2, "Y2", IssueService.AllScope));
    }

    [Fact]
    public void VariantScope_OnlyDemandOfThatVariant()
    {
        var (f, iss, o) = Setup();
        Assert.Equal(10m, iss.ScopeDemand(o, "AO")["VAI"]);
        Assert.Equal(10m, iss.ScopeDemand(o, "QUAN")["VAI"]);
    }

    [Fact]
    public void Issue_BlockedWhenStockInsufficient_AndNothingWritten()
    {
        var (f, iss, o) = Setup(stockIn: 10m);
        var d = iss.CreateIssueDraft(o, "X1");
        var w = iss.SetActual(d, "VAI", 20m);
        Assert.Contains(w, x => x.Contains("tồn khả dụng"));
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(d));
        Assert.Equal(10m, f.Stock.OnHand("VAI"));
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM carry_ledger"));
    }

    [Fact]
    public void Incomplete_Data_NoIssueDraft()
    {
        using var f = new Fixture();
        var o = f.Orders.CreateOrder(f.Prod, "D", DateTime.Now, new[] { new OrderLine("110/56", 1) });   // chưa có BOM
        Assert.Throws<BusinessException>(() => new IssueService(f.C, f.Orders, f.Stock).CreateIssueDraft(o, "X"));
    }

    [Fact]
    public void Return_IncreasesStockAndReducesIssued_Once()
    {
        var (f, iss, o) = Setup();
        var d = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d, "VAI", 20m); f.Stock.Confirm(d);   // tồn 80
        var r = iss.CreateReturn(o, "R1", new[] { ("VAI", 6m) });
        f.Stock.Confirm(r);
        Assert.Equal(86m, f.Stock.OnHand("VAI"));                                    // tăng tồn đúng 1 lần
        Assert.Equal(-6m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));            // giảm lượng đã cấp
        Assert.Equal(14m, f.Stock.CarryIssued(o, Mid(f, "VAI"), "ALL"));
        var tooMuch = iss.CreateReturn(o, "R2", new[] { ("VAI", 15m) });
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(tooMuch));
        Assert.Equal(86m, f.Stock.OnHand("VAI"));
    }

    [Fact]
    public void Supplement_LinksToOriginal_AndOnlyAfterConfirm()
    {
        var (f, iss, o) = Setup();
        var d = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d, "VAI", 15m);
        Assert.Throws<BusinessException>(() => iss.CreateSupplement(d, "B1", new[] { ("VAI", 5m) }));   // gốc chưa xác nhận
        f.Stock.Confirm(d);
        var b = iss.CreateSupplement(d, "B1", new[] { ("VAI", 5m) });
        f.Stock.Confirm(b);
        Assert.Equal(80m, f.Stock.OnHand("VAI"));
        Assert.Equal(0m, f.Stock.CarryClosing(o, Mid(f, "VAI"), "ALL"));
        Assert.Equal(d, (long)Db.Scalar(f.C, "SELECT ref_doc_id FROM stock_doc WHERE id=" + b)!);
    }

    [Fact]
    public void Issue_ConsumesOwnReservation()
    {
        var (f, iss, o) = Setup();
        f.Stock.Reserve(o, "VAI", 20m);
        var d = iss.CreateIssueDraft(o, "X1"); iss.SetActual(d, "VAI", 20m); f.Stock.Confirm(d);
        Assert.Equal(0m, f.Stock.ReservedForOthers("VAI", null));
    }

    [Fact]
    public void Migration2_UpgradesExistingV1Database_KeepingData()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        Db.Exec(c, Schema.Migrations[0]); Db.Exec(c, "PRAGMA user_version=1;");
        Db.Exec(c, "INSERT INTO customer(code,name) VALUES('K','K')");
        Db.Migrate(c);
        Assert.Equal((long)Schema.Migrations.Length, Db.Scalar(c, "PRAGMA user_version"));
        Assert.Equal(1L, Db.Scalar(c, "SELECT COUNT(*) FROM customer"));
        Db.Exec(c, "SELECT scope, ref_doc_id FROM stock_doc");
    }
}

public class CountTests
{
    [Fact]
    public void Count_PositiveAndNegativeDiff_CreateAdjustments_AtomicAndAudited()
    {
        using var f = new Fixture();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m), ("CHI", 10m), ("KHOA", 5m) }));
        var cs = new StockCountService(f.C, f.Stock);
        var id = cs.Create("KK1", new[] { ("VAI", 95m), ("CHI", 12m), ("KHOA", 5m) }, "Kiểm kê cuối tháng");
        Assert.Equal(100m, f.Stock.OnHand("VAI"));                // nháp không đổi tồn
        cs.Confirm(id);
        Assert.Equal(95m, f.Stock.OnHand("VAI")); Assert.Equal(12m, f.Stock.OnHand("CHI")); Assert.Equal(5m, f.Stock.OnHand("KHOA"));
        Assert.Equal(2L, Db.Scalar(f.C, "SELECT COUNT(*) FROM audit_log WHERE entity='stock_count' AND action='Chốt kiểm kê'"));
        Assert.Throws<BusinessException>(() => cs.Confirm(id));   // không xác nhận trùng
        Assert.Equal(-5m, Db.D((string)Db.Scalar(f.C, "SELECT diff FROM stock_count_line l JOIN material m ON m.id=l.material_id WHERE m.code='VAI'")!));
    }

    [Fact]
    public void Count_Validation()
    {
        using var f = new Fixture();
        var cs = new StockCountService(f.C, f.Stock);
        Assert.Throws<BusinessException>(() => cs.Create("K", new[] { ("VAI", -1m) }));
        Assert.Throws<BusinessException>(() => cs.Create("K", new[] { ("VAI", 1m), ("VAI", 2m) }));
        Assert.Throws<BusinessException>(() => cs.Confirm(cs.Create("K2", Array.Empty<(string, decimal)>())));
    }
}

public class ExcelTests
{
    static MemoryStream Book(Action<IXLWorksheet> fill)
    {
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("BOM");
        for (var i = 0; i < BomExcel.Headers.Length; i++) ws.Cell(1, i + 1).SetValue(BomExcel.Headers[i]);
        fill(ws);
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms;
    }

    [Fact]
    public void Template_HasThreeSheets_AndSizeList()
    {
        var ms = new MemoryStream();
        BomExcel.WriteTemplate(ms, "KH1", "MH1", "AO", "AO – Tổ cắt", new[] { "S", "M" }); ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        Assert.Equal(new[] { "BOM", "HƯỚNG DẪN", "DANH SÁCH SIZE" }, wb.Worksheets.Select(w => w.Name));
        Assert.Equal("M", wb.Worksheet("DANH SÁCH SIZE").Cell(3, 1).GetString());
    }

    [Fact]
    public void Import_Valid_CreatesNewVersion()
    {
        using var f = new Fixture();
        var bom = f.Bom.CreateBom(f.VAo, BomType.ToCat);
        var ms = Book(ws => {
            ws.Cell(2, 1).SetValue("VAI"); ws.Cell(2, 2).SetValue("*"); ws.Cell(2, 3).SetValue(1.25m); ws.Cell(2, 4).SetValue(3m);
            ws.Cell(3, 1).SetValue("VAI"); ws.Cell(3, 2).SetValue("110/60"); ws.Cell(3, 3).SetValue("1,5");           // dấu phẩy
            ws.Cell(4, 1).SetValue("CHI"); ws.Cell(4, 2).SetValue("*"); ws.Cell(4, 3).SetValue(0.1m); ws.Cell(4, 5).SetValue("AO"); });
        var v = BomExcel.Import(f.Bom, bom, ms, new DateTime(2026, 1, 1));
        Assert.Equal(3L, Db.Scalar(f.C, "SELECT COUNT(*) FROM bom_line WHERE bom_version_id=" + v));
        Assert.Equal("1.5", Db.Scalar(f.C, "SELECT usage FROM bom_line WHERE bom_version_id=" + v + " AND size_code='110/60'"));
    }

    [Fact]
    public void Import_AnyError_WritesNothing_AndReportsExcelRows()
    {
        using var f = new Fixture();
        var bom = f.Bom.CreateBom(f.VAo, BomType.ToCat);
        var ms = Book(ws => {
            ws.Cell(2, 1).SetValue("VAI"); ws.Cell(2, 2).SetValue("*"); ws.Cell(2, 3).SetValue(1m);
            ws.Cell(3, 1).SetValue("KHONGCO"); ws.Cell(3, 2).SetValue("*"); ws.Cell(3, 3).SetValue(1m);   // NPL lạ (lỗi nghiệp vụ)
            ws.Cell(4, 1).SetValue("VAI"); ws.Cell(4, 2).SetValue("XXL"); ws.Cell(4, 3).SetValue(1m);     // size lạ
            ws.Cell(5, 1).SetValue("CHI"); ws.Cell(5, 2).SetValue("*"); ws.Cell(5, 3).SetValue("abc"); }); // không phải số
        var ex = Assert.Throws<BusinessException>(() => BomExcel.Import(f.Bom, bom, ms, DateTime.Now));
        Assert.Contains("Dòng 5", ex.Message);                                                            // lỗi cú pháp
        ms.Position = 0;
        var ms2 = Book(ws => {
            ws.Cell(2, 1).SetValue("VAI"); ws.Cell(2, 2).SetValue("*"); ws.Cell(2, 3).SetValue(1m);
            ws.Cell(3, 1).SetValue("KHONGCO"); ws.Cell(3, 2).SetValue("*"); ws.Cell(3, 3).SetValue(1m);
            ws.Cell(4, 1).SetValue("VAI"); ws.Cell(4, 2).SetValue("XXL"); ws.Cell(4, 3).SetValue(1m); });
        var ex2 = Assert.Throws<BusinessException>(() => BomExcel.Import(f.Bom, bom, ms2, DateTime.Now));
        Assert.Contains("Dòng 3", ex2.Message); Assert.Contains("Dòng 4", ex2.Message);
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM bom_version"));
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM bom_line"));
    }

    [Fact]
    public void Import_RejectsWrongHeader_Duplicates_AndEmpty()
    {
        using var f = new Fixture();
        var bom = f.Bom.CreateBom(f.VAo, BomType.ToCat);
        using (var wb = new XLWorkbook()) { wb.AddWorksheet("BOM").Cell(1, 1).SetValue("sai"); var m = new MemoryStream(); wb.SaveAs(m); m.Position = 0;
            Assert.Throws<BusinessException>(() => BomExcel.Import(f.Bom, bom, m, DateTime.Now)); }
        var dup = Book(ws => { for (var r = 2; r <= 3; r++) { ws.Cell(r, 1).SetValue("VAI"); ws.Cell(r, 2).SetValue("*"); ws.Cell(r, 3).SetValue(1m); } });
        Assert.Contains("trùng", Assert.Throws<BusinessException>(() => BomExcel.Import(f.Bom, bom, dup, DateTime.Now)).Message);
        Assert.Throws<BusinessException>(() => BomExcel.Import(f.Bom, bom, Book(_ => { }), DateTime.Now));
    }
}
