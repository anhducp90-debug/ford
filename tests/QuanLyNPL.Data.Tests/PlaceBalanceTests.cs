using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

/// <summary>Cân đối cấp phát theo NƠI NHẬN và các đề xuất: phạm vi theo loại BOM, nhiều nháp, đợt, bù thiếu/hao hụt, điều chuyển dư, quyết toán.</summary>
public class PlaceBalanceTests
{
    // Đơn 10 bộ size 110/56. BOM: Tổ cắt VAI 1/SP (áo, quần) → 2/bộ; Sản xuất áo CHI 0,5/SP; Đóng gói theo bộ KHOA 1/bộ.
    static (Fixture f, IssueService iss, long o) Setup(decimal sets = 10)
    {
        var f = new Fixture();
        var d = new DateTime(2026, 1, 1);
        f.BomAll(d, new[] { new BomLineInput("VAI", "*", 1m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.5m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), d, new[] { new BomLineInput("KHOA", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "DH1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", sets) });
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 500m), ("CHI", 100m), ("KHOA", 100m) }));
        return (f, f.Iss, o);
    }
    static OrderLine[] S(decimal n) => new[] { new OrderLine("110/56", n) };
    static decimal Sug(Fixture f, long doc, string mat) =>
        Db.D((string)Db.Scalar(f.C, "SELECT l.qty_suggested FROM stock_doc_line l JOIN material m ON m.id=l.material_id WHERE l.doc_id=" + doc + " AND m.code='" + mat + "'")!);
    static void Issue(Fixture f, IssueService iss, long doc, params (string m, decimal q)[] qty)
    {
        foreach (var l in new Queries(f.C).DocLines(doc)) iss.SetActual(doc, l.Material, qty.FirstOrDefault(x => x.m == l.Material).q);
        f.Stock.Confirm(doc);
    }

    [Fact]
    public void BalanceOfOnePlace_IsNeverUsedByAnother_SameMaterial()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(5), "TO1", IssueScopes.ToCat); f.Assign(o, S(5), "TO2", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("TO1"), IssueScopes.ToCat, "X1");
        Assert.Equal(10m, Sug(f, d1, "VAI"));
        Issue(f, iss, d1, ("VAI", 13m));                                  // tổ 1 cấp thừa 3
        Assert.Equal(3m, f.Closing(o, "VAI", "TO1", IssueScopes.ToCat));
        var d2 = iss.CreateIssueDraft(o, f.Place("TO2"), IssueScopes.ToCat, "X2");
        Assert.Equal(10m, Sug(f, d2, "VAI"));                             // tổ 2 KHÔNG được trừ phần dư của tổ 1
        Assert.Equal(0m, f.Closing(o, "VAI", "TO2", IssueScopes.ToCat));
    }

    [Fact]
    public void Assignment_CannotExceedOrderedSets_PerSizeAndBomType()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(4), "TO1", IssueScopes.All);
        f.Assign(o, S(6), "TO2", IssueScopes.ToCat);
        f.Assign(o, S(6), "TO3", IssueScopes.SanXuat);
        Assert.Equal(0m, iss.Remaining(o, IssueScopes.ToCat)["110/56"]);
        Assert.Equal(0m, iss.Remaining(o, IssueScopes.All)["110/56"]);
        Assert.Equal(6m, iss.Remaining(o, IssueScopes.DongGoi)["110/56"]);
        Assert.Throws<BusinessException>(() => f.Assign(o, S(1), "TO4", IssueScopes.ToCat));
        Assert.Throws<BusinessException>(() => f.Assign(o, S(1), "TO4", IssueScopes.All));
        f.Assign(o, S(6), "TO4", IssueScopes.DongGoi);
        f.Assign(o, S(5), "TO2", IssueScopes.ToCat);                       // sửa giao việc của chính nơi đó (không tính trùng)
        Assert.Throws<BusinessException>(() => f.Assign(o, new[] { new OrderLine("120/60", 1) }, "TO5", IssueScopes.ToCat));   // size không có trong đơn
        var ncc = new PlaceService(f.C).Add("NCC", "NCC", PlaceKind.Xuat);
        Assert.Throws<BusinessException>(() => iss.SetAssignment(o, ncc, IssueScopes.All, S(1)));   // chỉ là nơi xuất
    }

    [Fact]
    public void ScopeByBomType_OnlyMaterialsOfThatType()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(10), "CAT", IssueScopes.ToCat); f.Assign(o, S(10), "CHUYEN", IssueScopes.SanXuat); f.Assign(o, S(10), "HT", IssueScopes.DongGoi);
        Assert.Equal(new[] { "VAI" }, iss.KeyDemand(o, f.Place("CAT"), IssueScopes.ToCat).Keys);
        Assert.Equal(5m, iss.KeyDemand(o, f.Place("CHUYEN"), IssueScopes.SanXuat)["CHI"]);
        Assert.Equal(new[] { "KHOA" }, iss.KeyDemand(o, f.Place("HT"), IssueScopes.DongGoi).Keys);
        Assert.Equal(20m, iss.KeyDemand(o, f.Place("CAT"), IssueScopes.ToCat)["VAI"]);
    }

    [Fact]
    public void ParallelDrafts_PerPlaceAndScope_DuplicateKeyBlocked()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(10), "CAT", IssueScopes.ToCat); f.Assign(o, S(10), "CHUYEN", IssueScopes.SanXuat);
        iss.CreateIssueDraft(o, f.Place("CAT"), IssueScopes.ToCat, "X1");
        iss.CreateIssueDraft(o, f.Place("CHUYEN"), IssueScopes.SanXuat, "X2");     // song song được
        Assert.Throws<BusinessException>(() => iss.CreateIssueDraft(o, f.Place("CAT"), IssueScopes.ToCat, "X3"));
        Assert.Throws<BusinessException>(() => iss.CreateIssueDraft(o, f.Place("HT"), IssueScopes.DongGoi, "X4"));   // chưa giao việc
    }

    [Fact]
    public void Batches_IncreaseAssignment_NextDraftSuggestsOnlyIncrement()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(4), "CAT", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("CAT"), IssueScopes.ToCat, "X1");
        Assert.Equal(8m, Sug(f, d1, "VAI")); Issue(f, iss, d1, ("VAI", 8m));
        f.Assign(o, S(10), "CAT", IssueScopes.ToCat);                          // đợt 2
        var d2 = iss.CreateIssueDraft(o, f.Place("CAT"), IssueScopes.ToCat, "X2");
        Assert.Equal(12m, Sug(f, d2, "VAI"));
    }

    [Fact]
    public void LossSupplement_DoesNotCreateSurplus_ShortageSupplement_Warns()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(10), "CAT", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("CAT"), IssueScopes.ToCat, "X1"); Issue(f, iss, d1, ("VAI", 18m));   // thiếu 2
        Assert.Contains(iss.SupplementWarnings(d1, new[] { ("VAI", 5m) }, SupplementKind.BuThieu), w => w.Contains("vượt phần đang thiếu"));
        Assert.Empty(iss.SupplementWarnings(d1, new[] { ("VAI", 2m) }, SupplementKind.BuThieu));
        Assert.Contains(iss.SupplementWarnings(d1, new[] { ("CHI", 1m) }, SupplementKind.BuHaoHut), w => w.Contains("không có trong định mức"));
        var b1 = iss.CreateSupplement(d1, "B1", new[] { ("VAI", 2m) }, SupplementKind.BuThieu); f.Stock.Confirm(b1);
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT", IssueScopes.ToCat));
        var b2 = iss.CreateSupplement(d1, "B2", new[] { ("VAI", 3m) }, SupplementKind.BuHaoHut); f.Stock.Confirm(b2);   // cắt hỏng
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT", IssueScopes.ToCat));       // hao hụt không làm số dư dương
        Assert.Equal(477m, f.Stock.OnHand("VAI"));                              // nhưng vẫn trừ tồn kho
        Assert.Equal(20m, f.Stock.IssuedNet(o, "VAI"));                         // đã cấp ròng không tính phần hao hụt
        var loss = new ReportService(f.C, f.Orders, f.Stock).LossReport(new ReportFilter(OrderId: o)).Rows.Single();
        Assert.Equal((3m, 15m), ((decimal)loss[7]!, (decimal)loss[8]!));        // 3/20 = 15%
        Assert.Contains(new ReportService(f.C, f.Orders, f.Stock).TransactionHistory(new ReportFilter()).Rows, r => (string)r[2]! == "Xuất bù hao hụt");
    }

    [Fact]
    public void Return_OnlyWhatThatPlaceReceived()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(5), "TO1", IssueScopes.ToCat); f.Assign(o, S(5), "TO2", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("TO1"), IssueScopes.ToCat, "X1"); Issue(f, iss, d1, ("VAI", 10m));
        var bad = iss.CreateReturn(o, f.Place("TO2"), IssueScopes.ToCat, "R1", new[] { ("VAI", 1m) });   // tổ 2 chưa nhận gì
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(bad));
        var ok = iss.CreateReturn(o, f.Place("TO1"), IssueScopes.ToCat, "R2", new[] { ("VAI", 4m) }); f.Stock.Confirm(ok);
        Assert.Equal(-4m, f.Closing(o, "VAI", "TO1", IssueScopes.ToCat));
    }

    [Fact]
    public void SurplusTransfer_ExplicitOnly_WithinSurplus_SameCustomer_NoStockChange()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(5), "TO1", IssueScopes.ToCat); f.Assign(o, S(5), "TO2", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("TO1"), IssueScopes.ToCat, "X1"); Issue(f, iss, d1, ("VAI", 13m));   // dư 3
        var stockBefore = f.Stock.OnHand("VAI");
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC0", o, f.Place("TO1"), IssueScopes.ToCat, o, f.Place("TO2"), IssueScopes.ToCat, new[] { new SurplusLine("VAI", 4m) }));
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC0", o, f.Place("TO1"), IssueScopes.ToCat, o, f.Place("TO3"), IssueScopes.ToCat, new[] { new SurplusLine("VAI", 1m) }));   // TO3 chưa giao việc
        iss.TransferSurplus("DC1", o, f.Place("TO1"), IssueScopes.ToCat, o, f.Place("TO2"), IssueScopes.ToCat, new[] { new SurplusLine("VAI", 3m) }, "Tổ 1 thừa vải");
        Assert.Equal(0m, f.Closing(o, "VAI", "TO1", IssueScopes.ToCat));
        Assert.Equal(3m, f.Closing(o, "VAI", "TO2", IssueScopes.ToCat));
        Assert.Equal(stockBefore, f.Stock.OnHand("VAI"));
        var d2 = iss.CreateIssueDraft(o, f.Place("TO2"), IssueScopes.ToCat, "X2");
        Assert.Equal(7m, Sug(f, d2, "VAI"));                                  // 10 − 3 đã nhận qua điều chuyển
        Assert.Contains(new Queries(f.C).Carry(o), r => r.DocNo == "DC1");
        // khác khách hàng
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        var g = (long)Db.Scalar(f.C, "SELECT id FROM size_grid WHERE code='G_VOC'")!;
        var p2 = f.Cat.AddProduct(kh2, "MH2", "x", g); f.Cat.AddVariant(p2, "V", "v", "Áo", 1);
        var o2 = f.Orders.CreateOrder(p2, "DH2", DateTime.Now, S(1));
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC2", o, f.Place("TO2"), IssueScopes.ToCat, o2, f.Place("TO2"), IssueScopes.ToCat, new[] { new SurplusLine("VAI", 1m) }));
    }

    [Fact]
    public void SurplusTransfer_BetweenOrdersOfSameCustomer()
    {
        var (f, iss, o) = Setup();
        var o2 = f.Orders.CreateOrder(f.Prod, "DH2", new DateTime(2026, 2, 1), S(5));
        f.Assign(o, S(10), "TO1", IssueScopes.ToCat); f.Assign(o2, S(5), "TO1", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("TO1"), IssueScopes.ToCat, "X1"); Issue(f, iss, d1, ("VAI", 24m));   // dư 4
        iss.TransferSurplus("DC1", o, f.Place("TO1"), IssueScopes.ToCat, o2, f.Place("TO1"), IssueScopes.ToCat, new[] { new SurplusLine("VAI", 4m) });
        Assert.Equal(0m, f.Closing(o, "VAI", "TO1", IssueScopes.ToCat));
        Assert.Equal(6m, Sug(f, iss.CreateIssueDraft(o2, f.Place("TO1"), IssueScopes.ToCat, "X2"), "VAI"));
    }

    [Fact]
    public void CloseOrder_SurplusBlocks_ShortageNeedsConfirmation_BalancedCloses()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(10), "TO1", IssueScopes.All);
        var d1 = iss.CreateIssueDraft(o, f.Place("TO1"), IssueScopes.All, "X1"); Issue(f, iss, d1, ("VAI", 22m), ("CHI", 5m), ("KHOA", 9m));   // VAI dư 2, KHOA thiếu 1
        var s = iss.Settlement(o);
        Assert.Equal(2m, s.Single(x => x.Material == "VAI").Balance);
        Assert.Equal("Thiếu", s.Single(x => x.Material == "KHOA").Conclusion);
        Assert.Contains("VAI", Assert.Throws<BusinessException>(() => iss.CloseOrder(o)).Message);
        f.Stock.Confirm(iss.CreateReturn(o, f.Place("TO1"), IssueScopes.All, "R1", new[] { ("VAI", 2m) }));
        var ex = Assert.Throws<ShortageOnCloseException>(() => iss.CloseOrder(o));
        Assert.Contains("KHOA", ex.Message);
        iss.CloseOrder(o, allowShortage: true);
        Assert.Equal("CLOSED", Db.Scalar(f.C, "SELECT status FROM \"order\" WHERE id=" + o));
    }

    [Fact]
    public void Settlement_ReflectsAssignmentReduction_AndUnassignedSets()
    {
        var (f, iss, o) = Setup();
        f.Assign(o, S(10), "TO1", IssueScopes.ToCat);
        var d1 = iss.CreateIssueDraft(o, f.Place("TO1"), IssueScopes.ToCat, "X1"); Issue(f, iss, d1, ("VAI", 20m));
        f.Assign(o, S(6), "TO1", IssueScopes.ToCat);                             // giảm giao việc → dư 8
        Assert.Equal(8m, iss.Settlement(o).Single(x => x.Material == "VAI").Balance);
        var rep = new ReportService(f.C, f.Orders, f.Stock).SettlementReport(o);
        Assert.Contains(rep.Filters, x => x.StartsWith("Chưa giao việc") && x.Contains("Tổ cắt: 110/56 còn 4 bộ"));
        Assert.Contains(rep.Filters, x => x.Contains("1 dòng dư"));
    }

    [Fact]
    public void Migration9_KeepsLegacyLedgerReadable()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        for (var i = 0; i < 8; i++) Db.Exec(c, Schema.Migrations[i]);
        Db.Exec(c, "PRAGMA user_version=8;");
        Db.Exec(c, @"INSERT INTO customer(id,code,name) VALUES(1,'K','K'); INSERT INTO size_grid(id,code,name) VALUES(1,'G','G');
                     INSERT INTO product(id,customer_id,code,name,default_grid_id) VALUES(1,1,'P','P',1);
                     INSERT INTO material(id,code,name,unit,customer_id) VALUES(1,'M','m','m',1);
                     INSERT INTO ""order""(id,customer_id,product_id,order_no,order_date) VALUES(1,1,1,'O','2026-01-01T00:00:00');
                     INSERT INTO carry_ledger(order_id,material_id,scope,opening,issued,demand,closing,created_at) VALUES(1,1,'ALL','0','12','10','2','2026-01-02T00:00:00');");
        Db.Migrate(c);
        var st = new StockService(c);
        Assert.Equal(2m, st.CarryClosing(1, 1, "ALL", null));
        Assert.Equal(12m, st.IssuedNet(1, "M"));
        var s = new IssueService(c, new OrderService(c), st).Settlement(1).Single();
        Assert.Equal((10m, 2m), (s.Demand, s.Balance));
    }
}
