#if DEBUG
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RailDisplay.Infrastructure.Logging;

internal static class ApplicationExitChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RailDisplay-shutdown-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var factory = ApplicationLogging.CreateFactory(directory))
                Run(factory);
            string text = File.ReadAllText(Directory.GetFiles(directory, "*.log").Single());
            if (!text.Contains("正在释放应用资源") || !text.Contains("应用资源释放结束"))
                throw new Exception("关闭主窗口必须在释放日志工厂前记录资源释放结果。");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void Run(ILoggerFactory factory)
    {
        AppBuilder.Configure(() => new RailDisplay.App(factory)).UsePlatformDetect().WithInterFont()
            .SetupWithClassicDesktopLifetime(Array.Empty<string>());
        var lifetime = (ClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var main = lifetime.MainWindow!;
        bool pluginInstalled = File.Exists(Path.Combine(AppContext.BaseDirectory, "Debug.dll"));
        Exception? failure = null;
        int exited = 0;
        lifetime.Exit += (_, _) => exited++;
        main.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (lifetime.ShutdownMode != ShutdownMode.OnMainWindowClose || Application.Current.Name != "RailDiaplay")
                    throw new Exception("Application name and close-main-window shutdown policy must be configured.");
                main.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F12 });
                await Task.Delay(150);
                if (main.OwnedWindows.Count != (pluginInstalled ? 1 : 0))
                    throw new Exception("Debug must open only when its optional DLL is installed.");
                main.Close();
            }
            catch (Exception error)
            {
                failure = error;
                lifetime.Shutdown(1);
            }
        });
        int exitCode = lifetime.Start(Array.Empty<string>());
        if (failure is not null)
            throw new Exception("主窗口退出检查失败。", failure);
        if (exitCode != 0 || exited != 1 || lifetime.Windows.Any())
            throw new Exception("Closing the main window must exit once and close every window without errors.");
        Console.WriteLine(pluginInstalled
            ? "主窗口关闭、Debug 插件释放与整程序退出检查通过。"
            : "缺少可选 Debug DLL 时，主程序正常启动和退出检查通过。");
    }
}
#endif
