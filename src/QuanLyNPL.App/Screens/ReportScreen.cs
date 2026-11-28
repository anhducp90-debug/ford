using Avalonia.Controls;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

public sealed class ReportScreen : Screen
{
    public static readonly (string Id, string Title)[] Kinds =
    {
        ("stock", "Tồn hiện tại"), ("inout", "Nhập – xuất – tồn"), ("norm", "Định mức một bộ"), ("demand", "Nhu cầu theo đơn"),
        ("short", "Cảnh báo thiếu"), ("max", "Số bộ tối đa sản xuất được"), ("fulfil", "Đáp ứng đơn hàng"), ("hist", "Lịch sử giao dịch"), ("cfg", "Lịch sử cấu hình")
    };

    readonly string _kind;
    DateTime? _from, _to; long? _cust; long? _order; string? _mat; bool _onlyShort, _detail;
    public long? ProductId { get; set; }
    public long? OrderId { get => _order; set => _order = value; }
    DateTime _asOf = DateTime.Today;

    public ReportScreen(string kind)
    {
        _kind = kind;
        if (kind is "inout" or "hist") { _from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); _to = DateTime.Today; }
    }

    public override string Title => Kinds.First(k => k.Id == _kind).Title;
    public override string Subtitle => _kind switch
    {
        "norm" => "Lượng NPL cho 1 bộ theo từng size = Σ (SL chủng loại trong bộ × định mức × (1 + hao hụt)) + định mức BOM theo bộ. Chọn \"Chi tiết nguồn\" để xem từng chủng loại/BOM.",
        "inout" => "Tồn cuối = tồn đầu + nhập − xuất + trả + điều chỉnh (gồm cả chuyển NPL giữa khách hàng).",
        "short" => "Nhu cầu của đơn so với tồn khả dụng (đã trừ lượng giữ cho đơn khác).",
        "max" => "Theo cơ cấu size của đơn: số bộ tối đa = tổng số bộ × min(tồn khả dụng / nhu cầu) của mọi mã NPL. Riêng từng size: số bộ tối đa nếu chỉ sản xuất size đó (các size không cộng với nhau).",
        _ => ""
    };

    protected override Control Build()
    {
        var filters = Ui.Row();
        Control F(string label, Control c) => new StackPanel { Children = { Ui.Text(label, Ui.Muted), c } };
        DatePickerFilter();
        void DatePickerFilter()
        {
            if (_kind is not ("inout" or "hist" or "fulfil")) return;
            var f = Ui.DateBox(_from); f.SelectedDateChanged += (_, _) => { if (f.SelectedDate?.Date != _from?.Date) { _from = f.SelectedDate?.Date; Reload(); } };
            var t = Ui.DateBox(_to); t.SelectedDateChanged += (_, _) => { if (t.SelectedDate?.Date != _to?.Date) { _to = t.SelectedDate?.Date; Reload(); } };
            filters.Children.Add(F("Từ ngày", f)); filters.Children.Add(F("Đến ngày", t));
        }
        if (_kind is "stock" or "inout" or "fulfil" or "hist")
            filters.Children.Add(F("Khách hàng", Ui.Combo(Opts.Customers(false, "Tất cả"), _cust, 220, v => { _cust = v; _mat = null; Reload(); })));
        if (_kind is "stock" or "inout" or "hist")
        {
            var mats = Opts.Materials(_cust, false); mats.Insert(0, new Opt<string>("Tất cả NPL", ""));
            filters.Children.Add(F("Mã NPL", Ui.Combo(mats, _mat ?? "", 260, v => { _mat = v == "" ? null : v; Reload(); })));
        }
        if (_kind is "demand" or "short" or "cfg" or "hist" or "max")
        {
            var orders = Opts.Orders(openOnly: false);
            if (_kind == "hist") orders.Insert(0, new Opt<long?>("Tất cả đơn", null));
            else _order ??= orders.FirstOrDefault()?.Value;
            filters.Children.Add(F("Đơn hàng", Ui.Combo(orders, _order, 320, v => { _order = v; Reload(); })));
        }
        if (_kind == "norm")
        {
            var prods = Opts.Products(false);
            ProductId ??= prods.FirstOrDefault()?.Value;
            filters.Children.Add(F("Mã hàng", Ui.Combo(prods, ProductId, 320, v => { ProductId = v; Reload(); })));
            var d = Ui.DateBox(_asOf); d.SelectedDateChanged += (_, _) => { var v = (d.SelectedDate ?? DateTime.Today).Date; if (v != _asOf.Date) { _asOf = v; Reload(); } };
            filters.Children.Add(F("BOM hiệu lực ngày", d));
        }
        if (_kind is "short" or "fulfil")
        {
            var cb = new CheckBox { Content = _kind == "short" ? "Chỉ mã thiếu" : "Chỉ đơn thiếu", IsChecked = _onlyShort };
            cb.IsCheckedChanged += (_, _) => { _onlyShort = cb.IsChecked == true; Reload(); };
            filters.Children.Add(F(" ", cb));
        }
        if (_kind is "norm" or "demand" or "max")
        {
            var cb = new CheckBox { Content = _kind == "max" ? "Riêng từng size" : "Chi tiết nguồn", IsChecked = _detail };
            cb.IsCheckedChanged += (_, _) => { _detail = cb.IsChecked == true; Reload(); };
            filters.Children.Add(F(" ", cb));
        }

        ReportTable? t = null; string? empty = null;
        var f = new ReportFilter(_from, _to, _cust, null, _order, _mat, _onlyShort);
        switch (_kind)
        {
            case "stock": t = X.Reports.CurrentStock(f); break;
            case "inout": t = X.Reports.InOutStock(f); break;
            case "fulfil": t = X.Reports.Fulfillment(f with { OrderId = null }); break;
            case "hist": t = X.Reports.TransactionHistory(f); break;
            case "demand": if (_order is long o1) t = _detail ? X.Reports.OrderDemandDetail(o1) : X.Reports.OrderDemand(o1); else empty = "Chưa có đơn hàng."; break;
            case "short": if (_order is long o2) t = X.Reports.OrderAvailability(o2, _onlyShort); else empty = "Chưa có đơn hàng."; break;
            case "max":
                if (_order is long o4) { var (mix, bySize, _) = X.Reports.MaxSets(o4); t = _detail ? bySize : mix; } else empty = "Chưa có đơn hàng."; break;
            case "cfg": if (_order is long o3) t = X.Reports.ConfigHistory(o3); else empty = "Chưa có đơn hàng."; break;
            case "norm":
                if (ProductId is long p) { var (sum, det, _) = X.Reports.PerSetNorm(p, _asOf.Date.AddDays(1).AddSeconds(-1)); t = _detail ? det : sum; }
                else empty = "Chưa có mã hàng."; break;
        }
        if (t is null) return Ui.Col(filters, Ui.Banner(empty ?? "Không có dữ liệu.", "warn"));
        var notes = t.Filters.Where(x => x.StartsWith("CHƯA ĐỦ") || x.StartsWith("Trạng thái") || x.StartsWith("Theo cơ cấu")).ToList();
        return Ui.Col(filters,
            Ui.Row(Ui.BtnAsync("Xuất Excel", () => Ui.Export(t, false), primary: true), Ui.BtnAsync("Xuất PDF", () => Ui.Export(t, true)),
                Ui.Text($"{t.Rows.Count} dòng", Ui.Muted)),
            notes.Count > 0 ? Ui.Banner(string.Join("\n", notes), notes.Any(n => n.Contains("THIẾU") || n.StartsWith("CHƯA")) ? "warn" : "muted") : null,
            t.Rows.Count == 0 ? Ui.Hint("Không có dòng nào.") : Ui.Table(t, maxHeight: 600));
    }
}
