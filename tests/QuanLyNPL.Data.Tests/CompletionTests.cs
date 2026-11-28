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
        var d = iss.CreateIssueDraft(o, "X1");
        Assert.Throws<BusinessException>(() => f.Orders.SetStatus(o, open: false));      // còn phiếu nháp
        f.Stock.DeleteDraft(d, "x");
        f.Orders.SetStatus(o, open: false);
        Assert.Empty(new Queries(f.C).Reservations(o));
        Assert.Throws<BusinessException>(() => iss.CreateIssueDraft(o, "X2"));
        Assert.Throws<BusinessException>(() => f.Stock.Reserve(o, "VAI", 1m));
        f.Orders.SetStatus(o, open: true);
        iss.CreateIssueDraft(o, "X3");
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
