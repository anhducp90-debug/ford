using Avalonia;

namespace QuanLyNPL.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Task", e.Exception); e.SetObserved(); };
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

/// <summary>Ghi lỗi vào %LOCALAPPDATA%\QuanLyNPL\logs để hỗ trợ khi có sự cố.</summary>
public static class Log
{
    public static void Error(string where, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Ctx.DefaultDataDir(), "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"loi_{DateTime.Now:yyyyMM}.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\n\n");
        }
        catch { /* không để lỗi ghi log làm sập ứng dụng */ }
    }
}
