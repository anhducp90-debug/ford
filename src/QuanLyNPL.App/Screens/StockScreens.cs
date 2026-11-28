using Avalonia.Controls;
using Avalonia.Layout;
using QuanLyNPL.Core;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

internal static class DocText
{
    public static string Type(DocRow d) => d.Kind == TransferService.Kind ? (d.Type == "OUT" ? "Chuyển sang KH khác" : "Nhận từ KH khác") : d.Type switch
    {
        "IN" => "Nhập", "OUT" => d.RefDocId is not null ? "Xuất bù" : d.Scope is not null ? "Cấp phát" : "Xuất", "RETURN" => "Trả",
        "ADJUST_UP" => "Điều chỉnh tăng", "ADJUST_DOWN" => "Điều chỉnh giảm", _ => d.Type
    };
    public static string Status(string s) => s == "DRAFT" ? "Nháp" : "Đã xác nhận";
    public static string Scope(string? s) => s switch { null => "", IssueService.AllScope => "Cả đơn", DemandCalculator.SetScope => "Theo bộ", _ => "Chủng loại " + s };

    public static (string, Func<DocRow, string>, bool)[] Cols => new (string, Func<DocRow, string>, bool)[]
    {
        ("Số phiếu", d => d.DocNo, false), ("Loại", Type, false), ("Khách hàng", d => d.CustomerCode, false), ("Đơn", d => d.OrderNo, false),
        ("Phạm vi", d => Scope(d.Scope), false), ("Trạng thái", d => Status(d.Status), false), ("Tạo lúc", d => Ui.DT(d.CreatedAt), false),
        ("Xác nhận lúc", d => d.ConfirmedAt is DateTime t ? Ui.DT(t) : "", false), ("Ghi chú", d => d.Note ?? "", false)
    };

    public static Control Lines(long docId) =>
        Ui.List(App.X.Q.DocLines(docId), new (string, Func<DocLineRow, string>, bool)[]
        {
            ("Mã NPL", l => l.Material, false), ("Tên", l => l.MaterialName, false), ("ĐVT", l => l.Unit, false),
            ("Đề xuất", l => l.Suggested is decimal s ? Ui.N(s) : "", true), ("Số lượng", l => l.Entered ? Ui.N(l.Qty) : "(chưa nhập)", true)
        }, maxHeight: 300);
}

public sealed class ReceiptScreen : Screen
{
    public override string Title => "Phiếu nhập kho";
    public override string Subtitle => "Mỗi phiếu thuộc một khách hàng. Phiếu nháp không đổi tồn; chỉ khi xác nhận mới ghi sổ. Phiếu đã xác nhận không sửa, chỉ điều chỉnh có lưu vết.";
    DocRow? _sel; long? _cust; string _no = "", _note = "";
    readonly List<LineState> _lines = new();

    protected override Control Build()
    {
        var docs = X.Q.Docs("IN");
        if (_sel is not null) _sel = docs.FirstOrDefault(d => d.Id == _sel.Id);
        var editingDraft = _sel is { Status: "DRAFT", Kind: null };
        if (_no.Length == 0) _no = X.Q.NextNo("NK");
        var custs = Opts.Customers();
        _cust ??= custs.FirstOrDefault()?.Value;
        var no = Ui.Input(140, null, _no); no.TextChanged += (_, _) => _no = no.Text ?? ""; no.IsEnabled = !editingDraft;
        var note = Ui.Input(260, "Ghi chú / nguồn", _note); note.TextChanged += (_, _) => _note = note.Text ?? "";
        var custCb = Ui.Combo(custs, _cust, 260, v => { _cust = v; _lines.RemoveAll(l => l.Mat is not null && X.Cat.MaterialCustomerId(l.Mat) != v); Reload(); });
        custCb.IsEnabled = !editingDraft;
        var editor = new LinesEditor(_lines, Opts.Materials(_cust));

        var form = Ui.Card(editingDraft ? $"Phiếu nháp {_sel!.DocNo}" : "Phiếu nhập mới",
            Ui.Row(Ui.Label("Khách hàng", true), custCb, Ui.Label("Số phiếu", true), no, Ui.Label("Ghi chú"), note),
            editor.View,
            Ui.Row(
                Ui.Btn("Lưu nháp", () => Ui.Try(() => { Save(); Reload(); }, "Đã lưu phiếu nháp."), primary: !editingDraft),
                Ui.BtnAsync("Xác nhận nhập", async () =>
                {
                    var id = Save();
                    var lines = X.Q.DocLines(id);
                    if (!await Dialog.Confirm("Xác nhận nhập kho", $"Xác nhận phiếu {X.Q.Docs("IN").First(d => d.Id == id).DocNo}?\n" +
                        string.Join("\n", lines.Select(l => $"  {l.Material}: +{Ui.N(l.Qty)} {l.Unit}")) + "\n\nSau khi xác nhận, phiếu không sửa được.", "Xác nhận")) { Reload(); return; }
                    X.Stock.Confirm(id);
                    New(); App.Main.Status("Đã xác nhận nhập kho.", "ok"); Reload();
                }, primary: true),
                editingDraft ? Ui.BtnAsync("Hủy phiếu nháp", async () =>
                {
                    if (!await Dialog.Confirm("Hủy phiếu nháp", $"Hủy phiếu nháp {_sel!.DocNo}?", "Hủy phiếu", danger: true)) return;
                    X.Stock.DeleteDraft(_sel.Id, "Hủy phiếu nhập nháp"); New(); Reload();
                }, danger: true) : null,
                Ui.Btn("Phiếu mới", () => { New(); Reload(); })));

        return Ui.Col(form,
            Ui.Card("Danh sách phiếu nhập (chọn để xem / sửa phiếu nháp)",
                Ui.List(docs, DocText.Cols, d =>
                {
                    if (d is null || d.Id == _sel?.Id) return;
                    _sel = d;
                    if (d is { Status: "DRAFT", Kind: null })
                    {
                        _lines.Clear(); _lines.AddRange(X.Q.DocLines(d.Id).Select(l => new LineState { Mat = l.Material, Qty = Ui.N(l.Qty) }));
                        _no = d.DocNo; _note = d.Note ?? ""; _cust = X.Q.Customers().FirstOrDefault(c => c.Code == d.CustomerCode)?.Id ?? _cust;
                    }
                    Reload();
                }, 300, d => d.Id == _sel?.Id)),
            _sel is { } s && !(s is { Status: "DRAFT", Kind: null }) ? Ui.Card($"Chi tiết {s.DocNo} ({DocText.Type(s)} – {DocText.Status(s.Status)})", DocText.Lines(s.Id)) : null);
    }

    long Save()
    {
        var lines = LinesEditor.Read(_lines);
        if (_sel is { Status: "DRAFT", Kind: null } d) { X.Stock.ReplaceDraftLines(d.Id, lines); return d.Id; }
        var id = X.Stock.CreateDraft("IN", _no.Trim(), lines, null, _note, _cust ?? throw new BusinessException("Chọn khách hàng."));
        _sel = X.Q.Docs("IN").First(x => x.Id == id);
        return id;
    }

    void New() { _sel = null; _lines.Clear(); _no = ""; _note = ""; }
}

public sealed class CountScreen : Screen
{
    public override string Title => "Kiểm kê & điều chỉnh tồn";
    public override string Subtitle => "Mỗi phiếu kiểm kê thuộc một khách hàng. Chênh lệch = thực đếm − tồn sổ tại lúc chốt; dương tạo điều chỉnh tăng, âm tạo điều chỉnh giảm, có lưu số trước/sau và lý do.";
    long? _cust; CountRow? _sel; string _reason = "";
    readonly List<LineState> _lines = new();

    protected override Control Build()
    {
        var custs = Opts.Customers();
        _cust ??= custs.FirstOrDefault()?.Value;
        var mats = X.Q.Materials(_cust, true).Where(m => m.CustomerId == _cust)
            .Select(m => new Opt<string>($"{m.Code} – {m.Name} ({m.Unit}) · tồn sổ {Ui.N(X.Stock.OnHand(m.Code))}", m.Code)).ToList();
        var editor = new LinesEditor(_lines, mats, "Thực đếm");
        var reason = Ui.Input(320, "Lý do kiểm kê", _reason); reason.TextChanged += (_, _) => _reason = reason.Text ?? "";
        var counts = X.Q.Counts();
        if (_sel is not null) _sel = counts.FirstOrDefault(c => c.Id == _sel.Id);
        return Ui.Col(
            Ui.Card("Lập phiếu kiểm kê",
                Ui.Row(Ui.Label("Khách hàng", true), Ui.Combo(custs, _cust, 260, v => { _cust = v; _lines.Clear(); Reload(); }),
                    Ui.Btn("Nạp toàn bộ NPL của khách hàng (thực đếm = tồn sổ)", () =>
                    {
                        _lines.Clear();
                        _lines.AddRange(X.Q.Materials(_cust, true).Where(m => m.CustomerId == _cust).Select(m => new LineState { Mat = m.Code, Qty = Ui.N(X.Stock.OnHand(m.Code)) }));
                        Reload();
                    })),
                editor.View,
                Ui.Row(Ui.Label("Lý do"), reason,
                    Ui.Btn("Lưu phiếu kiểm kê (nháp)", () => Ui.Try(() =>
                    {
                        var id = X.Count.Create(X.Q.NextNo("KK"), LinesEditor.Read(_lines, "thực đếm"), _reason, _cust);
                        _lines.Clear(); _reason = ""; _sel = X.Q.Counts().First(c => c.Id == id); Reload();
                    }, "Đã lưu phiếu kiểm kê nháp. Kiểm tra rồi bấm Chốt kiểm kê."), primary: true))),
            Ui.Card("Danh sách phiếu kiểm kê",
                Ui.List(counts, new (string, Func<CountRow, string>, bool)[]
                {
                    ("Số phiếu", c => c.DocNo, false), ("Khách hàng", c => c.CustomerCode, false), ("Trạng thái", c => DocText.Status(c.Status), false),
                    ("Lý do", c => c.Reason ?? "", false), ("Tạo lúc", c => Ui.DT(c.CreatedAt), false)
                }, c => { if (c is not null && c.Id != _sel?.Id) { _sel = c; Reload(); } }, 260, c => c.Id == _sel?.Id)),
            _sel is null ? null : Detail(_sel));
    }

    Control Detail(CountRow s)
    {
        var lines = X.Q.CountLines(s.Id);
        return Ui.Card($"Phiếu {s.DocNo} – {DocText.Status(s.Status)}",
            Ui.List(lines, new (string, Func<CountLineRow, string>, bool)[]
            {
                ("Mã NPL", l => l.Material, false), ("Tên", l => l.MaterialName, false),
                ("Tồn sổ", l => l.Book is decimal b ? Ui.N(b) : Ui.N(X.Stock.OnHand(l.Material)) + " (hiện tại)", true),
                ("Thực đếm", l => Ui.N(l.Counted), true),
                ("Chênh lệch", l => Ui.N(l.Diff ?? l.Counted - X.Stock.OnHand(l.Material)), true)
            }, maxHeight: 300),
            s.Status != "DRAFT" ? null : Ui.Row(
                Ui.BtnAsync("Chốt kiểm kê", async () =>
                {
                    if (!await Dialog.Confirm("Chốt kiểm kê", $"Chốt phiếu {s.DocNo}? Hệ thống tạo phiếu điều chỉnh theo chênh lệch tại thời điểm chốt. Không hoàn tác được.", "Chốt")) return;
                    X.Count.Confirm(s.Id); App.Main.Status("Đã chốt kiểm kê và điều chỉnh tồn.", "ok"); Reload();
                }, primary: true),
                Ui.BtnAsync("Hủy phiếu nháp", async () =>
                {
                    if (!await Dialog.Confirm("Hủy phiếu kiểm kê", $"Hủy phiếu nháp {s.DocNo}?", "Hủy phiếu", danger: true)) return;
                    X.Count.DeleteDraft(s.Id, "Hủy phiếu kiểm kê nháp"); _sel = null; Reload();
                }, danger: true)));
    }
}

public sealed class CheckScreen : Screen
{
    public override string Title => "Kiểm tra khả năng đáp ứng";
    public override string Subtitle => "Đối chiếu nhu cầu của đơn với tồn khả dụng (tồn thực tế − lượng giữ cho đơn khác). Thiếu BOM/định mức thì báo CHƯA ĐỦ DỮ LIỆU, không kết luận đủ.";
    long? _order; bool _onlyShort; string? _mat; string _resQty = "";

    public CheckScreen(long? orderId = null) { _order = orderId; }

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _mat = null; Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = _order.Value;
        var r = X.Stock.CheckOrder(o, X.Orders);
        var d = X.Orders.CalculateDemand(o);
        var table = X.Reports.OrderAvailability(o, _onlyShort);
        var only = new CheckBox { Content = "Chỉ mã thiếu", IsChecked = _onlyShort };
        only.IsCheckedChanged += (_, _) => { _onlyShort = only.IsChecked == true; Reload(); };
        var banner = r.Status switch
        {
            FulfillStatus.Enough => Ui.Banner("ĐỦ NPL – tồn khả dụng đáp ứng toàn bộ nhu cầu.", "ok"),
            FulfillStatus.Short => Ui.Banner($"THIẾU NPL – {r.Lines.Count(l => l.Shortage > 0)} mã thiếu: {string.Join(", ", r.Lines.Where(l => l.Shortage > 0).Select(l => l.MaterialCode))}", "bad"),
            _ => Ui.Banner("CHƯA ĐỦ DỮ LIỆU: " + string.Join("; ", d.Issues.Select(i => i.Message).Distinct()), "warn")
        };
        var max = X.Reports.MaxSets(o).Summary;
        var maxLine = max.Complete
            ? Ui.Banner($"Có thể sản xuất tối đa {Ui.N(max.MaxSets)} / {Ui.N(max.OrderedSets)} bộ theo cơ cấu size của đơn ({Ui.N(Math.Round(max.Ratio * 100, 1))}%)" +
                (max.MaxSets < max.OrderedSets ? $" – giới hạn bởi {string.Join(", ", max.Limiting)}." : "."), max.MaxSets < max.OrderedSets ? "muted" : "ok")
            : null;
        var res = X.Q.Reservations(o);
        ReservationRow? selRes = null;
        var resQty = Ui.Input(100, "Số lượng", _resQty); resQty.TextChanged += (_, _) => _resQty = resQty.Text ?? "";
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb, only, Ui.BtnAsync("Xuất Excel", () => Ui.Export(table, false)), Ui.BtnAsync("Xuất PDF", () => Ui.Export(table, true)),
                Ui.Btn("Tạo phiếu xuất", () => App.Main.Open("Phiếu xuất (cấp phát)", new IssueScreen(o)))),
            banner, maxLine,
            Ui.Row(Ui.Btn("Xem số bộ tối đa theo từng size", () => App.Main.Open("Số bộ tối đa sản xuất được", new ReportScreen("max") { OrderId = o }))),
            Ui.Table(table, row => { var m = row?[0] as string; if (m is not null && m != _mat) { _mat = m; Reload(); } }),
            _mat is null ? Ui.Hint("Chọn một mã NPL để xem nhu cầu đến từ BOM / chủng loại / size nào và để giữ kho.") :
            Ui.Card($"Nguồn nhu cầu {_mat}",
                Ui.Table(new ReportTable("", Array.Empty<string>(), new[] { "BOM", "Chủng loại", "Size", "Nhu cầu" },
                    d.Sources.Where(s => s.MaterialCode == _mat).Select(s => new object?[] {
                        X.Orders.LoadSnapshot(o).Boms.FirstOrDefault(b => b.Id == s.BomId)?.Name ?? s.BomId,
                        s.Variant == DemandCalculator.SetScope ? "Cả bộ" : s.Variant, s.VariantSize, Math.Round(s.Qty, 4) }).ToList()), maxHeight: 260),
                Ui.Row(Ui.Label("Giữ kho cho đơn"), resQty, Ui.Btn("Giữ kho", () => Ui.Try(() =>
                {
                    X.Stock.Reserve(o, _mat, Ui.Dec(_resQty, "số lượng giữ")); _resQty = ""; Reload();
                }, "Đã giữ kho cho đơn.")), Ui.Hint("Lượng giữ trừ vào tồn khả dụng của các đơn khác; tự giảm khi xác nhận xuất cho đơn này."))),
            Ui.Card("Đang giữ kho cho đơn",
                Ui.List(res, new (string, Func<ReservationRow, string>, bool)[] { ("Mã NPL", x => x.Material, false), ("Số lượng giữ", x => Ui.N(x.Qty), true) }, x => selRes = x, 200),
                Ui.Row(Ui.Btn("Nhả giữ kho dòng đang chọn", () => Ui.Try(() =>
                {
                    if (selRes is null) throw new BusinessException("Chọn một dòng giữ kho.");
                    X.Stock.ReleaseReservation(selRes.Id); Reload();
                }, "Đã nhả giữ kho.")))));
    }
}

public sealed class IssueScreen : Screen
{
    public override string Title => "Phiếu xuất (cấp phát)";
    public override string Subtitle => "Đề xuất = nhu cầu kỳ − số dư cân đối (cấp thừa giảm, cấp thiếu cộng). Kho nhập thực cấp từng dòng; nhập 0 được giữ nguyên. Chỉ khi xác nhận mới trừ tồn.";
    long? _order; string _scope = IssueService.AllScope; DocRow? _sel;
    readonly Dictionary<string, string> _actual = new();

    public IssueScreen(long? orderId = null) { _order = orderId; }

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _sel = null; _actual.Clear(); Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var scopes = new List<Opt<string>> { new("Cả đơn", IssueService.AllScope), new("Theo bộ (NPL của BOM theo bộ)", DemandCalculator.SetScope) };
        scopes.AddRange(X.Q.Variants(o.ProductId).Select(v => new Opt<string>("Chủng loại " + v.Code + " – " + v.Name, v.Code)));
        var docs = X.Q.Docs("OUT", o.Id).Where(d => d.Scope is not null && d.RefDocId is null).ToList();
        _sel = _sel is not null ? docs.FirstOrDefault(d => d.Id == _sel.Id) : docs.FirstOrDefault(d => d.Status == "DRAFT");
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb, Ui.Label("Phạm vi cấp"), Ui.Combo(scopes, _scope, 280, v => _scope = v),
                Ui.Btn("Tạo phiếu xuất nháp", () => Ui.Try(() =>
                {
                    var id = X.Issue.CreateIssueDraft(o.Id, X.Q.NextNo("PX"), _scope);
                    _sel = X.Q.Docs("OUT", o.Id).First(d => d.Id == id); _actual.Clear(); Reload();
                }, "Đã tạo phiếu xuất nháp theo nhu cầu còn lại."), primary: true)),
            Ui.Hint("Không trộn phạm vi \"Cả đơn\" với phạm vi chủng loại/bộ trên cùng một đơn. Mỗi lúc chỉ có một phiếu nháp cho đơn."),
            Ui.Card("Phiếu cấp phát của đơn", Ui.List(docs, DocText.Cols, d => { if (d is not null && d.Id != _sel?.Id) { _sel = d; _actual.Clear(); Reload(); } }, 240, d => d.Id == _sel?.Id)),
            _sel is null ? null : _sel.Status == "DRAFT" ? DraftCard(_sel) : Ui.Card($"Phiếu {_sel.DocNo} – đã xác nhận", DocText.Lines(_sel.Id)));
    }

    Control DraftCard(DocRow d)
    {
        var lines = X.Q.DocLines(d.Id);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,240,110,130,130,*"), RowDefinitions = new RowDefinitions() };
        void Cell(Control c, int row, int col) { c.Margin = new Avalonia.Thickness(0, 2, 10, 2); Grid.SetRow(c, row); Grid.SetColumn(c, col); grid.Children.Add(c); }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var hdr = new[] { "Mã NPL", "Tên", "Đề xuất", "Tồn khả dụng", "Thực cấp", "" };
        for (var i = 0; i < hdr.Length; i++) Cell(Ui.Text(hdr[i], Ui.Muted, true), 0, i);
        var r = 1;
        foreach (var l in lines)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (!_actual.ContainsKey(l.Material)) _actual[l.Material] = l.Entered ? Ui.N(l.Qty) : "";
            var avail = X.Stock.OnHand(l.Material) - X.Stock.ReservedForOthers(l.Material, d.OrderId);
            var tb = Ui.Input(110, "chưa nhập", _actual[l.Material]); tb.HorizontalContentAlignment = HorizontalAlignment.Right;
            var mat = l.Material; tb.TextChanged += (_, _) => _actual[mat] = tb.Text ?? "";
            Cell(Ui.Text(l.Material), r, 0); Cell(Ui.Text(l.MaterialName), r, 1); Cell(Ui.Text(Ui.N(l.Suggested ?? 0)), r, 2);
            Cell(Ui.Text(Ui.N(avail), avail < (l.Suggested ?? 0) ? Ui.Bad : null), r, 3); Cell(tb, r, 4);
            Cell(Ui.Text(l.Entered ? "" : "chưa nhập", Ui.Warn), r, 5);
            r++;
        }
        List<string> SaveActual()
        {
            var warns = new List<string>();
            foreach (var (m, v) in _actual.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)))
                warns.AddRange(X.Issue.SetActual(d.Id, m, Ui.Dec(v, "thực cấp " + m)));
            return warns;
        }
        return Ui.Card($"Phiếu nháp {d.DocNo} – {DocText.Scope(d.Scope)}", grid,
            Ui.Row(
                Ui.BtnAsync("Lưu thực cấp", async () =>
                {
                    var w = SaveActual(); Reload();
                    if (w.Count > 0) await Dialog.Info("Cảnh báo", string.Join("\n", w)); else App.Main.Status("Đã lưu thực cấp.", "ok");
                }),
                Ui.BtnAsync("Xác nhận xuất", async () =>
                {
                    var w = SaveActual();
                    var msg = $"Xác nhận phiếu {d.DocNo}? Hệ thống trừ tồn và ghi sổ cân đối cấp phát. Không sửa được sau khi xác nhận." + (w.Count > 0 ? "\n\nCảnh báo:\n" + string.Join("\n", w) : "");
                    if (!await Dialog.Confirm("Xác nhận xuất", msg, "Xác nhận")) { Reload(); return; }
                    X.Stock.Confirm(d.Id); _actual.Clear(); App.Main.Status("Đã xác nhận xuất kho.", "ok"); Reload();
                }, primary: true),
                Ui.BtnAsync("Hủy phiếu nháp", async () =>
                {
                    if (!await Dialog.Confirm("Hủy phiếu nháp", $"Hủy phiếu nháp {d.DocNo}?", "Hủy phiếu", danger: true)) return;
                    X.Stock.DeleteDraft(d.Id, "Hủy phiếu xuất nháp"); _sel = null; _actual.Clear(); Reload();
                }, danger: true)));
    }
}

public sealed class ReturnScreen : Screen
{
    public override string Title => "Trả NPL & xuất bù";
    public override string Subtitle => "Trả NPL theo đơn: khi xác nhận vừa tăng tồn vừa giảm lượng đã cấp (một lần, không tính hai lần). Xuất bù: phiếu bổ sung liên kết phiếu cấp gốc đã xác nhận.";
    long? _order; string _scope = IssueService.AllScope; long? _src; DocRow? _sel;
    readonly List<LineState> _ret = new(), _sup = new();

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _sel = null; _src = null; _ret.Clear(); _sup.Clear(); Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var mats = Opts.Materials(o.CustomerId);
        var scopes = new List<Opt<string>> { new("Cả đơn", IssueService.AllScope), new("Theo bộ", DemandCalculator.SetScope) };
        scopes.AddRange(X.Q.Variants(o.ProductId).Select(v => new Opt<string>("Chủng loại " + v.Code, v.Code)));
        var sources = X.Q.Docs("OUT", o.Id, "CONFIRMED").Where(d => d.Scope is not null && d.RefDocId is null).ToList();
        _src ??= sources.FirstOrDefault()?.Id;
        var docs = X.Q.Docs(null, o.Id).Where(d => d.Type == "RETURN" || (d.Type == "OUT" && d.RefDocId is not null)).ToList();
        if (_sel is not null) _sel = docs.FirstOrDefault(d => d.Id == _sel.Id);
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb),
            Ui.Card("Phiếu trả NPL",
                Ui.Row(Ui.Label("Phạm vi"), Ui.Combo(scopes, _scope, 240, v => _scope = v)),
                new LinesEditor(_ret, mats, "Số lượng trả").View,
                Ui.Row(Ui.Btn("Tạo phiếu trả nháp", () => Ui.Try(() =>
                {
                    var id = X.Issue.CreateReturn(o.Id, X.Q.NextNo("TR"), LinesEditor.Read(_ret, "số lượng trả"), _scope);
                    _ret.Clear(); _sel = X.Q.Docs(null, o.Id).First(d => d.Id == id); Reload();
                }, "Đã tạo phiếu trả nháp."), primary: true))),
            Ui.Card("Phiếu xuất bù",
                sources.Count == 0 ? Ui.Hint("Đơn chưa có phiếu cấp phát đã xác nhận để xuất bù.") :
                Ui.Row(Ui.Label("Phiếu cấp gốc"), Ui.Combo(sources.Select(s => new Opt<long?>($"{s.DocNo} ({DocText.Scope(s.Scope)})", s.Id)), _src, 260, v => _src = v)),
                new LinesEditor(_sup, mats, "Số lượng bù").View,
                Ui.Row(Ui.Btn("Tạo phiếu xuất bù nháp", () => Ui.Try(() =>
                {
                    var id = X.Issue.CreateSupplement(_src ?? throw new BusinessException("Chọn phiếu cấp gốc."), X.Q.NextNo("XB"), LinesEditor.Read(_sup, "số lượng bù"));
                    _sup.Clear(); _sel = X.Q.Docs(null, o.Id).First(d => d.Id == id); Reload();
                }, "Đã tạo phiếu xuất bù nháp."), primary: true))),
            Ui.Card("Phiếu trả / xuất bù của đơn", Ui.List(docs, DocText.Cols, d => { if (d is not null && d.Id != _sel?.Id) { _sel = d; Reload(); } }, 240, d => d.Id == _sel?.Id)),
            _sel is null ? null : Ui.Card($"{DocText.Type(_sel)} {_sel.DocNo} – {DocText.Status(_sel.Status)}", DocText.Lines(_sel.Id),
                _sel.Status != "DRAFT" ? null : Ui.Row(
                    Ui.BtnAsync("Xác nhận", async () =>
                    {
                        if (!await Dialog.Confirm("Xác nhận phiếu", $"Xác nhận {DocText.Type(_sel)} {_sel.DocNo}? Không sửa được sau khi xác nhận.", "Xác nhận")) return;
                        X.Stock.Confirm(_sel.Id); App.Main.Status("Đã xác nhận phiếu.", "ok"); Reload();
                    }, primary: true),
                    Ui.BtnAsync("Hủy phiếu nháp", async () =>
                    {
                        if (!await Dialog.Confirm("Hủy phiếu nháp", $"Hủy phiếu nháp {_sel.DocNo}?", "Hủy phiếu", danger: true)) return;
                        X.Stock.DeleteDraft(_sel.Id, "Hủy phiếu nháp"); _sel = null; Reload();
                    }, danger: true))));
    }
}

public sealed class TransferScreen : Screen
{
    public override string Title => "Chuyển NPL giữa khách hàng";
    public override string Subtitle => "Dùng NPL của khách hàng A cho khách hàng B: A xuất theo mã NPL của A, B nhập theo mã NPL của B; hai phiếu xác nhận cùng một giao dịch. Hoàn lại thì lập phiếu chuyển ngược.";
    long? _from, _to; string _reason = ""; TransferRow? _sel;
    readonly List<LineState> _lines = new();

    protected override Control Build()
    {
        var custs = Opts.Customers();
        _from ??= custs.FirstOrDefault()?.Value; _to ??= custs.Skip(1).FirstOrDefault()?.Value ?? _from;
        var reason = Ui.Input(320, "Lý do", _reason); reason.TextChanged += (_, _) => _reason = reason.Text ?? "";
        var transfers = X.Q.Transfers();
        if (_sel is not null) _sel = transfers.FirstOrDefault(t => t.Id == _sel.Id);
        return Ui.Col(
            Ui.Card("Lập phiếu chuyển",
                Ui.Row(Ui.Label("Từ khách hàng (A)", true), Ui.Combo(custs, _from, 240, v => { _from = v; _lines.Clear(); Reload(); }),
                    Ui.Label("Sang khách hàng (B)", true), Ui.Combo(custs, _to, 240, v => { _to = v; _lines.Clear(); Reload(); })),
                new LinesEditor(_lines, Opts.Materials(_from), "Số lượng", mats2: Opts.Materials(_to), matLabel: "Mã NPL của A (xuất)", mat2Label: "Mã NPL của B (nhập)").View,
                Ui.Row(Ui.Label("Lý do"), reason, Ui.Btn("Tạo phiếu chuyển nháp", () => Ui.Try(() =>
                {
                    var lines = _lines.Where(l => !string.IsNullOrEmpty(l.Mat)).Select((l, i) => new TransferLine(l.Mat!,
                        l.Mat2 ?? throw new BusinessException($"Dòng {i + 1}: chưa chọn mã NPL của B."), Ui.Dec(l.Qty, $"số lượng dòng {i + 1}"))).ToList();
                    var id = X.Transfer.Create(X.Q.NextNo("CH"), _from ?? 0, _to ?? 0, lines, _reason);
                    _lines.Clear(); _reason = ""; _sel = X.Q.Transfers().First(t => t.Id == id); Reload();
                }, "Đã tạo phiếu chuyển nháp."), primary: true))),
            Ui.Card("Danh sách phiếu chuyển",
                Ui.List(transfers, new (string, Func<TransferRow, string>, bool)[]
                {
                    ("Số phiếu", t => t.DocNo, false), ("Từ", t => t.FromCustomer, false), ("Sang", t => t.ToCustomer, false), ("Trạng thái", t => DocText.Status(t.Status), false),
                    ("Lý do", t => t.Reason ?? "", false), ("Tạo lúc", t => Ui.DT(t.CreatedAt), false)
                }, t => { if (t is not null && t.Id != _sel?.Id) { _sel = t; Reload(); } }, 240, t => t.Id == _sel?.Id)),
            _sel is null ? null : Ui.Card($"Phiếu {_sel.DocNo}: {_sel.FromCustomer} → {_sel.ToCustomer} – {DocText.Status(_sel.Status)}",
                Ui.List(X.Q.TransferLines(_sel.Id), new (string, Func<TransferLineRow, string>, bool)[]
                {
                    ("A xuất (mã của A)", l => l.From, false), ("B nhập (mã của B)", l => l.To, false), ("Số lượng", l => Ui.N(l.Qty), true),
                    ("Tồn khả dụng của A", l => Ui.N(X.Stock.OnHand(l.From) - X.Stock.ReservedForOthers(l.From, null)), true)
                }, maxHeight: 240),
                _sel.Status != "DRAFT" ? null : Ui.Row(
                    Ui.BtnAsync("Xác nhận chuyển", async () =>
                    {
                        if (!await Dialog.Confirm("Xác nhận chuyển NPL", $"Xác nhận phiếu {_sel.DocNo}: {_sel.FromCustomer} xuất, {_sel.ToCustomer} nhập trong cùng một giao dịch?", "Xác nhận")) return;
                        X.Transfer.Confirm(_sel.Id); App.Main.Status("Đã xác nhận chuyển NPL.", "ok"); Reload();
                    }, primary: true),
                    Ui.BtnAsync("Hủy phiếu nháp", async () =>
                    {
                        if (!await Dialog.Confirm("Hủy phiếu chuyển", $"Hủy phiếu nháp {_sel.DocNo}?", "Hủy phiếu", danger: true)) return;
                        X.Transfer.DeleteDraft(_sel.Id, "Hủy phiếu chuyển nháp"); _sel = null; Reload();
                    }, danger: true))));
    }
}

public sealed class CarryScreen : Screen
{
    public override string Title => "Cân đối cấp phát";
    public override string Subtitle => "Số dư cuối = số dư đầu + thực cấp − nhu cầu kỳ. Dương: cấp thừa (giảm nhu cầu lần sau); âm: cấp thiếu (cộng vào nhu cầu lần sau). Hệ thống đọc số dư cuối mới nhất, không cộng dồn lịch sử.";
    long? _order;

    protected override Control Build()
    {
        var orders = Opts.Orders(openOnly: false); orders.Insert(0, new Opt<long?>("Tất cả đơn", null));
        return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), Ui.Combo(orders, _order, 380, v => { _order = v; Reload(); })),
            Ui.List(X.Q.Carry(_order), new (string, Func<CarryRow, string>, bool)[]
            {
                ("Đơn", r => r.OrderNo, false), ("Mã NPL", r => r.Material, false), ("Phạm vi", r => DocText.Scope(r.Scope), false),
                ("Số dư đầu", r => Ui.N(r.Opening), true), ("Thực cấp", r => Ui.N(r.Issued), true), ("Nhu cầu kỳ", r => Ui.N(r.Demand), true),
                ("Số dư cuối", r => Ui.N(r.Closing), true), ("Tình trạng", r => r.Closing > 0 ? "Cấp thừa" : r.Closing < 0 ? "Cấp thiếu" : "Cân đối", false),
                ("Phiếu", r => r.DocNo, false), ("Thời điểm", r => Ui.DT(r.At), false)
            }, maxHeight: 620));
    }
}
