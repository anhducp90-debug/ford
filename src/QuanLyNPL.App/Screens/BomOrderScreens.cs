using Avalonia.Controls;
using Avalonia.Layout;
using QuanLyNPL.Core;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

public sealed class BomScreen : Screen
{
    public override string Title => "BOM & định mức";
    public override string Subtitle => "Chọn mã hàng, loại BOM và phạm vi. Tổ cắt, Sản xuất: theo bộ hoặc theo chủng loại; Đóng gói: luôn theo bộ. Đổi định mức thì tạo phiên bản mới; đơn hàng dùng phiên bản hiệu lực tại ngày lập đơn.";

    long? _prod; BomType _type = BomType.ToCat; bool _perSet; long? _variant; long? _version; long? _copyFrom;
    readonly List<LineState> _edit = new();
    long? _editFor;

    static readonly (BomType t, string label)[] Types = { (BomType.ToCat, "Tổ cắt"), (BomType.SanXuat, "Sản xuất"), (BomType.DongGoi, "Đóng gói") };

    public BomScreen(long? productId = null) { _prod = productId; }

    protected override Control Build()
    {
        var prods = Opts.Products();
        _prod ??= prods.FirstOrDefault()?.Value;
        var prodCb = Ui.Combo(prods, _prod, 340, v => { _prod = v; _variant = null; _version = null; _copyFrom = null; Reload(); });
        if (_prod is null) return Ui.Col(Ui.Row(Ui.Label("Mã hàng", true), prodCb), Ui.Banner("Chưa có mã hàng. Khai báo mã hàng và chủng loại trước.", "warn"));
        var p = X.Q.Products().Single(x => x.Id == _prod);
        var variants = X.Q.Variants(p.Id);
        if (_type == BomType.DongGoi) _perSet = true;
        if (!_perSet && (_variant is null || variants.All(v => v.Id != _variant))) _variant = variants.FirstOrDefault()?.Id;
        var sizes = X.Q.ProductSizes(p.Id);
        var allBoms = X.Q.Boms(p.Id);
        var code = BomTypes.Code(_type);

        // Loại BOM / phạm vi
        var typeRow = new WrapPanel();
        typeRow.Children.Add(Ui.Label("Loại BOM", true));
        foreach (var (t, label) in Types)
        {
            var rb = new RadioButton { Content = label, GroupName = "bt", IsChecked = _type == t, Margin = new Avalonia.Thickness(6, 0, 10, 0) };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true && _type != t) { _type = t; _version = null; Reload(); } };
            typeRow.Children.Add(rb);
        }
        var scopeRow = new WrapPanel();
        scopeRow.Children.Add(Ui.Label("Phạm vi", true));
        var rbSet = new RadioButton { Content = "Theo bộ", GroupName = "sc", IsChecked = _perSet, Margin = new Avalonia.Thickness(6, 0, 10, 0) };
        var rbVar = new RadioButton { Content = "Theo chủng loại", GroupName = "sc", IsChecked = !_perSet, IsEnabled = _type != BomType.DongGoi, Margin = new Avalonia.Thickness(6, 0, 10, 0) };
        rbSet.IsCheckedChanged += (_, _) => { if (rbSet.IsChecked == true && !_perSet) { _perSet = true; _version = null; Reload(); } };
        rbVar.IsCheckedChanged += (_, _) => { if (rbVar.IsChecked == true && _perSet) { _perSet = false; _version = null; Reload(); } };
        scopeRow.Children.Add(rbSet); scopeRow.Children.Add(rbVar);
        if (!_perSet) scopeRow.Children.Add(Ui.Combo(variants.Select(v => new Opt<long?>($"{v.Code} – {v.Name}", v.Id)), _variant, 240, v => { _variant = v; _version = null; Reload(); }));
        if (_type == BomType.DongGoi) scopeRow.Children.Add(Ui.Hint("BOM đóng gói luôn tính theo bộ."));

        var sameCust = X.Q.Products(p.CustomerId).Where(x => x.Id != p.Id && x.VariantCount > 0).Select(x => new Opt<long?>($"{x.Code} – {x.Name}", x.Id)).ToList();
        _copyFrom ??= sameCust.FirstOrDefault()?.Value;
        var head = Ui.Card(null,
            Ui.Row(Ui.Label("Mã hàng", true), prodCb, Ui.Chip("Khách hàng: " + p.CustomerCode, "muted"), Ui.Chip("Size: " + string.Join(", ", sizes), "muted"),
                Ui.Btn("Xem định mức một bộ", () => App.Main.Open("Định mức một bộ", new ReportScreen("norm") { ProductId = p.Id }))),
            sameCust.Count == 0 ? null : Ui.Row(Ui.Label("Chép toàn bộ BOM từ mã hàng"), Ui.Combo(sameCust, _copyFrom, 300, v => _copyFrom = v),
                Ui.BtnAsync("Chép BOM", async () =>
                {
                    var from = _copyFrom ?? throw new BusinessException("Chọn mã hàng nguồn.");
                    if (!await Dialog.Confirm("Chép BOM", $"Chép phiên bản mới nhất của mọi BOM từ mã hàng nguồn sang {p.Code}? BOM theo chủng loại ghép theo MÃ chủng loại; mỗi BOM đích có thêm một phiên bản mới (không xóa phiên bản cũ).", "Chép BOM")) return;
                    var (n, notes) = X.Bom.CopyFromProduct(from, p.Id, DateTime.Now);
                    _version = null; _editFor = null; Reload();
                    if (notes.Count > 0) await Dialog.Info("Đã chép BOM", $"Đã chép {n} BOM.\n" + string.Join("\n", notes));
                    App.Main.Status($"Đã chép {n} BOM sang {p.Code}.", "ok");
                }), Ui.Hint("Cùng khách hàng; size không có ở lưới đích được bỏ và báo lại.")),
            typeRow, scopeRow,
            Ui.Hint(_perSet ? "Số lượng tính theo số bộ; định mức nhập cho 1 bộ." : "Số lượng tính theo số sản phẩm của chủng loại; định mức nhập cho 1 sản phẩm."));

        // BOM hiện tại hoặc xung đột
        long? bomId = _perSet ? X.Bom.FindSet(p.Id, _type) : _variant is long vid ? X.Bom.Find(vid, _type) : null;
        string? conflict = null;
        if (_perSet && allBoms.Any(b => b.Type == code && !b.PerSet)) conflict = $"Mã hàng đã có BOM {BomTypes.Label(_type)} theo chủng loại.";
        if (!_perSet && allBoms.Any(b => b.Type == code && b.PerSet)) conflict = $"Mã hàng đã có BOM {BomTypes.Label(_type)} theo bộ.";

        Control body;
        if (conflict is not null) body = Ui.Banner(conflict + " Một loại BOM chỉ khai báo theo bộ hoặc theo chủng loại để không tính trùng.", "warn");
        else if (!_perSet && _variant is null) body = Ui.Banner("Mã hàng chưa có chủng loại. Khai báo chủng loại trước.", "warn");
        else if (bomId is null)
            body = Ui.Card(null, Ui.Banner($"Chưa có BOM {BomTypes.Label(_type)} {(_perSet ? "theo bộ" : "cho chủng loại " + variants.First(v => v.Id == _variant).Code)}.", "warn"),
                Ui.Row(Ui.Btn("Tạo BOM", () => Ui.Try(() =>
                {
                    if (_perSet) X.Bom.CreateSetBom(p.Id, _type); else X.Bom.CreateBom(_variant!.Value, _type);
                    Reload();
                }, "Đã tạo BOM. Nhập định mức rồi lưu phiên bản đầu tiên."), primary: true)));
        else body = BomBody(bomId.Value, p, sizes);

        var overview = Ui.Card("Các BOM của mã hàng",
            Ui.List(allBoms, new (string, Func<BomRow, string>, bool)[]
            {
                ("BOM", b => b.Name, false), ("Loại", b => b.Type switch { "TO_CAT" => "Tổ cắt", "SAN_XUAT" => "Sản xuất", "DONG_GOI" => "Đóng gói", _ => b.Type }, false),
                ("Phạm vi", b => b.PerSet ? "Theo bộ" : b.VariantCode.Length > 0 ? "Chủng loại " + b.VariantCode : "Cả mã hàng (cũ)", false), ("Số phiên bản", b => b.VersionCount.ToString(), true)
            }, maxHeight: 260));
        return Ui.Col(head, body, overview);
    }

    Control BomBody(long bomId, ProductRow p, List<string> sizes)
    {
        var versions = X.Q.BomVersions(bomId);
        if (_version is null || versions.All(v => v.Id != _version)) _version = versions.FirstOrDefault()?.Id;
        if (_editFor != bomId) { _edit.Clear(); _editFor = bomId; if (_version is long cur) CopyLines(cur); }
        var info = X.Bom.Info(bomId);
        var current = _version is long vv ? X.Q.BomLines(vv) : new List<BomLineRow>();
        var mats = Opts.Materials(p.CustomerId);
        var sizeOpts = new[] { new Opt<string>("* (nền)", "*") }.Concat(sizes.Select(s => new Opt<string>(s, s))).ToList();
        var editor = new LinesEditor(_edit, mats, info.PerSet ? "Định mức / bộ" : "Định mức / SP", sizeOpts, waste: true);

        return Ui.Col(
            Ui.Card($"{info.Name}",
                versions.Count == 0 ? Ui.Banner("BOM chưa có phiên bản nào: nhập định mức ở dưới rồi lưu phiên bản.", "warn")
                : Ui.Row(Ui.Label("Phiên bản"), Ui.Combo(versions.Select(v => new Opt<long?>($"v{v.No} – {Ui.DT(v.CreatedAt)}", v.Id)), _version, 240, v => { _version = v; Reload(); })),
                versions.Count == 0 ? null : Ui.List(current, new (string, Func<BomLineRow, string>, bool)[]
                {
                    ("Mã NPL", l => l.Material, false), ("Tên", l => l.MaterialName, false), ("Size", l => l.Size == "*" ? "* (nền)" : l.Size, false),
                    ("Định mức", l => Ui.N(l.Usage), true), ("Hao hụt %", l => Ui.N(l.Waste), true), ("Ghi chú", l => l.Size == "*" ? "" : "ghi đè nền", false)
                }, maxHeight: 300)),
            Ui.Card("Tạo phiên bản mới",
                Ui.Hint("Size '*' là định mức nền; dòng size cụ thể thay thế dòng nền cho đúng size đó. Chỉ chọn được NPL của khách hàng " + p.CustomerCode + ". Có lỗi thì không lưu dòng nào."),
                editor.View,
                Ui.Row(
                    Ui.Btn("Lưu phiên bản mới", () => Ui.Try(() =>
                    {
                        var lines = _edit.Where(l => !string.IsNullOrEmpty(l.Mat)).Select((l, i) => new BomLineInput(l.Mat!, l.Size ?? "*",
                            Ui.Dec(l.Qty, $"định mức dòng {i + 1}"), string.IsNullOrWhiteSpace(l.Waste) ? 0m : Ui.Dec(l.Waste, $"hao hụt dòng {i + 1}"))).ToList();
                        _version = X.Bom.AddVersion(bomId, DateTime.Now, lines); Reload();
                    }, "Đã lưu phiên bản BOM mới."), primary: true),
                    _version is long v2 ? Ui.Btn("Chép từ phiên bản đang xem", () => { CopyLines(v2); Reload(); }) : null,
                    Ui.BtnAsync("Tải file mẫu Excel", async () =>
                    {
                        var path = await Ui.SavePath("Lưu file mẫu BOM", $"Mau BOM {info.Name}", "xlsx", "Bảng tính Excel");
                        if (path is null) return;
                        await using (var fs = File.Create(path)) BomExcel.WriteTemplate(X.C, bomId, fs);
                        App.Main.Status("Đã lưu file mẫu: " + path, "ok");
                    }),
                    Ui.BtnAsync("Nhập từ Excel → phiên bản mới", async () =>
                    {
                        var path = await Ui.OpenPath("Chọn file BOM", "xlsx", "Bảng tính Excel");
                        if (path is null) return;
                        await using (var fs = File.OpenRead(path)) _version = BomExcel.Import(X.Bom, bomId, fs, DateTime.Now);
                        _editFor = null; Reload();
                        App.Main.Status("Đã nhập BOM từ Excel thành phiên bản mới.", "ok");
                    }))));
    }

    void CopyLines(long versionId)
    {
        _edit.Clear();
        _edit.AddRange(X.Q.BomLines(versionId).Select(l => new LineState { Mat = l.Material, Size = l.Size, Qty = Ui.N(l.Usage), Waste = l.Waste == 0 ? "" : Ui.N(l.Waste) }));
    }
}

public sealed class OrderScreen : Screen
{
    public override string Title => "Đơn hàng";
    public override string Subtitle => "Chọn mã hàng (khách hàng tự lấy theo mã hàng), nhập số bộ theo size. Hệ thống tự phân rã theo chủng loại và chụp cấu hình + phiên bản BOM tại ngày lập đơn.";
    long? _prod; OrderRow? _sel; bool _openOnly = true;
    readonly Dictionary<string, string> _sets = new();

    public OrderScreen(long? orderId = null) { if (orderId is long id) _sel = X.Q.Orders().FirstOrDefault(o => o.Id == id); }

    protected override Control Build()
    {
        var prods = Opts.Products();
        _prod ??= prods.FirstOrDefault()?.Value;
        // Đơn đang chọn (các chặng + việc tiếp theo) lên trên; lập đơn mới ở dưới
        return Ui.Col(ListCard(), _sel is null ? null : DetailCard(_sel), NewOrderCard(prods));
    }

    Control NewOrderCard(List<Opt<long?>> prods)
    {
        var prodCb = Ui.Combo(prods, _prod, 340, v => { _prod = v; _sets.Clear(); Reload(); });
        if (_prod is null) return Ui.Card("Lập đơn hàng", Ui.Row(Ui.Label("Mã hàng", true), prodCb), Ui.Banner("Chưa có mã hàng.", "warn"));
        var p = X.Q.Products().Single(x => x.Id == _prod);
        var no = Ui.Input(140, null, X.Q.NextNo("DH"));
        var date = Ui.DateBox(DateTime.Today); var due = Ui.DateBox();
        var sizesPanel = new WrapPanel();
        var sizes = X.Q.ProductSizes(p.Id);
        foreach (var s in sizes)
        {
            var tb = Ui.Input(80, "0", _sets.GetValueOrDefault(s)); tb.HorizontalContentAlignment = HorizontalAlignment.Right;
            tb.TextChanged += (_, _) => _sets[s] = tb.Text ?? "";
            sizesPanel.Children.Add(new StackPanel { Margin = new Avalonia.Thickness(0, 0, 10, 6), Children = { Ui.Text(s, Ui.Muted), tb } });
        }
        return Ui.Card("Lập đơn hàng",
            Ui.Row(Ui.Label("Mã hàng", true), prodCb, Ui.Chip("Khách hàng: " + p.CustomerCode, "muted")),
            Ui.Row(Ui.Label("Số đơn", true), no, Ui.Label("Ngày đặt", true), date, Ui.Label("Ngày giao dự kiến"), due),
            Ui.Text("Số bộ theo size:", Ui.Muted), sizesPanel,
            Ui.Row(Ui.BtnAsync("Lưu đơn", async () =>
            {
                var lines = sizes.Where(s => !string.IsNullOrWhiteSpace(_sets.GetValueOrDefault(s)))
                    .Select(s => new OrderLine(s, Ui.Dec(_sets[s], "số bộ size " + s))).Where(l => l.Sets != 0).ToList();
                var orderDate = date.SelectedDate ?? DateTime.Today;
                // Kiểm tra BOM TRƯỚC khi lưu: thiếu định mức thì hỏi lại (đơn thiếu dữ liệu sẽ không cấp phát được)
                var preview = OrderService.Calculate(X.Orders.BuildSnapshot(p.Id, orderDate), lines);
                if (lines.Count > 0 && !preview.IsComplete && !await Dialog.Confirm("BOM chưa đủ cho đơn này",
                        "Đơn sẽ ở trạng thái CHƯA ĐỦ DỮ LIỆU và chưa cấp phát được:\n" + string.Join("\n", preview.Issues.Select(i => "  • " + i.Message).Distinct().Take(12)) +
                        (preview.Issues.Count > 12 ? "\n  …" : "") + "\n\nNên khai báo đủ BOM trước. Vẫn lưu đơn?", "Vẫn lưu đơn")) return;
                var id = X.Orders.CreateOrder(p.Id, Ui.Req(no, "số đơn"), orderDate, lines, due.SelectedDate);
                _sets.Clear(); _sel = X.Q.Orders().First(o => o.Id == id); Reload();
                App.Main.Status("Đã lưu đơn hàng.", "ok");
            }, primary: true)));
    }

    Control ListCard()
    {
        var cb = new CheckBox { Content = "Chỉ đơn đang mở", IsChecked = _openOnly };
        cb.IsCheckedChanged += (_, _) => { _openOnly = cb.IsChecked == true; Reload(); };
        return Ui.Card("Danh sách đơn hàng", cb,
            Ui.SearchList("orders", X.Q.Orders(status: _openOnly ? "OPEN" : null), new (string, Func<OrderRow, string>, bool)[]
            {
                ("Số đơn", o => o.No, false), ("Khách hàng", o => o.CustomerCode, false), ("Mã hàng", o => o.ProductCode, false), ("Ngày đặt", o => Ui.D(o.OrderDate), false),
                ("Ngày giao", o => o.DueDate is DateTime d ? Ui.D(d) : "", false), ("Tổng số bộ", o => Ui.N(o.TotalSets), true), ("Trạng thái", o => o.Status == "OPEN" ? "Đang mở" : "Đã đóng", false)
            }, o => { if (o is not null && o.Id != _sel?.Id) { _sel = o; Reload(); } }, 300, o => o.Id == _sel?.Id));
    }

    /// <summary>Vòng đời đơn: 7 chặng, trạng thái từng chặng và nút việc tiếp theo (mở phiếu điền sẵn).</summary>
    Control Stages(OrderRow o)
    {
        var stages = X.Flow.Stages(o.Id);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", stages.Select(_ => "*"))), Margin = new Avalonia.Thickness(0, 4, 0, 8) };
        for (var i = 0; i < stages.Count; i++)
        {
            var st = stages[i];
            var (bg, fg, mark) = st.State switch
            {
                "done" => (Ui.OkSoft, Ui.Ok, "✓"), "bad" => (Ui.BadSoft, Ui.Bad, "!"), "warn" => (Ui.WarnSoft, Ui.Warn, "!"),
                "active" => (Ui.AccentSoft, Ui.Accent, "→"), _ => (Ui.Surface2, Ui.Muted, "·")
            };
            var box = new StackPanel { Spacing = 2, Children = {
                new TextBlock { Text = $"{mark} {i + 1}. {st.Name}", Foreground = fg, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = st.Detail, Foreground = Ui.Ink, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap } } };
            var target = o; var action = st.Action;
            if (action is not null && o.Status == "OPEN")
                box.Children.Add(Ui.Btn(action switch { "bom" => "Khai báo BOM", "check" => "Kiểm tra", "receipt-short" => "Nhập phần thiếu", "issue" => "Cấp phát", "settle" => "Quyết toán", "close" => "Đóng đơn", _ => "Mở" }, () =>
                {
                    switch (action)
                    {
                        case "bom": App.Main.Open("BOM & định mức", new BomScreen(target.ProductId)); break;
                        case "check": App.Main.Open("Kiểm tra đáp ứng", new CheckScreen(target.Id)); break;
                        case "receipt-short": App.Main.Open("Phiếu nhập", new ReceiptScreen(shortForOrder: target.Id)); break;
                        case "issue": App.Main.Open("Phiếu xuất (cấp phát)", new IssueScreen(target.Id)); break;
                        case "settle": App.Main.Open("Quyết toán & đóng đơn", new SettleScreen(target.Id)); break;
                        case "close": _ = SettleScreen.CloseFlow(target.Id, target.No, Reload); break;
                    }
                }, primary: st.State is "active" or "bad"));
            var b = new Border { Background = bg, BorderBrush = fg, BorderThickness = new Avalonia.Thickness(0, 3, 0, 0), Padding = new Avalonia.Thickness(8, 6), Margin = new Avalonia.Thickness(0, 0, 4, 0), Child = box };
            Grid.SetColumn(b, i); grid.Children.Add(b);
        }
        return grid;
    }

    Control DetailCard(OrderRow o)
    {
        o = X.Q.Orders().First(x => x.Id == o.Id);
        var d = X.Orders.CalculateDemand(o.Id);
        var lines = X.Orders.LoadLines(o.Id);
        var names = X.Q.Variants(o.ProductId).ToDictionary(v => v.Code, v => v.Name);
        return Ui.Card($"Đơn {o.No} – {o.ProductCode} ({o.CustomerCode})",
            Ui.Row(Ui.Chip(o.Status == "OPEN" ? "Đang mở" : "Đã đóng", o.Status == "OPEN" ? "ok" : "muted"),
                Ui.Text($"Ngày đặt {Ui.D(o.OrderDate)} · Số bộ: " + string.Join(", ", lines.Select(l => $"{l.DefaultSize}: {Ui.N(l.Sets)}"))),
                Ui.Btn("Kiểm tra đáp ứng", () => App.Main.Open("Kiểm tra đáp ứng", new CheckScreen(o.Id))),
                Ui.Btn("Điều chỉnh đơn", () => App.Main.Open("Điều chỉnh đơn", new AdjustScreen(o.Id))),
                Ui.Btn("Cấp phát", () => App.Main.Open("Phiếu xuất (cấp phát)", new IssueScreen(o.Id))),
                Ui.Btn("Quyết toán", () => App.Main.Open("Quyết toán & đóng đơn", new SettleScreen(o.Id))),
                o.Status == "OPEN"
                    ? Ui.BtnAsync("Đóng đơn", () => SettleScreen.CloseFlow(o.Id, o.No, Reload))
                    : Ui.Btn("Mở lại đơn", () => Ui.Try(() => { X.Orders.SetStatus(o.Id, true); Reload(); }, "Đã mở lại đơn."))),
            Stages(o),
            d.IsComplete ? null : Ui.Banner("CHƯA ĐỦ DỮ LIỆU: " + string.Join("; ", d.Issues.Select(i => i.Message).Distinct()), "warn"),
            Ui.Text("Phân rã theo chủng loại và size:", Ui.Muted),
            Ui.List(d.Breakdown, new (string, Func<BreakdownRow, string>, bool)[]
            {
                ("Chủng loại", b => $"{b.Variant} – {names.GetValueOrDefault(b.Variant, "")}", false), ("Size", b => b.VariantSize, false), ("Số lượng", b => Ui.N(b.Qty), true)
            }, maxHeight: 320));
    }
}

public sealed class AdjustScreen : Screen
{
    public override string Title => "Điều chỉnh đơn";
    public override string Subtitle => "Xem chênh lệch nhu cầu trước khi lưu; bắt buộc có lý do. Phiếu đã xác nhận không bị thay đổi, chỉ nhu cầu còn lại thay đổi (phiếu cấp sau tự bù theo sổ cân đối).";
    long? _order; bool _rebind; AdjustmentPreview? _preview; string _reason = "";
    readonly Dictionary<string, string> _sets = new();

    public AdjustScreen(long? orderId = null) { _order = orderId; }

    protected override Control Build()
    {
        var orders = Opts.Orders();
        if (_order is null || orders.All(o => o.Value != _order)) { _order = orders.FirstOrDefault()?.Value; _sets.Clear(); _preview = null; }
        var orderCb = Ui.Combo(orders, _order, 360, v => { _order = v; _sets.Clear(); _preview = null; Reload(); });
        if (_order is null) return Ui.Col(Ui.Row(Ui.Label("Đơn hàng", true), orderCb), Ui.Banner("Không có đơn đang mở.", "warn"));
        var o = X.Q.Orders().First(x => x.Id == _order);
        var cur = X.Orders.LoadLines(o.Id).ToDictionary(l => l.DefaultSize, l => l.Sets);
        var sizes = X.Q.ProductSizes(o.ProductId);
        var grid = new WrapPanel();
        foreach (var s in sizes)
        {
            if (!_sets.ContainsKey(s)) _sets[s] = cur.TryGetValue(s, out var q) ? Ui.N(q) : "";
            var tb = Ui.Input(80, "0", _sets[s]); tb.HorizontalContentAlignment = HorizontalAlignment.Right;
            tb.TextChanged += (_, _) => { _sets[s] = tb.Text ?? ""; };
            grid.Children.Add(new StackPanel { Margin = new Avalonia.Thickness(0, 0, 10, 6), Children = { Ui.Text($"{s} (hiện {Ui.N(cur.GetValueOrDefault(s))})", Ui.Muted), tb } });
        }
        var rebind = new CheckBox { Content = "Áp dụng phiên bản BOM mới nhất cho phần chưa cấp", IsChecked = _rebind };
        rebind.IsCheckedChanged += (_, _) => { _rebind = rebind.IsChecked == true; _preview = null; };
        var reason = Ui.Input(420, "Lý do điều chỉnh (bắt buộc)", _reason); reason.TextChanged += (_, _) => _reason = reason.Text ?? "";
        List<OrderLine> NewLines() => sizes.Where(s => !string.IsNullOrWhiteSpace(_sets.GetValueOrDefault(s)))
            .Select(s => new OrderLine(s, Ui.Dec(_sets[s], "số bộ size " + s))).Where(l => l.Sets != 0).ToList();

        return Ui.Col(
            Ui.Row(Ui.Label("Đơn hàng", true), orderCb),
            Ui.Card("Số bộ mới", grid, rebind, Ui.Row(Ui.Label("Lý do", true), reason),
                Ui.Row(Ui.Btn("Xem chênh lệch", () => Ui.Try(() => { _preview = X.Adjust.Preview(o.Id, NewLines(), _rebind); Reload(); })),
                    Ui.Btn("Lưu điều chỉnh", () => Ui.Try(() =>
                    {
                        X.Adjust.Apply(o.Id, NewLines(), _reason, _rebind);
                        _preview = null; _reason = ""; _sets.Clear(); Reload();
                    }, "Đã lưu điều chỉnh, tạo phiên bản cấu hình mới và ghi nhật ký."), primary: true))),
            _preview is null ? null : Ui.Card("Chênh lệch nhu cầu theo mã NPL",
                _preview.Issues.Count > 0 ? Ui.Banner(string.Join("\n", _preview.Issues.Select(i => i.Message)), "warn") : null,
                _preview.Warnings.Count > 0 ? Ui.Banner(string.Join("\n", _preview.Warnings), "warn") : null,
                Ui.List(_preview.Deltas, new (string, Func<MaterialDelta, string>, bool)[]
                {
                    ("Mã NPL", d => d.Material, false), ("Trước", d => Ui.N(d.Before), true), ("Sau", d => Ui.N(d.After), true),
                    ("Chênh lệch", d => (d.Diff > 0 ? "+" : "") + Ui.N(d.Diff), true), ("Đã cấp", d => Ui.N(d.Issued), true)
                }, maxHeight: 320)),
            Ui.Card("Lịch sử cấu hình của đơn",
                Ui.List(X.Adjust.History(o.Id), new (string, Func<OrderVersionInfo, string>, bool)[]
                {
                    ("Phiên bản", v => v.No.ToString(), true), ("Thời điểm", v => Ui.DT(v.At), false), ("Lý do", v => v.Reason ?? "", false), ("BOM áp dụng", v => v.BomSummary, false)
                }, maxHeight: 240)));
    }
}
