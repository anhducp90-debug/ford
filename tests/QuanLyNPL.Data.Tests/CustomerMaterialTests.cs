using Microsoft.Data.Sqlite;
using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

public class CustomerMaterialTests
{
    [Fact]
    public void Material_RequiresExistingActiveCustomer()
    {
        using var f = new Fixture();
        Assert.Throws<BusinessException>(() => f.Cat.AddMaterial("X1", "x", "m", 9999));
        var kh2 = f.Cat.AddCustomer("KH2", "Khách 2");
        Db.Exec(f.C, "UPDATE customer SET active=0 WHERE id=" + kh2);
        Assert.Throws<BusinessException>(() => f.Cat.AddMaterial("X2", "x", "m", kh2));
        Assert.Equal(f.Cust, f.Cat.MaterialCustomerId("VAI"));
    }

    [Fact]
    public void Bom_RejectsMaterialOfOtherCustomer_AndUnassigned()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "Khách 2");
        f.Cat.AddMaterial("VAI_KH2", "Vải khách 2", "m", kh2);
        Db.Exec(f.C, "INSERT INTO material(code,name,unit) VALUES('CU','Cũ','m')");   // dữ liệu cũ chưa gán khách hàng
        var bom = f.Bom.CreateBom(f.Prod, "B");
        var ex = Assert.Throws<BusinessException>(() => f.Bom.AddVersion(bom, DateTime.Now, new[] {
            new BomLineInput("VAI", "*", 1m), new BomLineInput("VAI_KH2", "*", 1m), new BomLineInput("CU", "*", 1m) }));
        Assert.Contains("Dòng 2", ex.Message); Assert.Contains("khách hàng khác", ex.Message);
        Assert.Contains("Dòng 3", ex.Message); Assert.Contains("chưa gán khách hàng", ex.Message);
        Assert.Equal(0L, Db.Scalar(f.C, "SELECT COUNT(*) FROM bom_version"));
    }

    [Fact]
    public void OrderDocs_OnlyUseCustomerMaterials()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "Khách 2");
        f.Cat.AddMaterial("VAI_KH2", "Vải khách 2", "m", kh2);
        var b = f.Bom.CreateBom(f.Prod, "B");
        f.Bom.AddVersion(b, new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        var o = f.Orders.CreateOrder(f.Cust, f.Prod, "O1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", 1) });
        Assert.Throws<BusinessException>(() => f.Stock.CreateDraft("RETURN", "R1", new[] { ("VAI_KH2", 1m) }, o));
        f.Stock.CreateDraft("IN", "N1", new[] { ("VAI_KH2", 5m) });      // nhập kho không gắn đơn: tự do
        f.Stock.CreateDraft("RETURN", "R2", new[] { ("VAI", 1m) }, o);   // NPL cùng khách hàng: hợp lệ
    }

    [Fact]
    public void AssignCustomer_OnlyWhenUnusedOrUnassigned_AndLegacyListed()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "Khách 2");
        Db.Exec(f.C, "INSERT INTO material(code,name,unit) VALUES('CU','Cũ','m')");
        Assert.Equal(new[] { "CU" }, f.Cat.UnassignedMaterials());
        f.Cat.AssignMaterialCustomer("CU", f.Cust);                     // chưa có khách hàng → gán được
        Assert.Empty(f.Cat.UnassignedMaterials());
        f.Cat.AssignMaterialCustomer("CU", kh2);                        // chưa dùng → đổi được
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("CU", 3m) }));
        Assert.Throws<BusinessException>(() => f.Cat.AssignMaterialCustomer("CU", f.Cust));   // đã dùng → không đổi
        Assert.Throws<BusinessException>(() => f.Cat.AssignMaterialCustomer("KHONGCO", f.Cust));
    }

    [Fact]
    public void Reports_ShowCustomer_AndFilterByIt()
    {
        using var f = new Fixture();
        var kh2 = f.Cat.AddCustomer("KH2", "Khách 2");
        f.Cat.AddMaterial("VAI_KH2", "Vải khách 2", "m", kh2);
        f.Stock.Confirm(f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 10m), ("VAI_KH2", 7m) }));
        var rep = new ReportService(f.C, f.Orders, f.Stock);
        var all = rep.CurrentStock().Rows;
        Assert.Equal(4, all.Count);
        var only2 = rep.CurrentStock(new ReportFilter(CustomerId: kh2)).Rows.Single();
        Assert.Equal("VAI_KH2", only2[0]); Assert.Equal("KH2", only2[2]);
        Assert.Single(rep.TransactionHistory(new ReportFilter(CustomerId: kh2)).Rows);
        Assert.Single(rep.InOutStock(new ReportFilter(DateTime.Today, DateTime.Today, CustomerId: kh2)).Rows);
    }

    [Fact]
    public void Migration3_UpgradesV2Database_LeavingMaterialsUnassigned()
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        Db.Exec(c, "PRAGMA foreign_keys=ON;");
        Db.Exec(c, Schema.Migrations[0]); Db.Exec(c, Schema.Migrations[1]); Db.Exec(c, "PRAGMA user_version=2;");
        Db.Exec(c, "INSERT INTO material(code,name,unit) VALUES('M','m','m')");
        Db.Migrate(c);
        Assert.Equal(new[] { "M" }, new CatalogRepository(c).UnassignedMaterials());
    }
}
