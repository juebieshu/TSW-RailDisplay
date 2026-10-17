#if DEBUG
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace RailDisplay.Debug;

internal sealed class Win32DebugMenu : IDisposable
{
    private const uint OpenCommand = 0x7101;
    private const uint SettingsCommand = 0x7103;
    private readonly Window _window;
    private readonly Action _openDebug;
    private readonly Action _openSettings;
    private readonly ILogger _logger;
    private IntPtr _menu;
    private IntPtr _windowHandle;
    private bool _disposed;

    public Win32DebugMenu(Window window, Action openDebug, Action openSettings, ILogger logger)
    {
        _window = window;
        _openDebug = openDebug;
        _openSettings = openSettings;
        _logger = logger;
        window.Opened += OnOpened;
        Win32Properties.AddWndProcHookCallback(window, WndProc);
        try
        {
            if (window.IsVisible)
                Attach();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnOpened(object? sender, EventArgs args) => Attach();

    private void Attach()
    {
        if (_disposed || _menu != IntPtr.Zero)
            return;
        _windowHandle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("未取得原生菜单所需的窗口句柄。");
        if (GetMenu(_windowHandle) != IntPtr.Zero)
            throw new InvalidOperationException("窗口已经有原生菜单，不能覆盖。");

        IntPtr menu = CreateMenu();
        if (menu == IntPtr.Zero) throw new Win32Exception();
        IntPtr popup = IntPtr.Zero;
        bool assigned = false;
        try
        {
            popup = CreatePopupMenu();
            if (popup == IntPtr.Zero) throw new Win32Exception();
            Check(AppendMenuW(menu, 0x10 /* MF_POPUP */, (nuint)popup, "&Debug"));
            IntPtr debugMenu = popup;
            popup = IntPtr.Zero; // 从此由父菜单递归释放。
            Check(AppendMenuW(debugMenu, 0, OpenCommand, NativeDebugMenu.OpenLabel));
            Check(AppendMenuW(debugMenu, 0, SettingsCommand, NativeDebugMenu.SettingsLabel));
            Check(GetClientRect(_windowHandle, out var before));
            Check(GetWindowRect(_windowHandle, out var bounds));
            Check(SetMenu(_windowHandle, menu));
            assigned = true;
            Check(DrawMenuBar(_windowHandle));
            Check(GetClientRect(_windowHandle, out var after));
            // 给系统菜单增加外框高度，保留原来的屏幕内容尺寸。
            Check(SetWindowPos(_windowHandle, IntPtr.Zero, 0, 0,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top + before.Bottom - after.Bottom,
                0x16 /* SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE */));
            _menu = menu;
        }
        catch
        {
            if (assigned && !SetMenu(_windowHandle, IntPtr.Zero))
                _logger.LogError("失败回滚时无法移除原生菜单，Win32 错误码 {ErrorCode}", Marshal.GetLastWin32Error());
            if (popup != IntPtr.Zero) ReleaseMenu(popup);
            if (!assigned || GetMenu(_windowHandle) != menu) ReleaseMenu(menu);
            throw;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed || message != 0x111 /* WM_COMMAND */ || lParam != IntPtr.Zero)
            return IntPtr.Zero;
        Action? action = (nuint)wParam switch
        {
            OpenCommand => _openDebug,
            SettingsCommand => _openSettings,
            _ => null
        };
        if (action is not null)
        {
            handled = true;
            // 菜单消息返回后再执行，避免重绘或异常跨越原生回调。
            Dispatcher.UIThread.Post(() => { if (!_disposed) action(); });
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Opened -= OnOpened;
        Win32Properties.RemoveWndProcHookCallback(_window, WndProc);
        if (_menu == IntPtr.Zero) return;
        // HWND 已销毁时，Windows 已递归释放其菜单。
        if (IsWindow(_windowHandle))
        {
            if (GetMenu(_windowHandle) == _menu)
            {
                Check(SetMenu(_windowHandle, IntPtr.Zero));
                Check(DrawMenuBar(_windowHandle));
            }
            ReleaseMenu(_menu);
        }
        _menu = IntPtr.Zero;
    }

    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception();
    }

    private void ReleaseMenu(IntPtr menu)
    {
        if (!DestroyMenu(menu))
            _logger.LogError("释放原生菜单失败，Win32 错误码 {ErrorCode}", Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateMenu();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMenu(IntPtr window, IntPtr menu);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DrawMenuBar(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
#endif
