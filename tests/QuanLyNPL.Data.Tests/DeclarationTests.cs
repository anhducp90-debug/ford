using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

/// <summary>Quy tắc khai báo: mỗi khai báo chọn đối tượng cấp trên, khách hàng được kế thừa.</summary>
public class DeclarationTests
{
    [Fact]
    public void Product_RequiresActiveCustomer_Variant_InheritsCustomer()
    {
        using var f = new Fixture();
        Assert.Throws<BusinessException>(() => f.Cat.AddProduct(9999, "X", "x", f.Grid));
        var kh2 = f.Cat.AddCustomer("KH2", "K2"); Db.Exec(f.C, "UPDATE customer SET active=0 WHERE id=" + kh2);
        Assert.Throws<BusinessException>(() => f.Cat.AddProduct(kh2, "X", "x", f.Grid));
        Assert.Equal(f.Cust, f.Cat.VariantCustomerId(f.VAo));
        Db.Exec(f.C, "UPDATE product SET active=0 WHERE id=" + f.Prod);
        Assert.Throws<BusinessException>(() => f.Cat.AddVariant(f.Prod, "V3", "v3", "Áo", 1));
    }

    [Fact]
    public void Bom_ChooseVariantAndType_NameAndCustomerInherited_OnePerType()
    {
        using var f = new Fixture();
        var cut = f.Bom.CreateBom(f.VAo, BomType.ToCat);
        Assert.Throws<BusinessException>(() => f.Bom.CreateBom(f.VAo, BomType.DongGoi));   // đóng gói phải theo bộ
        var pack = f.Bom.CreateSetBom(f.Prod, BomType.DongGoi);
        var info = f.Bom.Info(cut);
        Assert.Equal(("AO – Tổ cắt", "TO_CAT", f.Prod, f.Cust), (info.Name, info.Type, info.ProductId, info.CustomerId));
        Assert.Equal(("MH1 – Đóng gói (theo bộ)", true, 0L), (f.Bom.Info(pack).Name, f.Bom.Info(pack).PerSet, f.Bom.Info(pack).VariantId));
        Assert.Throws<BusinessException>(() => f.Bom.CreateBom(f.VAo, BomType.ToCat));      // trùng loại → thêm phiên bản
        Assert.Equal(cut, f.Bom.Find(f.VAo, BomType.ToCat));
        Assert.Null(f.Bom.Find(f.VQuan, BomType.ToCat));
        Assert.Equal(new[] { "110/56", "110/60", "120/60" }, f.Bom.Sizes(cut));   // size của mã hàng
    }

    [Fact]
    public void Bom_SizeMustBelongToProductGrid_AndNoDuplicateLines()
    {
        using var f = new Fixture();
        var b = f.Bom.CreateBom(f.VAo, BomType.ToCat);
        var ex = Assert.Throws<BusinessException>(() => f.Bom.AddVersion(b, DateTime.Now, new[] {
            new BomLineInput("VAI", "S", 1m), new BomLineInput("VAI", "*", 1m), new BomLineInput("VAI", "*", 2m) }));
        Assert.Contains("Dòng 1", ex.Message); Assert.Contains("lưới size của mã hàng", ex.Message);
        Assert.Contains("Dòng 3", ex.Message); Assert.Contains("trùng", ex.Message);
        Assert.Throws<BusinessException>(() => f.Bom.AddVersion(b, DateTime.Now, Array.Empty<BomLineInput>()));
    }

    [Fact]
    public void ThreeBomTypes_SumIntoDemand()
    {
        using var f = new Fixture();
        var d = new DateTime(2026, 1, 1);
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 1.2m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.02m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), d, new[] { new BomLineInput("CHI", "*", 0.01m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VQuan, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 1m), new BomLineInput("KHOA", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 100) });
        var r = f.Orders.CalculateDemand(o);
        Assert.True(r.IsComplete);
        Assert.Equal(220m, r.TotalByMaterial["VAI"]);   // áo 100×1.2 + quần 100×1
        Assert.Equal(3m, r.TotalByMaterial["CHI"]);     // áo sản xuất 100×0.02 + đóng gói theo bộ 100 bộ×0.01
        Assert.Equal(100m, r.TotalByMaterial["KHOA"]);
    }

    [Fact]
    public void BomTemplate_FromDeclaredBom_UsesVariantSizesAndNames()
    {
        using var f = new Fixture();
        var b = f.Bom.CreateBom(f.VQuan, BomType.ToCat);
        var ms = new MemoryStream(); BomExcel.WriteTemplate(f.C, b, ms); ms.Position = 0;
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var guide = wb.Worksheet("HƯỚNG DẪN");
        Assert.Equal("Khách hàng: KH1", guide.Cell(1, 1).GetString());
        Assert.Equal("Chủng loại: QUAN", guide.Cell(3, 1).GetString());
        Assert.Equal("120/60", wb.Worksheet("DANH SÁCH SIZE").Cell(4, 1).GetString());
        Assert.Equal(4, wb.Worksheet("BOM").LastColumnUsed()!.ColumnNumber());
    }

    [Fact]
    public void StockDoc_CustomerFromOrder_OrGivenOrInferred_NeverMixed()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        f.Cat.AddMaterial("VAI2", "Vải KH2", "m", kh2);
        var d1 = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 1m) });
        Assert.Equal(f.Cust, (long)Db.Scalar(f.C, "SELECT customer_id FROM stock_doc WHERE id=" + d1)!);   // suy ra từ NPL
        Assert.Throws<BusinessException>(() => f.Stock.CreateDraft("IN", "N2", new[] { ("VAI", 1m), ("VAI2", 1m) }));
        Assert.Throws<BusinessException>(() => f.Stock.CreateDraft("IN", "N3", new[] { ("VAI", 1m) }, customerId: kh2));
        var d4 = f.Stock.CreateDraft("IN", "N4", new[] { ("VAI2", 1m) }, customerId: kh2);
        Assert.Equal(kh2, (long)Db.Scalar(f.C, "SELECT customer_id FROM stock_doc WHERE id=" + d4)!);
    }

    [Fact]
    public void Count_IsPerCustomer_AndAdjustmentsCarryCustomer()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        f.Cat.AddMaterial("VAI2", "Vải KH2", "m", kh2);
        var cs = new StockCountService(f.C, f.Stock);
        Assert.Throws<BusinessException>(() => cs.Create("K0", new[] { ("VAI", 1m), ("VAI2", 1m) }));
        cs.Confirm(cs.Create("K1", new[] { ("VAI2", 4m) }));
        Assert.Equal(kh2, (long)Db.Scalar(f.C, "SELECT customer_id FROM stock_doc WHERE doc_no='K1-TANG'")!);
    }

    [Fact]
    public void Reserve_OnlyMaterialsOfOrderCustomer()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        f.Cat.AddMaterial("VAI2", "Vải KH2", "m", kh2);
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI2", 10m) }));
        var o = f.Orders.CreateOrder(f.Prod, "O", DateTime.Now, new[] { new OrderLine("110/56", 1) });
        Assert.Throws<BusinessException>(() => f.Stock.Reserve(o, "VAI2", 1m));
    }

    [Fact]
    public void Migration4_BackfillsDocCustomer()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        for (var i = 0; i < 3; i++) Db.Exec(c, Schema.Migrations[i]);
        Db.Exec(c, "PRAGMA user_version=3;");
        Db.Exec(c, "INSERT INTO customer(id,code,name) VALUES(1,'K','K'); INSERT INTO material(id,code,name,unit,customer_id) VALUES(1,'M','m','m',1);");
        Db.Exec(c, "INSERT INTO stock_doc(id,type,doc_no,created_at) VALUES(1,'IN','N','2026-01-01T00:00:00'); INSERT INTO stock_doc_line(doc_id,material_id,qty) VALUES(1,1,'5');");
        Db.Migrate(c);
        Assert.Equal(1L, Db.Scalar(c, "SELECT customer_id FROM stock_doc WHERE id=1"));
    }
}

public class PerSetBomDataTests
{
    [Fact]
    public void SetBom_SizesAreDefaultGrid_DemandBySets_SnapshotKeepsScope()
    {
        using var f = new Fixture();
        var pack = f.Bom.CreateSetBom(f.Prod, BomType.DongGoi);
        Assert.Equal(new[] { "110/56", "110/60", "120/60" }, f.Bom.Sizes(pack));
        Assert.Throws<BusinessException>(() => f.Bom.AddVersion(pack, DateTime.Now, new[] { new BomLineInput("CHI", "S", 1m) }));   // size ngoài lưới của mã hàng
        f.Bom.AddVersion(pack, new DateTime(2026, 1, 1), new[] { new BomLineInput("KHOA", "*", 1m), new BomLineInput("KHOA", "120/60", 2m) });
        var cut = f.Bom.CreateSetBom(f.Prod, BomType.ToCat);
        f.Bom.AddVersion(cut, new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 2.5m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10), new OrderLine("120/60", 4) });
        var r = f.Orders.CalculateDemand(o);
        Assert.True(r.IsComplete);                         // không có BOM chủng loại nhưng BOM theo bộ phủ đủ
        Assert.Equal(18m, r.TotalByMaterial["KHOA"]);      // 10×1 + 4×2 (theo bộ, không nhân 2 chủng loại)
        Assert.Equal(35m, r.TotalByMaterial["VAI"]);       // 14 bộ × 2.5
        Assert.Contains(r.Sources, s => s.Variant == DemandCalculator.SetScope && s.VariantSize == "120/60");
        Assert.True(f.Orders.LoadSnapshot(o).Boms.All(b => b.PerSet));
    }

    [Fact]
    public void SameType_EitherPerSetOrPerVariant_NotBoth()
    {
        using var f = new Fixture();
        f.Bom.CreateBom(f.VAo, BomType.ToCat);
        Assert.Throws<BusinessException>(() => f.Bom.CreateSetBom(f.Prod, BomType.ToCat));
        f.Bom.CreateSetBom(f.Prod, BomType.SanXuat);
        Assert.Throws<BusinessException>(() => f.Bom.CreateBom(f.VQuan, BomType.SanXuat));
        Assert.Throws<BusinessException>(() => f.Bom.CreateSetBom(f.Prod, BomType.SanXuat));          // trùng → thêm phiên bản
        Assert.Equal(f.Bom.FindSet(f.Prod, BomType.SanXuat), f.Bom.FindSet(f.Prod, BomType.SanXuat));
        Assert.Null(f.Bom.FindSet(f.Prod, BomType.ToCat));
    }

    [Fact]
    public void SetScope_CanBeIssuedSeparately()
    {
        using var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), new DateTime(2026, 1, 1), new[] { new BomLineInput("KHOA", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) });
        var iss = new IssueService(f.C, f.Orders, f.Stock);
        Assert.Equal(new[] { "KHOA" }, iss.ScopeDemand(o, DemandCalculator.SetScope).Keys);
        Assert.Equal(10m, iss.ScopeDemand(o, DemandCalculator.SetScope)["KHOA"]);
        Assert.False(iss.ScopeDemand(o, "AO").ContainsKey("KHOA"));
    }

    [Fact]
    public void Migration5_RenamesChungLoaiToSanXuat()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        for (var i = 0; i < 4; i++) Db.Exec(c, Schema.Migrations[i]);
        Db.Exec(c, "PRAGMA user_version=4;");
        Db.Exec(c, @"INSERT INTO customer(id,code,name) VALUES(1,'K','K'); INSERT INTO size_grid(id,code,name) VALUES(1,'G','G');
                     INSERT INTO product(id,customer_id,code,name,default_grid_id) VALUES(1,1,'P','P',1);
                     INSERT INTO variant(id,product_id,code,name,qty_per_set,grid_id) VALUES(1,1,'AO','Áo','1',1);
                     INSERT INTO bom(product_id,name,type,scope_variant_id) VALUES(1,'AO – Chủng loại','CHUNG_LOAI',1);");
        Db.Migrate(c);
        Assert.Equal("SAN_XUAT|AO – Sản xuất|0", Db.Scalar(c, "SELECT type||'|'||name||'|'||per_set FROM bom"));
    }
}

public class TransferTests
{
    // KH1 có VAI (100 m). KH2 khai báo mã riêng VAI2 (m).
    static (Fixture f, TransferService t, long kh2) Setup()
    {
        var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "Khách 2");
        f.Cat.AddMaterial("VAI2", "Vải KH2", "m", kh2);
        f.Cat.AddMaterial("CHI2", "Chỉ KH2", "cuộn", kh2);
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        return (f, new TransferService(f.C, f.Stock), kh2);
    }

    [Fact]
    public void Transfer_AOutByOwnCode_BInByOwnCode_Atomically()
    {
        var (f, t, kh2) = Setup();
        var id = t.Create("CH1", f.Cust, kh2, new[] { new TransferLine("VAI", "VAI2", 30m) }, "Mượn vải cho đơn KH2");
        Assert.Equal(100m, f.Stock.OnHand("VAI"));                         // nháp chưa đổi tồn
        t.Confirm(id);
        Assert.Equal(70m, f.Stock.OnHand("VAI"));
        Assert.Equal(30m, f.Stock.OnHand("VAI2"));
        Assert.Throws<BusinessException>(() => t.Confirm(id));
        var docs = Db.Query(f.C, "SELECT type,customer_id,status FROM stock_doc WHERE kind='TRANSFER' ORDER BY id", r => (r.GetString(0), r.GetInt64(1), r.GetString(2)));
        Assert.Equal(new[] { ("OUT", f.Cust, "CONFIRMED"), ("IN", kh2, "CONFIRMED") }, docs);
        var hist = new ReportService(f.C, f.Orders, f.Stock).TransactionHistory(new ReportFilter()).Rows;
        Assert.Contains(hist, r => (string)r[2]! == "Chuyển sang khách hàng khác" && (string)r[4]! == "VAI");
        Assert.Contains(hist, r => (string)r[2]! == "Nhận từ khách hàng khác" && (string)r[4]! == "VAI2");
    }

    [Fact]
    public void Transfer_Validation_CustomersCodesUnits()
    {
        var (f, t, kh2) = Setup();
        Assert.Throws<BusinessException>(() => t.Create("X0", f.Cust, f.Cust, new[] { new TransferLine("VAI", "CHI", 1m) }));
        var ex = Assert.Throws<BusinessException>(() => t.Create("X1", f.Cust, kh2, new[] {
            new TransferLine("VAI2", "VAI", 1m),        // mã nguồn không thuộc A, mã đích không thuộc B
            new TransferLine("VAI", "CHI2", 1m),        // khác đơn vị tính
            new TransferLine("VAI", "VAI2", 0m) }));    // số lượng không dương
        Assert.Contains("Dòng 1", ex.Message); Assert.Contains("Dòng 2", ex.Message); Assert.Contains("Dòng 3", ex.Message);
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM material_transfer"));
    }

    [Fact]
    public void Transfer_InsufficientStock_WritesNothing()
    {
        var (f, t, kh2) = Setup();
        var id = t.Create("CH1", f.Cust, kh2, new[] { new TransferLine("VAI", "VAI2", 60m), new TransferLine("VAI", "VAI2", 60m) });
        Assert.Throws<BusinessException>(() => t.Confirm(id));             // tổng 120 > 100
        Assert.Equal(100m, f.Stock.OnHand("VAI")); Assert.Equal(0m, f.Stock.OnHand("VAI2"));
    }

    [Fact]
    public void TransferDocs_CannotBeConfirmedOrDeletedAsNormalDocs_DraftCanBeCancelled()
    {
        var (f, t, kh2) = Setup();
        var id = t.Create("CH1", f.Cust, kh2, new[] { new TransferLine("VAI", "VAI2", 5m) });
        var outId = (long)Db.Scalar(f.C, "SELECT out_doc_id FROM material_transfer WHERE id=" + id)!;
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(outId));
        Assert.Throws<BusinessException>(() => f.Stock.DeleteDraft(outId, "x"));
        t.DeleteDraft(id, "Nhập nhầm");
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM stock_doc WHERE kind='TRANSFER'"));
        var id2 = t.Create("CH2", f.Cust, kh2, new[] { new TransferLine("VAI", "VAI2", 5m) }); t.Confirm(id2);
        Assert.Throws<BusinessException>(() => t.DeleteDraft(id2, "x"));
    }

    [Fact]
    public void TransferredMaterial_CanThenBeUsedForCustomerBOrder()
    {
        var (f, t, kh2) = Setup();
        var grid = (long)Db.Scalar(f.C, "SELECT id FROM size_grid WHERE code='G_SML'")!;
        var p2 = f.Cat.AddProduct(kh2, "MH2", "Mã KH2", grid);
        var v2 = f.Cat.AddVariant(p2, "AO2", "Áo KH2", "Áo", 1);
        f.Bom.AddVersion(f.Bom.CreateBom(v2, BomType.ToCat), new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI2", "*", 1m) });
        var o2 = f.Orders.CreateOrder(p2, "O2", new DateTime(2026, 2, 1), new[] { new OrderLine("S", 20) });
        Assert.Equal(FulfillStatus.Short, f.Stock.CheckOrder(o2, f.Orders).Status);
        t.Confirm(t.Create("CH1", f.Cust, kh2, new[] { new TransferLine("VAI", "VAI2", 20m) }));
        Assert.Equal(FulfillStatus.Enough, f.Stock.CheckOrder(o2, f.Orders).Status);
    }
}

public class NoSizeConversionTests
{
    [Fact]
    public void Variants_UseProductGrid_BreakdownKeepsOrderSize()
    {
        using var f = new Fixture();
        Assert.Equal(f.Grid, (long)Db.Scalar(f.C, "SELECT grid_id FROM variant WHERE id=" + f.VAo)!);
        var d = new DateTime(2026, 1, 1);
        f.Bom.AddVersion(f.Bom.CreateBom(f.VAo, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 1.5m), new BomLineInput("VAI", "120/60", 1.6m) });
        f.Bom.AddVersion(f.Bom.CreateBom(f.VQuan, BomType.ToCat), d, new[] { new BomLineInput("VAI", "*", 1m), new BomLineInput("VAI", "120/60", 1.2m) });
        var o = f.Orders.CreateOrder(f.Prod, "O", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10), new OrderLine("110/60", 10), new OrderLine("120/60", 5) });
        var r = f.Orders.CalculateDemand(o);
        Assert.True(r.IsComplete);
        Assert.Equal(6, r.Breakdown.Count);                                              // 2 chủng loại × 3 size, không gộp size
        Assert.All(r.Breakdown, b => Assert.Contains(b.VariantSize, new[] { "110/56", "110/60", "120/60" }));
        Assert.Equal(64m, r.TotalByMaterial["VAI"]);                                     // áo 20×1.5+5×1.6=38 ; quần 20×1+5×1.2=26
    }

    [Fact]
    public void Migration6_MovesVariantsToProductGrid()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        for (var i = 0; i < 5; i++) Db.Exec(c, Schema.Migrations[i]);
        Db.Exec(c, "PRAGMA user_version=5;");
        Db.Exec(c, @"INSERT INTO customer(id,code,name) VALUES(1,'K','K'); INSERT INTO size_grid(id,code,name) VALUES(1,'G1','G1'),(2,'G2','G2');
                     INSERT INTO product(id,customer_id,code,name,default_grid_id) VALUES(1,1,'P','P',1);
                     INSERT INTO variant(id,product_id,code,name,qty_per_set,grid_id) VALUES(1,1,'AO','Áo','1',2);");
        Db.Migrate(c);
        Assert.Equal(1L, Db.Scalar(c, "SELECT grid_id FROM variant WHERE id=1"));
    }
}
