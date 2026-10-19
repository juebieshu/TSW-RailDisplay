#if DEBUG
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Path = System.IO.Path;
using RailDisplay.Infrastructure.Logging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RailDisplay;
using RailDisplay.Controls;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Debug;
using RailDisplay.Platform;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Debug.ViewModels;

namespace RailDisplay.DesktopChecks;

internal static class DebugUiChecks
{
    public static void Run()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        model.Update(new CtcsData { Speed = 123, PermittedSpeed = 180 });
        var snapshot = model.Data;
        var block = new Rectangle { Width = 20, Height = 20, Fill = Brushes.White };
        var canvas = new Canvas();
        canvas.Children.Add(block);
        var display = new PixelDisplay { Content = canvas };
        var window = new Window { Content = display, DataContext = model, Width = 640, Height = 480 };
        int opened = 0, settingsOpened = 0;
        using var menu = new NativeApplicationMenu(window, () => opened++, () => settingsOpened++,
            ("Debug", "Simulation", "Debug Setting"));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            HotReloadHandler.ForceRedraw(window);
            var image = display.GetVisualDescendants().OfType<Image>().Single();
            var previous = image.Source;
            block.Width = 30;
            InvokeMenu(0);
            InvokeMenu(1);
            InvokeMenu(1);
            HotReloadHandler.ForceRedraw(window);
            if (opened != 1 || settingsOpened != 2 || ReferenceEquals(previous, image.Source) ||
                image.Source is not RenderTargetBitmap { PixelSize: var pixels } || pixels != new PixelSize(640, 480) ||
                block.Bounds.Width != 30 || !ReferenceEquals(model.Data, snapshot) ||
                !ReferenceEquals(window.DataContext, model))
                Assert.Fail("Native menus and hot reload must preserve state and dispatch their own actions.");
            display.UsePixelRendering = false;
            HotReloadHandler.ForceRedraw(window);
            Assert.False(image.Source is not null || image.IsVisible, "Force redraw must respect native rendering mode.");
            display.UsePixelRendering = true;
            foreach (var size in new[] { new Size(320, 240), new Size(800, 600), new Size(1280, 960) })
            {
                display.Measure(size);
                display.Arrange(new Rect(size));
                display.ForceRedraw();
                Assert.False(image.Source is not RenderTargetBitmap frame || frame.PixelSize != new PixelSize(640, 480), "Force redraw must preserve the source resolution at all window sizes.");
            }
        }
        finally
        {
            window.Close();
        }
        CheckMenuLifetime();

        void InvokeMenu(int index)
        {
            if (OperatingSystem.IsMacOS())
            {
                var root = NativeMenu.GetMenu(window)!;
                var debug = (NativeMenuItem)root.Items.Single();
                var item = (NativeMenuItem)debug.Menu!.Items[index];
                Assert.False(debug.Header != "Debug" || debug.Menu.Items.Count != 2 ||
                    item.Header != (index == 0 ? "Simulation" : "Debug Setting"),
                    "Incorrect native Debug menu labels.");
                item.Command!.Execute(null);
            }
            else if (OperatingSystem.IsWindows())
            {
                var hwnd = window.TryGetPlatformHandle()!.Handle;
                Assert.False(GetMenu(hwnd) != IntPtr.Zero, "Windows Debug actions must use a client-area popup, not a title-bar menu.");
                SendMessageW(hwnd, 0x111, (IntPtr)(0x7100 + index), IntPtr.Zero);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    private static void CheckMenuLifetime()
    {
        var window = new Window();
        var root = new NativeMenu();
        var existing = new NativeMenuItem("保留菜单") { Menu = new NativeMenu() };
        if (OperatingSystem.IsMacOS())
        {
            root.Items.Add(existing);
            NativeMenu.SetMenu(window, root);
        }
        using var menu = new NativeApplicationMenu(window, () => { }, () => { },
            ("Debug", "Simulation", "Debug Setting"));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            menu.Dispose();
            menu.Dispose();
            Dispatcher.UIThread.RunJobs();
            Assert.False(OperatingSystem.IsMacOS() &&
                (!ReferenceEquals(NativeMenu.GetMenu(window), root) || root.Items.Single() != existing),
                "Disposing Debug must preserve the existing native menu and its root instance.");
            using var second = new NativeApplicationMenu(window, () => { }, () => { },
                ("Debug", "Simulation", "Debug Setting"));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(OperatingSystem.IsMacOS() &&
                (!ReferenceEquals(NativeMenu.GetMenu(window), root) || root.Items.Single() != existing),
                "Closing a window must remove only its Debug menu without replacing the root.");
        }
        finally
        {
            window.Close();
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
#endif
