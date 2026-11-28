using Avalonia.Controls;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

public sealed class BackupScreen : Screen
{
    public override string Title => "Sao lưu / Phục hồi";
    public override string Subtitle => "Toàn bộ dữ liệu nằm trong một tệp SQLite. Phần mềm tự sao lưu mỗi ngày khi mở (giữ 14 bản). Nên sao lưu ra USB/ổ khác định kỳ.";

    protected override Control Build()
    {
        var ver = Db.Scalar(X.C, "PRAGMA user_version");
        var size = new FileInfo(X.DbPath).Length;
        var autos = Directory.Exists(X.BackupDir) ? Directory.GetFiles(X.BackupDir, "*.db").OrderByDescending(f => f).ToList() : new List<string>();
        return Ui.Col(
            Ui.Card("Thông tin dữ liệu",
                Ui.Text("Tệp dữ liệu: " + X.DbPath), Ui.Text($"Phiên bản cấu trúc: {ver} · Dung lượng: {size / 1024.0:#,##0} KB"),
                Ui.Text("Thư mục sao lưu tự động: " + X.BackupDir)),
            Ui.Card("Sao lưu",
                Ui.Hint("Chọn nơi lưu bản sao (ví dụ ổ USB). Có thể sao lưu khi đang làm việc."),
                Ui.Row(Ui.BtnAsync("Sao lưu ngay…", async () =>
                {
                    var p = await Ui.SavePath("Sao lưu dữ liệu", BackupService.DefaultFileName(), "db", "Tệp dữ liệu");
                    if (p is null) return;
                    X.Backup.Backup(p);
                    App.Main.Status("Đã sao lưu: " + p, "ok");
                }, primary: true))),
            Ui.Card("Phục hồi",
                Ui.Banner("Phục hồi sẽ ghi đè toàn bộ dữ liệu hiện tại. Phần mềm tự sao lưu dữ liệu hiện tại vào thư mục sao lưu trước khi ghi đè.", "warn"),
                Ui.Row(Ui.BtnAsync("Phục hồi từ tệp…", async () =>
                {
                    var p = await Ui.OpenPath("Chọn tệp sao lưu", "db", "Tệp dữ liệu");
                    if (p is null) return;
                    BackupService.Validate(p);
                    if (!await Dialog.Confirm("Phục hồi dữ liệu", $"Ghi đè dữ liệu hiện tại bằng tệp:\n{p}\n\nThao tác này không hoàn tác được (ngoài bản sao lưu an toàn tự động).", "Phục hồi", danger: true)) return;
                    var safety = X.Backup.Restore(p, X.BackupDir);
                    App.Main.Status("Đã phục hồi. Bản an toàn trước khi phục hồi: " + safety, "ok");
                    Reload();
                }, danger: true))),
            Ui.Card("Các bản sao lưu tự động",
                Ui.List(autos, new (string, Func<string, string>, bool)[] { ("Tệp", f => Path.GetFileName(f), false), ("Thời gian", f => Ui.DT(File.GetLastWriteTime(f)), false), ("KB", f => (new FileInfo(f).Length / 1024).ToString("#,##0"), true) }, maxHeight: 240)));
    }
}

public sealed class SecurityScreen : Screen
{
    public override string Title => "Bảo mật & khóa PIN";
    public override string Subtitle => "Phần mềm dùng cho một người, chạy offline. Mã PIN lưu dạng băm (PBKDF2 + salt), không lưu PIN gốc.";

    protected override Control Build()
    {
        var idle = Ui.Input(80, null, X.Pin.IdleMinutes.ToString());
        var oldPin = new TextBox { PasswordChar = '•', Width = 140, Watermark = "PIN hiện tại" };
        var newPin = new TextBox { PasswordChar = '•', Width = 140, Watermark = "PIN mới (≥ 4 số)" };
        var newPin2 = new TextBox { PasswordChar = '•', Width = 140, Watermark = "Nhập lại PIN mới" };
        return Ui.Col(
            Ui.Card("Tự khóa", Ui.Row(Ui.Label("Tự khóa sau"), idle, Ui.Label("phút không thao tác"),
                Ui.Btn("Lưu", () => Ui.Try(() =>
                {
                    var m = (int)Ui.Dec(idle.Text, "số phút");
                    if (m < 1 || m > 240) throw new BusinessException("Số phút từ 1 đến 240.");
                    X.Pin.IdleMinutes = m; App.Main.ResetIdle();
                }, "Đã lưu thời gian tự khóa."), primary: true))),
            Ui.Card("Đổi mã PIN", Ui.Row(oldPin, newPin, newPin2,
                Ui.Btn("Đổi mã PIN", () => Ui.Try(() =>
                {
                    if (!X.Pin.Verify(oldPin.Text ?? "")) throw new BusinessException("PIN hiện tại không đúng.");
                    if (newPin.Text != newPin2.Text) throw new BusinessException("Hai lần nhập PIN mới không khớp.");
                    X.Pin.SetPin(newPin.Text ?? "");
                    oldPin.Text = newPin.Text = newPin2.Text = "";
                }, "Đã đổi mã PIN."), primary: true))));
    }
}

public sealed class AuditScreen : Screen
{
    public override string Title => "Nhật ký thay đổi";
    public override string Subtitle => "Mọi thao tác ghi dữ liệu: thêm/sửa/ngừng danh mục, BOM, lập/điều chỉnh/đóng đơn, lập/sửa/xác nhận/hủy phiếu, giữ kho, kiểm kê, chuyển NPL, nơi xuất/nhận, cài đặt, mã PIN, sao lưu/phục hồi. Không ghi giá trị mã PIN.";
    DateTime? _from = DateTime.Today.AddDays(-30), _to = DateTime.Today; string? _group; string _kw = "";

    protected override Control Build()
    {
        var from = Ui.DateBox(_from); from.SelectedDateChanged += (_, _) => { if (from.SelectedDate?.Date != _from?.Date) { _from = from.SelectedDate?.Date; Reload(); } };
        var to = Ui.DateBox(_to); to.SelectedDateChanged += (_, _) => { if (to.SelectedDate?.Date != _to?.Date) { _to = to.SelectedDate?.Date; Reload(); } };
        var groups = new List<Opt<string>> { new("Tất cả nhóm", "") };
        groups.AddRange(AuditLog.Groups.Where(g => !g.Key.EndsWith("_draft")).Select(g => new Opt<string>(g.Value, g.Key)));
        var kw = Ui.Input(260, "Từ khóa: số phiếu, mã NPL, lý do…", _kw);
        kw.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { _kw = kw.Text ?? ""; Reload(); } };
        var f = new AuditFilter(_from, _to, _group, _kw);
        var t = X.Reports.AuditReport(f);
        Control F(string label, Control c) => new StackPanel { Children = { Ui.Text(label, Ui.Muted), c } };
        return Ui.Col(
            Ui.Row(F("Từ ngày", from), F("Đến ngày", to), F("Nhóm", Ui.Combo(groups, _group ?? "", 200, v => { _group = v == "" ? null : v; Reload(); })),
                F("Tìm", kw), F(" ", Ui.Btn("Lọc", () => { _kw = kw.Text ?? ""; Reload(); }, primary: true)),
                F(" ", Ui.Btn("Xóa lọc", () => { _from = null; _to = null; _group = null; _kw = ""; Reload(); }))),
            Ui.Row(Ui.BtnAsync("Xuất Excel", () => Ui.Export(t, false)), Ui.BtnAsync("Xuất PDF", () => Ui.Export(t, true)), Ui.Text($"{t.Rows.Count:#,##0} dòng", Ui.Muted)),
            t.Rows.Count == 0 ? Ui.Hint("Không có dòng nào phù hợp.") : Ui.Table(t, maxHeight: 640));
    }
}

public sealed class PlaceScreen : Screen
{
    public override string Title => "Nơi xuất / Nơi nhận";
    public override string Subtitle => "Danh mục dùng để hoàn thiện chứng từ: phiếu nhập ghi nơi xuất (bên giao: nhà cung cấp, khách hàng…) và nơi nhận (kho); phiếu xuất ghi nơi xuất (kho) và nơi nhận (tổ cắt, chuyền may…).";
    PlaceRow? _sel;

    static readonly Opt<PlaceKind>[] Kinds = { new("Nơi xuất và nơi nhận", PlaceKind.CaHai), new("Chỉ là nơi xuất", PlaceKind.Xuat), new("Chỉ là nơi nhận", PlaceKind.Nhan) };

    protected override Control Build()
    {
        var code = Ui.Input(120, "Mã"); var name = Ui.Input(240, "Tên"); var addr = Ui.Input(280, "Địa chỉ / ghi chú");
        var kind = Ui.Combo(Kinds, PlaceKind.CaHai, 200);
        if (_sel is not null) { code.Text = _sel.Code; code.IsEnabled = false; name.Text = _sel.Name; addr.Text = _sel.Address; kind.SelectedItem = Kinds.First(k => PlaceService.Code(k.Value) == _sel.Kind); }
        var inTo = Ui.Combo(DocText.PlaceOpts(false), X.Places.DefaultInTo, 260);
        var outFrom = Ui.Combo(DocText.PlaceOpts(true), X.Places.DefaultOutFrom, 260);
        var cName = Ui.Input(320, "Tên đơn vị", X.Company.Name); var cAddr = Ui.Input(420, "Địa chỉ", X.Company.Address);
        return Ui.Col(
            Ui.Card(_sel is null ? "Khai báo nơi xuất / nơi nhận" : $"Sửa {_sel.Code}",
                Ui.Row(Ui.Label("Mã", true), code, Ui.Label("Tên", true), name, Ui.Label("Loại", true), kind),
                Ui.Row(Ui.Label("Địa chỉ"), addr,
                    _sel is null
                        ? Ui.Btn("Lưu", () => Ui.Try(() => { X.Places.Add(Ui.Req(code, "mã"), Ui.Req(name, "tên"), Ui.Sel<PlaceKind>(kind), addr.Text); Reload(); }, "Đã lưu nơi xuất / nơi nhận."), primary: true)
                        : Ui.Btn("Lưu thay đổi", () => Ui.Try(() => { X.Places.Update(_sel.Id, Ui.Req(name, "tên"), Ui.Sel<PlaceKind>(kind), addr.Text); _sel = null; Reload(); }, "Đã cập nhật."), primary: true),
                    _sel is null ? null : Ui.Btn(_sel.Active ? "Ngừng sử dụng" : "Dùng lại", () => Ui.Try(() => { X.Places.SetActive(_sel.Id, !_sel.Active); _sel = null; Reload(); }, "Đã cập nhật trạng thái.")),
                    _sel is null ? null : Ui.Btn("Khai báo mới", () => { _sel = null; Reload(); }))),
            Ui.List(X.Q.Places(), new (string, Func<PlaceRow, string>, bool)[]
            {
                ("Mã", p => p.Code, false), ("Tên", p => p.Name, false), ("Loại", p => PlaceService.Label(p.Kind), false),
                ("Địa chỉ / ghi chú", p => p.Address ?? "", false), ("Trạng thái", p => p.Active ? "Đang dùng" : "Ngừng sử dụng", false)
            }, p => { if (p is not null && p.Id != _sel?.Id) { _sel = p; Reload(); } }, 300, p => p.Id == _sel?.Id),
            Ui.Card("Mặc định khi lập phiếu",
                Ui.Row(Ui.Label("Nơi nhận của phiếu nhập / phiếu trả"), inTo),
                Ui.Row(Ui.Label("Nơi xuất của phiếu xuất / cấp phát"), outFrom,
                    Ui.Btn("Lưu mặc định", () => Ui.Try(() => { X.Places.DefaultInTo = Ui.Sel<long?>(inTo); X.Places.DefaultOutFrom = Ui.Sel<long?>(outFrom); }, "Đã lưu mặc định."), primary: true))),
            Ui.Card("Thông tin đơn vị (in trên đầu chứng từ)",
                Ui.Row(Ui.Label("Tên đơn vị"), cName), Ui.Row(Ui.Label("Địa chỉ"), cAddr,
                    Ui.Btn("Lưu", () => Ui.Try(() => { X.Company.Name = cName.Text ?? ""; X.Company.Address = cAddr.Text ?? ""; }, "Đã lưu thông tin đơn vị."), primary: true))));
    }
}
