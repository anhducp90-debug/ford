using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

/// <summary>Cấp phát theo nơi nhận + loại BOM + số lượng sản phẩm theo size (không vượt đơn), cân đối theo nơi nhận, bù, trả, điều chuyển dư, quyết toán.</summary>
public class PlaceBalanceTests
{
    // Đơn 10 bộ size 110/56. BOM: Tổ cắt VAI 1/SP (áo, quần) → 2/bộ; Sản xuất áo CHI 0,5/SP; Đóng gói theo bộ KHOA 1/bộ.
    static (Fixture f, IssueService iss, long o) Setup(decimal sets = 10, bool quanSx = false)
    {
        var f = new Fixture();
        var d = new DateTime(2026, 1, 1);
        f.BomAll(d, new[] { new BomLineInput("VAI", "*", 1m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.5m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), d, new[] { new BomLineInput("KHOA", "*", 1m) });
        if (quanSx) f.Bom.AddVersion(f.Bom.CreateBom(f.VQuan, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.3m) });   // quần: CHI 0,3
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
    static readonly string[] AZ = { T, SX, DG };
    /// <summary>Phiếu cấp tổ cắt cả bộ n bộ size 110/56.</summary>
    static long Cut(Fixture f, long o, string no, string place, decimal n) => f.Issue(o, no, place, new[] { T }, null, (Z, n));
    static long One(Fixture f, long o, string no, string place, string type, string[]? vars, decimal n) => f.Issue(o, no, place, new[] { type }, vars, (Z, n));
    static string[] Mats(Fixture f, long doc) => new Queries(f.C).DocLines(doc).Select(l => l.Material).ToArray();

    [Fact]
    public void BalanceOfOnePlace_IsNeverUsedByAnother_SameMaterial()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "TO1", 5);
        Assert.Equal(10m, Sug(f, d1, "VAI"));                             // cả bộ 5 bộ = 5 áo + 5 quần
        Issue(f, iss, d1, ("VAI", 13m));                                  // tổ 1 cấp thừa 3
        Assert.Equal(3m, f.Closing(o, "VAI", "TO1"));
        var d2 = Cut(f, o, "X2", "TO2", 5);
        Assert.Equal(10m, Sug(f, d2, "VAI"));                             // tổ 2 KHÔNG được trừ phần dư của tổ 1
        Assert.Equal(0m, f.Closing(o, "VAI", "TO2"));
    }

    [Fact]
    public void Cut_DefaultBySet_OrSingleVariant_CapacityPerSize()
    {
        var (f, iss, o) = Setup();
        var all = new[] { "AO", "QUAN" };
        Assert.Equal(10m, iss.Capacity(o, new[] { T }, all).sizes.Single().MaxSets);
        var d = One(f, o, "X1", "CAT", T, new[] { "AO" }, 4);                       // lẻ chủng loại áo
        Assert.Equal(4m, Sug(f, d, "VAI"));
        Assert.Contains("Lẻ chủng loại: AO", IssueService.Describe(f.C, d));
        Assert.Equal(6m, iss.Capacity(o, new[] { T }, all).sizes.Single().MaxSets);   // cả bộ bị giới hạn bởi áo còn 6
        Assert.Equal(10m, iss.Capacity(o, new[] { T }, new[] { "QUAN" }).sizes.Single().MaxSets);
        Assert.Contains("vượt số lượng còn lại 6", Assert.Throws<BusinessException>(() => Cut(f, o, "X2", "CAT2", 7)).Message);
        var d2 = Cut(f, o, "X2", "CAT2", 6);
        Assert.Contains("Cả bộ", IssueService.Describe(f.C, d2));
        Assert.Throws<BusinessException>(() => One(f, o, "X3", "CAT3", T, new[] { "AO" }, 1));   // áo đã cấp đủ 10
        Assert.Throws<BusinessException>(() => f.Issue(o, "X3", "CAT3", new[] { T }, null, ("120/60", 1)));   // size không có trong đơn
        var ncc = new PlaceService(f.C).Add("NCC", "NCC", PlaceKind.Xuat);
        Assert.Throws<BusinessException>(() => iss.CreateIssueDraft(o, ncc, new[] { T }, all, S(1), "X4"));   // chỉ là nơi xuất
        f.Stock.DeleteDraft(d, "Lập sai");                                                // hủy nháp → trả lại số lượng
        Assert.Equal(4m, iss.Capacity(o, new[] { T }, all).sizes.Single().MaxSets);
    }

    [Fact]
    public void Production_Checkboxes_SingleVariant_Combined_WholeSet()
    {
        var (f, iss, o) = Setup();
        var t1 = One(f, o, "X1", "TO1", SX, new[] { "AO" }, 6);                         // tổ 1 may 6 áo (lẻ)
        Assert.Equal(new[] { "CHI" }, Mats(f, t1)); Assert.Equal(3m, Sug(f, t1, "CHI"));
        Assert.Contains("chưa có BOM Sản xuất", Assert.Throws<BusinessException>(() => One(f, o, "X2", "TO2", SX, new[] { "QUAN" }, 1)).Message);
        Assert.Throws<BusinessException>(() => One(f, o, "X2", "TO2", SX, new[] { "AO", "QUAN" }, 4));
        (f, iss, o) = Setup(quanSx: true);
        t1 = One(f, o, "X1", "TO1", SX, new[] { "AO" }, 6);
        var t2 = One(f, o, "X2", "TO2", SX, new[] { "AO", "QUAN" }, 4);                 // sau khi khai báo BOM sản xuất cho quần
        Assert.Equal(3.2m, Sug(f, t2, "CHI"));                                            // 4 áo × 0,5 + 4 quần × 0,3
        Assert.Contains("Cả bộ", IssueService.Describe(f.C, t2));                          // tích đủ 2/2 chủng loại = cả bộ
        Assert.Equal(new[] { new IssueQtyRow(SX, "AO", Z, 6) }, iss.DocQuantities(t1));
    }

    [Fact]
    public void VariantWithoutBom_IsFlagged_AndBlocked_NeverSilentlySkipped()
    {
        var (f, iss, o) = Setup();
        Assert.All(iss.VariantsFor(o, new[] { T }).variants, v => Assert.Empty(v.MissingTypes));
        var sx = iss.VariantsFor(o, new[] { SX }).variants;
        Assert.Equal(new[] { "AO", "QUAN" }, sx.Select(v => v.Variant.Code));               // vẫn hiện quần
        Assert.Equal(new[] { SX }, sx.Single(v => v.Variant.Code == "QUAN").MissingTypes);   // nhưng đánh dấu thiếu BOM sản xuất
        var dg = iss.VariantsFor(o, new[] { DG });
        Assert.True(dg.setOnly); Assert.All(dg.variants, v => Assert.Empty(v.MissingTypes)); // đóng gói theo bộ: không cần BOM chủng loại
        var ex = Assert.Throws<BusinessException>(() => One(f, o, "X1", "TO1", SX, null, 10));   // cả bộ với BOM sản xuất: quần thiếu BOM → chặn
        Assert.Contains("QUAN chưa có BOM Sản xuất", ex.Message);
        Assert.Contains("QUAN chưa có BOM Sản xuất", string.Join("\n", iss.Capacity(o, new[] { SX }, new[] { "AO", "QUAN" }).errors));
        Assert.Throws<BusinessException>(() => f.Issue(o, "X1", "TO1", AZ, null, (Z, 1)));   // trọn gói cũng chặn
        Assert.Equal(new[] { new IssueQtyRow(SX, "AO", Z, 10) }, iss.DocQuantities(One(f, o, "X1", "TO1", SX, new[] { "AO" }, 10)));
    }

    [Fact]
    public void Packing_IsBySet_RequiresAllVariants()
    {
        var (f, iss, o) = Setup();
        Assert.Contains("phải tích đủ tất cả chủng loại", Assert.Throws<BusinessException>(() => One(f, o, "X1", "HT", DG, new[] { "AO" }, 1)).Message);
        var ht = One(f, o, "X1", "HT", DG, null, 10);
        Assert.Equal(new[] { "KHOA" }, Mats(f, ht)); Assert.Equal(10m, Sug(f, ht, "KHOA"));
        Assert.Equal(new[] { new IssueQtyRow(DG, IssueScopes.SetObject, Z, 10) }, iss.DocQuantities(ht));
    }

    [Fact]
    public void WholeUnit_AtoZ_OneDocument_AllBomTypes()
    {
        var (f, iss, o) = Setup(quanSx: true);
        Assert.Throws<BusinessException>(() => f.Issue(o, "X0", "XN", AZ, new[] { "AO" }, (Z, 1)));   // trọn gói gồm đóng gói → phải cả bộ
        var d = f.Issue(o, "X1", "XN", AZ, null, (Z, 10));
        Assert.Equal(new[] { "CHI", "KHOA", "VAI" }, Mats(f, d));
        Assert.Equal((20m, 8m, 10m), (Sug(f, d, "VAI"), Sug(f, d, "CHI"), Sug(f, d, "KHOA")));
        Assert.StartsWith("Trọn gói", IssueScopes.Label((string)Db.Scalar(f.C, "SELECT scope FROM stock_doc WHERE id=" + d)!));
        Assert.All(iss.Progress(o), p => Assert.Equal(0m, p.P.Remaining));
        Issue(f, iss, d, ("VAI", 21m), ("CHI", 8m), ("KHOA", 10m));
        Assert.Equal(1m, f.Closing(o, "VAI", "XN"));
        Assert.Single(iss.Settlement(o).Where(x => x.Material == "VAI"));
    }

    [Fact]
    public void OneDraftPerPlace_OtherPlacesInParallel()
    {
        var (f, iss, o) = Setup();
        Cut(f, o, "X1", "CAT", 5);
        One(f, o, "X2", "TO2", SX, new[] { "AO" }, 5);
        Assert.Throws<BusinessException>(() => One(f, o, "X3", "CAT", SX, new[] { "AO" }, 1));
    }

    [Fact]
    public void Batches_NextIssueSuggestsOnlyItsQuantity_ShortageCarriedForward()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "CAT", 4);
        Assert.Equal(8m, Sug(f, d1, "VAI")); Issue(f, iss, d1, ("VAI", 6m));            // cấp thiếu 2
        Assert.Equal(-2m, f.Closing(o, "VAI", "CAT"));
        var d2 = Cut(f, o, "X2", "CAT", 6);
        Assert.Equal(14m, Sug(f, d2, "VAI"));                                            // 12 cho đợt 2 + 2 còn thiếu
        Issue(f, iss, d2, ("VAI", 14m));
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT"));
        Assert.Equal(0m, iss.Capacity(o, new[] { T }, new[] { "AO", "QUAN" }).sizes.Single().MaxSets);
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
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT"));
        var b2 = iss.CreateSupplement(d1, "B2", new[] { ("VAI", 3m) }, SupplementKind.BuHaoHut); f.Stock.Confirm(b2);   // cắt hỏng
        Assert.Equal(0m, f.Closing(o, "VAI", "CAT"));                            // hao hụt không làm số dư dương
        Assert.Equal(477m, f.Stock.OnHand("VAI"));                              // nhưng vẫn trừ tồn kho
        Assert.Equal(20m, f.Stock.IssuedNet(o, "VAI"));                         // đã cấp ròng không tính phần hao hụt
        var loss = new ReportService(f.C, f.Orders, f.Stock).LossReport(new ReportFilter(OrderId: o)).Rows.Single();
        Assert.Equal((3m, 15m), ((decimal)loss[7]!, (decimal)loss[8]!));        // 3/20 = 15%
        Assert.Contains(new ReportService(f.C, f.Orders, f.Stock).TransactionHistory(new ReportFilter()).Rows, r => (string)r[2]! == "Xuất bù hao hụt");
        Assert.Equal(20m, iss.Objects(o, T).Sum(x => x.Sizes.Sum(p => p.Issued)));   // phiếu bù không cộng thêm số lượng sản phẩm
    }

    [Fact]
    public void Return_OnlyWhatThatPlaceReceived()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "TO1", 5); Issue(f, iss, d1, ("VAI", 10m));
        var bad = iss.CreateReturn(o, f.Place("TO2"), "R1", new[] { ("VAI", 1m) });   // tổ 2 chưa nhận gì
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(bad));
        var ok = iss.CreateReturn(o, f.Place("TO1"), "R2", new[] { ("VAI", 4m) }); f.Stock.Confirm(ok);
        Assert.Equal(-4m, f.Closing(o, "VAI", "TO1"));
    }

    [Fact]
    public void SurplusTransfer_ExplicitOnly_WithinSurplus_SameCustomer_NoStockChange()
    {
        var (f, iss, o) = Setup();
        var d1 = Cut(f, o, "X1", "TO1", 5); Issue(f, iss, d1, ("VAI", 13m));            // dư 3
        var stockBefore = f.Stock.OnHand("VAI");
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC0", o, f.Place("TO1"), o, f.Place("TO2"), new[] { new SurplusLine("VAI", 4m) }));
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC0", o, f.Place("TO1"), o, f.Place("TO1"), new[] { new SurplusLine("VAI", 1m) }));   // cùng nơi
        iss.TransferSurplus("DC1", o, f.Place("TO1"), o, f.Place("TO2"), new[] { new SurplusLine("VAI", 3m) }, "Tổ 1 thừa vải");
        Assert.Equal(0m, f.Closing(o, "VAI", "TO1"));
        Assert.Equal(3m, f.Closing(o, "VAI", "TO2"));
        Assert.Equal(stockBefore, f.Stock.OnHand("VAI"));
        var d2 = Cut(f, o, "X2", "TO2", 5);
        Assert.Equal(7m, Sug(f, d2, "VAI"));                                  // 10 − 3 đã nhận qua điều chuyển
        Assert.Contains(new Queries(f.C).Carry(o), r => r.DocNo == "DC1");
        // khác khách hàng
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        var g = (long)Db.Scalar(f.C, "SELECT id FROM size_grid WHERE code='G_VOC'")!;
        var p2 = f.Cat.AddProduct(kh2, "MH2", "x", g); f.Cat.AddVariant(p2, "V", "v", "Áo", 1);
        var o2 = f.Orders.CreateOrder(p2, "DH2", DateTime.Now, S(1));
        Assert.Throws<BusinessException>(() => iss.TransferSurplus("DC2", o, f.Place("TO2"), o2, f.Place("TO2"), new[] { new SurplusLine("VAI", 1m) }));
    }

    [Fact]
    public void SurplusTransfer_BetweenOrdersOfSameCustomer()
    {
        var (f, iss, o) = Setup();
        var o2 = f.Orders.CreateOrder(f.Prod, "DH2", new DateTime(2026, 2, 1), S(5));
        var d1 = Cut(f, o, "X1", "TO1", 10); Issue(f, iss, d1, ("VAI", 24m));            // dư 4
        iss.TransferSurplus("DC1", o, f.Place("TO1"), o2, f.Place("TO1"), new[] { new SurplusLine("VAI", 4m) });
        Assert.Equal(0m, f.Closing(o, "VAI", "TO1"));
        Assert.Equal(6m, Sug(f, Cut(f, o2, "X2", "TO1", 5), "VAI"));
    }

    [Fact]
    public void CloseOrder_SurplusBlocks_ShortageNeedsConfirmation_BalancedCloses()
    {
        var (f, iss, o) = Setup(quanSx: true);
        Issue(f, iss, f.Issue(o, "X1", "XN", AZ, null, (Z, 10)), ("VAI", 22m), ("CHI", 8m), ("KHOA", 9m));   // VAI dư 2, KHOA thiếu 1
        var s = iss.Settlement(o);
        Assert.Equal(2m, s.Single(x => x.Material == "VAI").Balance);
        Assert.Equal("Thiếu", s.Single(x => x.Material == "KHOA").Conclusion);
        Assert.Contains("VAI", Assert.Throws<BusinessException>(() => iss.CloseOrder(o)).Message);
        f.Stock.Confirm(iss.CreateReturn(o, f.Place("XN"), "R1", new[] { ("VAI", 2m) }));
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
        Issue(f, iss, One(f, o, "X1", "CAT", T, new[] { "AO" }, 10), ("VAI", 10m));     // quần chưa cắt
        Assert.Equal(0m, iss.Settlement(o).Single().Balance);
        var rep = new ReportService(f.C, f.Orders, f.Stock).SettlementReport(o);
        Assert.Contains(rep.Filters, x => x.StartsWith("Chưa cấp") && x.Contains("Quần đen"));
        var prog = new ReportService(f.C, f.Orders, f.Stock).IssueProgress(o);
        Assert.Contains(prog.Rows, r => r.Contains("Đã cấp đủ")); Assert.Contains(prog.Rows, r => r.Contains("Chưa cấp"));
        Assert.Contains("chưa cấp 10 / 10", Assert.Throws<ShortageOnCloseException>(() => iss.CloseOrder(o)).Message);
    }

    [Fact]
    public void Migration11_MergesPlaceLedgerAcrossBomTypes()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        for (var i = 0; i < 10; i++) Db.Exec(c, Schema.Migrations[i]);
        Db.Exec(c, "PRAGMA user_version=10;");
        Db.Exec(c, @"INSERT INTO customer(id,code,name) VALUES(1,'K','K'); INSERT INTO size_grid(id,code,name) VALUES(1,'G','G');
                     INSERT INTO product(id,customer_id,code,name,default_grid_id) VALUES(1,1,'P','P',1);
                     INSERT INTO material(id,code,name,unit,customer_id) VALUES(1,'M','m','m',1);
                     INSERT INTO place(id,code,name,kind) VALUES(1,'T','Tổ','CA_HAI');
                     INSERT INTO ""order""(id,customer_id,product_id,order_no,order_date) VALUES(1,1,1,'O','2026-01-01T00:00:00');
                     INSERT INTO stock_doc(id,type,doc_no,status,order_id,scope,issue_place_id,created_at) VALUES(1,'OUT','X','CONFIRMED',1,'TO_CAT',1,'2026-01-02T00:00:00');
                     INSERT INTO issue_qty(doc_id,object,size_code,qty) VALUES(1,'A','S','5');
                     INSERT INTO carry_ledger(order_id,material_id,scope,place_id,opening,issued,demand,closing,created_at) VALUES(1,1,'TO_CAT',1,'0','12','10','2','2026-01-02T00:00:00');
                     INSERT INTO carry_ledger(order_id,material_id,scope,place_id,opening,issued,demand,closing,created_at) VALUES(1,1,'SAN_XUAT',1,'0','3','5','-2','2026-01-03T00:00:00');");
        Db.Migrate(c);
        Assert.Equal(0m, new StockService(c).CarryClosing(1, 1, IssueScopes.Place, 1));
        Assert.Equal("TO_CAT", Db.Scalar(c, "SELECT bom_type FROM issue_qty"));
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
