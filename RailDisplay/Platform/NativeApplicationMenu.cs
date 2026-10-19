using System;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RailDisplay.Platform.Windows;

namespace RailDisplay.Platform;

public sealed class NativeApplicationMenu : IDisposable
{
    private const string AlwaysOnTopLabel = "Always on Top";
    private const string HideTitleBarLabel = "Hide Title Bar";
    private readonly Window _window;
    private readonly NativeMenu? _menu;
    private readonly NativeMenuItem? _debugItem;
    private readonly Win32Menu? _windowsMenu;
    private bool _disposed;

    public NativeApplicationMenu(Window window, Action? openDebug = null, Action? openSettings = null,
        (string Root, string Simulation, string Settings)? debugMenuLabels = null,
        ILogger<NativeApplicationMenu>? logger = null)
    {
        if ((openDebug is null) != (openSettings is null) || (openDebug is null) != (debugMenuLabels is null))
            throw new ArgumentException("Debug 菜单需要同时提供菜单文字和两个操作。");

        _window = window;
        if (OperatingSystem.IsMacOS())
        {
            if (openDebug is null)
                return;

            _menu = NativeMenu.GetMenu(window);
            if (_menu is null)
            {
                _menu = new NativeMenu();
                NativeMenu.SetMenu(window, _menu);
            }
            _debugItem = new NativeMenuItem(debugMenuLabels!.Value.Root)
            {
                Menu = new NativeMenu
                {
                    new NativeMenuItem(debugMenuLabels!.Value.Simulation) { Command = new RelayCommand(openDebug!) },
                    new NativeMenuItem(debugMenuLabels.Value.Settings) { Command = new RelayCommand(openSettings!) }
                }
            };
            _menu.Items.Add(_debugItem);
        }
        else if (OperatingSystem.IsWindows())
        {
            var items = new System.Collections.Generic.List<Win32MenuItem>();
            if (openDebug is not null)
                items.Add(new Win32MenuItem(debugMenuLabels!.Value.Root, Children:
                [
                    new Win32MenuItem(debugMenuLabels.Value.Simulation, openDebug),
                    new Win32MenuItem(debugMenuLabels.Value.Settings, openSettings!)
                ]));
            items.Add(new Win32MenuItem(AlwaysOnTopLabel, IsEnabled: false));
            items.Add(new Win32MenuItem(HideTitleBarLabel, IsEnabled: false));
            _windowsMenu = new Win32Menu(window, items, logger ?? NullLogger<NativeApplicationMenu>.Instance);
        }
        else
            throw new PlatformNotSupportedException("原生应用菜单仅支持 Windows 和 macOS。");

        window.Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Closed -= OnClosed;
        _windowsMenu?.Dispose();
        // 保持根菜单实例不变；Avalonia 12.1.4 的原生导出器不接受替换已绑定的根菜单。
        if (_debugItem is not null)
            _menu!.Items.Remove(_debugItem);
    }
}
