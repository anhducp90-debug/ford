using Avalonia.Controls;
using Avalonia.Layout;
using QuanLyNPL.Core;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

internal static class DocText
{
    public static string Type(DocRow d) => d.Kind == TransferService.Kind ? (d.Type == "OUT" ? "Chuyển sang KH khác" : "Nhận từ KH khác") : d.Type switch
    {
        "IN" => "Nhập", "OUT" => d.RefDocId is not null ? (d.SubKind == "BU_HAO_HUT" ? "Bù hao hụt" : "Bù thiếu") : d.Scope is not null ? "Cấp phát" : "Xuất", "RETURN" => "Trả",
        "ADJUST_UP" => "Điều chỉnh tăng", "ADJUST_DOWN" => "Điều chỉnh giảm", _ => d.Type
    };
    public static string Status(string s) => s == "DRAFT" ? "Nháp" : "Đã xác nhận";
    public static string Scope(string? s) => IssueScopes.Label(s);

    public static (string, Func<DocRow, string>, bool)[] Cols => new (string, Func<DocRow, string>, bool)[]
    {
        ("Số phiếu", d => d.DocNo, false), ("Loại", Type, false), ("Khách hàng", d => d.CustomerCode, false), ("Đơn", d => d.OrderNo, false),
        ("Nơi nhận cấp", d => d.IssuePlace, false), ("Loại BOM", d => Scope(d.Scope), false), ("Nơi xuất", d => d.FromPlace, false), ("Nơi nhận", d => d.ToPlace, false), ("Trạng thái", d => Status(d.Status), false), ("Tạo lúc", d => Ui.DT(d.CreatedAt), false),
        ("Xác nhận lúc", d => d.ConfirmedAt is DateTime t ? Ui.DT(t) : "", false), ("Ghi chú", d => d.Note ?? "", false)
    };

    public static List<Opt<long?>> PlaceOpts(bool fromSide) =>
        new[] { new Opt<long?>("(chưa chọn)", null) }.Concat(App.X.Q.Places(fromSide, activeOnly: true).Select(p => new Opt<long?>($"{p.Code} – {p.Name}", p.Id))).ToList();

    /// <summary>Nơi xuất / nơi nhận (sửa được khi phiếu còn nháp) + in phiếu PDF.</summary>
    public static Control PlacesAndPrint(DocRow d, Action reload)
    {
        if (d.Status != "DRAFT" || d.Kind == TransferService.Kind)
            return Ui.Row(Ui.Text($"Nơi xuất: {(d.FromPlace.Length > 0 ? d.FromPlace : "—")}   ·   Nơi nhận: {(d.ToPlace.Length > 0 ? d.ToPlace : "—")}", Ui.Muted), PrintButton(d.Id));
        var from = Ui.Combo(PlaceOpts(true), d.FromPlaceId, 240);
        var to = Ui.Combo(PlaceOpts(false), d.ToPlaceId, 240);
        return Ui.Row(Ui.Label("Nơi xuất"), from, Ui.Label("Nơi nhận"), to,
            Ui.Btn("Lưu nơi xuất/nhận", () => Ui.Try(() => { App.X.Places.SetDocPlaces(d.Id, Ui.Sel<long?>(from), Ui.Sel<long?>(to)); reload(); }, "Đã lưu nơi xuất / nơi nhận.")),
            PrintButton(d.Id));
    }

    public static Button PrintButton(long docId) => Ui.BtnAsync("In phiếu (PDF)", async () =>
    {
        var d = App.X.Q.Docs().First(x => x.Id == docId);
        var path = await Ui.SavePath("Lưu phiếu PDF", $"{d.DocNo} {VoucherPdf.Title(d)}", "pdf", "Tệp PDF");
        if (path is null) return;
        await using (var fs = File.Create(path)) VoucherPdf.Generate(App.X.C, docId, fs);
        App.Main.Status("Đã lưu phiếu: " + path, "ok");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* không có trình xem PDF thì bỏ qua */ }
    });

    public static Control Lines(long docId)
    {
        var lines = App.X.Q.DocLines(docId);
        var cols = new List<(string, Func<DocLineRow, string>, bool)>
        {
            ("Mã NPL", l => l.Material, false), ("Tên", l => l.MaterialName, false), ("ĐVT", l => l.Unit, false),
            ("Đề xuất", l => l.Suggested is decimal s ? Ui.N(s) : "", true), ("Số lượng", l => l.Entered ? Ui.N(l.Qty) : "(chưa nhập)", true)
        };
        if (lines.Any(l => l.EntryUnit is not null)) cols.Add(("Nhập theo / quy đổi", l => l.EntryText, false));
        return Ui.List(lines, cols.ToArray(), maxHeight: 300);
    }

    /// <summary>Cột danh sách phiếu trong một đơn (bỏ cột khách hàng / đơn trùng lặp).</summary>
    public static (string, Func<DocRow, string>, bool)[] OrderCols => new (string, Func<DocRow, string>, bool)[]
    {
        ("Số phiếu", d => d.DocNo, false), ("Loại", Type, false), ("Nơi nhận cấp", d => d.IssuePlace, false), ("Loại BOM", d => Scope(d.Scope), false),
        ("Trạng thái", d => Status(d.Status), false), ("Tạo lúc", d => Ui.DT(d.CreatedAt), false), ("Xác nhận lúc", d => d.ConfirmedAt is DateTime t ? Ui.DT(t) : "", false), ("Ghi chú", d => d.Note ?? "", false)
    };

    /// <summary>Cột danh sách phiếu nhập (không có cột cấp phát).</summary>
    public static (string, Func<DocRow, string>, bool)[] InCols => new (string, Func<DocRow, string>, bool)[]
    {
        ("Số phiếu", d => d.DocNo, false), ("Loại", Type, false), ("Khách hàng", d => d.CustomerCode, false), ("Số hóa đơn", d => d.InvoiceNo ?? "", false),
        ("Nơi xuất (bên giao)", d => d.FromPlace, false), ("Nơi nhận", d => d.ToPlace, false), ("Trạng thái", d => Status(d.Status), false),
        ("Tạo lúc", d => Ui.DT(d.CreatedAt), false), ("Xác nhận lúc", d => d.ConfirmedAt is DateTime t ? Ui.DT(t) : "", false), ("Ghi chú", d => d.Note ?? "", false)
    };
}

public sealed class ReceiptScreen : Screen
{
    public override string Title => "Phiếu nhập kho";
    public override string Subtitle => "Mỗi phiếu thuộc một khách hàng. Phiếu nháp không đổi tồn; chỉ khi xác nhận mới ghi sổ. Ghi số hóa đơn để đối chiếu. Có thể nạp phần đang thiếu của đơn, nhập dòng từ Excel, nhập vải theo kg (quy ra m theo hệ số của lô).";
    DocRow? _sel; long? _cust; string _no = "", _note = "", _invoice = ""; long? _shortOrder;
    long? _fromPlace, _toPlace = App.X.Places.DefaultInTo;
    readonly List<LineState> _lines = new();

    public ReceiptScreen(long? docId = null, long? shortForOrder = null)
    {
        if (docId is long d && X.Q.Docs("IN").FirstOrDefault(x => x.Id == d) is { } doc) Load(doc);
        if (shortForOrder is long o) { _shortOrder = o; _cust = X.Q.Orders().FirstOrDefault(x => x.Id == o)?.CustomerId; LoadShortage(o); }
    }

    void Load(DocRow d)
    {
        _sel = d;
        if (d is not { Status: "DRAFT", Kind: null }) return;
        _lines.Clear();
        _lines.AddRange(X.Q.DocLines(d.Id).Select(l => l.EntryUnit is not null
            ? new LineState { Mat = l.Material, Qty = Ui.N(l.EntryQty ?? 0), Unit = l.EntryUnit, Factor = Ui.N(l.EntryFactor ?? 0) }
            : new LineState { Mat = l.Material, Qty = Ui.N(l.Qty) }));
        _no = d.DocNo; _note = d.Note ?? ""; _invoice = d.InvoiceNo ?? ""; _cust = X.Q.Customers().FirstOrDefault(c => c.Code == d.CustomerCode)?.Id ?? _cust;
        _fromPlace = d.FromPlaceId; _toPlace = d.ToPlaceId;
    }

    /// <summary>Nạp phần đang thiếu của đơn (nhu cầu còn lại − tồn khả dụng) vào bảng dòng.</summary>
    void LoadShortage(long orderId)
    {
        var shortage = X.Stock.CheckOrder(orderId, X.Orders).Lines.Where(l => l.Shortage > 0).ToList();
        _lines.RemoveAll(l => string.IsNullOrEmpty(l.Mat));
        foreach (var l in shortage)
        {
            var ex = _lines.FirstOrDefault(x => x.Mat == l.MaterialCode);
            if (ex is null) _lines.Add(new LineState { Mat = l.MaterialCode, Qty = Ui.N(Math.Ceiling(l.Shortage * 100) / 100) });
            else { ex.Qty = Ui.N(Math.Ceiling(l.Shortage * 100) / 100); ex.Unit = null; ex.Factor = ""; }
        }
        var o = X.Q.Orders().First(x => x.Id == orderId);
        if (_note.Length == 0) _note = "Nhập cho đơn " + o.No;
        App.Main?.Status(shortage.Count == 0 ? $"Đơn {o.No} không thiếu NPL." : $"Đã nạp {shortage.Count} mã đang thiếu của đơn {o.No}. Sửa số lượng theo hóa đơn thực nhận.", shortage.Count == 0 ? "ok" : "warn");
    }

    protected override Control Build()
    {
        var docs = X.Q.Docs("IN");
        if (_sel is not null) _sel = docs.FirstOrDefault(d => d.Id == _sel.Id);
        var editingDraft = _sel is { Status: "DRAFT", Kind: null };
        if (_no.Length == 0) _no = X.Q.NextNo("NK");
        var custs = Opts.Customers();
        _cust ??= custs.FirstOrDefault()?.Value;
        var no = Ui.Input(140, null, _no); no.TextChanged += (_, _) => _no = no.Text ?? ""; no.IsEnabled = !editingDraft;
        var invoice = Ui.Input(180, "Số hóa đơn / chứng từ", _invoice); invoice.TextChanged += (_, _) => _invoice = invoice.Text ?? "";
        var note = Ui.Input(260, "Ghi chú / nguồn", _note); note.TextChanged += (_, _) => _note = note.Text ?? "";
        var custCb = Ui.Combo(custs, _cust, 260, v => { _cust = v; _shortOrder = null; _lines.RemoveAll(l => l.Mat is not null && X.Cat.MaterialCustomerId(l.Mat) != v); Reload(); });
        custCb.IsEnabled = !editingDraft;
        var matRows = X.Q.Materials(_cust).ToDictionary(m => m.Code);
        var editor = new LinesEditor(_lines, Opts.Materials(_cust), "Số lượng", units: code => matRows.GetValueOrDefault(code));
        var orders = X.Q.Orders(_cust, "OPEN").Select(o => new Opt<long?>($"{o.No} – {o.ProductCode}", o.Id)).ToList();

        var form = Ui.Card(editingDraft ? $"Phiếu nháp {_sel!.DocNo}" : "Phiếu nhập mới",
            Ui.Row(Ui.Label("Khách hàng", true), custCb, Ui.Label("Số phiếu", true), no, Ui.Label("Số hóa đơn"), invoice, Ui.Label("Ghi chú"), note),
            Ui.Row(Ui.Label("Nơi xuất (bên giao)"), Ui.Combo(DocText.PlaceOpts(true), _fromPlace, 260, v => _fromPlace = v),
                Ui.Label("Nơi nhận"), Ui.Combo(DocText.PlaceOpts(false), _toPlace, 260, v => _toPlace = v),
                editingDraft ? DocText.PrintButton(_sel!.Id) : null),
            Ui.Row(Ui.Label("Nạp phần đang thiếu của đơn"), orders.Count == 0 ? Ui.Hint("(khách hàng không có đơn đang mở)") : Ui.Combo(orders, _shortOrder, 300, v => _shortOrder = v),
                orders.Count == 0 ? null : Ui.Btn("Nạp phần thiếu", () => Ui.Try(() => { LoadShortage(_shortOrder ?? throw new BusinessException("Chọn đơn hàng.")); Reload(); })),
                Ui.BtnAsync("Tải mẫu Excel", async () =>
                {
                    var path = await Ui.SavePath("Lưu mẫu phiếu nhập", "Mau phieu nhap", "xlsx", "Bảng tính Excel");
                    if (path is null) return;
                    await using (var fs = File.Create(path)) ReceiptExcel.WriteTemplate(matRows.Values.Where(m => m.Active), fs);
                    App.Main.Status("Đã lưu mẫu: " + path, "ok");
                }),
                Ui.BtnAsync("Nhập dòng từ Excel", async () =>
                {
                    var path = await Ui.OpenPath("Chọn file phiếu nhập", "xlsx", "Bảng tính Excel");
                    if (path is null) return;
                    List<ReceiptExcelLine> rows;
                    await using (var fs = File.OpenRead(path)) rows = ReceiptExcel.Read(fs);
                    ImportRows(rows, matRows); Reload();
                })),
            editor.View,
            Ui.Row(
                Ui.Btn("Lưu nháp", () => Ui.Try(() => { Save(); Reload(); }, "Đã lưu phiếu nháp."), primary: !editingDraft),
                Ui.BtnAsync("Xác nhận nhập", async () =>
                {
                    var id = Save();
                    var lines = X.Q.DocLines(id);
                    if (!await Dialog.Confirm("Xác nhận nhập kho", $"Xác nhận phiếu {X.Q.Docs("IN").First(d => d.Id == id).DocNo}?\n" +
                        string.Join("\n", lines.Select(l => $"  {l.Material}: +{Ui.N(l.Qty)} {l.Unit}" + (l.EntryUnit is null ? "" : $" ({l.EntryText})"))) + "\n\nSau khi xác nhận, phiếu không sửa được.", "Xác nhận")) { Reload(); return; }
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
                Ui.SearchList("receipts", docs, DocText.InCols, d =>
                {
                    if (d is null || d.Id == _sel?.Id) return;
                    Load(d); Reload();
                }, 300, d => d.Id == _sel?.Id)),
            _sel is { } s && !(s is { Status: "DRAFT", Kind: null }) ? Ui.Card($"Chi tiết {s.DocNo} ({DocText.Type(s)} – {DocText.Status(s.Status)})" + (s.InvoiceNo is null ? "" : $" · HĐ {s.InvoiceNo}"), DocText.PlacesAndPrint(s, Reload), DocText.Lines(s.Id)) : null);
    }

    void ImportRows(List<ReceiptExcelLine> rows, Dictionary<string, MaterialRow> mats)
    {
        var errs = rows.Where(r => !mats.ContainsKey(r.Material)).Select(r => $"{r.Material}: không phải NPL của khách hàng đang chọn.").ToList();
        errs.AddRange(rows.Where(r => mats.TryGetValue(r.Material, out var m) && r.Unit is not null && !string.Equals(r.Unit, m.Unit, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(r.Unit, m.AltUnit, StringComparison.OrdinalIgnoreCase)).Select(r => $"{r.Material}: đơn vị {r.Unit} chưa khai báo quy đổi."));
        if (errs.Count > 0) throw new BusinessException(string.Join("\n", errs));
        _lines.RemoveAll(l => string.IsNullOrEmpty(l.Mat));
        foreach (var r in rows)
        {
            var m = mats[r.Material];
            var alt = r.Unit is not null && string.Equals(r.Unit, m.AltUnit, StringComparison.OrdinalIgnoreCase);
            _lines.Add(new LineState { Mat = r.Material, Qty = Ui.N(r.Qty), Unit = alt ? m.AltUnit : null, Factor = alt ? Ui.N(r.Factor ?? m.AltFactor ?? 0) : "" });
        }
        App.Main.Status($"Đã nạp {rows.Count} dòng từ Excel – kiểm tra rồi Lưu nháp / Xác nhận.", "ok");
    }

    long Save()
    {
        var rows = LinesEditor.ReadUnits(_lines);
        var lines = rows.Select(r => (r.mat, r.unit is null ? r.qty : Math.Round(r.qty * r.factor!.Value, 2, MidpointRounding.AwayFromZero))).ToList();
        var entries = rows.Where(r => r.unit is not null).Select(r => (r.mat, r.qty, r.unit!, r.factor!.Value)).ToList();
        long id;
        if (_sel is { Status: "DRAFT", Kind: null } d) { X.Stock.ReplaceDraftLines(d.Id, lines); X.Places.SetDocPlaces(d.Id, _fromPlace, _toPlace); id = d.Id; }
        else
        {
            id = X.Stock.CreateDraft("IN", _no.Trim(), lines, null, _note, _cust ?? throw new BusinessException("Chọn khách hàng."));
            X.Places.SetDocPlaces(id, _fromPlace, _toPlace);
        }
        X.Stock.ApplyEntries(id, entries);
        X.Stock.SetInvoice(id, _invoice);
        _sel = X.Q.Docs("IN").First(x => x.Id == id);
        return id;
    }

    void New() { _sel = null; _lines.Clear(); _no = ""; _note = ""; _invoice = ""; _fromPlace = null; _toPlace = X.Places.DefaultInTo; }
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
