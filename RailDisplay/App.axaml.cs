using Avalonia;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.Interactivity;
using RailDisplay.ViewModels;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Views;
using RailDisplay.Debug;
using RailDisplay.Platform;
using RailDisplay.Infrastructure.Controllers.Audio.CTCS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RailDisplay;

public partial class App : Application
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<App> _logger;

    public App() : this(NullLoggerFactory.Instance) { }

    public App(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<App>();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var audio = new Ctcs2_200HAudioController(_loggerFactory.CreateLogger<Ctcs2_200HAudioController>());
            _logger.LogInformation("音频输出可用：{AudioSupported}", audio.IsSupported);
            var ctcsKeys = CtcsNavigationKeys.Create(FunctionKeyMenu.NavigationBindings);
            var viewModel = new MainViewModel(
                new CtcsViewModel(new CtcsController(ctcsKeys), CtcsScreenProfile.CTCS2_200H), audio);
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.MainWindow = mainWindow;
            bool released = false;
            bool shuttingDown = false;
            var debugPlugin = new DebugPluginLoader(mainWindow, viewModel.Ctcs, _loggerFactory);
            bool debugPluginFound = debugPlugin.IsAvailable;
            (string Root, string Simulation, string Settings)? debugMenuLabels = null;
            if (debugPluginFound)
            {
                try
                {
                    debugMenuLabels = debugPlugin.GetMenuLabels();
                }
                catch (Exception error)
                {
                    _logger.LogError(error, "无法读取 Debug 插件菜单");
                }
            }
            bool debugAvailable = debugMenuLabels is not null;
            Action? openDebug = null;
            Action? openDebugSettings = null;
            if (debugAvailable)
            {
                async void ShowDebugWindow()
                {
                    if (shuttingDown)
                        return;
                    try
                    {
                        await debugPlugin.ShowAsync();
                    }
                    catch (Exception error)
                    {
                        _logger.LogError(error, "无法打开本地模拟插件");
                    }
                }
                void ShowDebugSettings()
                {
                    if (shuttingDown)
                        return;
                    try
                    {
                        debugPlugin.ShowSettings();
                    }
                    catch (Exception error)
                    {
                        _logger.LogError(error, "无法打开 Debug 设置");
                    }
                }
                openDebug = ShowDebugWindow;
                openDebugSettings = ShowDebugSettings;
                mainWindow.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, args) =>
                {
                    if (args.Key != Key.F12)
                        return;
                    ShowDebugWindow();
                    args.Handled = true;
                }, RoutingStrategies.Tunnel);
            }
            else if (!debugPluginFound)
                _logger.LogInformation("未安装可选 Debug 插件");
            if (OperatingSystem.IsWindows() || debugAvailable)
                _ = new NativeApplicationMenu(mainWindow, openDebug, openDebugSettings,
                    debugMenuLabels,
                    _loggerFactory.CreateLogger<NativeApplicationMenu>());
            async Task ShutdownAsync()
            {
                if (shuttingDown)
                    return;
                shuttingDown = true;
                _logger.LogInformation("正在释放应用资源");
                int exitCode = 0;
                try
                {
                    try
                    {
                        await debugPlugin.DisposeAsync();
                    }
                    finally
                    {
                        await audio.DisposeAsync();
                    }
                }
                catch (Exception error)
                {
                    _logger.LogError(error, "退出释放失败");
                    exitCode = 1;
                }
                released = true;
                _logger.LogInformation("应用资源释放结束，退出码 {ExitCode}", exitCode);
                desktop.Shutdown(exitCode);
            }
            desktop.MainWindow.Closing += async (_, args) =>
            {
                if (released)
                    return;
                args.Cancel = true;
                await ShutdownAsync();
            };
            desktop.ShutdownRequested += async (_, args) =>
            {
                if (released)
                    return;
                args.Cancel = true;
                await ShutdownAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
