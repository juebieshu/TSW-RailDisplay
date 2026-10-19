using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Debug.CTCS;
using RailDisplay.Debug.ViewModels;
using RailDisplay.Debug.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RailDisplay.Debug;

public sealed class DebugPlugin : IAsyncDisposable
{
    private readonly Window _owner;
    private readonly DebugViewModel _handles;
    private readonly CtcsDebugSimulation _simulation;
    private readonly DebugSettingsViewModel _settings;
    private readonly ILogger<DebugPlugin> _logger;
    private DebugWindow? _window;
    private DebugSettingsWindow? _settingsWindow;
    private CancellationTokenSource? _cancellation;
    private Task _running = Task.CompletedTask;
    private Task _stopping = Task.CompletedTask;
    private bool _disposed;

    public static (string Root, string Simulation, string Settings) GetMenuLabels() =>
        ("Debug", "Simulation", "Debug Setting");

    public DebugPlugin(Window owner, Func<CtcsData> readData, Action<CtcsData> updateData,
        Action<TimeSpan> advanceBrakeWarning, Func<CtcsButton, bool> keyHandle,
        Func<CtcsButton, bool> canHandle, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(readData);
        ArgumentNullException.ThrowIfNull(updateData);
        ArgumentNullException.ThrowIfNull(advanceBrakeWarning);
        _handles = new DebugViewModel(keyHandle, canHandle);
        _owner = owner;
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<DebugPlugin>();
        _simulation = new CtcsDebugSimulation(readData, updateData, advanceBrakeWarning, _handles,
            loggerFactory.CreateLogger<CtcsDebugSimulation>());
        updateData(CtcsDebugData.Create(readData()));
        _settings = new DebugSettingsViewModel(readData, updateData);
        _logger.LogInformation("Debug 插件已初始化");
    }

    // 主程序仅通过这一固定入口加载插件，不引用插件程序集或其界面类型。
    public static (Func<Task> Show, Action ShowSettings, Action RefreshButtons, IAsyncDisposable Lifetime) Create(Window owner,
        Func<CtcsData> readData, Action<CtcsData> updateData, Action<TimeSpan> advanceBrakeWarning,
        Func<CtcsButton, bool> keyHandle, Func<CtcsButton, bool> canHandle, ILoggerFactory loggerFactory)
    {
        var plugin = new DebugPlugin(owner, readData, updateData, advanceBrakeWarning, keyHandle, canHandle, loggerFactory);
        return (plugin.ShowAsync, plugin.ShowSettings, plugin._handles.RefreshButtons, plugin);
    }

    public void ShowSettings()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settings.Reload();
        _settingsWindow = new DebugSettingsWindow { DataContext = _settings };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        try
        {
            _settingsWindow.Show(_owner);
        }
        catch
        {
            _settingsWindow?.Close();
            throw;
        }
    }

    public async Task ShowAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        await _stopping;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null)
        {
            _window.Activate();
            return;
        }
        _window = new DebugWindow { DataContext = _handles };
        _window.Closed += OnWindowClosed;
        _cancellation = new CancellationTokenSource();
        _running = _simulation.RunAsync(_cancellation.Token);
        try
        {
            _window.Show(_owner);
            _logger.LogInformation("启动模拟");
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _window!.Closed -= OnWindowClosed;
        _window = null;
        _stopping = StopSimulationAsync();
    }

    public Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _window?.Close();
        return _stopping;
    }

    private async Task StopSimulationAsync()
    {
        var cancellation = _cancellation;
        if (cancellation is null)
            return;
        _cancellation = null;
        cancellation.Cancel();
        try
        {
            await _running;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
            _logger.LogInformation("模拟已暂停");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _settingsWindow?.Close();
        await StopAsync();
    }
}
