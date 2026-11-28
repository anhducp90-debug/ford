using Avalonia.Controls;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

internal static class Opts
{
    public static List<Opt<long?>> Customers(bool activeOnly = true, string? all = null)
    {
        var l = App.X.Q.Customers(activeOnly).Select(c => new Opt<long?>($"{c.Code} – {c.Name}", c.Id)).ToList();
        if (all is not null) l.Insert(0, new Opt<long?>(all, null));
        return l;
    }

    public static List<Opt<long?>> Products(bool activeOnly = true, long? customerId = null) =>
        App.X.Q.Products(customerId, activeOnly).Select(p => new Opt<long?>($"{p.Code} – {p.Name} ({p.CustomerCode})", p.Id)).ToList();

    public static List<Opt<string>> Materials(long? customerId, bool activeOnly = true) =>
        App.X.Q.Materials(customerId, activeOnly).Where(m => customerId is null || m.CustomerId == customerId)
            .Select(m => new Opt<string>($"{m.Code} – {m.Name} ({m.Unit})", m.Code)).ToList();

    public static List<Opt<long?>> Orders(bool openOnly = true) =>
        App.X.Q.Orders(status: openOnly ? "OPEN" : null).Select(o => new Opt<long?>($"{o.No} – {o.ProductCode} ({o.CustomerCode}){(o.Status == "OPEN" ? "" : " · đã đóng")}", o.Id)).ToList();
}

public sealed class CustomerScreen : Screen
{
    public override string Title => "Khách hàng";
    public override string Subtitle => "Mã khách hàng không trùng. Khách hàng đã phát sinh dữ liệu không xóa, chỉ chuyển ngừng sử dụng.";
    CustomerRow? _sel;

    protected override Control Build()
    {
        var code = Ui.Input(140, "Mã"); var name = Ui.Input(300, "Tên khách hàng");
        return Ui.Col(
            Ui.Card("Khai báo khách hàng", Ui.Row(Ui.Label("Mã", true), code, Ui.Label("Tên", true), name,
                Ui.Btn("Lưu khách hàng", () => Ui.Try(() => { X.Cat.AddCustomer(Ui.Req(code, "mã"), Ui.Req(name, "tên")); Reload(); }, "Đã lưu khách hàng."), primary: true))),
            Ui.Row(Ui.Btn("Ngừng sử dụng / Dùng lại khách hàng đang chọn", () => Ui.Try(() =>
            {
                if (_sel is null) throw new BusinessException("Chọn một khách hàng trong danh sách.");
                X.Cat.SetCustomerActive(_sel.Id, !_sel.Active); Reload();
            }, "Đã cập nhật trạng thái."))),
            Ui.List(X.Q.Customers(), new (string, Func<CustomerRow, string>, bool)[]
            {
                ("Mã", c => c.Code, false), ("Tên", c => c.Name, false),
                ("Mã hàng", c => X.Q.Products(c.Id).Count.ToString(), true), ("NPL", c => X.Q.Materials(c.Id).Count.ToString(), true),
                ("Trạng thái", c => c.Active ? "Đang dùng" : "Ngừng sử dụng", false)
            }, c => _sel = c));
    }
}

public sealed class MaterialScreen : Screen
{
    public override string Title => "Nguyên phụ liệu";
    public override string Subtitle => "Mỗi NPL thuộc một khách hàng; mã NPL duy nhất toàn hệ thống. Mỗi loại/màu/quy cách cần tồn riêng là một mã riêng.";
    long? _filter; MaterialRow? _sel;

    protected override Control Build()
    {
        var cust = Ui.Combo(Opts.Customers(), _filter);
        var code = Ui.Input(140, "Mã NPL"); var name = Ui.Input(220, "Tên"); var unit = Ui.Input(80, "ĐVT"); var note = Ui.Input(200, "Ghi chú");
        var unassigned = X.Cat.UnassignedMaterials();
        return Ui.Col(
            unassigned.Count > 0 ? Ui.Banner($"Có {unassigned.Count} NPL chưa gán khách hàng: {string.Join(", ", unassigned.Take(10))}. Chọn NPL, chọn khách hàng rồi bấm \"Gán khách hàng\".", "warn") : null,
            Ui.Card("Khai báo / sửa NPL",
                Ui.Row(Ui.Label("Khách hàng", true), cust, Ui.Label("Mã", true), code, Ui.Label("Tên", true), name, Ui.Label("ĐVT", true), unit, Ui.Label("Ghi chú"), note),
                Ui.Row(
                    Ui.Btn("Lưu NPL mới", () => Ui.Try(() =>
                    {
                        var k = Ui.Sel<long?>(cust) ?? throw new BusinessException("Phải chọn khách hàng cho NPL.");
                        X.Cat.AddMaterial(Ui.Req(code, "mã NPL"), Ui.Req(name, "tên"), Ui.Req(unit, "đơn vị tính"), k, note.Text);
                        _filter = k; Reload();
                    }, "Đã lưu NPL."), primary: true),
                    Ui.Btn("Cập nhật tên/ĐVT/ghi chú của NPL đang chọn", () => Ui.Try(() =>
                    {
                        if (_sel is null) throw new BusinessException("Chọn một NPL trong danh sách.");
                        X.Cat.UpdateMaterial(_sel.Code, Ui.Req(name, "tên"), Ui.Req(unit, "đơn vị tính"), note.Text); Reload();
                    }, "Đã cập nhật NPL.")),
                    Ui.Btn("Gán khách hàng", () => Ui.Try(() =>
                    {
                        if (_sel is null) throw new BusinessException("Chọn một NPL trong danh sách.");
                        X.Cat.AssignMaterialCustomer(_sel.Code, Ui.Sel<long?>(cust) ?? throw new BusinessException("Chọn khách hàng.")); Reload();
                    }, "Đã gán khách hàng.")),
                    Ui.Btn("Ngừng sử dụng / Dùng lại", () => Ui.Try(() =>
                    {
                        if (_sel is null) throw new BusinessException("Chọn một NPL trong danh sách.");
                        X.Cat.SetMaterialActive(_sel.Code, !_sel.Active); Reload();
                    }, "Đã cập nhật trạng thái."))),
                Ui.Hint("Chọn một dòng để nạp vào ô nhập khi cần sửa. NPL đã có giao dịch thì không đổi đơn vị tính; đã dùng thì không đổi khách hàng.")),
            Ui.Row(Ui.Label("Lọc theo khách hàng"), Ui.Combo(Opts.Customers(false, "Tất cả khách hàng"), _filter, 260, v => { _filter = v; Reload(); })),
            Ui.List(X.Q.Materials(_filter), new (string, Func<MaterialRow, string>, bool)[]
            {
                ("Mã", m => m.Code, false), ("Tên", m => m.Name, false), ("Khách hàng", m => m.CustomerCode.Length > 0 ? m.CustomerCode : "(chưa gán)", false), ("ĐVT", m => m.Unit, false),
                ("Tồn thực tế", m => Ui.N(X.Stock.OnHand(m.Code)), true), ("Đã giữ", m => Ui.N(X.Stock.ReservedForOthers(m.Code, null)), true),
                ("Trạng thái", m => m.Active ? "Đang dùng" : "Ngừng sử dụng", false), ("Ghi chú", m => m.Note ?? "", false)
            }, m => { _sel = m; if (m is not null) { code.Text = m.Code; name.Text = m.Name; unit.Text = m.Unit; note.Text = m.Note; } }));
    }
}

public sealed class GridScreen : Screen
{
    public override string Title => "Lưới size";
    public override string Subtitle => "Khai báo một lần, dùng chung cho mọi khách hàng và mã hàng. Lưới đã dùng không sửa cấu trúc: sao chép thành lưới mới rồi gán cho mã hàng mới.";
    SizeGridRow? _sel;

    protected override Control Build()
    {
        var code = Ui.Input(140, "Mã lưới"); var name = Ui.Input(220, "Tên lưới");
        var sizes = Ui.Input(460, "Ví dụ: 110/56; 110/60; 120/60 hoặc S; M; L; XL");
        static List<(string, string?, string?)> Parse(string? text) =>
            (text ?? "").Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => { var p = s.Split('/'); return (s, p.Length == 2 ? p[0] : (string?)null, p.Length == 2 ? p[1] : (string?)null); }).ToList();
        return Ui.Col(
            Ui.Card("Khai báo lưới size",
                Ui.Row(Ui.Label("Mã", true), code, Ui.Label("Tên", true), name),
                Ui.Row(Ui.Label("Các size", true), sizes),
                Ui.Hint("Phân cách size bằng dấu chấm phẩy. Size dạng Vóc/Chiều cao (110/56) tự tách thành Vóc 110, Chiều cao 56."),
                Ui.Row(
                    Ui.Btn("Lưu lưới mới", () => Ui.Try(() => { X.Cat.AddGrid(Ui.Req(code, "mã lưới"), Ui.Req(name, "tên lưới"), Parse(sizes.Text)); Reload(); }, "Đã lưu lưới size."), primary: true),
                    Ui.Btn("Sao chép lưới đang chọn thành lưới mới (mã, tên ở trên)", () => Ui.Try(() =>
                    {
                        if (_sel is null) throw new BusinessException("Chọn một lưới trong danh sách.");
                        X.Cat.CopyGrid(_sel.Id, Ui.Req(code, "mã lưới mới"), Ui.Req(name, "tên lưới mới")); Reload();
                    }, "Đã sao chép lưới.")),
                    Ui.Btn("Ngừng sử dụng / Dùng lại", () => Ui.Try(() =>
                    {
                        if (_sel is null) throw new BusinessException("Chọn một lưới trong danh sách.");
                        X.Cat.SetGridActive(_sel.Id, !_sel.Active); Reload();
                    }, "Đã cập nhật trạng thái.")))),
            Ui.List(X.Q.Grids(), new (string, Func<SizeGridRow, string>, bool)[]
            {
                ("Mã", g => g.Code, false), ("Tên", g => g.Name, false), ("Các size", g => g.Sizes, false),
                ("Tình trạng", g => g.Used ? "Đã dùng – khóa cấu trúc" : "Chưa dùng", false), ("Trạng thái", g => g.Active ? "Đang dùng" : "Ngừng sử dụng", false)
            }, g => _sel = g));
    }
}

public sealed class ProductScreen : Screen
{
    public override string Title => "Mã hàng";
    public override string Subtitle => "Chọn khách hàng sở hữu và lưới size. Lưới size dùng chung cho đơn hàng, mọi chủng loại và BOM của mã hàng (hệ thống không quy đổi size).";
    long? _cust; ProductRow? _sel;

    protected override Control Build()
    {
        var cust = Ui.Combo(Opts.Customers(), _cust, onChange: v => _cust = v);
        var code = Ui.Input(140, "Mã hàng"); var name = Ui.Input(240, "Tên");
        var grid = Ui.Combo(X.Q.Grids().Where(g => g.Active).Select(g => new Opt<long?>($"{g.Code} – {g.Name} ({g.Sizes})", g.Id)), null, 360);
        return Ui.Col(
            Ui.Card("Khai báo mã hàng",
                Ui.Row(Ui.Label("Khách hàng", true), cust, Ui.Label("Mã", true), code, Ui.Label("Tên", true), name),
                Ui.Row(Ui.Label("Lưới size", true), grid,
                    Ui.Btn("Lưu mã hàng", () => Ui.Try(() =>
                    {
                        X.Cat.AddProduct(Ui.Sel<long?>(cust) ?? throw new BusinessException("Phải chọn khách hàng cho mã hàng."),
                            Ui.Req(code, "mã hàng"), Ui.Req(name, "tên"), Ui.Sel<long?>(grid) ?? throw new BusinessException("Phải chọn lưới size."));
                        Reload();
                    }, "Đã lưu mã hàng. Tiếp theo: khai báo chủng loại."), primary: true))),
            Ui.Row(Ui.Btn("Ngừng sử dụng / Dùng lại mã hàng đang chọn", () => Ui.Try(() =>
            {
                if (_sel is null) throw new BusinessException("Chọn một mã hàng trong danh sách.");
                X.Cat.SetProductActive(_sel.Id, !_sel.Active); Reload();
            }, "Đã cập nhật trạng thái."))),
            Ui.List(X.Q.Products(), new (string, Func<ProductRow, string>, bool)[]
            {
                ("Mã hàng", p => p.Code, false), ("Tên", p => p.Name, false), ("Khách hàng", p => p.CustomerCode, false), ("Lưới size", p => p.GridCode, false),
                ("Chủng loại", p => p.VariantCount.ToString(), true), ("Trạng thái", p => p.Active ? "Đang dùng" : "Ngừng sử dụng", false)
            }, p => _sel = p));
    }
}

public sealed class VariantScreen : Screen
{
    public override string Title => "Chủng loại";
    public override string Subtitle => "Chọn mã hàng; chủng loại thuộc khách hàng và dùng lưới size của mã hàng. Nhóm Áo/Quần chỉ để phân loại, không gộp chủng loại.";
    long? _prod;

    protected override Control Build()
    {
        var prods = Opts.Products();
        _prod ??= prods.FirstOrDefault()?.Value;
        var prodCb = Ui.Combo(prods, _prod, 320, v => { _prod = v; Reload(); });
        if (_prod is null) return Ui.Col(Ui.Row(Ui.Label("Mã hàng", true), prodCb), Ui.Banner("Chưa có mã hàng. Khai báo mã hàng trước.", "warn"));
        var p = X.Q.Products().Single(x => x.Id == _prod);
        var code = Ui.Input(120, "Mã"); var name = Ui.Input(200, "Tên");
        var group = Ui.Combo(new[] { "Áo", "Quần", "Khác" }.Select(g => new Opt<string>(g, g)), "Áo", 100);
        var qty = Ui.Input(70, null, "1");
        return Ui.Col(
            Ui.Row(Ui.Label("Mã hàng", true), prodCb, Ui.Chip($"Khách hàng: {p.CustomerCode}", "muted"), Ui.Chip($"Lưới size: {p.GridCode} ({string.Join(", ", X.Q.ProductSizes(p.Id))})", "muted")),
            Ui.Card("Khai báo chủng loại",
                Ui.Row(Ui.Label("Mã", true), code, Ui.Label("Tên", true), name, Ui.Label("Nhóm"), group, Ui.Label("SL/bộ", true), qty,
                    Ui.Btn("Lưu chủng loại", () => Ui.Try(() =>
                    {
                        X.Cat.AddVariant(p.Id, Ui.Req(code, "mã chủng loại"), Ui.Req(name, "tên"), Ui.Sel<string>(group) ?? "Khác", Ui.Dec(qty.Text, "SL/bộ")); Reload();
                    }, "Đã lưu chủng loại."), primary: true)),
                Ui.Hint("Định mức theo quy ước size khác (ví dụ áo S, M, L) phải được quy đổi sang size của mã hàng ngoài hệ thống trước khi khai báo BOM.")),
            Ui.Card($"Cấu thành một bộ – {p.Code}",
                Ui.List(X.Q.Variants(p.Id), new (string, Func<VariantRow, string>, bool)[]
                {
                    ("Mã", v => v.Code, false), ("Chủng loại", v => v.Name, false), ("Nhóm", v => v.Group, false), ("SL trong 1 bộ", v => Ui.N(v.QtyPerSet), true)
                }, maxHeight: 300)));
    }
}
