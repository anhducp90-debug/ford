using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using QuanLyNPL.Data;

namespace QuanLyNPL.App.Screens;

/// <summary>Trang chủ: các bước thiết lập (khi mới dùng) và việc cần làm hằng ngày; bấm vào việc để mở đúng phiếu / màn hình với dữ liệu điền sẵn.</summary>
public sealed class HomeScreen : Screen
{
    public override string Title => "Việc cần làm";
    public override string Subtitle => "Tổng hợp những việc đang chờ: phiếu nháp chưa xác nhận, đơn thiếu dữ liệu hoặc thiếu NPL, nơi nhận đang dư / thiếu, đơn sẵn sàng đóng. Bấm nút ở mỗi dòng để mở đúng việc đó.";

    protected override Control Build()
    {
        var setup = X.Flow.Setup();
        var todos = X.Flow.Todos();
        var last = X.LastBackup;
        var parts = new List<Control?>();
        var summary = new WrapPanel();
        void Tile(string n, string label, string kind) => summary.Children.Add(new Border
        {
            Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 10, 10), MinWidth = 170,
            Child = new StackPanel { Children = { new TextBlock { Text = n, FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = kind switch { "bad" => Ui.Bad, "warn" => Ui.Warn, "ok" => Ui.Ok, _ => Ui.Ink } }, Ui.Text(label, Ui.Muted) } }
        });
        Tile(todos.Count(t => t.Group == "Phiếu nháp chờ xác nhận").ToString(), "phiếu nháp chờ xác nhận", todos.Any(t => t.Group == "Phiếu nháp chờ xác nhận" && t.Severity == "warn") ? "warn" : "info");
        Tile(todos.Count(t => t.Group == "Đơn hàng cần xử lý").ToString(), "đơn thiếu dữ liệu / NPL", todos.Any(t => t.Group == "Đơn hàng cần xử lý" && t.Severity == "bad") ? "bad" : todos.Any(t => t.Group == "Đơn hàng cần xử lý") ? "warn" : "ok");
        Tile(todos.Count(t => t.Group == "Nơi nhận đang thiếu / dư").ToString(), "nơi nhận đang thiếu / dư", todos.Any(t => t.Group == "Nơi nhận đang thiếu / dư") ? "warn" : "ok");
        Tile(todos.Count(t => t.Group == "Đơn sẵn sàng đóng").ToString(), "đơn sẵn sàng đóng", "ok");
        Tile(last is DateTime b ? Ui.DT(b) : "chưa có", "sao lưu gần nhất", last is DateTime b2 && (DateTime.Now - b2).TotalDays < 2 ? "ok" : "warn");
        parts.Add(summary);

        if (setup.Any(s => !s.Done))
        {
            var card = Ui.Card("Thiết lập ban đầu – làm lần lượt từ trên xuống");
            var col = (StackPanel)card.Child!;
            foreach (var s in setup)
                col.Children.Add(Ui.Row(Ui.Chip(s.Done ? "Xong" : "Chưa", s.Done ? "ok" : "warn"), Ui.Text(s.Title, s.Done ? Ui.Muted : Ui.Ink, !s.Done),
                    s.Done ? null : Ui.Btn("Mở " + s.Hint, () => App.Main.Go(s.Screen))));
            parts.Add(card);
        }
        if (todos.Count == 0 && setup.All(s => s.Done)) parts.Add(Ui.Banner("Không có việc tồn đọng. Mọi phiếu đã xác nhận, các nơi nhận đều cân đối.", "ok"));
        foreach (var g in todos.GroupBy(t => t.Group))
        {
            var card = Ui.Card($"{g.Key} ({g.Count()})");
            var col = (StackPanel)card.Child!;
            foreach (var t in g)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 2, 0, 6) };
                var chip = Ui.Chip(t.Severity switch { "bad" => "Cần xử lý", "warn" => "Lưu ý", "ok" => "Sẵn sàng", _ => "Chờ" }, t.Severity == "info" ? "muted" : t.Severity);
                chip.Margin = new Thickness(0, 2, 10, 0); chip.VerticalAlignment = VerticalAlignment.Top;
                var text = new StackPanel { Children = { Ui.Text(t.Title, null, true), Ui.Hint(t.Detail) } };
                var btn = Ui.Btn(t.Action, () => Open(t), primary: t.Severity is "bad" or "ok");
                btn.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(text, 1); Grid.SetColumn(btn, 2);
                row.Children.Add(chip); row.Children.Add(text); row.Children.Add(btn);
                col.Children.Add(row);
            }
            parts.Add(card);
        }
        return Ui.Col(parts.ToArray());
    }

    /// <summary>Mở đúng màn hình cho một việc, dữ liệu điền sẵn.</summary>
    public static void Open(TodoItem t)
    {
        switch (t.Target)
        {
            case "receipt": App.Main.Open("Phiếu nhập", new ReceiptScreen(docId: t.DocId)); break;
            case "receipt-short": App.Main.Open("Phiếu nhập", new ReceiptScreen(shortForOrder: t.OrderId)); break;
            case "issue": App.Main.Open("Phiếu xuất (cấp phát)", new IssueScreen(t.OrderId, t.DocId)); break;
            case "return" when t.DocId is null: App.Main.Open("Trả NPL & xuất bù", new ReturnScreen(t.OrderId, t.PlaceId, ReturnScreen.Prefill.Surplus)); break;
            case "supplement": App.Main.Open("Trả NPL & xuất bù", new ReturnScreen(t.OrderId, t.PlaceId, ReturnScreen.Prefill.Shortage)); break;
            case "return": App.Main.Open("Trả NPL & xuất bù", new ReturnScreen(t.OrderId, docId: t.DocId)); break;
            case "count": App.Main.Go("Kiểm kê & điều chỉnh tồn"); break;
            case "transfer": App.Main.Go("Chuyển NPL giữa khách hàng"); break;
            case "order": App.Main.Open("Đơn hàng", new OrderScreen(t.OrderId)); break;
            case "settle": App.Main.Open("Quyết toán & đóng đơn", new SettleScreen(t.OrderId)); break;
        }
    }
}
