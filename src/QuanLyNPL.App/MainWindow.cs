using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuanLyNPL.App.Screens;
using QuanLyNPL.Core;

namespace QuanLyNPL.App;

public sealed record NavItem(string Title, Func<Screen> Make);
public sealed record Module(string Title, NavItem[] Items);

public sealed class MainWindow : Window
{
    public static readonly Module[] Modules =
    {
        new("Quản trị", new NavItem[] { new("Sao lưu / Phục hồi", () => new BackupScreen()), new("Bảo mật & khóa PIN", () => new SecurityScreen()), new("Nơi xuất / Nơi nhận", () => new PlaceScreen()), new("Nhật ký thay đổi", () => new AuditScreen()) }),
        new("Khai báo", new NavItem[] { new("Khách hàng", () => new CustomerScreen()), new("Nguyên phụ liệu", () => new MaterialScreen()), new("Lưới size", () => new GridScreen()),
            new("Mã hàng", () => new ProductScreen()), new("Chủng loại", () => new VariantScreen()), new("BOM & định mức", () => new BomScreen()),
            new("Đơn hàng", () => new OrderScreen()), new("Điều chỉnh đơn", () => new AdjustScreen()) }),
        new("Nhập kho", new NavItem[] { new("Phiếu nhập", () => new ReceiptScreen()), new("Kiểm kê & điều chỉnh tồn", () => new CountScreen()) }),
        new("Xuất kho", new NavItem[] { new("Kiểm tra đáp ứng", () => new CheckScreen()), new("Giao việc theo nơi nhận", () => new AssignScreen()),
            new("Phiếu xuất (cấp phát)", () => new IssueScreen()), new("Trả NPL & xuất bù", () => new ReturnScreen()), new("Điều chuyển NPL dư", () => new SurplusScreen()),
            new("Chuyển NPL giữa khách hàng", () => new TransferScreen()), new("Cân đối cấp phát", () => new CarryScreen()), new("Quyết toán & đóng đơn", () => new SettleScreen()) }),
        new("Báo cáo", ReportScreen.Kinds.Select(k => new NavItem(k.Title, () => new ReportScreen(k.Id))).ToArray()),
    };

    readonly StackPanel _modBar = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10, 0) };
    readonly StackPanel _nav = new() { Spacing = 2, Margin = new Thickness(8, 10) };
    readonly ContentControl _content = new();
    readonly TextBlock _status = new() { Margin = new Thickness(16, 6), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Panel _lockLayer = new() { Background = Ui.Bg };
    int _mod = 1, _item = 6;
    AutoLock _autoLock;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        Title = "Quản lý Nguyên phụ liệu";
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://QuanLyNPL/app.ico"))); } catch (Exception ex) { Log.Error("Icon", ex); }
        Width = 1280; Height = 820; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Bg;
        FontSize = 13.5;

        var header = new Border
        {
            Background = Ui.Surface2, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 8),
            Child = new DockPanel
            {
                Children =
                {
                    Dock(Ui.Btn("Khóa ngay", Lock), Avalonia.Controls.Dock.Right),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
                        new TextBlock { Text = "Quản lý Nguyên phụ liệu", FontWeight = FontWeight.Bold, FontSize = 15, Foreground = Ui.Ink, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "Dữ liệu: " + App.X.DbPath, Foreground = Ui.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } } }
                }
            }
        };
        var mods = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = _modBar };
        var left = new Border { Width = 230, Background = Ui.Surface2, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = _nav } };
        var statusBar = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = _status };
        var main = new DockPanel { Background = Ui.Surface };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top); DockPanel.SetDock(mods, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(statusBar, Avalonia.Controls.Dock.Bottom); DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
        main.Children.Add(header); main.Children.Add(mods); main.Children.Add(statusBar); main.Children.Add(left); main.Children.Add(_content);

        Content = new Panel { Children = { main, _lockLayer } };

        _autoLock = new AutoLock(TimeSpan.FromMinutes(App.X.Pin.IdleMinutes), () => DateTime.Now);
        AddHandler(PointerPressedEvent, (_, _) => _autoLock.Touch(), RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, (_, _) => _autoLock.Touch(), RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, _) => _autoLock.Touch(), RoutingStrategies.Tunnel, true);
        _timer.Tick += (_, _) => { if (!_lockLayer.IsVisible && _autoLock.Tick()) ShowLock(); };
        _timer.Start();

        Status("Sẵn sàng.", "muted");
        Navigate(_mod, _item);
        ShowLock();
    }

    static Control Dock(Control c, Avalonia.Controls.Dock d) { DockPanel.SetDock(c, d); return c; }

    /// <summary>Áp dụng thời gian tự khóa mới ngay (khi đang mở khóa).</summary>
    public void ResetIdle()
    {
        if (IsLocked) return;
        _autoLock = new AutoLock(TimeSpan.FromMinutes(App.X.Pin.IdleMinutes), () => DateTime.Now);
        _autoLock.Unlock();
    }

    public string StatusText => _status.Text ?? "";

    public void Status(string text, string kind)
    {
        _status.Text = text;
        _status.Foreground = kind switch { "ok" => Ui.Ok, "bad" => Ui.Bad, "warn" => Ui.Warn, _ => Ui.Muted };
    }

    public void Navigate(int module, int item, Screen? screen = null)
    {
        _mod = module; _item = item;
        _modBar.Children.Clear();
        for (var i = 0; i < Modules.Length; i++)
        {
            var idx = i; var active = i == module;
            var b = new Button
            {
                Content = Modules[i].Title, Background = Brushes.Transparent, Padding = new Thickness(18, 11), FontWeight = FontWeight.SemiBold, CornerRadius = new CornerRadius(0),
                Foreground = active ? Ui.Accent : Ui.Muted, BorderBrush = active ? Ui.Accent : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 3)
            };
            b.Click += (_, _) => Navigate(idx, 0);
            _modBar.Children.Add(b);
        }
        _nav.Children.Clear();
        _nav.Children.Add(new TextBlock { Text = Modules[module].Title.ToUpperInvariant(), Foreground = Ui.Muted, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 4, 0, 6) });
        for (var i = 0; i < Modules[module].Items.Length; i++)
        {
            var idx = i; var active = i == item;
            var b = new Button
            {
                Content = Modules[module].Items[i].Title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = active ? Ui.Accent : Brushes.Transparent, Foreground = active ? Brushes.White : Ui.Ink, Padding = new Thickness(10, 7),
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal
            };
            b.Click += (_, _) => Navigate(module, idx);
            _nav.Children.Add(b);
        }
        var s = screen ?? Modules[module].Items[item].Make();
        s.Reload();
        _content.Content = s;
    }

    /// <summary>Mở một màn hình theo tên mục (dùng khi chuyển từ màn này sang màn khác).</summary>
    public void Open(string itemTitle, Screen screen)
    {
        for (var m = 0; m < Modules.Length; m++)
            for (var i = 0; i < Modules[m].Items.Length; i++)
                if (Modules[m].Items[i].Title == itemTitle) { Navigate(m, i, screen); return; }
    }

    // ---------- Khóa PIN ----------
    void Lock() => ShowLock();

    int _wrong; DateTime _blockedUntil;

    void ShowLock()
    {
        _autoLock.Lock();
        _lockLayer.IsVisible = true;
        _lockLayer.Children.Clear();
        var hasPin = App.X.Pin.HasPin;
        var msg = new TextBlock { Foreground = Ui.Bad, TextWrapping = TextWrapping.Wrap, MinHeight = 18, HorizontalAlignment = HorizontalAlignment.Center };
        var pin = new TextBox { PasswordChar = '•', Width = 220, FontSize = 20, HorizontalContentAlignment = HorizontalAlignment.Center, Watermark = hasPin ? "Mã PIN" : "PIN mới (≥ 4 số)" };
        var pin2 = new TextBox { PasswordChar = '•', Width = 220, FontSize = 20, HorizontalContentAlignment = HorizontalAlignment.Center, Watermark = "Nhập lại PIN", IsVisible = !hasPin };
        void Submit()
        {
            if (DateTime.Now < _blockedUntil) { msg.Text = $"Nhập sai nhiều lần. Thử lại sau {(int)(_blockedUntil - DateTime.Now).TotalSeconds + 1} giây."; return; }
            if (!hasPin)
            {
                if (pin.Text != pin2.Text) { msg.Text = "Hai lần nhập PIN không khớp."; return; }
                try { App.X.Pin.SetPin(pin.Text ?? ""); } catch (Exception ex) { msg.Text = ex.Message; return; }
                Unlock(); Status("Đã tạo mã PIN.", "ok"); return;
            }
            if (App.X.Pin.Verify(pin.Text ?? "")) { _wrong = 0; Unlock(); return; }
            pin.Text = "";
            if (++_wrong >= 5) { _blockedUntil = DateTime.Now.AddSeconds(30); _wrong = 0; msg.Text = "Nhập sai 5 lần. Tạm khóa 30 giây."; }
            else msg.Text = "Mã PIN không đúng.";
        }
        pin.KeyDown += (_, e) => { if (e.Key == Key.Enter) { if (hasPin) Submit(); else pin2.Focus(); } };
        pin2.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        var keys = new UniformGrid { Columns = 3, Width = 220, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "⌫", "0", "OK" })
        {
            var b = new Button { Content = k, Height = 44, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 17 };
            if (k == "OK") { b.Background = Ui.Accent; b.Foreground = Brushes.White; }
            b.Click += (_, _) =>
            {
                var target = !hasPin && pin2.IsFocused ? pin2 : pin;
                if (k == "OK") Submit();
                else if (k == "⌫") target.Text = (target.Text ?? "").Length > 0 ? target.Text![..^1] : "";
                else target.Text += k;
            };
            keys.Children.Add(b);
        }
        var box = new Border
        {
            Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(28, 24), Width = 320,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Quản lý Nguyên phụ liệu", FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Ink },
                    new TextBlock { Text = hasPin ? "Nhập mã PIN để mở khóa" : "Lần đầu sử dụng: tạo mã PIN (ít nhất 4 chữ số)", Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                    pin, pin2, msg, keys,
                    new TextBlock { Text = $"Tự khóa sau {App.X.Pin.IdleMinutes} phút không thao tác.", Foreground = Ui.Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
        };
        _lockLayer.Children.Add(box);
        Dispatcher.UIThread.Post(() => pin.Focus(), DispatcherPriority.Background);
    }

    void Unlock()
    {
        _lockLayer.IsVisible = false;
        _lockLayer.Children.Clear();
        _autoLock = new AutoLock(TimeSpan.FromMinutes(App.X.Pin.IdleMinutes), () => DateTime.Now);
        _autoLock.Unlock();
    }

    public bool IsLocked => _lockLayer.IsVisible;

    /// <summary>Dùng cho kiểm thử tự động: mở khóa bằng PIN.</summary>
    public bool TryUnlock(string pin) { if (!App.X.Pin.Verify(pin)) return false; Unlock(); return true; }
}
