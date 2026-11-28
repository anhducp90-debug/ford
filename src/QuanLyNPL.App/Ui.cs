using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Data.Sqlite;
using QuanLyNPL.Data;

namespace QuanLyNPL.App;

/// <summary>Lựa chọn trong ComboBox: hiển thị Text, giữ Value.</summary>
public sealed record Opt<T>(string Text, T Value)
{
    public override string ToString() => Text;
}

/// <summary>Một dòng của bảng hiển thị: các ô đã định dạng + đối tượng gốc.</summary>
public sealed class GridRow(string[] c, object? tag = null)
{
    public string[] C { get; } = c;
    public object? Tag { get; } = tag;
}

public static class Ui
{
    public static readonly IBrush Accent = Brush.Parse("#2D5B9A"), AccentSoft = Brush.Parse("#DCE7F6"), Ink = Brush.Parse("#1B2435"),
        Muted = Brush.Parse("#5D6A80"), Line = Brush.Parse("#D3DAE6"), Surface = Brushes.White, Surface2 = Brush.Parse("#F4F6FA"),
        Bg = Brush.Parse("#E9EDF3"), Ok = Brush.Parse("#1D7A4B"), OkSoft = Brush.Parse("#DCF1E5"), Warn = Brush.Parse("#A85D00"),
        WarnSoft = Brush.Parse("#FBE9D0"), Bad = Brush.Parse("#B3261E"), BadSoft = Brush.Parse("#F9DEDC");

    static readonly NumberFormatInfo Vn = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    // ---------- Định dạng ----------
    public static string N(decimal d) => d.ToString("#,##0.####", Vn);
    public static string N2(decimal d) => d.ToString("#,##0.00", Vn);
    public static string D(DateTime d) => d.ToString("dd/MM/yyyy");
    public static string DT(DateTime d) => d.ToString("dd/MM/yyyy HH:mm");
    public static string Cell(object? v) => v switch
    {
        null => "", decimal d => N(d), DateTime t => D(t), bool b => b ? "Có" : "", _ => v.ToString() ?? ""
    };

    /// <summary>Đọc số: chấp nhận "1,5" và "1.5"; có cả hai dấu thì hiểu theo kiểu Việt Nam (1.234,5).</summary>
    public static decimal Dec(string? text, string field)
    {
        var t = (text ?? "").Trim().Replace(" ", "");
        if (t.Length == 0) throw new BusinessException($"Chưa nhập {field}.");
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(',', '.');
        else t = t.Replace(',', '.');
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) throw new BusinessException($"{field} không phải số: \"{text}\".");
        return v;
    }

    public static string Req(TextBox tb, string field) =>
        string.IsNullOrWhiteSpace(tb.Text) ? throw new BusinessException($"Chưa nhập {field}.") : tb.Text.Trim();

    // ---------- Khối giao diện ----------
    public static TextBlock H1(string t) => new() { Text = t, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Ink };
    public static TextBlock H3(string t) => new() { Text = t, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 6) };
    public static TextBlock Hint(string t) => new() { Text = t, Foreground = Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    public static TextBlock Text(string t, IBrush? fg = null, bool bold = false) =>
        new() { Text = t, Foreground = fg ?? Ink, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    public static Control Label(string t, bool required = false)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        p.Children.Add(new TextBlock { Text = t, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        if (required) p.Children.Add(new TextBlock { Text = " *", Foreground = Warn, VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    public static TextBox Input(double width = 160, string? watermark = null, string? text = null) =>
        new() { Width = width, Watermark = watermark, Text = text, VerticalAlignment = VerticalAlignment.Center };

    public static Button Btn(string text, Action onClick, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 5) };
        if (primary) { b.Background = Accent; b.Foreground = Brushes.White; b.FontWeight = FontWeight.SemiBold; }
        if (danger) b.Foreground = Bad;
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button BtnAsync(string text, Func<Task> onClick, bool primary = false, bool danger = false)
    {
        var b = Btn(text, () => { }, primary, danger);
        b.Click += async (_, _) => await TryAsync(onClick);
        return b;
    }

    public static WrapPanel Row(params Control?[] items)
    {
        var w = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var i in items) if (i is not null) { i.Margin = new Thickness(0, 0, 8, 6); w.Children.Add(i); }
        return w;
    }

    public static StackPanel Col(params Control?[] items)
    {
        var s = new StackPanel { Spacing = 6 };
        foreach (var i in items) if (i is not null) s.Children.Add(i);
        return s;
    }

    public static Border Card(string? title, params Control?[] items)
    {
        var s = Col(items);
        if (title is not null) s.Children.Insert(0, H3(title));
        return new Border { Child = s, Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Margin = new Thickness(0, 0, 0, 12) };
    }

    public static Border Banner(string text, string kind)
    {
        var (bg, fg) = kind switch { "ok" => (OkSoft, Ok), "bad" => (BadSoft, Bad), "warn" => (WarnSoft, Warn), _ => (Surface2, Muted) };
        return new Border
        {
            Background = bg, BorderBrush = fg, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 9), Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock { Text = text, Foreground = fg, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }
        };
    }

    public static Border Chip(string text, string kind)
    {
        var (bg, fg) = kind switch { "ok" => (OkSoft, Ok), "bad" => (BadSoft, Bad), "warn" => (WarnSoft, Warn), _ => (Surface2, Muted) };
        return new Border { Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, Foreground = fg, FontSize = 12, FontWeight = FontWeight.SemiBold } };
    }

    // ---------- Chọn ----------
    public static ComboBox Combo<T>(IEnumerable<Opt<T>> items, T? selected = default, double width = 220, Action<T>? onChange = null)
    {
        var list = items.ToList();
        var cb = new ComboBox { ItemsSource = list, Width = width, VerticalAlignment = VerticalAlignment.Center, MaxDropDownHeight = 420 };
        var sel = list.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, selected!));
        if (sel is not null) cb.SelectedItem = sel;
        var last = sel is null ? default : sel.Value;
        // Chỉ báo khi giá trị thực sự đổi (tránh vẽ lại lặp khi điều khiển tự đặt lại lựa chọn).
        if (onChange is not null) cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is Opt<T> o && !EqualityComparer<T>.Default.Equals(o.Value, last!)) { last = o.Value; onChange(o.Value); }
        };
        return cb;
    }

    public static T? Sel<T>(ComboBox cb) => cb.SelectedItem is Opt<T> o ? o.Value : default;

    public static CalendarDatePicker DateBox(DateTime? value = null) =>
        new() { SelectedDate = value, Width = 150, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", VerticalAlignment = VerticalAlignment.Center };

    // ---------- Bảng ----------
    public static DataGrid Table(IReadOnlyList<string> headers, IEnumerable<GridRow> rows, ISet<int>? numeric = null, double maxHeight = 460, Action<object?>? onSelect = null, int selectedIndex = -1)
    {
        var dg = new DataGrid
        {
            ItemsSource = rows.ToList(), IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, CanUserReorderColumns = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HeadersVisibility = DataGridHeadersVisibility.Column,
            MaxHeight = maxHeight, MinHeight = 60, SelectionMode = DataGridSelectionMode.Single, BorderBrush = Line, BorderThickness = new Thickness(1),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        for (var i = 0; i < headers.Count; i++)
        {
            var idx = i; var right = numeric?.Contains(i) == true;
            dg.Columns.Add(new DataGridTemplateColumn
            {
                Header = headers[i],
                CellTemplate = new FuncDataTemplate<GridRow>((_, _) => new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding($"C[{idx}]"),
                    HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 3), TextWrapping = TextWrapping.NoWrap
                })
            });
        }
        if (selectedIndex >= 0) dg.SelectedIndex = selectedIndex;
        if (onSelect is not null) dg.SelectionChanged += (_, _) => onSelect((dg.SelectedItem as GridRow)?.Tag);
        return dg;
    }

    public static DataGrid Table(ReportTable t, Action<object?[]?>? onSelect = null, double maxHeight = 460)
    {
        var numeric = new HashSet<int>();
        for (var i = 0; i < t.Headers.Count; i++) if (t.Rows.Count > 0 && t.Rows.All(r => r[i] is decimal or null) && t.Rows.Any(r => r[i] is decimal)) numeric.Add(i);
        return Table(t.Headers, t.Rows.Select(r => new GridRow(r.Select(Cell).ToArray(), r)), numeric, maxHeight,
            onSelect is null ? null : o => onSelect(o as object?[]));
    }

    /// <param name="isSelected">Dòng được chọn sẵn (giữ lựa chọn khi màn hình vẽ lại).</param>
    public static DataGrid List<T>(IEnumerable<T> items, (string header, Func<T, string> get, bool num)[] cols, Action<T?>? onSelect = null, double maxHeight = 420, Func<T, bool>? isSelected = null)
    {
        var list = items.ToList();
        return Table(cols.Select(c => c.header).ToList(), list.Select(i => new GridRow(cols.Select(c => c.get(i)).ToArray(), i)),
            cols.Select((c, i) => (c, i)).Where(x => x.c.num).Select(x => x.i).ToHashSet(), maxHeight,
            onSelect is null ? null : o => onSelect(o is T t ? t : default), isSelected is null ? -1 : list.FindIndex(i => isSelected(i)));
    }

    // ---------- Thông báo / xử lý lỗi ----------
    public static void Try(Action a, string? success = null)
    {
        try { a(); if (success is not null) App.Main.Status(success, "ok"); }
        catch (Exception ex) { ShowError(ex); }
    }

    public static async Task TryAsync(Func<Task> a)
    {
        try { await a(); }
        catch (Exception ex) { ShowError(ex); }
    }

    public static void ShowError(Exception ex)
    {
        var msg = ex switch
        {
            BusinessException b => b.Message,
            SqliteException { SqliteErrorCode: 19 } s when s.Message.Contains("UNIQUE") => "Mã hoặc số phiếu đã tồn tại.",
            SqliteException { SqliteErrorCode: 5 or 6 } => "Dữ liệu đang bận, vui lòng thử lại.",
            _ => "Lỗi không mong muốn: " + ex.Message
        };
        if (ex is not BusinessException) Log.Error("UI", ex);
        if (msg.Contains('\n')) _ = Dialog.Info("Không thực hiện được", msg);
        App.Main.Status(msg.Split('\n')[0] + (msg.Contains('\n') ? " …" : ""), "bad");
    }

    // ---------- Tệp ----------
    public static async Task<string?> SavePath(string title, string suggested, string ext, string typeName)
    {
        var f = await App.Main.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggested, DefaultExtension = ext,
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { "*." + ext } } }
        });
        return f?.TryGetLocalPath();
    }

    public static async Task<string?> OpenPath(string title, string ext, string typeName)
    {
        var fs = await App.Main.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = new[] { "*." + ext } } }
        });
        return fs.Count > 0 ? fs[0].TryGetLocalPath() : null;
    }

    public static async Task Export(ReportTable t, bool pdf)
    {
        var name = $"{new string(t.Title.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim()} {DateTime.Now:yyyy-MM-dd}";
        var path = await SavePath(pdf ? "Xuất PDF" : "Xuất Excel", name, pdf ? "pdf" : "xlsx", pdf ? "Tệp PDF" : "Bảng tính Excel");
        if (path is null) return;
        await using (var fs = File.Create(path))
        {
            if (pdf) ReportExporter.ToPdf(t, fs); else ReportExporter.ToExcel(t, fs);
        }
        App.Main.Status("Đã xuất: " + path, "ok");
    }
}

/// <summary>Hộp thoại xác nhận / thông báo (modal).</summary>
public static class Dialog
{
    /// <summary>Chỉ dùng cho kiểm thử tự động: trả lời sẵn thay vì hiện hộp thoại.</summary>
    public static bool? AutoAnswer { get; set; }
    public static string? LastMessage { get; private set; }

    public static async Task<bool> Confirm(string title, string message, string ok = "Đồng ý", bool danger = false)
    {
        LastMessage = message;
        if (AutoAnswer is bool auto) return auto;
        var result = false;
        var w = Make(title, message);
        var okBtn = Ui.Btn(ok, () => { result = true; w.Close(); }, primary: !danger, danger: danger);
        var cancel = Ui.Btn("Hủy", () => w.Close());
        ((StackPanel)w.Content!).Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, okBtn } });
        await w.ShowDialog(App.Main);
        return result;
    }

    public static async Task Info(string title, string message)
    {
        LastMessage = message;
        if (AutoAnswer is not null) return;
        var w = Make(title, message);
        ((StackPanel)w.Content!).Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { Ui.Btn("Đóng", () => w.Close(), primary: true) } });
        await w.ShowDialog(App.Main);
    }

    static Window Make(string title, string message) => new()
    {
        Title = title, Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 640, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Surface,
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Ui.Ink },
                new ScrollViewer { MaxHeight = 440, Content = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Ink } }
            }
        }
    };
}

/// <summary>Trạng thái một dòng nhập liệu (giữ lại khi màn hình vẽ lại).</summary>
public sealed class LineState
{
    public string? Mat { get; set; }
    public string? Mat2 { get; set; }
    public string? Size { get; set; }
    public string Qty { get; set; } = "";
    public string Waste { get; set; } = "";
}

/// <summary>Bảng nhập dòng: NPL (+ NPL đích / size) + số lượng (+ hao hụt), thêm/xóa dòng.</summary>
public sealed class LinesEditor
{
    readonly List<LineState> _lines;
    readonly IList<Opt<string>> _mats;
    readonly IList<Opt<string>>? _mats2, _sizes;
    readonly bool _waste;
    readonly string _qtyLabel, _matLabel, _mat2Label;
    readonly StackPanel _rows = new() { Spacing = 4 };

    public Control View { get; }

    public LinesEditor(List<LineState> lines, IList<Opt<string>> mats, string qtyLabel = "Số lượng", IList<Opt<string>>? sizes = null, bool waste = false,
        IList<Opt<string>>? mats2 = null, string matLabel = "Mã NPL", string mat2Label = "Mã NPL đích")
    {
        _lines = lines; _mats = mats; _mats2 = mats2; _sizes = sizes; _waste = waste; _qtyLabel = qtyLabel; _matLabel = matLabel; _mat2Label = mat2Label;
        if (_lines.Count == 0) _lines.Add(new LineState { Size = sizes is null ? null : "*" });
        Render();
        View = Ui.Col(Header(), _rows, Ui.Row(Ui.Btn("+ Thêm dòng", () => { _lines.Add(new LineState { Size = _sizes is null ? null : "*" }); Render(); })));
    }

    Grid NewGrid()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(), Margin = new Thickness(0) };
        g.ColumnDefinitions.Add(new ColumnDefinition(300, GridUnitType.Pixel));
        if (_mats2 is not null) g.ColumnDefinitions.Add(new ColumnDefinition(300, GridUnitType.Pixel));
        if (_sizes is not null) g.ColumnDefinitions.Add(new ColumnDefinition(120, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(120, GridUnitType.Pixel));
        if (_waste) g.ColumnDefinitions.Add(new ColumnDefinition(110, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(60, GridUnitType.Pixel));
        return g;
    }

    Control Header()
    {
        var g = NewGrid(); var col = 0;
        void H(string t) { var tb = new TextBlock { Text = t, Foreground = Ui.Muted, FontSize = 12, FontWeight = FontWeight.SemiBold }; Grid.SetColumn(tb, col++); g.Children.Add(tb); }
        H(_matLabel); if (_mats2 is not null) H(_mat2Label); if (_sizes is not null) H("Size"); H(_qtyLabel); if (_waste) H("Hao hụt %");
        return g;
    }

    void Render()
    {
        _rows.Children.Clear();
        foreach (var l in _lines.ToList())
        {
            var g = NewGrid(); var col = 0;
            void Add(Control c) { c.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(c, col++); g.Children.Add(c); }
            var m = Ui.Combo(_mats, l.Mat, 290, v => l.Mat = v); Add(m);
            if (_mats2 is not null) Add(Ui.Combo(_mats2, l.Mat2, 290, v => l.Mat2 = v));
            if (_sizes is not null) Add(Ui.Combo(_sizes, l.Size, 110, v => l.Size = v));
            var q = Ui.Input(110, "0", l.Qty); q.TextChanged += (_, _) => l.Qty = q.Text ?? ""; q.HorizontalContentAlignment = HorizontalAlignment.Right; Add(q);
            if (_waste) { var w = Ui.Input(100, "0", l.Waste); w.TextChanged += (_, _) => l.Waste = w.Text ?? ""; w.HorizontalContentAlignment = HorizontalAlignment.Right; Add(w); }
            Add(Ui.Btn("✕", () => { _lines.Remove(l); Render(); }, danger: true));
            _rows.Children.Add(g);
        }
    }

    /// <summary>Đọc các dòng đã chọn NPL; dòng trống bị bỏ qua.</summary>
    public static List<(string mat, decimal qty)> Read(IEnumerable<LineState> lines, string qtyField = "số lượng") =>
        lines.Where(l => !string.IsNullOrEmpty(l.Mat)).Select((l, i) => (l.Mat!, Ui.Dec(l.Qty, $"{qtyField} dòng {i + 1}"))).ToList();
}
