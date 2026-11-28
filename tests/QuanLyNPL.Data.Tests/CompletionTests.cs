using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

public class PerSetNormTests
{
    [Fact]
    public void Norm_PerSize_SumsVariantsTimesQtyPerSet_PlusSetBoms_WithWaste()
    {
        using var f = new Fixture();
        var v3 = f.Cat.AddVariant(f.Prod, "AO2", "Áo phụ", "Áo", 2);           // 2 cái trong 1 bộ
        var d = new DateTime(2026, 1, 1);
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.02m), new BomLineInput("KHOA", "*", 5m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VQuan, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.02m) });
        f.Bom.AddVersion(f.Bom.CreateBom(v3, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.01m), new BomLineInput("CHI", "120/60", 0.03m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 1.3m, 3m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VQuan, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 1m) });
        f.Bom.AddVersion(f.Bom.CreateBom(v3, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 0.5m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), d, new[] { new BomLineInput("KHOA", "*", 1m) });
        var (sum, det, issues) = new ReportService(f.C, f.Orders, f.Stock).PerSetNorm(f.Prod, new DateTime(2026, 2, 1));
        Assert.Empty(issues);
        Assert.Equal(new[] { "Mã NPL", "Tên", "ĐVT", "Size 110/56", "Size 110/60", "Size 120/60" }, sum.Headers);
        var chi = sum.Rows.Single(r => (string)r[0]! == "CHI");
        Assert.Equal(0.06m, chi[3]);                     // 0.02 + 0.02 + 2×0.01
        Assert.Equal(0.10m, chi[5]);                     // 0.02 + 0.02 + 2×0.03 (size 120/60 ghi đè)
        Assert.Equal(6m, sum.Rows.Single(r => (string)r[0]! == "KHOA")[3]);   // áo 5 + đóng gói 1/bộ
        Assert.Equal(3.339m, sum.Rows.Single(r => (string)r[0]! == "VAI")[3]); // 1.3×1.03 + 1 + 2×0.5
        Assert.Contains(det.Rows, r => (string)r[1]! == "KHOA" && (string)r[4]! == "Cả bộ");
    }

    [Fact]
    public void Norm_ReportsMissingData()
    {
        using var f = new Fixture();
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.ToCat), new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var (sum, _, issues) = new ReportService(f.C, f.Orders, f.Stock).PerSetNorm(f.Prod);
        Assert.Contains(issues, i => i.Contains("QUAN"));
        Assert.Contains(sum.Filters, x => x.StartsWith("CHƯA ĐỦ DỮ LIỆU"));
    }
}

public class BackupTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "qlnpl_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Backup_ThenRestore_RollsBackData_AndKeepsSafetyCopy()
    {
        var dir = Tmp(); Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "data.db");
        using (var c = Db.Open(dbPath))
        {
            var cat = new CatalogRepository(c);
            cat.AddCustomer("K1", "K1");
            var bak = Path.Combine(dir, "b.db");
            new BackupService(c).Backup(bak);
            cat.AddCustomer("K2", "K2");
            Assert.Equal(2L, Db.Scalar(c, "SELECT COUNT(*) FROM customer"));
            var safety = new BackupService(c).Restore(bak, Path.Combine(dir, "safety"));
            Assert.Equal(1L, Db.Scalar(c, "SELECT COUNT(*) FROM customer"));
            Assert.True(File.Exists(safety));
        }
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Restore_RejectsForeignFiles()
    {
        var dir = Tmp(); Directory.CreateDirectory(dir);
        var txt = Path.Combine(dir, "x.db"); File.WriteAllText(txt, "không phải db");
        Assert.Throws<BusinessException>(() => BackupService.Validate(txt));
        var other = Path.Combine(dir, "o.db");
        using (var o = new SqliteConnection("Data Source=" + other + ";Pooling=False")) { o.Open(); Db.Exec(o, "CREATE TABLE t(a)"); }
        Assert.Throws<BusinessException>(() => BackupService.Validate(other));
        Directory.Delete(dir, true);
    }
}

public class OperationTests
{
    [Fact]
    public void Deactivate_BlocksNewUse_KeepsData()
    {
        using var f = new Fixture();
        f.Cat.SetMaterialActive("VAI", false);
        Assert.Throws<BusinessException>(() => f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.ToCat), DateTime.Now, new[] { new BomLineInput("VAI", "*", 1m) }));
        f.Cat.SetProductActive(f.Prod, false);
        Assert.Throws<BusinessException>(() => f.Orders.CreateOrder(f.Prod, "O", DateTime.Now, new[] { new OrderLine("110/56", 1) }));
        f.Cat.SetProductActive(f.Prod, true);
        f.Cat.SetCustomerActive(f.Cust, false);
        Assert.Throws<BusinessException>(() => f.Orders.CreateOrder(f.Prod, "O", DateTime.Now, new[] { new OrderLine("110/56", 1) }));
        Assert.Single(new Queries(f.C).Customers());
        Assert.Empty(new Queries(f.C).Customers(activeOnly: true));
    }

    [Fact]
    public void CloseOrder_BlocksIssueAndReserve_ReleasesReservations_CanReopen()
    {
        using var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 1) });
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 10m) }));
        f.Stock.Reserve(o, "VAI", 2m);
        var iss = new IssueService(f.C, f.Orders, f.Stock);
        var d = f.IssueDraft(o, "X1");
        Assert.Throws<BusinessException>(() => f.Orders.SetStatus(o, open: false));      // còn phiếu nháp
        f.Stock.DeleteDraft(d, "x");
        f.Orders.SetStatus(o, open: false);
        Assert.Empty(new Queries(f.C).Reservations(o));
        Assert.Throws<BusinessException>(() => f.IssueDraft(o, "X2"));
        Assert.Throws<BusinessException>(() => f.Stock.Reserve(o, "VAI", 1m));
        f.Orders.SetStatus(o, open: true);
        f.IssueDraft(o, "X3");
    }

    [Fact]
    public void NextNo_And_ReplaceDraftLines()
    {
        using var f = new Fixture();
        var q = new Queries(f.C);
        Assert.Equal("NK-0001", q.NextNo("NK"));
        var d = f.Stock.CreateDraft("IN", q.NextNo("NK"), new[] { ("VAI", 1m) });
        Assert.Equal("NK-0002", q.NextNo("NK"));
        f.Stock.ReplaceDraftLines(d, new[] { ("VAI", 5m), ("CHI", 2m) });
        Assert.Equal(new[] { ("VAI", 5m), ("CHI", 2m) }, q.DocLines(d).Select(l => (l.Material, l.Qty)));
        f.Stock.Confirm(d);
        Assert.Throws<BusinessException>(() => f.Stock.ReplaceDraftLines(d, new[] { ("VAI", 1m) }));
    }

    [Fact]
    public void Grid_Validation_And_Queries()
    {
        using var f = new Fixture();
        Assert.Throws<BusinessException>(() => f.Cat.AddGrid("G", "G", Array.Empty<(string, string?, string?)>()));
        Assert.Throws<BusinessException>(() => f.Cat.AddGrid("G", "G", new[] { ("S", (string?)null, (string?)null), ("S", null, null) }));
        var q = new Queries(f.C);
        Assert.Contains(q.Grids(), g => g.Code == "G_VOC" && g.Used && g.Sizes == "110/56, 110/60, 120/60");
        Assert.Equal(new[] { "110/56", "110/60", "120/60" }, q.ProductSizes(f.Prod));
        Assert.Equal(2, q.Variants(f.Prod).Count);
        Assert.Equal(3, q.Materials(f.Cust).Count);
    }
}

public class MaxSetsTests
{
    // Đơn: 10 bộ 110/56 + 10 bộ 120/60. BOM: VAI 1/SP (áo, quần) → 2/bộ; KHOA đóng gói theo bộ: 1 (110/56), 2 (120/60).
    static (Fixture f, long o) Setup()
    {
        var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), new DateTime(2026, 1, 1), new[] { new BomLineInput("KHOA", "*", 1m), new BomLineInput("KHOA", "120/60", 2m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10), new OrderLine("120/60", 10) });
        return (f, o);
    }

    [Fact]
    public void ByOrderMix_UsesTightestMaterial_AndCapsAtOrdered()
    {
        var (f, o) = Setup();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 30m), ("KHOA", 100m) }));   // VAI cần 40 → 75%
        var (mix, size, sum) = new ReportService(f.C, f.Orders, f.Stock).MaxSets(o);
        Assert.True(sum.Complete);
        Assert.Equal(15m, sum.MaxSets);                   // 75% × 20 bộ
        Assert.Equal(new[] { "VAI" }, sum.Limiting);
        Assert.Equal("VAI", mix.Rows[0][0]);
        Assert.Equal(75m, mix.Rows[0][4]);
        // Riêng từng size: 110/56 cần VAI 2 + KHOA 1/bộ → min(30/2, 100/1)=15 ; 120/60 → min(15, 100/2=50)=15
        Assert.Equal(15m, size.Rows.Single(r => (string)r[0]! == "110/56")[2]);
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N2", new[] { ("VAI", 100m) }));   // dư thừa → không vượt số bộ đặt
        Assert.Equal(20m, new ReportService(f.C, f.Orders, f.Stock).MaxSets(o).Summary.MaxSets);
        Assert.Equal("KHOA", new ReportService(f.C, f.Orders, f.Stock).MaxSets(o).BySize.Rows.Single(r => (string)r[0]! == "120/60")[3]);
    }

    [Fact]
    public void ReservationsOfOtherOrders_ReduceAvailability_NoStock_IsZero()
    {
        var (f, o) = Setup();
        Assert.Equal(0m, new ReportService(f.C, f.Orders, f.Stock).MaxSets(o).Summary.MaxSets);
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 40m), ("KHOA", 30m) }));
        var o2 = f.Orders.CreateOrder(f.Prod, "O2", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 5) });
        f.Stock.Reserve(o2, "VAI", 20m);
        Assert.Equal(10m, new ReportService(f.C, f.Orders, f.Stock).MaxSets(o).Summary.MaxSets);   // còn 20 VAI → 50%
    }

    [Fact]
    public void Incomplete_NoConclusion()
    {
        using var f = new Fixture();
        var o = f.Orders.CreateOrder(f.Prod, "O", DateTime.Now, new[] { new OrderLine("110/56", 1) });
        var r = new ReportService(f.C, f.Orders, f.Stock).MaxSets(o);
        Assert.False(r.Summary.Complete);
        Assert.Contains(r.ByOrderMix.Filters, x => x.StartsWith("CHƯA ĐỦ"));
    }
}

public class PlaceTests
{
    static (Fixture f, PlaceService p, long kho, long ncc, long toCat) Setup()
    {
        var f = new Fixture(); var p = new PlaceService(f.C);
        var kho = p.Add("KHO", "Kho NPL", PlaceKind.CaHai, "Xưởng 1");
        var ncc = p.Add("NCC1", "Nhà cung cấp Phúc An", PlaceKind.Xuat);
        var toCat = p.Add("TOCAT", "Tổ cắt", PlaceKind.Nhan);
        return (f, p, kho, ncc, toCat);
    }

    [Fact]
    public void Places_ByKind_AndDefaults()
    {
        var (f, p, kho, ncc, toCat) = Setup();
        var q = new Queries(f.C);
        Assert.Equal(new[] { "KHO", "NCC1" }, q.Places(forFrom: true).Select(x => x.Code));
        Assert.Equal(new[] { "KHO", "TOCAT" }, q.Places(forFrom: false).Select(x => x.Code));
        Assert.Null(p.DefaultInTo);
        p.DefaultInTo = kho; p.DefaultOutFrom = kho;
        Assert.Equal(kho, p.DefaultInTo);
        p.DefaultInTo = null; Assert.Null(p.DefaultInTo);
        Assert.Throws<SqliteException>(() => p.Add("KHO", "Trùng", PlaceKind.CaHai));
    }

    [Fact]
    public void DocPlaces_ValidatedByKind_OnlyOnDraft_ShownInList()
    {
        var (f, p, kho, ncc, toCat) = Setup();
        var d = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 10m) });
        Assert.Throws<BusinessException>(() => p.SetDocPlaces(d, toCat, kho));     // tổ cắt chỉ là nơi nhận
        Assert.Throws<BusinessException>(() => p.SetDocPlaces(d, kho, ncc));       // NCC chỉ là nơi xuất
        Assert.Throws<BusinessException>(() => p.SetDocPlaces(d, kho, kho));
        p.SetDocPlaces(d, ncc, kho);
        var row = new Queries(f.C).Docs("IN").Single();
        Assert.Equal(("Nhà cung cấp Phúc An", "Kho NPL"), (row.FromPlace, row.ToPlace));
        f.Stock.Confirm(d);
        Assert.Throws<BusinessException>(() => p.SetDocPlaces(d, null, null));
        p.SetActive(toCat, false);
        var x = f.Stock.CreateDraft("OUT", "X1", new[] { ("VAI", 1m) });
        Assert.Throws<BusinessException>(() => p.SetDocPlaces(x, kho, toCat));     // đã ngừng sử dụng
    }

    [Fact]
    public void Voucher_Pdf_ForInAndIssue()
    {
        var (f, p, kho, ncc, toCat) = Setup();
        new CompanyInfo(f.C) { Name = "Công ty May Ford", Address = "KCN A" };
        var d = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 10m), ("CHI", 3m) }, note: "Nhập theo HĐ 12");
        p.SetDocPlaces(d, ncc, kho);
        var ms = new MemoryStream(); VoucherPdf.Generate(f.C, d, ms);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(ms.ToArray(), 0, 4));
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        f.Stock.Confirm(d);
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 2) });
        var px = f.IssueDraft(o, "PX1");
        p.SetDocPlaces(px, kho, toCat);
        var ms2 = new MemoryStream(); VoucherPdf.Generate(f.C, px, ms2);
        Assert.True(ms2.Length > 1000);
        Assert.Equal("PHIẾU XUẤT KHO (CẤP PHÁT NPL)", VoucherPdf.Title(new Queries(f.C).Docs().First(x => x.Id == px)));
    }

    [Fact]
    public void Migration7_AddsPlaces()
    {
        using var c = Db.Open(":memory:");
        Assert.Equal((long)Schema.Migrations.Length, Db.Scalar(c, "PRAGMA user_version"));
        Db.Exec(c, "SELECT from_place_id,to_place_id FROM stock_doc");
    }
}

public class IssuedCountsTowardsDemandTests
{
    [Fact]
    public void AfterIssue_CheckAndMaxSets_CountIssuedQuantities()
    {
        using var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) });   // cần 20
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 20m) }));
        Assert.Equal(FulfillStatus.Enough, f.Stock.CheckOrder(o, f.Orders).Status);
        var iss = new IssueService(f.C, f.Orders, f.Stock);
        var d = f.IssueDraft(o, "X1"); iss.SetActual(d, "VAI", 15m); f.Stock.Confirm(d);   // tồn còn 5, đã cấp 15
        var r = f.Stock.CheckOrder(o, f.Orders);
        Assert.Equal(FulfillStatus.Enough, r.Status);                     // 20 − 15 đã cấp − 5 tồn = 0
        Assert.Equal(15m, r.Lines.Single().Issued);
        Assert.Equal(10m, new ReportService(f.C, f.Orders, f.Stock).MaxSets(o).Summary.MaxSets);
    }
}

public class AuditAndHistoryTests
{
    [Fact]
    public void EveryWriteOperation_IsLogged_WithActionAndReference()
    {
        using var f = new Fixture();   // Fixture đã thêm khách hàng, NPL, lưới, mã hàng, chủng loại
        var q = new Queries(f.C);
        f.Cat.UpdateMaterial("VAI", "Vải chính đỏ", "m", "khổ 1m6");
        f.Cat.SetMaterialActive("CHI", false); f.Cat.SetMaterialActive("CHI", true);
        var b = f.Bom.CreateBom(f.VAo, BomType.ToCat);
        f.Bom.AddVersion(b, new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1.2m, 3m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VQuan, BomType.ToCat), new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "DH9", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 5) });
        var d = f.Stock.CreateDraft("IN", "NK9", new[] { ("VAI", 50m) });
        f.Stock.ReplaceDraftLines(d, new[] { ("VAI", 60m) });
        var places = new PlaceService(f.C); var kho = places.Add("KHO", "Kho", PlaceKind.CaHai);
        places.SetDocPlaces(d, null, kho);
        f.Stock.Confirm(d);
        f.Stock.Reserve(o, "VAI", 5m);
        f.Orders.SetStatus(o, false);
        new PinService(f.C).SetPin("1234"); new PinService(f.C).IdleMinutes = 9;
        new CompanyInfo(f.C).Name = "Cty A";

        var log = q.Audit();
        void Has(string group, string action, string refText) =>
            Assert.True(log.Any(a => a.Group == group && a.ActionText == action && a.RefText.Contains(refText)), $"thiếu: {group} / {action} / {refText}");
        Has("Khách hàng", "Thêm", "KH1"); Has("Nguyên phụ liệu", "Thêm", "VAI"); Has("Nguyên phụ liệu", "Sửa", "VAI");
        Has("Nguyên phụ liệu", "Ngừng sử dụng", "CHI"); Has("Nguyên phụ liệu", "Dùng lại", "CHI");
        Has("Lưới size", "Thêm", "G_VOC"); Has("Mã hàng", "Thêm", "MH1"); Has("Chủng loại", "Thêm", "MH1 / AO");
        Has("BOM", "Thêm", "AO – Tổ cắt"); Has("BOM", "Tạo phiên bản", "AO – Tổ cắt v1");
        Has("Đơn hàng", "Lập đơn", "DH9"); Has("Đơn hàng", "Đóng đơn", "DH9");
        Has("Phiếu kho", "Lập phiếu nháp", "NK9"); Has("Phiếu kho", "Sửa phiếu nháp", "NK9"); Has("Phiếu kho", "Nơi xuất / nhận", "NK9"); Has("Phiếu kho", "Xác nhận", "NK9");
        Has("Giữ kho", "Giữ kho", "DH9 / VAI"); Has("Nơi xuất / nhận", "Thêm", "KHO");
        Has("Bảo mật", "Tạo mã PIN", "Mã PIN"); Has("Bảo mật", "Đổi thời gian tự khóa", "Tự khóa"); Has("Cài đặt", "Sửa", "Tên đơn vị");
        Assert.Contains(log, a => a.ActionText == "Xác nhận" && a.After == "VAI +60" && a.Reason == "Nhập kho");
        Assert.DoesNotContain(log, a => (a.After ?? "").Contains("1234"));   // không lộ PIN
    }

    [Fact]
    public void AuditFilter_ByDateGroupKeyword_AndExport()
    {
        using var f = new Fixture();
        var q = new Queries(f.C);
        Assert.NotEmpty(q.Audit(new AuditFilter(Entity: "material")));
        Assert.All(q.Audit(new AuditFilter(Entity: "material")), a => Assert.Equal("Nguyên phụ liệu", a.Group));
        Assert.Single(q.Audit(new AuditFilter(Keyword: "Vải chính")));
        Assert.Empty(q.Audit(new AuditFilter(From: DateTime.Today.AddDays(1))));
        Assert.NotEmpty(q.Audit(new AuditFilter(From: DateTime.Today, To: DateTime.Today)));
        var t = new ReportService(f.C, f.Orders, f.Stock).AuditReport(new AuditFilter(Keyword: "KH1"));
        Assert.Equal("Nhật ký thay đổi", t.Title);
        var ms = new MemoryStream(); ReportExporter.ToExcel(t, ms); Assert.True(ms.Length > 0);
    }

    [Fact]
    public void Restore_WritesAuditIntoRestoredData()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qlnpl_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        using (var c = Db.Open(Path.Combine(dir, "data.db")))
        {
            new CatalogRepository(c).AddCustomer("K1", "K1");
            var bs = new BackupService(c); bs.Backup(Path.Combine(dir, "b.db"));
            bs.Restore(Path.Combine(dir, "b.db"), Path.Combine(dir, "safe"));
            var log = new Queries(c).Audit();
            Assert.Contains(log, a => a.ActionText == "Phục hồi" && a.RefText == "b.db");
            Assert.Contains(log, a => a.ActionText == "Sao lưu");
        }
        Directory.Delete(dir, true);
    }

    [Fact]
    public void OrderHistory_NotConfusedByCloseReopen()
    {
        using var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 1) });
        f.Orders.SetStatus(o, false); f.Orders.SetStatus(o, true);
        new OrderAdjustmentService(f.C, f.Orders, f.Stock).Apply(o, new[] { new OrderLine("110/56", 2) }, "Tăng đơn");
        var h = new OrderAdjustmentService(f.C, f.Orders, f.Stock).History(o);
        Assert.Equal("Tăng đơn", h[1].Reason);
    }

    [Fact]
    public void TransactionHistory_HasCustomerPlacesScopeNote()
    {
        using var f = new Fixture();
        var p = new PlaceService(f.C); var ncc = p.Add("NCC", "Nhà cung cấp", PlaceKind.Xuat); var kho = p.Add("KHO", "Kho", PlaceKind.CaHai);
        var d = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 10m) }, note: "HĐ 01"); p.SetDocPlaces(d, ncc, kho); f.Stock.Confirm(d);
        var row = new ReportService(f.C, f.Orders, f.Stock).TransactionHistory(new ReportFilter()).Rows.Single();
        Assert.Equal(new object?[] { "Nhập", "KH1", "VAI", 10m, "", "", "Nhà cung cấp", "Kho", "HĐ 01" }, row.Skip(2).ToArray());
    }

    [Fact]
    public void StockCard_RunningBalance_MatchesOnHand_AndPeriodOpening()
    {
        using var f = new Fixture();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        f.Stock.Confirm(f.Stock.CreateDraft("OUT", "X1", new[] { ("VAI", 30m) }));
        f.Stock.Confirm(f.Stock.CreateDraft("RETURN", "R1", new[] { ("VAI", 5m) }));
        var cs = new StockCountService(f.C, f.Stock); cs.Confirm(cs.Create("K1", new[] { ("VAI", 70m) }));   // 75 → 70
        var rep = new ReportService(f.C, f.Orders, f.Stock);
        var card = rep.StockCard("VAI", DateTime.Today, DateTime.Today);
        Assert.Equal(0m, card.Rows[0][5]);                                   // tồn đầu kỳ
        Assert.Equal(new[] { 100m, 70m, 75m, 70m }, card.Rows.Skip(1).Take(4).Select(r => (decimal)r[5]!));
        var last = card.Rows.Last();
        Assert.Equal((105m, 35m, 70m), ((decimal)last[3]!, (decimal)last[4]!, (decimal)last[5]!));
        Assert.Equal(f.Stock.OnHand("VAI"), (decimal)last[5]!);
        var later = rep.StockCard("VAI", DateTime.Today.AddDays(1), null);
        Assert.Equal(70m, later.Rows[0][5]);                                  // đầu kỳ sau = tồn hiện tại
        Assert.Equal(2, later.Rows.Count);
    }
}
