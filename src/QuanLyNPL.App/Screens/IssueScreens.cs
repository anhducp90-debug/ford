using Avalonia.Controls;
using Avalonia.Layout;
using QuanLyNPL.Core;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

internal static class Keys
{
    /// <summary>Các nơi nhận đã có sổ cân đối (đã nhận NPL) của đơn.</summary>
    public static List<Opt<long?>> ForOrder(long orderId) =>
        App.X.Issue.LedgerKeys(orderId).Where(k => k.place is not null).GroupBy(k => k.place).Select(g => new Opt<long?>(g.First().placeName, g.Key)).ToList();
    public static List<Opt<long?>> Receivers() => App.X.Q.Places(forFrom: false, activeOnly: true).Select(p => new Opt<long?>($"{p.Code} – {p.Name}", p.Id)).ToList();
}

public sealed class IssueScreen : Screen
{
    public override string Title => "Phiếu xuất (cấp phát)";
    public override string Subtitle => "Mỗi phiếu cấp cho MỘT nơi nhận: tích loại BOM (Tổ cắt cho nhà cắt, Sản xuất cho các tổ, Đóng gói theo bộ; tích cả ba = trọn gói A–Z), tích chủng loại (tất cả = cả bộ, một = lẻ, vài = gộp), nhập số bộ theo size. Hệ thống tính NPL theo BOM và chặn khi lũy kế số lượng cấp vượt đơn hàng ở từng size. Đề xuất NPL = nhu cầu − số dư của đúng nơi nhận đó.";
    long? _order; long? _place; DocRow? _sel;
    readonly HashSet<string> _types = new() { IssueScopes.ToCat };
    HashSet<string>? _vars;                                   // null = tất cả chủng loại (mặc định cấp theo bộ)
    readonly Dictionary<string, string> _actual = new();
    readonly Dictionary<string, string> _qty = new();

    public IssueScreen(long? orderId = null) { _order = orderId; }

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _sel = null; _vars = null; _actual.Clear(); _qty.Clear(); Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = _order.Value;
        var places = Keys.Receivers();
        if (_place is null || places.All(p => p.Value != _place)) _place = places.FirstOrDefault()?.Value;
        var docs = X.Q.Docs("OUT", o).Where(d => d.Scope is not null && d.RefDocId is null).ToList();
        _sel = _sel is not null ? docs.FirstOrDefault(d => d.Id == _sel.Id) : docs.FirstOrDefault(d => d.Status == "DRAFT");
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb),
            places.Count == 0
                ? Ui.Banner("Chưa có nơi nhận. Khai báo ở Quản trị → Nơi xuất / Nơi nhận (ví dụ Nhà cắt, Tổ may 1, Tổ hoàn thiện).", "warn")
                : NewCard(o, places),
            Ui.Card("Phiếu cấp phát của đơn", Ui.List(docs, DocText.Cols, d => { if (d is not null && d.Id != _sel?.Id) { _sel = d; _actual.Clear(); Reload(); } }, 240, d => d.Id == _sel?.Id)),
            _sel is null ? null : _sel.Status == "DRAFT" ? DraftCard(_sel)
                : Ui.Card($"Phiếu {_sel.DocNo} – {_sel.IssuePlace} · {DocText.Scope(_sel.Scope)} – đã xác nhận", QtyLine(_sel.Id), DocText.PlacesAndPrint(_sel, Reload), DocText.Lines(_sel.Id)));
    }

    static Control? QtyLine(long docId)
    {
        var t = IssueService.Describe(App.X.C, docId);
        return t.Length == 0 ? null : Ui.Text("Số lượng: " + t, Ui.Muted);
    }

    Control NewCard(long o, List<Opt<long?>> places)
    {
        var types = IssueScopes.Types.Where(_types.Contains).ToList();
        var orderVars = X.Issue.Variants(o).Select(v => v.Code).ToList();
        var (variants, setOnly) = X.Issue.VariantsFor(o, types);              // chủng loại tự load theo BOM của loại đã tích
        var shown = variants.Select(v => v.Code).ToHashSet();
        var all = _vars is null || setOnly || shown.All(_vars.Contains);
        var vars = all ? shown : _vars!.Where(shown.Contains).ToHashSet();
        var typeRow = new WrapPanel();
        foreach (var t in IssueScopes.Types)
        {
            var cb = new CheckBox { Content = new TextBlock { Text = "BOM " + IssueScopes.Label(t) }, IsChecked = _types.Contains(t), Margin = new Avalonia.Thickness(0, 0, 18, 0) };
            cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _types.Add(t); else _types.Remove(t); Reload(); };
            typeRow.Children.Add(cb);
        }
        var varRow = new WrapPanel();
        var allCb = new CheckBox { Content = new TextBlock { Text = "Cả bộ (tất cả)" }, IsChecked = all, IsEnabled = !setOnly, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 0, 18, 0) };
        allCb.IsCheckedChanged += (_, _) => { _vars = allCb.IsChecked == true ? null : new HashSet<string>(); Reload(); };
        varRow.Children.Add(allCb);
        foreach (var v in variants)
        {
            var cb = new CheckBox { Content = new TextBlock { Text = $"{v.Code} – {v.Name}" }, IsChecked = vars.Contains(v.Code), IsEnabled = !setOnly, Margin = new Avalonia.Thickness(0, 0, 18, 0) };
            cb.IsCheckedChanged += (_, _) =>
            {
                var set = new HashSet<string>(vars);
                if (cb.IsChecked == true) set.Add(v.Code); else set.Remove(v.Code);
                _vars = shown.All(set.Contains) ? null : set; Reload();
            };
            varRow.Children.Add(cb);
        }
        // Cả bộ = mọi chủng loại của đơn (chủng loại không có BOM của loại nào thì hệ thống tự bỏ qua)
        var chosen = all ? orderVars : orderVars.Where(vars.Contains).ToList();
        var noBom = types.Count > 0 && variants.Count == 0 && !setOnly;
        var mode = types.Count == 0 ? "Chưa tích loại BOM." : noBom ? "Đơn không có BOM hiệu lực cho loại đã tích." : chosen.Count == 0 ? "Chưa tích chủng loại."
            : (types.Count == IssueScopes.Types.Length ? "Trọn gói A–Z (Tổ cắt + Sản xuất + Đóng gói)" : "BOM " + string.Join(" + ", types.Select(IssueScopes.Label)))
              + " · " + (all ? "cấp cả bộ" : chosen.Count == 1 ? "cấp lẻ chủng loại " + chosen[0] : $"cấp gộp {chosen.Count} chủng loại");
        var (cap, errs) = types.Count == 0 || chosen.Count == 0 || noBom ? (new List<SizeCapacity>(), new List<string>()) : X.Issue.Capacity(o, types, chosen);

        var grid = new WrapPanel();
        foreach (var c in cap)
        {
            var size = c.Size;
            var tb = Ui.Input(90, "0", _qty.GetValueOrDefault(size, "")); tb.HorizontalContentAlignment = HorizontalAlignment.Right;
            tb.TextChanged += (_, _) => _qty[size] = tb.Text ?? "";
            grid.Children.Add(new StackPanel { Margin = new Avalonia.Thickness(0, 0, 16, 6), Children = {
                Ui.Text(size, Ui.Muted, true), tb, Ui.Text($"còn {Ui.N(c.MaxSets)}/{Ui.N(c.OrderedSets)} bộ", c.MaxSets <= 0 ? Ui.Bad : Ui.Muted) } });
        }
        List<OrderLine> Read() => _qty.Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && cap.Any(c => c.Size == kv.Key))
            .Select(kv => new OrderLine(kv.Key, Ui.Dec(kv.Value, "số bộ size " + kv.Key))).Where(x => x.Sets != 0).ToList();
        return Ui.Card("Lập phiếu cấp",
            Ui.Row(Ui.Label("Nơi nhận", true), Ui.Combo(places, _place, 300, v => { _place = v; Reload(); })),
            Ui.Row(Ui.Label("Loại BOM", true), typeRow),
            Ui.Row(Ui.Label("Chủng loại", true), varRow),
            setOnly ? Ui.Hint("Có loại BOM khai báo theo bộ (ví dụ Đóng gói) nên phiếu này cấp cả bộ.") : null,
            Ui.Banner(mode, errs.Count > 0 || types.Count == 0 || chosen.Count == 0 || noBom ? "warn" : "ok"),
            errs.Count > 0 ? Ui.Banner(string.Join("\n", errs), "bad") : null,
            cap.Count == 0 ? null : Ui.Text(all ? "Số bộ cấp theo size:" : "Số lượng cấp theo size (tính theo bộ – mỗi chủng loại đã tích = số này × SL chủng loại/bộ):", Ui.Muted),
            cap.Count == 0 ? null : grid,
            cap.Count == 0 ? null : Ui.Row(
                Ui.Btn("Tạo phiếu xuất nháp", () => Ui.Try(() =>
                {
                    var place = _place ?? throw new BusinessException("Chọn nơi nhận.");
                    var id = X.Issue.CreateIssueDraft(o, place, types, chosen, Read(), X.Q.NextNo("PX"));
                    X.Places.SetDocPlaces(id, X.Places.DefaultOutFrom == place ? null : X.Places.DefaultOutFrom, place);
                    _sel = X.Q.Docs("OUT", o).First(d => d.Id == id); _actual.Clear(); _qty.Clear(); Reload();
                }, "Đã tạo phiếu xuất nháp."), primary: true),
                Ui.Btn("Điền tối đa còn lại", () => { foreach (var c in cap) _qty[c.Size] = c.MaxSets > 0 ? Ui.N(c.MaxSets) : ""; Reload(); }),
                Ui.Btn("Xóa số lượng", () => { _qty.Clear(); Reload(); })),
            Ui.Hint("Danh sách chủng loại tự hiện theo BOM của các loại đã tích. Tổ cắt mặc định cấp theo bộ (tích \"Cả bộ\"); bỏ tích để cấp lẻ một hoặc vài chủng loại. Sản xuất: tích 1 chủng loại = cấp lẻ, tích nhiều = cấp gộp, tích tất cả = cả bộ. " +
                    "Tích cả ba loại BOM = cấp trọn gói từ A–Z cho một đơn vị sản xuất. Đóng gói khai báo theo bộ nên phải tích đủ chủng loại. " +
                    "\"còn x/y bộ\": số bộ còn cấp được cho lựa chọn này (không vượt số lượng đơn ở từng size). Để trống số bộ: chỉ lập phiếu cấp bù phần nơi nhận đang thiếu."));
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
        return Ui.Card($"Phiếu nháp {d.DocNo} – nơi nhận {d.IssuePlace} · {DocText.Scope(d.Scope)}", QtyLine(d.Id), DocText.PlacesAndPrint(d, Reload), grid,
            Ui.Row(
                Ui.BtnAsync("Lưu thực cấp", async () =>
                {
                    var w = SaveActual(); Reload();
                    if (w.Count > 0) await Dialog.Info("Cảnh báo", string.Join("\n", w)); else App.Main.Status("Đã lưu thực cấp.", "ok");
                }),
                Ui.BtnAsync("Xác nhận xuất", async () =>
                {
                    var w = SaveActual();
                    var msg = $"Xác nhận phiếu {d.DocNo} cho {d.IssuePlace}? Hệ thống trừ tồn, ghi số lượng đã cấp theo size và ghi sổ cân đối của nơi nhận này. Không sửa được sau khi xác nhận." + (w.Count > 0 ? "\n\nCảnh báo:\n" + string.Join("\n", w) : "");
                    if (!await Dialog.Confirm("Xác nhận xuất", msg, "Xác nhận")) { Reload(); return; }
                    X.Stock.Confirm(d.Id); _actual.Clear(); App.Main.Status("Đã xác nhận xuất kho.", "ok"); Reload();
                }, primary: true),
                Ui.BtnAsync("Hủy phiếu nháp", async () =>
                {
                    if (!await Dialog.Confirm("Hủy phiếu nháp", $"Hủy phiếu nháp {d.DocNo}? Số lượng trên phiếu được trả lại phần còn cấp.", "Hủy phiếu", danger: true)) return;
                    X.Stock.DeleteDraft(d.Id, "Hủy phiếu xuất nháp"); _sel = null; _actual.Clear(); Reload();
                }, danger: true)));
    }
}

public sealed class ReturnScreen : Screen
{
    public override string Title => "Trả NPL & xuất bù";
    public override string Subtitle => "Trả: nơi nhận trả NPL về kho – tăng tồn và giảm lượng đã cấp của ĐÚNG nơi nhận đó (không trả vượt lượng nơi đó đã nhận). Xuất bù gắn phiếu cấp gốc: Bù thiếu tính vào cân đối; Bù hao hụt ghi là hao hụt vượt định mức, không làm số dư dương.";
    long? _order; long? _key; long? _src; SupplementKind _kind = SupplementKind.BuThieu; DocRow? _sel;
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
                keys.Count == 0 ? Ui.Hint("Chưa có nơi nhận nào nhận NPL của đơn.") : Ui.Row(Ui.Label("Nơi nhận trả"), Ui.Combo(keys, _key, 420, v => _key = v)),
                new LinesEditor(_ret, mats, "Số lượng trả").View,
                Ui.Row(Ui.Btn("Tạo phiếu trả nháp", () => Ui.Try(() =>
                {
                    var place = _key ?? throw new BusinessException("Chọn nơi nhận.");
                    var id = X.Issue.CreateReturn(o.Id, place, X.Q.NextNo("TR"), LinesEditor.Read(_ret, "số lượng trả"));
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
    long? _order; int _row = -1; long? _toOrder; long? _toPlace; string _qty = "", _reason = "";

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) _order = orders.FirstOrDefault()?.Value;
        var orderCb = Ui.Combo(orders, _order, 380, v => { _order = v; _row = -1; _toOrder = null; _toPlace = null; Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var surplus = X.Issue.Surpluses(o.Id);
        var rows = surplus.Select((s, i) => (s, i)).ToList();
        var sameCust = X.Q.Orders(o.CustomerId, "OPEN").Select(x => new Opt<long?>($"{x.No} – {x.ProductCode}", x.Id)).ToList();
        if (_toOrder is null || sameCust.All(x => x.Value != _toOrder)) _toOrder = o.Id;   // đơn nhận phải cùng khách hàng với đơn nguồn
        var from = _row >= 0 && _row < surplus.Count ? surplus[_row] : default;
        var places = Keys.Receivers();
        if (_toPlace is null || places.All(p => p.Value != _toPlace)) _toPlace = places.FirstOrDefault(p => p.Value != from.PlaceId)?.Value;
        var qty = Ui.Input(110, "Số lượng", _qty); qty.TextChanged += (_, _) => _qty = qty.Text ?? "";
        var reason = Ui.Input(300, "Lý do", _reason); reason.TextChanged += (_, _) => _reason = reason.Text ?? "";
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng (nơi chuyển)"), orderCb),
            Ui.Card("Số dư đang DƯ theo nơi nhận (chọn một dòng)",
                surplus.Count == 0 ? Ui.Hint("Đơn không có NPL dư ở nơi nhận nào.") :
                Ui.List(rows, new (string, Func<(( long PlaceId, string Place, string Scope, string Material, decimal Surplus) s, int i), string>, bool)[]
                {
                    ("Nơi nhận", r => r.s.Place, false), ("Mã NPL", r => r.s.Material, false), ("Dư", r => Ui.N(r.s.Surplus), true)
                }, r => { if (r.i != _row) { _row = r.i; _qty = Ui.N(surplus[r.i].Surplus); _toPlace = null; Reload(); } }, 240, r => r.i == _row)),
            from.Material is null ? null : Ui.Card($"Điều chuyển {from.Material} từ {from.Place} (dư {Ui.N(from.Surplus)})",
                Ui.Row(Ui.Label("Sang đơn"), Ui.Combo(sameCust, _toOrder, 300, v => { _toOrder = v; Reload(); }),
                    Ui.Label("Nơi nhận"), Ui.Combo(places, _toPlace, 260, v => _toPlace = v)),
                Ui.Hint("NPL chuyển phải có trong BOM của đơn nhận. Số dư chuyển sang được trừ vào đề xuất của các phiếu cấp sau cho nơi nhận đó."),
                Ui.Row(Ui.Label("Số lượng", true), qty, Ui.Label("Lý do"), reason,
                    Ui.BtnAsync("Điều chuyển", async () =>
                    {
                        var tp = _toPlace ?? throw new BusinessException("Chọn nơi nhận.");
                        var q = Ui.Dec(_qty, "số lượng");
                        if (!await Dialog.Confirm("Điều chuyển NPL dư", $"Chuyển {Ui.N(q)} {from.Material} từ {from.Place} sang nơi nhận đã chọn? Ghi sổ cân đối cả hai phía, không đổi tồn kho.", "Điều chuyển")) return;
                        X.Issue.TransferSurplus(X.Q.NextNo("DC"), o.Id, from.PlaceId, _toOrder!.Value, tp, new[] { new SurplusLine(from.Material, q) }, _reason);
                        _row = -1; _qty = ""; _reason = ""; App.Main.Status("Đã điều chuyển NPL dư.", "ok"); Reload();
                    }, primary: true))),
            Ui.Card("Lịch sử điều chuyển của đơn",
                Ui.List(X.Q.SurplusTransfers(o.Id), new (string, Func<(long Id, string DocNo, string From, string To, string Reason, DateTime At), string>, bool)[]
                {
                    ("Số phiếu", t => t.DocNo, false), ("Từ (đơn / nơi nhận)", t => t.From, false), ("Sang", t => t.To, false), ("Lý do", t => t.Reason, false), ("Thời điểm", t => Ui.DT(t.At), false)
                }, maxHeight: 240)));
    }
}

public sealed class CarryScreen : Screen
{
    public override string Title => "Cân đối cấp phát";
    public override string Subtitle => "Sổ theo đơn + nơi nhận + mã NPL. Nhu cầu kỳ = định mức × số lượng sản phẩm của phiếu cấp. Số dư cuối = số dư đầu + thực cấp − nhu cầu kỳ − hao hụt (bù hao hụt). Dương: cấp thừa; âm: cấp thiếu. Đọc số dư cuối mới nhất của đúng nơi nhận, không cộng dồn và không dùng chéo giữa các nơi nhận.";
    long? _order; long? _place;

    protected override Control Build()
    {
        var orders = Opts.Orders(openOnly: false); orders.Insert(0, new Opt<long?>("Tất cả đơn", null));
        var places = X.Q.Places(forFrom: false).Select(p => new Opt<long?>($"{p.Code} – {p.Name}", p.Id)).ToList(); places.Insert(0, new Opt<long?>("Tất cả nơi nhận", null));
        return Ui.Col(Ui.Row(Ui.Label("Đơn hàng"), Ui.Combo(orders, _order, 360, v => { _order = v; Reload(); }), Ui.Label("Nơi nhận"), Ui.Combo(places, _place, 260, v => { _place = v; Reload(); })),
            Ui.List(X.Q.Carry(_order, _place), new (string, Func<CarryRow, string>, bool)[]
            {
                ("Đơn", r => r.OrderNo, false), ("Nơi nhận", r => r.Place.Length > 0 ? r.Place : "(cũ)", false), ("Mã NPL", r => r.Material, false),
                ("Số dư đầu", r => Ui.N(r.Opening), true), ("Thực cấp", r => Ui.N(r.Issued), true), ("Nhu cầu kỳ", r => Ui.N(r.Demand), true), ("Hao hụt", r => r.Loss == 0 ? "" : Ui.N(r.Loss), true),
                ("Số dư cuối", r => Ui.N(r.Closing), true), ("Tình trạng", r => r.Closing > 0 ? "Cấp thừa" : r.Closing < 0 ? "Cấp thiếu" : "Cân đối", false),
                ("Phiếu", r => r.DocNo, false), ("Thời điểm", r => Ui.DT(r.At), false)
            }, maxHeight: 620));
    }
}

public sealed class SettleScreen : Screen
{
    public override string Title => "Quyết toán & đóng đơn";
    public override string Subtitle => "Quyết toán NPL theo nơi nhận / mã NPL: nhu cầu (định mức × số lượng đã cấp), đã cấp, bù hao hụt, trả/chuyển đi, số dư; kèm tiến độ số lượng cấp theo size so với đơn. Còn DƯ thì phải trả kho hoặc điều chuyển trước khi đóng; còn THIẾU NPL hoặc chưa cấp hết số lượng đơn thì phải xác nhận khi đóng.";
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
        var dư = t.Rows.Count(r => (string)r[9]! == "Dư"); var thiếu = t.Rows.Count(r => (string)r[9]! == "Thiếu");
        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng"), orderCb, Ui.Chip(o.Status == "OPEN" ? "Đang mở" : "Đã đóng", o.Status == "OPEN" ? "ok" : "muted"),
                Ui.BtnAsync("Xuất Excel", () => Ui.Export(t, false)), Ui.BtnAsync("Xuất PDF", () => Ui.Export(t, true))),
            Ui.Banner(string.Join("\n", t.Filters.Where(f => f.StartsWith("Kết luận") || f.StartsWith("Chưa cấp") || f.StartsWith("Cấp vượt"))), dư > 0 ? "bad" : thiếu > 0 ? "warn" : "ok"),
            t.Rows.Count == 0 ? Ui.Hint("Đơn chưa cấp phát.") : Ui.Table(t, maxHeight: 520),
            Ui.Card("Tiến độ cấp theo size", Ui.Table(X.Reports.IssueProgress(o.Id), maxHeight: 360)),
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
