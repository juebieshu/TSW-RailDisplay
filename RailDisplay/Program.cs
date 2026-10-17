using Avalonia;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RailDisplay.Infrastructure.Logging;
using RailDisplay.Logging;
using Serilog.Debugging;

namespace RailDisplay;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        SelfLog.Enable(TextWriter.Synchronized(Console.Error));
        ILoggerFactory? factory = null;
        Microsoft.Extensions.Logging.ILogger? logger = null;
        UnhandledExceptionEventHandler onUnhandled = (_, e) =>
            logger?.LogCritical(e.ExceptionObject as Exception, "未处理异常，进程即将终止：{Terminating}", e.IsTerminating);
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, e) =>
            logger?.LogError(e.Exception, "后台任务异常未被观察");
        int exitCode = 1;
        try
        {
            factory = ApplicationLogging.CreateFactory(minimumLevel: ApplicationLogging.ParseMinimumLevel(
                Environment.GetEnvironmentVariable("RAILDISPLAY_LOG_LEVEL")));
            logger = factory.CreateLogger<Program>();
            AppDomain.CurrentDomain.UnhandledException += onUnhandled;
            TaskScheduler.UnobservedTaskException += onUnobserved;
            logger.LogInformation("RailDisplay 启动，版本 {Version}，平台 {Platform}，日志目录 {LogDirectory}",
                typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Environment.OSVersion, ApplicationLogging.LogDirectory);
            exitCode = BuildAvaloniaApp(factory).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            if (logger is not null)
                logger.LogCritical(error, "RailDisplay 意外退出");
            else
                Console.Error.WriteLine($"无法初始化日志系统：{error}");
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= onUnhandled;
            TaskScheduler.UnobservedTaskException -= onUnobserved;
            Avalonia.Logging.Logger.Sink = null;
            logger?.LogInformation("RailDisplay 退出，退出码 {ExitCode}", exitCode);
            factory?.Dispose();
            SelfLog.Disable();
        }
        return exitCode;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => BuildAvaloniaApp(NullLoggerFactory.Instance);

    public static AppBuilder BuildAvaloniaApp(ILoggerFactory loggerFactory)
    {
        Avalonia.Logging.Logger.Sink = new AvaloniaLogSink(loggerFactory.CreateLogger("Avalonia"));
        return AppBuilder.Configure(() => new App(loggerFactory))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont();
    }
}
