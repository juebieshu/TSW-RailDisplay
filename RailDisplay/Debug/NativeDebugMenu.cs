#if DEBUG
using System;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RailDisplay.Debug;

public sealed class NativeDebugMenu : IDisposable
{
    internal const string OpenLabel = "Simulation";
    internal const string SettingsLabel = "Debug Setting";
    private readonly Window _window;
    private readonly NativeMenu? _menu;
    private readonly NativeMenuItem? _debugItem;
    private readonly Win32DebugMenu? _windowsMenu;
    private bool _disposed;

    public NativeDebugMenu(Window window, Action openDebug, Action openSettings,
        ILogger<NativeDebugMenu>? logger = null)
    {
        _window = window;
        if (OperatingSystem.IsMacOS())
        {
            _menu = NativeMenu.GetMenu(window);
            if (_menu is null)
            {
                _menu = new NativeMenu();
                NativeMenu.SetMenu(window, _menu);
            }
            _debugItem = new NativeMenuItem("Debug")
            {
                Menu = new NativeMenu
                {
                    new NativeMenuItem(OpenLabel) { Command = new RelayCommand(openDebug) },
                    new NativeMenuItem(SettingsLabel) { Command = new RelayCommand(openSettings) }
                }
            };
            _menu.Items.Add(_debugItem);
        }
        else if (OperatingSystem.IsWindows())
            _windowsMenu = new Win32DebugMenu(window, openDebug, openSettings,
                logger ?? NullLogger<NativeDebugMenu>.Instance);
        else
            throw new PlatformNotSupportedException("原生 Debug 菜单仅支持 Windows 和 macOS。");

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
#endif
