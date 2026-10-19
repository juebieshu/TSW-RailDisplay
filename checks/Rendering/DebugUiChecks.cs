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

internal static class DebugUiChecks
{
    public static void Run()
    {
        CtcsKeyUiChecks.Run();
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
                throw new Exception("Native menus and hot reload must preserve state and dispatch their own actions.");
            display.UsePixelRendering = false;
            HotReloadHandler.ForceRedraw(window);
            if (image.Source is not null || image.IsVisible)
                throw new Exception("Force redraw must respect native rendering mode.");
            display.UsePixelRendering = true;
            foreach (var size in new[] { new Size(320, 240), new Size(800, 600), new Size(1280, 960) })
            {
                display.Measure(size);
                display.Arrange(new Rect(size));
                display.ForceRedraw();
                if (image.Source is not RenderTargetBitmap frame || frame.PixelSize != new PixelSize(640, 480))
                    throw new Exception("Force redraw must preserve the source resolution at all window sizes.");
            }
        }
        finally
        {
            window.Close();
        }
        CheckMenuLifetime();
        CheckPluginLifetime();
        Console.WriteLine("原生 Debug 菜单、重绘、缩放及插件暂停、恢复和释放检查通过。");

        void InvokeMenu(int index)
        {
            if (OperatingSystem.IsMacOS())
            {
                var root = NativeMenu.GetMenu(window)!;
                var debug = (NativeMenuItem)root.Items.Single();
                var item = (NativeMenuItem)debug.Menu!.Items[index];
                if (debug.Header != "Debug" || debug.Menu.Items.Count != 2 ||
                    item.Header != (index == 0 ? "Simulation" : "Debug Setting"))
                    throw new Exception("Incorrect native Debug menu labels.");
                item.Command!.Execute(null);
            }
            else if (OperatingSystem.IsWindows())
            {
                var hwnd = window.TryGetPlatformHandle()!.Handle;
                if (GetMenu(hwnd) != IntPtr.Zero)
                    throw new Exception("Windows Debug actions must use a client-area popup, not a title-bar menu.");
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
            if (OperatingSystem.IsMacOS() &&
                (!ReferenceEquals(NativeMenu.GetMenu(window), root) || root.Items.Single() != existing))
                throw new Exception("Disposing Debug must preserve the existing native menu and its root instance.");
            using var second = new NativeApplicationMenu(window, () => { }, () => { },
                ("Debug", "Simulation", "Debug Setting"));
            window.Close();
            Dispatcher.UIThread.RunJobs();
            if (OperatingSystem.IsMacOS() &&
                (!ReferenceEquals(NativeMenu.GetMenu(window), root) || root.Items.Single() != existing))
                throw new Exception("Closing a window must remove only its Debug menu without replacing the root.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void CheckPluginLifetime()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        model.Update(new CtcsData { Speed = 20 });
        var snapshot = model.Data;
        var owner = new Window { Title = "Debug plugin check" };
        string logDirectory = Path.Combine(Path.GetTempPath(), "RailDisplay-plugin-log-" + Guid.NewGuid().ToString("N"));
        using var loggerFactory = ApplicationLogging.CreateFactory(logDirectory);
        var plugin = new DebugPluginLoader(owner, model, loggerFactory);
        try
        {
            owner.Show();
            if (!plugin.IsAvailable || !ReferenceEquals(snapshot, model.Data))
                throw new Exception("An installed plugin must stay inactive until requested.");
            plugin.ShowSettings();
            plugin.ShowSettings();
            snapshot = model.Data;
            Wait(Task.Delay(100));
            if (owner.OwnedWindows.Count != 1 || owner.OwnedWindows.Single().Title != "Debug Setting" ||
                !ReferenceEquals(snapshot, model.Data))
                throw new Exception("Settings must keep one window and must not start the simulation.");
            Wait(plugin.ShowAsync());
            var window = owner.OwnedWindows.Single(child => child.DataContext is DebugViewModel);
            var handles = (DebugViewModel)window.DataContext!;
            handles.Power = 7;
            PumpUntil(() => model.Data.Speed > 20);
            Wait(plugin.ShowAsync());
            if (owner.OwnedWindows.Count != 2)
                throw new Exception("Repeated activation must keep one Debug window and one simulation.");
            window.Close();
            snapshot = model.Data;
            Wait(Task.Delay(150));
            if (!ReferenceEquals(snapshot, model.Data) || owner.Title != "Debug plugin check")
                throw new Exception("Closing Debug must stop all updates and preserve the owner's title.");
            Wait(plugin.ShowAsync());
            if (!ReferenceEquals(owner.OwnedWindows.Single(child => child.DataContext is DebugViewModel).DataContext, handles) || handles.Power != 7 ||
                model.Data.Speed != snapshot.Speed || model.Data.TextMessages.Count != snapshot.TextMessages.Count)
                throw new Exception("Reopening Debug must preserve handles, speed and sample data.");
            PumpUntil(() => model.Data.Speed > snapshot.Speed);
            Wait(plugin.DisposeAsync().AsTask());
            Wait(plugin.DisposeAsync().AsTask());
            snapshot = model.Data;
            Wait(Task.Delay(100));
            if (owner.OwnedWindows.Count != 0 || !ReferenceEquals(snapshot, model.Data))
                throw new Exception("Disposing the plugin must close its window and stop simulation.");
            try
            {
                plugin.ShowAsync();
                throw new Exception("A disposed plugin accepted activation.");
            }
            catch (ObjectDisposedException)
            {
            }
            string text = File.ReadAllText(Directory.GetFiles(logDirectory, "*.log").Single());
            if (!text.Contains("Debug 插件已初始化") || !text.Contains("模拟已启动或恢复") ||
                !text.Contains("模拟已停止"))
                throw new Exception("插件必须使用启动入口传入的 Logger 记录生命周期。");
        }
        finally
        {
            Wait(plugin.DisposeAsync().AsTask());
            owner.Close();
            loggerFactory.Dispose();
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    private static void Wait(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Debug 插件生命周期检查超时。");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr hwnd);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
#endif
