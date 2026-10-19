using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace RailDisplay.Platform.Windows;

internal sealed record Win32MenuItem(string Text, Action? Action = null,
    IReadOnlyList<Win32MenuItem>? Children = null, bool IsEnabled = true);

internal sealed class Win32Menu : IDisposable
{
    private const uint MfPopup = 0x10;
    private const uint MfGrayed = 0x1;
    private const uint WmCommand = 0x111;
    private const uint WmNull = 0x0;
    private const uint WmRightButtonUp = 0x205;

    private readonly Window _window;
    private readonly IReadOnlyList<Win32MenuItem> _items;
    private readonly Dictionary<uint, Action> _actions = new();
    private readonly ILogger _logger;
    private IntPtr _menu;
    private uint _nextCommandId = 0x7100;
    private bool _disposed;

    public Win32Menu(Window window, IReadOnlyList<Win32MenuItem> items, ILogger logger)
    {
        _window = window;
        _items = items;
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

    private void OnOpened(object? sender, EventArgs args) => Dispatcher.UIThread.Post(Attach);

    private void Attach()
    {
        if (_disposed || _menu != IntPtr.Zero)
            return;
        IntPtr windowHandle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("未取得原生菜单所需的窗口句柄。");

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) throw new Win32Exception();
        try
        {
            AppendItems(menu, _items);
            _menu = menu;
        }
        catch
        {
            ReleaseMenu(menu);
            throw;
        }
    }

    private void AppendItems(IntPtr menu, IReadOnlyList<Win32MenuItem> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
                throw new ArgumentException("原生菜单项文字不能为空。", nameof(items));
            if (item.Children is not null)
            {
                if (item.Action is not null)
                    throw new ArgumentException("子菜单标题不能同时绑定操作。", nameof(items));
                IntPtr popup = CreatePopupMenu();
                if (popup == IntPtr.Zero) throw new Win32Exception();
                try
                {
                    AppendItems(popup, item.Children);
                    uint flags = MfPopup | (item.IsEnabled ? 0 : MfGrayed);
                    Check(AppendMenuW(menu, flags, (nuint)popup, item.Text));
                    popup = IntPtr.Zero;
                }
                finally
                {
                    if (popup != IntPtr.Zero)
                        ReleaseMenu(popup);
                }
                continue;
            }

            if (item.Action is null && item.IsEnabled)
                throw new ArgumentException("菜单命令必须绑定操作或子菜单。", nameof(items));
            if (item.Action is null)
            {
                Check(AppendMenuW(menu, MfGrayed, 0, item.Text));
                continue;
            }
            uint commandId = _nextCommandId++;
            _actions.Add(commandId, item.Action);
            Check(AppendMenuW(menu, item.IsEnabled ? 0 : MfGrayed, commandId, item.Text));
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed)
            return IntPtr.Zero;

        if (message == WmRightButtonUp && _menu != IntPtr.Zero)
        {
            handled = true;
            var position = new Point(GetSignedLowWord(lParam), GetSignedHighWord(lParam));
            Check(ClientToScreen(hwnd, ref position));
            SetForegroundWindow(hwnd);
            Check(TrackPopupMenuEx(_menu, 0, position.X, position.Y, hwnd, IntPtr.Zero));
            PostMessageW(hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            return IntPtr.Zero;
        }

        if (message != WmCommand || lParam != IntPtr.Zero)
            return IntPtr.Zero;
        uint commandId = unchecked((uint)wParam.ToInt64()) & 0xFFFF;
        if (_actions.TryGetValue(commandId, out var action))
        {
            handled = true;
            // 菜单消息返回后再执行，避免异常或窗口关闭跨越原生回调。
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
        ReleaseMenu(_menu);
        _menu = IntPtr.Zero;
    }

    private static int GetSignedLowWord(IntPtr value) => unchecked((short)(value.ToInt64() & 0xFFFF));
    private static int GetSignedHighWord(IntPtr value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

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
    private struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackPopupMenuEx(IntPtr menu, uint flags,
        int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
}
