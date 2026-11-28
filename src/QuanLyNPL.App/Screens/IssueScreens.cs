using Avalonia.Controls;
using Avalonia.Layout;
using QuanLyNPL.Core;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

/// <summary>Khóa cân đối: nơi nhận + phạm vi (loại BOM) đã giao việc cho đơn.</summary>
internal static class Keys
{
    public static string Encode(long placeId, string scope) => $"{placeId}|{scope}";
    public static (long place, string scope) Decode(string key) { var p = key.Split('|'); return (long.Parse(p[0]), p[1]); }

    public static List<Opt<string>> ForOrder(long orderId) =>
        App.X.Issue.Assignments(orderId).Select(a => new Opt<string>($"{a.PlaceCode} – {a.PlaceName} · {IssueScopes.Label(a.Scope)} · {Ui.N(a.Total)} bộ", Encode(a.PlaceId, a.Scope))).ToList();

    public static List<Opt<string>> Scopes() => IssueScopes.Values.Select(v => new Opt<string>(IssueScopes.Label(v), v)).ToList();

    public static string SetsText(IReadOnlyDictionary<string, decimal> sets) => string.Join("; ", sets.Where(kv => kv.Value != 0).Select(kv => $"{kv.Key}: {Ui.N(kv.Value)}"));
}

public sealed class AssignScreen : Screen
{
    public override string Title => "Giao việc theo nơi nhận";
    public override string Subtitle => "Giao cho từng nơi nhận (tổ cắt, chuyền, tổ hoàn thiện…) phạm vi NPL (cả bộ hoặc theo loại BOM) và số bộ theo size. Nhu cầu và sổ cân đối tính riêng cho từng nơi nhận; số dư của nơi này không dùng cho nơi khác. Tăng số bộ giao = thêm đợt.";
    long? _order; long? _place; string _scope = IssueScopes.ToCat;
    readonly Dictionary<string, string> _sets = new();

    public AssignScreen(long? orderId = null) { _order = orderId; }

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) { _order = orders.FirstOrDefault()?.Value; _sets.Clear(); }
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _sets.Clear(); Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = _order.Value;
        var places = X.Q.Places(forFrom: false, activeOnly: true);
        if (places.Count == 0) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Chưa có nơi nhận. Khai báo ở Quản trị → Nơi xuất / Nơi nhận (ví dụ Tổ cắt 1, Chuyền 2, Tổ hoàn thiện).", "warn"));
        _place ??= places.First().Id;
        var assigns = X.Issue.Assignments(o);
        var cur = assigns.FirstOrDefault(a => a.PlaceId == _place && a.Scope == _scope);
        var lines = X.Orders.LoadLines(o);
        var remaining = X.Issue.Remaining(o, _scope, _place);
        var grid = new WrapPanel();
        foreach (var l in lines)
        {
            var size = l.DefaultSize;
            if (!_sets.ContainsKey(size)) _sets[size] = cur?.Sets.TryGetValue(size, out var q) == true && q != 0 ? Ui.N(q) : "";
            var tb = Ui.Input(80, "0", _sets[size]); tb.HorizontalContentAlignment = HorizontalAlignment.Right;
            tb.TextChanged += (_, _) => _sets[size] = tb.Text ?? "";
            grid.Children.Add(new StackPanel { Margin = new Avalonia.Thickness(0, 0, 12, 6), Children = {
                Ui.Text($"{size} (đơn {Ui.N(l.Sets)}, còn giao được {Ui.N(remaining[size])})", Ui.Muted), tb } });
        }
        List<OrderLine> Read() => lines.Select(l => new OrderLine(l.DefaultSize, string.IsNullOrWhiteSpace(_sets.GetValueOrDefault(l.DefaultSize)) ? 0m : Ui.Dec(_sets[l.DefaultSize], "số bộ size " + l.DefaultSize))).ToList();

        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb),
            Ui.Card("Giao việc",
                Ui.Row(Ui.Label("Nơi nhận", true), Ui.Combo(places.Select(p => new Opt<long?>($"{p.Code} – {p.Name}", p.Id)), _place, 260, v => { _place = v; _sets.Clear(); Reload(); }),
                    Ui.Label("Phạm vi", true), Ui.Combo(Keys.Scopes(), _scope, 200, v => { _scope = v; _sets.Clear(); Reload(); })),
                Ui.Text("Số bộ giao theo size:", Ui.Muted), grid,
                Ui.Row(
                    Ui.Btn("Lưu giao việc", () => Ui.Try(() => { X.Issue.SetAssignment(o, _place!.Value, _scope, Read()); _sets.Clear(); Reload(); }, "Đã lưu giao việc."), primary: true),
                    Ui.Btn("Điền toàn bộ phần còn giao được", () =>
                    {
                        foreach (var l in lines) _sets[l.DefaultSize] = Ui.N((cur?.Sets.GetValueOrDefault(l.DefaultSize) ?? 0) + remaining[l.DefaultSize]);
                        Reload();
                    }),
                    Ui.Btn("Tạo phiếu xuất cho nơi nhận này", () => App.Main.Open("Phiếu xuất (cấp phát)", new IssueScreen(o, Keys.Encode(_place!.Value, _scope))))),
                Ui.Hint("\"Cả bộ NPL\" gồm mọi loại BOM. Tổng số bộ giao cho mọi nơi nhận không vượt số bộ của đơn ở từng size và từng loại BOM. Giảm số bộ giao dưới phần đã cấp thì phần đã cấp thành số dư, cần trả kho hoặc điều chuyển.")),
            Ui.Card("Đã giao việc",
                Ui.List(assigns, new (string, Func<AssignmentRow, string>, bool)[]
                {
                    ("Nơi nhận", a => $"{a.PlaceCode} – {a.PlaceName}", false), ("Phạm vi", a => IssueScopes.Label(a.Scope), false),
                    ("Số bộ theo size", a => Keys.SetsText(a.Sets), false), ("Tổng bộ", a => Ui.N(a.Total), true)
                }, a => { if (a is not null && (a.PlaceId != _place || a.Scope != _scope)) { _place = a.PlaceId; _scope = a.Scope; _sets.Clear(); Reload(); } }, 300,
                a => a.PlaceId == _place && a.Scope == _scope)));
    }
}

public sealed class IssueScreen : Screen
{
    public override string Title => "Phiếu xuất (cấp phát)";
    public override string Subtitle => "Mỗi phiếu cấp cho MỘT nơi nhận + phạm vi đã giao việc. Đề xuất = nhu cầu kỳ − số dư của đúng nơi nhận đó (cấp thừa giảm, cấp thiếu cộng). Kho nhập thực cấp; 0 được giữ nguyên. Các nơi nhận khác nhau lập phiếu song song được.";
    long? _order; string? _key; DocRow? _sel;
    readonly Dictionary<string, string> _actual = new();

    public IssueScreen(long? orderId = null, string? key = null) { _order = orderId; _key = key; }

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _sel = null; _key = null; _actual.Clear(); Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = _order.Value;
        var keys = Keys.ForOrder(o);
        if (_key is null || keys.All(k => k.Value != _key)) _key = keys.FirstOrDefault()?.Value;
        var docs = X.Q.Docs("OUT", o).Where(d => d.Scope is not null && d.RefDocId is null).ToList();
        _sel = _sel is not null ? docs.FirstOrDefault(d => d.Id == _sel.Id) : docs.FirstOrDefault(d => d.Status == "DRAFT");
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb),
            keys.Count == 0
                ? Ui.Row(Ui.Banner("Đơn chưa giao việc cho nơi nhận nào.", "warn"), Ui.Btn("Giao việc", () => App.Main.Open("Giao việc theo nơi nhận", new AssignScreen(o)), primary: true))
                : Ui.Row(Ui.Label("Nơi nhận / phạm vi"), Ui.Combo(keys, _key, 420, v => _key = v),
                    Ui.Btn("Tạo phiếu xuất nháp", () => Ui.Try(() =>
                    {
                        var (place, scope) = Keys.Decode(_key!);
                        var id = X.Issue.CreateIssueDraft(o, place, scope, X.Q.NextNo("PX"));
                        X.Places.SetDocPlaces(id, X.Places.DefaultOutFrom == place ? null : X.Places.DefaultOutFrom, place);
                        _sel = X.Q.Docs("OUT", o).First(d => d.Id == id); _actual.Clear(); Reload();
                    }, "Đã tạo phiếu xuất nháp theo nhu cầu còn lại của nơi nhận."), primary: true)),
            Ui.Card("Phiếu cấp phát của đơn", Ui.List(docs, DocText.Cols, d => { if (d is not null && d.Id != _sel?.Id) { _sel = d; _actual.Clear(); Reload(); } }, 240, d => d.Id == _sel?.Id)),
            _sel is null ? null : _sel.Status == "DRAFT" ? DraftCard(_sel) : Ui.Card($"Phiếu {_sel.DocNo} – {_sel.IssuePlace} · {DocText.Scope(_sel.Scope)} – đã xác nhận", DocText.PlacesAndPrint(_sel, Reload), DocText.Lines(_sel.Id)));
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
        return Ui.Card($"Phiếu nháp {d.DocNo} – nơi nhận {d.IssuePlace} · {DocText.Scope(d.Scope)}", DocText.PlacesAndPrint(d, Reload), grid,
            Ui.Row(
                Ui.BtnAsync("Lưu thực cấp", async () =>
                {
                    var w = SaveActual(); Reload();
                    if (w.Count > 0) await Dialog.Info("Cảnh báo", string.Join("\n", w)); else App.Main.Status("Đã lưu thực cấp.", "ok");
                }),
                Ui.BtnAsync("Xác nhận xuất", async () =>
                {
                    var w = SaveActual();
                    var msg = $"Xác nhận phiếu {d.DocNo} cho {d.IssuePlace}? Hệ thống trừ tồn và ghi sổ cân đối của nơi nhận này. Không sửa được sau khi xác nhận." + (w.Count > 0 ? "\n\nCảnh báo:\n" + string.Join("\n", w) : "");
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
    public override string Subtitle => "Trả: nơi nhận trả NPL về kho – tăng tồn và giảm lượng đã cấp của ĐÚNG nơi nhận đó (không trả vượt lượng nơi đó đã nhận). Xuất bù gắn phiếu cấp gốc: Bù thiếu tính vào cân đối; Bù hao hụt ghi là hao hụt vượt định mức, không làm số dư dương.";
    long? _order; string? _key; long? _src; SupplementKind _kind = SupplementKind.BuThieu; DocRow? _sel;
    readonly List<LineState> _ret = new(), _sup = new();

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _sel = null; _src = null; _key = null; _ret.Clear(); _sup.Clear(); Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var mats = Opts.Materials(o.CustomerId);
        var keys = Keys.ForOrder(o.Id);
        if (_key is null || keys.All(k => k.Value != _key)) _key = keys.FirstOrDefault()?.Value;
        var sources = X.Q.Docs("OUT", o.Id, "CONFIRMED").Where(d => d.Scope is not null && d.RefDocId is null).ToList();
        if (_src is null || sources.All(s => s.Id != _src)) _src = sources.FirstOrDefault()?.Id;
        var docs = X.Q.Docs(null, o.Id).Where(d => d.Type == "RETURN" || (d.Type == "OUT" && d.RefDocId is not null)).ToList();
        if (_sel is not null) _sel = docs.FirstOrDefault(d => d.Id == _sel.Id);
        var kindRow = new WrapPanel();
        foreach (var (k, label) in new[] { (SupplementKind.BuThieu, "Bù thiếu (tính vào cân đối)"), (SupplementKind.BuHaoHut, "Bù hao hụt vượt định mức") })
        {
            var rb = new RadioButton { Content = label, GroupName = "bk", IsChecked = _kind == k, Margin = new Avalonia.Thickness(0, 0, 16, 0) };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) _kind = k; };
            kindRow.Children.Add(rb);
        }
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb),
            Ui.Card("Phiếu trả NPL",
                keys.Count == 0 ? Ui.Hint("Đơn chưa giao việc cho nơi nhận nào.") : Ui.Row(Ui.Label("Nơi nhận / phạm vi trả"), Ui.Combo(keys, _key, 420, v => _key = v)),
                new LinesEditor(_ret, mats, "Số lượng trả").View,
                Ui.Row(Ui.Btn("Tạo phiếu trả nháp", () => Ui.Try(() =>
                {
                    var (place, scope) = Keys.Decode(_key ?? throw new BusinessException("Chọn nơi nhận / phạm vi."));
                    var id = X.Issue.CreateReturn(o.Id, place, scope, X.Q.NextNo("TR"), LinesEditor.Read(_ret, "số lượng trả"));
                    X.Places.SetDocPlaces(id, place, X.Places.DefaultInTo == place ? null : X.Places.DefaultInTo);
                    _ret.Clear(); _sel = X.Q.Docs(null, o.Id).First(d => d.Id == id); Reload();
                }, "Đã tạo phiếu trả nháp."), primary: true))),
            Ui.Card("Phiếu xuất bù",
                sources.Count == 0 ? Ui.Hint("Đơn chưa có phiếu cấp phát đã xác nhận để xuất bù.") :
                Ui.Row(Ui.Label("Phiếu cấp gốc"), Ui.Combo(sources.Select(s => new Opt<long?>($"{s.DocNo} – {s.IssuePlace} · {DocText.Scope(s.Scope)}", s.Id)), _src, 380, v => _src = v)),
                kindRow,
                new LinesEditor(_sup, mats, "Số lượng bù").View,
                Ui.Row(Ui.BtnAsync("Tạo phiếu xuất bù nháp", async () =>
                {
                    var src = _src ?? throw new BusinessException("Chọn phiếu cấp gốc.");
                    var lines = LinesEditor.Read(_sup, "số lượng bù");
                    var w = X.Issue.SupplementWarnings(src, lines, _kind);
                    if (w.Count > 0 && !await Dialog.Confirm("Cảnh báo xuất bù", string.Join("\n", w) + "\n\nVẫn lập phiếu xuất bù?", "Vẫn lập phiếu")) return;
                    var id = X.Issue.CreateSupplement(src, X.Q.NextNo("XB"), lines, _kind);
                    var srcDoc = X.Q.Docs().First(x => x.Id == src);
                    X.Places.SetDocPlaces(id, srcDoc.FromPlaceId ?? X.Places.DefaultOutFrom, srcDoc.ToPlaceId ?? srcDoc.IssuePlaceId);
                    _sup.Clear(); _sel = X.Q.Docs(null, o.Id).First(d => d.Id == id); Reload();
                    App.Main.Status("Đã tạo phiếu xuất bù nháp.", "ok");
                }, primary: true))),
            Ui.Card("Phiếu trả / xuất bù của đơn", Ui.List(docs, DocText.Cols, d => { if (d is not null && d.Id != _sel?.Id) { _sel = d; Reload(); } }, 240, d => d.Id == _sel?.Id)),
            _sel is null ? null : Ui.Card($"{DocText.Type(_sel)} {_sel.DocNo} – {_sel.IssuePlace} · {DocText.Status(_sel.Status)}", DocText.PlacesAndPrint(_sel, Reload), DocText.Lines(_sel.Id),
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

public sealed class SurplusScreen : Screen
{
    public override string Title => "Điều chuyển NPL dư";
    public override string Subtitle => "Chuyển NPL đang DƯ ở một nơi nhận sang nơi nhận khác hoặc đơn khác cùng khách hàng (NPL đã ở xưởng, không qua kho). Chỉ chuyển trong phạm vi số dư; ghi sổ cân đối cả hai phía. Đây là cách duy nhất để dùng phần dư của tổ này cho tổ khác.";
    long? _order; int _row = -1; long? _toOrder; string? _toKey; string _qty = "", _reason = "";

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _row = -1; _toOrder = null; _toKey = null; Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var surplus = X.Issue.Surpluses(o.Id);
        var rows = surplus.Select((s, i) => (s, i)).ToList();
        var sameCust = X.Q.Orders(o.CustomerId, "OPEN").Select(x => new Opt<long?>($"{x.No} – {x.ProductCode}", x.Id)).ToList();
        if (_toOrder is null || sameCust.All(x => x.Value != _toOrder)) _toOrder = o.Id;   // đơn nhận phải cùng khách hàng với đơn nguồn
        var toKeys = Keys.ForOrder(_toOrder.Value);
        var from = _row >= 0 && _row < surplus.Count ? surplus[_row] : default;
        var fromKey = from.Material is null ? null : Keys.Encode(from.PlaceId, from.Scope);
        // Ưu tiên nơi nhận cùng phạm vi và có mã NPL này trong định mức
        toKeys = toKeys.Where(k => !(_toOrder == o.Id && k.Value == fromKey))
            .Where(k => { try { var (p, sc) = Keys.Decode(k.Value); return from.Material is null || X.Issue.KeyDemand(_toOrder!.Value, p, sc).ContainsKey(from.Material); } catch (BusinessException) { return false; } })
            .OrderBy(k => from.Material is not null && Keys.Decode(k.Value).scope == from.Scope ? 0 : 1).ToList();
        if (_toKey is null || toKeys.All(k => k.Value != _toKey)) _toKey = toKeys.FirstOrDefault()?.Value;
        var qty = Ui.Input(110, "Số lượng", _qty); qty.TextChanged += (_, _) => _qty = qty.Text ?? "";
        var reason = Ui.Input(300, "Lý do", _reason); reason.TextChanged += (_, _) => _reason = reason.Text ?? "";
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng (nơi chuyển)"), orderCb),
            Ui.Card("Số dư đang DƯ theo nơi nhận (chọn một dòng)",
                surplus.Count == 0 ? Ui.Hint("Đơn không có NPL dư ở nơi nhận nào.") :
                Ui.List(rows, new (string, Func<(( long PlaceId, string Place, string Scope, string Material, decimal Surplus) s, int i), string>, bool)[]
                {
                    ("Nơi nhận", r => r.s.Place, false), ("Phạm vi", r => IssueScopes.Label(r.s.Scope), false), ("Mã NPL", r => r.s.Material, false), ("Dư", r => Ui.N(r.s.Surplus), true)
                }, r => { if (r.i != _row) { _row = r.i; _qty = Ui.N(surplus[r.i].Surplus); Reload(); } }, 240, r => r.i == _row)),
            from.Material is null ? null : Ui.Card($"Điều chuyển {from.Material} từ {from.Place} · {IssueScopes.Label(from.Scope)} (dư {Ui.N(from.Surplus)})",
                Ui.Row(Ui.Label("Sang đơn"), Ui.Combo(sameCust, _toOrder, 300, v => { _toOrder = v; _toKey = null; Reload(); }),
                    Ui.Label("Nơi nhận / phạm vi"), toKeys.Count == 0 ? Ui.Hint("Đơn nhận chưa giao việc cho nơi nhận khác.") : Ui.Combo(toKeys, _toKey, 380, v => _toKey = v)),
                Ui.Row(Ui.Label("Số lượng", true), qty, Ui.Label("Lý do"), reason,
                    Ui.BtnAsync("Điều chuyển", async () =>
                    {
                        var (tp, ts) = Keys.Decode(_toKey ?? throw new BusinessException("Chọn nơi nhận / phạm vi nhận."));
                        var q = Ui.Dec(_qty, "số lượng");
                        if (!await Dialog.Confirm("Điều chuyển NPL dư", $"Chuyển {Ui.N(q)} {from.Material} từ {from.Place} sang nơi nhận đã chọn? Ghi sổ cân đối cả hai phía, không đổi tồn kho.", "Điều chuyển")) return;
                        X.Issue.TransferSurplus(X.Q.NextNo("DC"), o.Id, from.PlaceId, from.Scope, _toOrder!.Value, tp, ts, new[] { new SurplusLine(from.Material, q) }, _reason);
                        _row = -1; _qty = ""; _reason = ""; App.Main.Status("Đã điều chuyển NPL dư.", "ok"); Reload();
                    }, primary: true))),
            Ui.Card("Lịch sử điều chuyển của đơn",
                Ui.List(X.Q.SurplusTransfers(o.Id), new (string, Func<(long Id, string DocNo, string From, string To, string Reason, DateTime At), string>, bool)[]
                {
                    ("Số phiếu", t => t.DocNo, false), ("Từ (đơn / nơi nhận / phạm vi)", t => t.From, false), ("Sang", t => t.To, false), ("Lý do", t => t.Reason, false), ("Thời điểm", t => Ui.DT(t.At), false)
                }, maxHeight: 240)));
    }
}

public sealed class CarryScreen : Screen
{
    public override string Title => "Cân đối cấp phát";
    public override string Subtitle => "Sổ theo đơn + nơi nhận + phạm vi + mã NPL. Số dư cuối = số dư đầu + thực cấp − nhu cầu kỳ − hao hụt (bù hao hụt). Dương: cấp thừa; âm: cấp thiếu. Đọc số dư cuối mới nhất của đúng nơi nhận, không cộng dồn và không dùng chéo giữa các nơi nhận.";
    long? _order; long? _place;

    protected override Control Build()
    {
        var orders = Opts.Orders(openOnly: false); orders.Insert(0, new Opt<long?>("Tất cả đơn", null));
        var places = X.Q.Places(forFrom: false).Select(p => new Opt<long?>($"{p.Code} – {p.Name}", p.Id)).ToList(); places.Insert(0, new Opt<long?>("Tất cả nơi nhận", null));
        return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), Ui.Combo(orders, _order, 360, v => { _order = v; Reload(); }), Ui.Label("Nơi nhận"), Ui.Combo(places, _place, 260, v => { _place = v; Reload(); })),
            Ui.List(X.Q.Carry(_order, _place), new (string, Func<CarryRow, string>, bool)[]
            {
                ("Đơn", r => r.OrderNo, false), ("Nơi nhận", r => r.Place.Length > 0 ? r.Place : "(cũ)", false), ("Phạm vi", r => DocText.Scope(r.Scope), false), ("Mã NPL", r => r.Material, false),
                ("Số dư đầu", r => Ui.N(r.Opening), true), ("Thực cấp", r => Ui.N(r.Issued), true), ("Nhu cầu kỳ", r => Ui.N(r.Demand), true), ("Hao hụt", r => r.Loss == 0 ? "" : Ui.N(r.Loss), true),
                ("Số dư cuối", r => Ui.N(r.Closing), true), ("Tình trạng", r => r.Closing > 0 ? "Cấp thừa" : r.Closing < 0 ? "Cấp thiếu" : "Cân đối", false),
                ("Phiếu", r => r.DocNo, false), ("Thời điểm", r => Ui.DT(r.At), false)
            }, maxHeight: 620));
    }
}

public sealed class SettleScreen : Screen
{
    public override string Title => "Quyết toán & đóng đơn";
    public override string Subtitle => "Quyết toán NPL theo nơi nhận / phạm vi / mã NPL: nhu cầu theo giao việc, đã cấp, bù hao hụt, trả/chuyển đi, số dư. Còn DƯ thì phải trả kho hoặc điều chuyển trước khi đóng; còn THIẾU thì phải xác nhận khi đóng.";
    long? _order;

    public SettleScreen(long? orderId = null) { _order = orderId; }

    protected override Control Build()
    {
        var orders = Opts.Orders(openOnly: false);
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Chưa có đơn hàng.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var t = X.Reports.SettlementReport(o.Id);
        var dư = t.Rows.Count(r => (string)r[10]! == "Dư"); var thiếu = t.Rows.Count(r => (string)r[10]! == "Thiếu");
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb, Ui.Chip(o.Status == "OPEN" ? "Đang mở" : "Đã đóng", o.Status == "OPEN" ? "ok" : "muted"),
                Ui.BtnAsync("Xuất Excel", () => Ui.Export(t, false)), Ui.BtnAsync("Xuất PDF", () => Ui.Export(t, true))),
            Ui.Banner(string.Join("\n", t.Filters.Where(f => f.StartsWith("Kết luận") || f.StartsWith("Chưa giao"))), dư > 0 ? "bad" : thiếu > 0 ? "warn" : "ok"),
            t.Rows.Count == 0 ? Ui.Hint("Đơn chưa giao việc / chưa cấp phát.") : Ui.Table(t, maxHeight: 520),
            o.Status != "OPEN" ? null : Ui.Row(
                dư > 0 ? Ui.Btn("Trả NPL dư về kho", () => App.Main.Open("Trả NPL & xuất bù", new ReturnScreen())) : null,
                dư > 0 ? Ui.Btn("Điều chuyển NPL dư", () => App.Main.Open("Điều chuyển NPL dư", new SurplusScreen())) : null,
                Ui.BtnAsync("Đóng đơn", async () => await CloseFlow(o.Id, o.No, Reload), primary: true)));
    }

    /// <summary>Đóng đơn có quyết toán: dư → báo lỗi; thiếu → hỏi xác nhận; đủ → hỏi xác nhận đóng.</summary>
    public static async Task CloseFlow(long orderId, string orderNo, Action reload)
    {
        try
        {
            if (!await Dialog.Confirm("Đóng đơn", $"Đóng đơn {orderNo}? Đơn đã đóng không cấp phát, điều chỉnh tiếp; lượng giữ kho của đơn được nhả.")) return;
            App.X.Issue.CloseOrder(orderId);
        }
        catch (ShortageOnCloseException ex)
        {
            if (!await Dialog.Confirm("Đóng đơn khi còn thiếu", ex.Message + "\n\nXác nhận đóng đơn khi còn thiếu NPL?", "Đóng đơn", danger: true)) return;
            App.X.Issue.CloseOrder(orderId, allowShortage: true);
        }
        App.Main.Status("Đã đóng đơn " + orderNo + ".", "ok");
        reload();
    }
}
