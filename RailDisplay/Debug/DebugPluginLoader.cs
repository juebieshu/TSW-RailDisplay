#if DEBUG
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Avalonia.Controls;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.ViewModels.CTCS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RailDisplay.Debug;

public sealed class DebugPluginLoader(Window owner, CtcsViewModel ctcs,
    ILoggerFactory? loggerFactory = null) : IAsyncDisposable
{
    private delegate (Func<Task> Show, Action ShowSettings, Action RefreshButtons, IAsyncDisposable Lifetime) CreatePlugin(Window owner,
        Func<CtcsData> readData, Action<CtcsData> updateData, Action<TimeSpan> advanceBrakeWarning,
        Func<CtcsButton, bool> keyHandle, Func<CtcsButton, bool> canHandle, ILoggerFactory loggerFactory);

    private static string PluginPath => Path.Combine(AppContext.BaseDirectory, "RailDisplay.Debug.dll");
    public bool IsAvailable => File.Exists(PluginPath);
    private Func<Task>? _show;
    private Action? _showSettings;
    private Action? _refreshButtons;
    private IAsyncDisposable? _lifetime;
    private bool _disposed;

    public Task ShowAsync()
    {
        Load();
        return _show!();
    }

    public void ShowSettings()
    {
        Load();
        _showSettings!();
    }

    private void Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_show is null)
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(PluginPath);
            var entry = assembly.GetType("RailDisplay.Debug.DebugPlugin", throwOnError: true)!
                .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("Debug 插件缺少 Create 入口。");
            (_show, _showSettings, _refreshButtons, _lifetime) = entry.CreateDelegate<CreatePlugin>()(
                owner, () => ctcs.Data, ctcs.Update, ctcs.AdvanceBrakeWarning,
                ctcs.KeyHandle, ctcs.CanHandle,
                loggerFactory ?? NullLoggerFactory.Instance);
            ctcs.PropertyChanged += OnCtcsChanged;
        }
    }

    private void OnCtcsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CtcsViewModel.FunctionKeys))
            _refreshButtons?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        ctcs.PropertyChanged -= OnCtcsChanged;
        if (_lifetime is not null)
            await _lifetime.DisposeAsync();
        _refreshButtons = null;
    }
}
#endif
