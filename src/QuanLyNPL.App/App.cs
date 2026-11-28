using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace QuanLyNPL.App;

public sealed class App : Application
{
    public static Ctx X { get; set; } = null!;
    public static MainWindow Main { get; set; } = null!;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://QuanLyNPL")) { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        Resources["SystemAccentColor"] = Color.Parse("#2D5B9A");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                X = new Ctx(Ctx.DefaultDataDir());
                X.AutoBackup();
                Main = new MainWindow();
                desktop.MainWindow = Main;
                desktop.ShutdownRequested += (_, _) => X.C.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Startup", ex);
                desktop.MainWindow = new Window
                {
                    Title = "Quản lý NPL – lỗi khởi động", Width = 560, Height = 240,
                    Content = new TextBlock { Text = "Không mở được dữ liệu:\n" + ex.Message + "\n\nChi tiết đã ghi trong thư mục logs của dữ liệu.", Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap }
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
