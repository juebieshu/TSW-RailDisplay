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

    private const string PluginFileName = "Debug.dll";
    private Assembly? _assembly;
    private static string? FindPluginPath()
    {
        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            if (string.Equals(Path.GetFileName(path), PluginFileName, StringComparison.OrdinalIgnoreCase))
                return path;
        return null;
    }

    public bool IsAvailable => FindPluginPath() is not null;
    private Func<Task>? _show;
    private Action? _showSettings;
    private Action? _refreshButtons;
    private IAsyncDisposable? _lifetime;
    private bool _disposed;

    public (string Root, string Simulation, string Settings) GetMenuLabels()
    {
        var method = GetPluginType().GetMethod("GetMenuLabels", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("Debug 插件缺少 GetMenuLabels 入口。");
        var labels = method.CreateDelegate<Func<(string Root, string Simulation, string Settings)>>()();
        if (string.IsNullOrWhiteSpace(labels.Root) || string.IsNullOrWhiteSpace(labels.Simulation) ||
            string.IsNullOrWhiteSpace(labels.Settings))
            throw new InvalidDataException("Debug 插件菜单文字不能为空。");
        return labels;
    }

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
            var entry = GetPluginType()
                .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("Debug 插件缺少 Create 入口。");
            (_show, _showSettings, _refreshButtons, _lifetime) = entry.CreateDelegate<CreatePlugin>()(
                owner, () => ctcs.Data, ctcs.Update, ctcs.AdvanceBrakeWarning,
                ctcs.KeyHandle, ctcs.CanHandle,
                loggerFactory ?? NullLoggerFactory.Instance);
            ctcs.PropertyChanged += OnCtcsChanged;
        }
    }

    private Type GetPluginType()
    {
        if (_assembly is null)
        {
            string pluginPath = FindPluginPath() ?? throw new FileNotFoundException(
                "应用目录中没有可加载的 Debug.dll。", Path.Combine(AppContext.BaseDirectory, PluginFileName));
            _assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
        }
        return _assembly.GetType("RailDisplay.Debug.DebugPlugin", throwOnError: true)!;
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
