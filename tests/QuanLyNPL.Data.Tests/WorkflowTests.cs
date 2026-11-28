using QuanLyNPL.Core;
using QuanLyNPL.Data;
using Xunit;

/// <summary>Đợt 1–3: việc cần làm, 7 chặng đơn hàng, sửa danh mục, chép BOM, phiếu nhập theo hóa đơn / Excel, quy đổi kg → m.</summary>
public class WorkflowTests
{
    static (Fixture f, long o) Order(decimal sets = 10)
    {
        var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1m) });
        return (f, f.Orders.CreateOrder(f.Prod, "DH1", new DateTime(2026, 2, 1), new[] { new OrderLine("110/56", sets) }));
    }
    static WorkflowService Wf(Fixture f) => new(f.C, f.Orders, f.Stock, f.Iss);

    [Fact]
    public void Conversion_KgToMeters_StoredWithTrace()
    {
        var f = new Fixture();
        Assert.Equal(3.3333m, CatalogRepository.MetersPerKg(200, 150));       // 1000 / (200 g/m² × 1,5 m)
        f.Cat.SetMaterialConversion("VAI", "kg", 3.2m);
        var m = new Queries(f.C).Materials().Single(x => x.Code == "VAI");
        Assert.Equal(("kg", 3.2m), (m.AltUnit, m.AltFactor));
        Assert.Throws<BusinessException>(() => f.Cat.SetMaterialConversion("VAI", "m", 1m));   // trùng đơn vị tồn
        Assert.Throws<BusinessException>(() => f.Cat.SetMaterialConversion("VAI", "kg", 0m));
        var d = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 0m), ("CHI", 5m) });
        f.Stock.ApplyEntries(d, new[] { ("VAI", 100.5m, "kg", 3.25m) });                   // lô thực tế 3,25 m/kg
        Assert.Throws<BusinessException>(() => f.Stock.ApplyEntries(d, new[] { ("CHI", 1m, "kg", 1m) }));   // CHI không có đơn vị kg
        var l = new Queries(f.C).DocLines(d).Single(x => x.Material == "VAI");
        Assert.Equal(326.63m, l.Qty);                                                     // 100,5 × 3,25 = 326,625 → 326,63
        Assert.Equal("100,5 kg × 3,25 m/kg", l.EntryText);
        f.Stock.Confirm(d);
        Assert.Equal(326.63m, f.Stock.OnHand("VAI"));
        f.Cat.SetMaterialConversion("VAI", null, null);
        Assert.Null(new Queries(f.C).Materials().Single(x => x.Code == "VAI").AltUnit);
    }

    [Fact]
    public void Receipt_InvoiceNo_OnlyWhileDraft()
    {
        var f = new Fixture();
        var d = f.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 5m) });
        f.Stock.SetInvoice(d, " HĐ 0001234 ");
        Assert.Equal("HĐ 0001234", new Queries(f.C).Docs("IN").Single().InvoiceNo);
        f.Stock.Confirm(d);
        Assert.Throws<BusinessException>(() => f.Stock.SetInvoice(d, "x"));
    }

    [Fact]
    public void ReceiptExcel_TemplateRoundTrip_AndErrors()
    {
        var f = new Fixture();
        f.Cat.SetMaterialConversion("VAI", "kg", 3m);
        using var ms = new MemoryStream();
        ReceiptExcel.WriteTemplate(new Queries(f.C).Materials(f.Cust), ms);
        ms.Position = 0;
        using var filled = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook(ms))
        {
            var ws = wb.Worksheet(1);
            var row = ws.RowsUsed().Skip(1).First(r => r.Cell(1).GetString() == "VAI"); row.Cell(2).Value = 120;
            ws.RowsUsed().Skip(1).First(r => r.Cell(1).GetString() == "CHI").Cell(2).Value = "4,5";
            wb.SaveAs(filled);
        }
        filled.Position = 0;
        var lines = ReceiptExcel.Read(filled);
        Assert.Equal(new[] { new ReceiptExcelLine("CHI", 4.5m, null, null), new ReceiptExcelLine("VAI", 120m, "kg", 3m) }, lines.OrderBy(l => l.Material));
    }

    [Fact]
    public void Catalog_Edit_VariantQtyLockedOnceOrdered()
    {
        var (f, _) = Order();
        f.Cat.UpdateCustomer(f.Cust, "Khách hàng mới");
        f.Cat.UpdateProduct(f.Prod, "Bộ đồng phục 2026");
        f.Cat.UpdateVariant(f.VAo, "Áo đỏ tươi", "Áo", 1);                                  // tên đổi được
        Assert.Contains("không đổi SL/bộ", Assert.Throws<BusinessException>(() => f.Cat.UpdateVariant(f.VAo, "Áo", "Áo", 2)).Message);
        Assert.Equal("Áo đỏ tươi", new Queries(f.C).Variants(f.Prod).First().Name);
        Assert.Contains(new Queries(f.C).Audit(), a => a.Action == "Sửa" && a.Entity == "variant");
    }

    [Fact]
    public void CopyBom_ByVariantCode_SameCustomerOnly_DropsMissingSizes()
    {
        var f = new Fixture();
        f.BomAll(new DateTime(2026, 1, 1), new[] { new BomLineInput("VAI", "*", 1.2m), new BomLineInput("VAI", "120/60", 1.4m) });
        f.Bom.AddVersion(f.Bom.CreateSetBom(f.Prod, BomType.DongGoi), new DateTime(2026, 1, 1), new[] { new BomLineInput("KHOA", "*", 1m) });
        var g2 = f.Cat.AddGrid("G2", "Hai size", new[] { ("110/56", (string?)"110", (string?)"56"), ("110/60", "110", "60") });
        var p2 = f.Cat.AddProduct(f.Cust, "MH2", "Mẫu mới", g2);
        f.Cat.AddVariant(p2, "AO", "Áo", "Áo", 1);                                            // chỉ có áo
        var (n, notes) = f.Bom.CopyFromProduct(f.Prod, p2, DateTime.Now);
        Assert.Equal(2, n);                                                                    // BOM tổ cắt áo + đóng gói theo bộ
        Assert.Contains(notes, x => x.Contains("120/60"));                                     // size không có ở lưới đích
        Assert.Contains(notes, x => x.Contains("chủng loại QUAN"));
        var o = f.Orders.CreateOrder(p2, "DH2", DateTime.Today, new[] { new OrderLine("110/56", 10) });
        Assert.True(f.Orders.CalculateDemand(o).IsComplete);
        var kh2 = f.Cat.AddCustomer("KH2", "K2");
        var p3 = f.Cat.AddProduct(kh2, "MH3", "x", g2);
        Assert.Throws<BusinessException>(() => f.Bom.CopyFromProduct(f.Prod, p3, DateTime.Now));
    }

    [Fact]
    public void Setup_And_Todos_FollowTheOperation()
    {
        var f = new Fixture();
        Assert.Contains(Wf(f).Setup(), s => !s.Done && s.Title.Contains("BOM"));
        var (g, o) = Order();
        var wf = Wf(g);
        Assert.Contains(wf.Todos(), t => t.Target == "receipt-short" && t.OrderId == o);           // kho trống → thiếu NPL
        Assert.Equal("active", wf.Stages(o).Single(s => s.Name == "Nhập kho").State);
        g.Stock.Confirm(g.Stock.CreateDraft("IN", "N1", new[] { ("VAI", 100m) }));
        var d = g.IssueDraft(o, "X1", "CAT");
        Assert.Contains(wf.Todos(), t => t.DocId == d && t.Target == "issue");                     // phiếu nháp chờ xác nhận
        Assert.Equal("active", wf.Stages(o).Single(s => s.Name == "Cấp phát").State);
        g.Iss.SetActual(d, "VAI", 22m); g.Stock.Confirm(d);                                         // dư 2
        var dư = wf.Todos().Single(t => t.Target == "return");
        Assert.Equal((o, g.Place("CAT")), (dư.OrderId!.Value, dư.PlaceId!.Value));
        Assert.Contains("VAI 2", dư.Detail);
        Assert.Equal("warn", wf.Stages(o).Single(s => s.Name == "Cân đối").State);
        g.Stock.Confirm(g.Return(o, "R1", new[] { ("VAI", 2m) }, "CAT"));
        Assert.Contains(wf.Todos(), t => t.Target == "settle" && t.OrderId == o);                 // sẵn sàng đóng
        Assert.Equal("active", wf.Stages(o).Single(s => s.Name == "Đóng đơn").State);
        g.Iss.CloseOrder(o);
        Assert.All(wf.Stages(o), s => Assert.Equal("done", s.State));
    }

    [Fact]
    public void Stage_IncompleteBom_IsBad()
    {
        var f = new Fixture();
        var o = f.Orders.CreateOrder(f.Prod, "DH1", DateTime.Today, new[] { new OrderLine("110/56", 5) });
        Assert.Equal("bad", Wf(f).Stages(o)[0].State);
        Assert.Contains(Wf(f).Todos(), t => t.Severity == "bad" && t.OrderId == o);
    }
}
