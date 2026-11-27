using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

public class Fixture : IDisposable
{
    public SqliteConnection C = Db.Open(":memory:");
    public CatalogRepository Cat; public BomRepository Bom; public OrderService Orders; public StockService Stock;
    public long Cust, Prod, VAo, VQuan, Grid;

    public Fixture()
    {
        Cat = new(C); Bom = new(C); Orders = new(C); Stock = new(C);
        Cust = Cat.AddCustomer("KH1", "Khách 1");
        Cat.AddMaterial("VAI", "Vải chính", "m", Cust); Cat.AddMaterial("CHI", "Chỉ", "cuộn", Cust); Cat.AddMaterial("KHOA", "Khóa", "cái", Cust);
        Grid = Cat.AddGrid("G_VOC", "Vóc/Cao", new[] { ("110/56", "110", "56"), ("110/60", "110", "60"), ("120/60", "120", "60") });
        var gSml = Cat.AddGrid("G_SML", "S M L", new[] { ("S", (string?)null, (string?)null), ("M", null, null), ("L", null, null) });
        Prod = Cat.AddProduct(Cust, "MH1", "Bộ đồng phục", Grid);
        VAo = Cat.AddVariant(Prod, "AO", "Áo đỏ", "Áo", 1, gSml);
        VQuan = Cat.AddVariant(Prod, "QUAN", "Quần đen", "Quần", 1, gSml);
        foreach (var (d, ao, q) in new[] { ("110/56", "S", "S"), ("110/60", "S", "M"), ("120/60", "M", "M") })
        { Cat.SetSizeMap(VAo, d, ao); Cat.SetSizeMap(VQuan, d, q); }
    }
    public void Dispose() => C.Dispose();
}

public class DataTests : IClassFixture<Fixture>
{
    readonly Fixture f;
    public DataTests(Fixture fx) => f = fx;

    [Fact]
    public void Migration_SetsUserVersion_AndIsIdempotent()
    {
        using var c = Db.Open(":memory:");
        Assert.Equal((long)Schema.Migrations.Length, Db.Scalar(c, "PRAGMA user_version"));
        Db.Migrate(c);
        Assert.Equal((long)Schema.Migrations.Length, Db.Scalar(c, "PRAGMA user_version"));
    }

    [Fact]
    public void UsedGrid_CannotBeChanged_ButCanBeCopied()
    {
        Assert.True(f.Cat.IsGridUsed(f.Grid));
        Assert.Throws<BusinessException>(() => f.Cat.AddGridItem(f.Grid, "130/60"));
        var copy = f.Cat.CopyGrid(f.Grid, "G_VOC_2", "Vóc/Cao v2");
        f.Cat.AddGridItem(copy, "130/60");
        Assert.Equal(4, f.Cat.GridSizes(copy).Count);
        Assert.Equal(3, f.Cat.GridSizes(f.Grid).Count);
    }

    [Fact]
    public void SizeMap_RejectsSizeOutsideGrid() =>
        Assert.Throws<BusinessException>(() => f.Cat.SetSizeMap(f.VAo, "999", "S"));

    [Fact]
    public void BomImport_AllOrNothing_ReportsRows()
    {
        var bom = f.Bom.CreateBom(f.Prod, "BOM cắt");
        var ex = Assert.Throws<BusinessException>(() => f.Bom.AddVersion(bom, DateTime.Now, new[] {
            new BomLineInput("VAI", "*", 1), new BomLineInput("KHONGCO", "*", 1), new BomLineInput("CHI", "XXL", 1) }));
        Assert.Contains("Dòng 2", ex.Message); Assert.Contains("Dòng 3", ex.Message);
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM bom_version WHERE bom_id=" + bom));
    }

    [Fact]
    public void Order_DemandMatchesHandCalc_AndSnapshotIsImmuneToLaterBomChange()
    {
        var bom = f.Bom.CreateBom(f.Prod, "BOM " + Guid.NewGuid().ToString("N")[..6]);
        var d0 = new DateTime(2026, 1, 1);
        f.Bom.AddVersion(bom, d0, new[] { new BomLineInput("VAI", "*", 1.0m), new BomLineInput("VAI", "M", 1.5m, 0, "QUAN") });
        // đơn: 10 bộ 110/56 -> áo S, quần S ; 5 bộ 110/60 -> áo S, quần M
        var oid = f.Orders.CreateOrder(f.Cust, f.Prod, "DH-" + Guid.NewGuid().ToString("N")[..6], new DateTime(2026, 2, 1),
            new[] { new OrderLine("110/56", 10), new OrderLine("110/60", 5) });
        var before = f.Orders.CalculateDemand(oid);
        Assert.True(before.IsComplete);
        // Áo: 15 × 1 = 15 ; Quần S: 10 × 1 (nền) ; Quần M: 5 × 1.5 (ghi đè) = 7.5 → 32.5
        Assert.Equal(32.5m, before.TotalByMaterial["VAI"]);

        // BOM đổi sau đó: đơn cũ không đổi
        f.Bom.AddVersion(bom, new DateTime(2026, 3, 1), new[] { new BomLineInput("VAI", "*", 9m) });
        Assert.Equal(32.5m, f.Orders.CalculateDemand(oid).TotalByMaterial["VAI"]);
        // Đơn mới sau ngày đó dùng bản mới
        var oid2 = f.Orders.CreateOrder(f.Cust, f.Prod, "DH-" + Guid.NewGuid().ToString("N")[..6], new DateTime(2026, 4, 1),
            new[] { new OrderLine("110/56", 1) });
        Assert.Equal(18m, f.Orders.CalculateDemand(oid2).TotalByMaterial["VAI"]);
    }

    [Fact]
    public void Order_Validation()
    {
        Assert.Throws<BusinessException>(() => f.Orders.CreateOrder(f.Cust, f.Prod, "X1", DateTime.Now, new[] { new OrderLine("NOPE", 1) }));
        Assert.Throws<BusinessException>(() => f.Orders.CreateOrder(f.Cust, f.Prod, "X2", DateTime.Now, new[] { new OrderLine("110/56", -1) }));
        var other = f.Cat.AddCustomer("KH_" + Guid.NewGuid().ToString("N")[..5], "Khác");
        Assert.Throws<BusinessException>(() => f.Orders.CreateOrder(other, f.Prod, "X3", DateTime.Now, new[] { new OrderLine("110/56", 1) }));
    }

    [Fact]
    public void Order_WithoutBom_IsIncomplete_NotEnough()
    {
        using var fx = new Fixture();
        var oid = fx.Orders.CreateOrder(fx.Cust, fx.Prod, "DH1", DateTime.Now, new[] { new OrderLine("110/56", 10) });
        fx.Stock.Confirm(fx.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 1000m) }));
        Assert.Equal(FulfillStatus.Incomplete, fx.Stock.CheckOrder(oid, fx.Orders).Status);
    }
}

public class StockTests
{
    [Fact]
    public void Draft_DoesNotChangeStock_ConfirmDoes_DoubleConfirmBlocked()
    {
        using var f = new Fixture();
        var d = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) });
        Assert.Equal(0m, f.Stock.OnHand("VAI"));
        f.Stock.Confirm(d);
        Assert.Equal(100m, f.Stock.OnHand("VAI"));
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(d));
        Assert.Equal(100m, f.Stock.OnHand("VAI"));
    }

    [Fact]
    public void Out_CannotGoNegative_AndIsAtomic()
    {
        using var f = new Fixture();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 50m), ("CHI", 5m) }));
        var d = f.Stock.CreateDraft("OUT", "X1", new[] { ("VAI", 30m), ("CHI", 6m) }); // dòng 2 vượt tồn
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(d));
        Assert.Equal(50m, f.Stock.OnHand("VAI"));                 // không dòng nào được ghi
        Assert.Equal("DRAFT", Db.Scalar(f.C, "SELECT status FROM stock_doc WHERE id=" + d));
    }

    [Fact]
    public void Out_SplitIntoSameMaterialLines_StillChecksTotal()
    {
        using var f = new Fixture();
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 10m) }));
        var d = f.Stock.CreateDraft("OUT", "X1", new[] { ("VAI", 6m), ("VAI", 6m) });
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(d));
    }

    [Fact]
    public void EmptyAndZeroDocs_AreRejected_NegativeQtyRejected()
    {
        using var f = new Fixture();
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(f.Stock.CreateDraft("IN", "E1", Array.Empty<(string, decimal)>())));
        Assert.Throws<BusinessException>(() => f.Stock.Confirm(f.Stock.CreateDraft("IN", "E2", new[] { ("VAI", 0m) })));
        Assert.Throws<BusinessException>(() => f.Stock.CreateDraft("IN", "E3", new[] { ("VAI", -1m) }));
    }

    [Fact]
    public void DuplicateDocNo_Rejected()
    {
        using var f = new Fixture();
        f.Stock.CreateDraft("IN", "DUP", new[] { ("VAI", 1m) });
        Assert.Throws<SqliteException>(() => f.Stock.CreateDraft("IN", "DUP", new[] { ("VAI", 1m) }));
    }

    [Fact]
    public void Reservation_ReducesAvailabilityForOtherOrders()
    {
        using var f = new Fixture();
        var b = f.Bom.CreateBom(f.Prod, "B");
        f.Bom.AddVersion(b, new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o1 = f.Orders.CreateOrder(f.Cust, f.Prod, "O1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) }); // cần 20 vải
        var o2 = f.Orders.CreateOrder(f.Cust, f.Prod, "O2", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 10) });
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 30m) }));
        f.Stock.Reserve(o1, "VAI", 20m);
        var r = f.Stock.CheckOrder(o2, f.Orders);
        Assert.Equal(FulfillStatus.Short, r.Status);
        Assert.Equal(10m, r.Lines.Single().Available);
        Assert.Equal(10m, r.Lines.Single().Shortage);
        Assert.Equal(FulfillStatus.Enough, f.Stock.CheckOrder(o1, f.Orders).Status);
        Assert.Throws<BusinessException>(() => f.Stock.Reserve(o2, "VAI", 11m));
    }
}

public class PinTests
{
    [Fact]
    public void Pin_SetVerify_AndRules()
    {
        using var c = Db.Open(":memory:");
        var p = new PinService(c);
        Assert.False(p.HasPin);
        Assert.Throws<BusinessException>(() => p.SetPin("12"));
        p.SetPin("2468");
        Assert.True(p.HasPin);
        Assert.True(p.Verify("2468")); Assert.False(p.Verify("1111"));
        Assert.NotEqual("2468", (string)Db.Scalar(c, "SELECT value FROM app_setting WHERE key='pin_hash'")!);
        Assert.Equal(5, p.IdleMinutes);
    }

    [Fact]
    public void AutoLock_LocksAfterIdle_TouchExtends()
    {
        var t = new DateTime(2026, 1, 1, 8, 0, 0);
        var al = new AutoLock(TimeSpan.FromMinutes(5), () => t);
        Assert.True(al.IsLocked);
        al.Unlock();
        t = t.AddMinutes(4); al.Touch(); t = t.AddMinutes(4);
        Assert.False(al.Tick());
        t = t.AddMinutes(2);
        Assert.True(al.Tick());
    }
}
