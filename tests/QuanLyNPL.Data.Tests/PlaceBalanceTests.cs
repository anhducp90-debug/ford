using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

/// <summary>Cấp phát theo nơi nhận + loại BOM + số lượng sản phẩm theo size (không vượt đơn), cân đối theo nơi nhận, bù, trả, điều chuyển dư, quyết toán.</summary>
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

    const string T = IssueScopes.ToCat, SX = IssueScopes.SanXuat, DG = IssueScopes.DongGoi, Z = "110/56";
    /// <summary>Phiếu cấp tổ cắt cho n áo + n quần size 110/56.</summary>
    static long Cut(Fixture f, long o, string no, string place, decimal n) => f.IssueQty(o, no, place, T, ("AO", Z, n), ("QUAN", Z, n));

    [Fact]
    public void BalanceOfOnePlace_IsNeverUsedByAnother_SameMaterial()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "TO1", 5);
        Assert.Equal(10m, Sug(f, d1, "VAI"));
        Issue(f, iss, d1, ("VAI", 13m));                                  // tổ 1 cấp thừa 3
        Assert.Equal(3m, f.Closing(o, "VAI", "TO1", T));
        var d2 = Cut(f, o, "X2", "TO2", 5);
        Assert.Equal(10m, Sug(f, d2, "VAI"));                             // tổ 2 KHÔNG được trừ phần dư của tổ 1
        Assert.Equal(0m, f.Closing(o, "VAI", "TO2", T));
    }

    [Fact]
    public void IssuedQuantity_PerObjectAndSize_CannotExceedOrder_DraftsCount()
    {
        var (f, iss, o) = Setup();
        f.IssueQty(o, "X1", "TO1", SX, ("AO", Z, 6));                                   // nháp cũng được tính
        Assert.Contains("vượt số lượng còn lại 4", Assert.Throws<BusinessException>(() => f.IssueQty(o, "X2", "TO2", SX, ("AO", Z, 5))).Message);
        f.IssueQty(o, "X2", "TO2", SX, ("AO", Z, 4));
        var ao = iss.Objects(o, SX).Single();                                            // chỉ áo có BOM sản xuất
        Assert.Equal(("AO", 10m, 0m, 10m, 0m), (ao.Code, ao.Sizes[0].Ordered, ao.Sizes[0].Issued, ao.Sizes[0].Draft, ao.Sizes[0].Remaining));
        Assert.Throws<BusinessException>(() => f.IssueQty(o, "X3", "TO3", SX, ("QUAN", Z, 1)));       // quần không có BOM sản xuất
        Assert.Throws<BusinessException>(() => f.IssueQty(o, "X3", "TO3", T, ("AO", "120/60", 1)));   // size không có trong đơn
        Assert.Equal(new[] { IssueScopes.SetObject }, iss.Objects(o, DG).Select(x => x.Code));        // đóng gói theo bộ
        Assert.Throws<BusinessException>(() => f.IssueQty(o, "X3", "TO3", DG, ("AO", Z, 1)));
        var ncc = new PlaceService(f.C).Add("NCC", "NCC", PlaceKind.Xuat);
        Assert.Throws<BusinessException>(() => iss.CreateIssueDraft(o, ncc, T, "X4", new[] { new IssueQty("AO", Z, 1) }));   // chỉ là nơi xuất
        // xóa nháp → trả lại số lượng
        f.Stock.DeleteDraft(Convert.ToInt64(Db.Scalar(f.C, "SELECT id FROM stock_doc WHERE doc_no='X1'")), "Lập sai");
        Assert.Equal(6m, iss.Objects(o, SX).Single().Sizes[0].Remaining);
    }

    [Fact]
    public void ManyTeams_EachMakesSomeVariants_DemandByBomType()
    {
        var (f, iss, o) = Setup();
        var cat = Cut(f, o, "X1", "CAT", 10);                                            // nhà cắt cắt cả áo và quần
        Assert.Equal(new[] { "VAI" }, new Queries(f.C).DocLines(cat).Select(l => l.Material));
        Assert.Equal(20m, Sug(f, cat, "VAI"));
        var t1 = f.IssueQty(o, "X2", "TO1", SX, ("AO", Z, 6));                           // tổ 1 may 6 áo
        var t2 = f.IssueQty(o, "X3", "TO2", SX, ("AO", Z, 4));                           // tổ 2 may 4 áo
        Assert.Equal((3m, 2m), (Sug(f, t1, "CHI"), Sug(f, t2, "CHI")));
        var ht = f.IssueQty(o, "X4", "HT", DG, (IssueScopes.SetObject, Z, 10));
        Assert.Equal(new[] { "KHOA" }, new Queries(f.C).DocLines(ht).Select(l => l.Material));
        Assert.Equal(10m, Sug(f, ht, "KHOA"));
        Assert.Equal(new[] { new IssueQty("AO", Z, 6) }, iss.DocQuantities(t1));
        Assert.All(iss.Progress(o), p => Assert.Equal(0m, p.P.Remaining));
    }

    [Fact]
    public void ParallelDrafts_PerPlaceAndType_DuplicateBlocked()
    {
        var (f, iss, o) = Setup();
        Cut(f, o, "X1", "CAT", 5);
        f.IssueQty(o, "X2", "CAT", SX, ("AO", Z, 5));                                   // khác loại BOM: song song được
        f.IssueQty(o, "X3", "TO2", SX, ("AO", Z, 5));                                   // khác nơi nhận: song song được
        Assert.Throws<BusinessException>(() => Cut(f, o, "X4", "CAT", 1));
    }

    [Fact]
    public void Batches_NextIssueSuggestsOnlyItsQuantity_ShortageCarriedForward()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "CAT", 4);
        Assert.Equal(8m, Sug(f, d1, "VAI")); Issue(f, iss, d1, ("VAI", 6m));            // cấp thiếu 2
        Assert.Equal(-2m, f.Closing(o, "VAI", "CAT", T));
        var d2 = Cut(f, o, "X2", "CAT", 6);
        Assert.Equal(14m, Sug(f, d2, "VAI"));                                            // 12 cho đợt 2 + 2 còn thiếu
        Issue(f, iss, d2, ("VAI", 14m));
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT", T));
        Assert.Equal(0m, iss.Objects(o, T).Sum(x => x.Sizes.Sum(p => p.Remaining)));
    }

    [Fact]
    public void LossSupplement_DoesNotCreateSurplus_ShortageSupplement_Warns()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "CAT", 10); Issue(f, iss, d1, ("VAI", 18m));            // thiếu 2
        Assert.Contains(iss.SupplementWarnings(d1, new[] { ("VAI", 5m) }, SupplementKind.BuThieu), w => w.Contains("vượt phần đang thiếu"));
        Assert.Empty(iss.SupplementWarnings(d1, new[] { ("VAI", 2m) }, SupplementKind.BuThieu));
        Assert.Contains(iss.SupplementWarnings(d1, new[] { ("CHI", 1m) }, SupplementKind.BuHaoHut), w => w.Contains("không có trong định mức"));
        var b1 = iss.CreateSupplement(d1, "B1", new[] { ("VAI", 2m) }, SupplementKind.BuThieu); f.Stock.Confirm(b1);
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT", T));
        var b2 = iss.CreateSupplement(d1, "B2", new[] { ("VAI", 3m) }, SupplementKind.BuHaoHut); f.Stock.Confirm(b2);   // cắt hỏng
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT", T));                         // hao hụt không làm số dư dương
        Assert.Equal(477m, f.Stock.OnHand("VAI"));                              // nhưng vẫn trừ tồn kho
        Assert.Equal(20m, f.Stock.IssuedNet(o, "VAI"));                         // đã cấp ròng không tính phần hao hụt
        var loss = new ReportService(f.C, f.Orders, f.Stock).LossReport(new ReportFilter(OrderId: o)).Rows.Single();
        Assert.Equal((3m, 15m), ((decimal)loss[7]!, (decimal)loss[8]!));        // 3/20 = 15%
        Assert.Contains(new ReportService(f.C, f.Orders, f.Stock).TransactionHistory(new ReportFilter()).Rows, r => (string)r[2]! == "Xuất bù hao hụt");
        Assert.Equal(0m, iss.Objects(o, T).Sum(x => x.Sizes.Sum(p => p.Issued)) - 20m);   // phiếu bù không cộng thêm số lượng sản phẩm
    }

    [Fact]
    public void Return_OnlyWhatThatPlaceReceived()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "TO1", 5); Issue(f, iss, d1, ("VAI", 10m));
        var bad = iss.CreateReturn(o, f.Place("TO2"), T, "R1", new[] { ("VAI", 1m) });   // tổ 2 chưa nhận gì
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(bad));
        var ok = iss.CreateReturn(o, f.Place("TO1"), T, "R2", new[] { ("VAI", 4m) }); f.Stock.Confirm(ok);
        Assert.Equal(-4m, f.Closing(o, "VAI", "TO1", T));
    }

    [Fact]
    public void SurplusTransfer_ExplicitOnly_WithinSurplus_SameCustomer_NoStockChange()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "TO1", 5); Issue(f, iss, d1, ("VAI", 13m));            // dư 3
        var stockBefore = f.Stock.OnHand("VAI");
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC0", o, f.Place("TO1"), T, o, f.Place("TO2"), T, new[] { new SurplusLine("VAI", 4m) }));
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC0", o, f.Place("TO1"), T, o, f.Place("TO1"), T, new[] { new SurplusLine("VAI", 1m) }));   // cùng nơi
        iss.TransferSurplus("DC1", o, f.Place("TO1"), T, o, f.Place("TO2"), T, new[] { new SurplusLine("VAI", 3m) }, "Tổ 1 thừa vải");
        Assert.Equal(0m, f.Closing(o, "VAI", "TO1", T));
        Assert.Equal(3m, f.Closing(o, "VAI", "TO2", T));
        Assert.Equal(stockBefore, f.Stock.OnHand("VAI"));
        var d2 = Cut(f, o, "X2", "TO2", 5);
        Assert.Equal(7m, Sug(f, d2, "VAI"));                                  // 10 − 3 đã nhận qua điều chuyển
        Assert.Contains(new Queries(f.C).Carry(o), r => r.DocNo == "DC1");
        Assert.Contains("không có trong BOM Đóng gói", Assert.Throws<BusinessException>(() =>
            iss.TransferSurplus("DCx", o, f.Place("TO2"), T, o, f.Place("HT"), DG, new[] { new SurplusLine("VAI", 1m) })).Message);
        // khác khách hàng
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        var g = (long)Db.Scalar(f.C, "SELECT id FROM size_grid WHERE code='G_VOC'")!;
        var p2 = f.Cat.AddProduct(kh2, "MH2", "x", g); f.Cat.AddVariant(p2, "V", "v", "Áo", 1);
        var o2 = f.Orders.CreateOrder(p2, "DH2", DateTime.Now, S(1));
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC2", o, f.Place("TO2"), T, o2, f.Place("TO2"), T, new[] { new SurplusLine("VAI", 1m) }));
    }

    [Fact]
    public void SurplusTransfer_BetweenOrdersOfSameCustomer()
    {
        var (f, iss, o) = Setup();
        var o2 = f.Orders.CreateOrder(f.Prod, "DH2", new DateTime(2026, 2, 1), S(5));
        var d1 = Cut(f, o, "X1", "TO1", 10); Issue(f, iss, d1, ("VAI", 24m));            // dư 4
        iss.TransferSurplus("DC1", o, f.Place("TO1"), T, o2, f.Place("TO1"), T, new[] { new SurplusLine("VAI", 4m) });
        Assert.Equal(0m, f.Closing(o, "VAI", "TO1", T));
        Assert.Equal(6m, Sug(f, Cut(f, o2, "X2", "TO1", 5), "VAI"));
    }

    [Fact]
    public void CloseOrder_SurplusBlocks_ShortageNeedsConfirmation_BalancedCloses()
    {
        var (f, iss, o) = Setup();
        Issue(f, iss, Cut(f, o, "X1", "CAT", 10), ("VAI", 22m));                                        // dư 2
        Issue(f, iss, f.IssueQty(o, "X2", "TO1", SX, ("AO", Z, 10)), ("CHI", 5m));
        Issue(f, iss, f.IssueQty(o, "X3", "HT", DG, (IssueScopes.SetObject, Z, 10)), ("KHOA", 9m));    // thiếu 1
        var s = iss.Settlement(o);
        Assert.Equal(2m, s.Single(x => x.Material == "VAI").Balance);
        Assert.Equal("Thiếu", s.Single(x => x.Material == "KHOA").Conclusion);
        Assert.Contains("VAI", Assert.Throws<BusinessException>(() => iss.CloseOrder(o)).Message);
        f.Stock.Confirm(iss.CreateReturn(o, f.Place("CAT"), T, "R1", new[] { ("VAI", 2m) }));
        var ex = Assert.Throws<ShortageOnCloseException>(() => iss.CloseOrder(o));
        Assert.Contains("KHOA", ex.Message);
        Assert.DoesNotContain(" size ", ex.Message);                                     // đã cấp đủ số lượng đơn
        iss.CloseOrder(o, allowShortage: true);
        Assert.Equal("CLOSED", Db.Scalar(f.C, "SELECT status FROM \"order\" WHERE id=" + o));
    }

    [Fact]
    public void Settlement_ShowsUnissuedQuantities_CloseNeedsConfirmation()
    {
        var (f, iss, o) = Setup();
        Issue(f, iss, f.IssueQty(o, "X1", "CAT", T, ("AO", Z, 10)), ("VAI", 10m));        // quần chưa cắt
        Assert.Equal(0m, iss.Settlement(o).Single().Balance);
        var rep = new ReportService(f.C, f.Orders, f.Stock).SettlementReport(o);
        Assert.Contains(rep.Filters, x => x.StartsWith("Chưa cấp") && x.Contains("Quần đen"));
        var prog = new ReportService(f.C, f.Orders, f.Stock).IssueProgress(o);
        Assert.Contains(prog.Rows, r => r.Contains("Đã cấp đủ")); Assert.Contains(prog.Rows, r => r.Contains("Chưa cấp"));
        Assert.Contains("chưa cấp 10 / 10", Assert.Throws<ShortageOnCloseException>(() => iss.CloseOrder(o)).Message);
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
