using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace QuanLyNPL.App;

/// <summary>
/// Màn hình nghiệp vụ: giữ trạng thái trong trường của lớp, Build() vẽ lại toàn bộ từ dữ liệu.
/// Mọi thao tác ghi xong gọi Reload() để thấy dữ liệu mới.
/// </summary>
public abstract class Screen : UserControl
{
    protected static Ctx X => App.X;
    public abstract string Title { get; }
    public virtual string Subtitle => "";
    protected abstract Control Build();

    public void Reload()
    {
        Control body;
        try { body = Build(); }
        catch (Exception ex) { Log.Error("Build " + GetType().Name, ex); body = Ui.Banner("Không hiển thị được màn hình: " + ex.Message, "bad"); }
        var head = Ui.Col(Ui.H1(Title), Subtitle.Length > 0 ? Ui.Hint(Subtitle) : null);
        head.Margin = new Thickness(0, 0, 0, 12);
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,   // nội dung co theo cửa sổ; bảng tự cuộn ngang
            Content = new StackPanel { Margin = new Thickness(20, 16), Children = { head, body } }
        };
    }
}
