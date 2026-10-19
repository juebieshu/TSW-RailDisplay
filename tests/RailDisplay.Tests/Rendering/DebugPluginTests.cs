using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Path = System.IO.Path;
using RailDisplay.Infrastructure.Logging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RailDisplay.Controls;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Debug;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Debug.ViewModels;

using Avalonia.Headless.XUnit;

namespace RailDisplay.Tests.Rendering;

public sealed class DebugPluginTests
{
    [AvaloniaFact]
    public void PluginLifetimeAndLogging()
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
            Assert.False(!plugin.IsAvailable || !ReferenceEquals(snapshot, model.Data), "An installed plugin must stay inactive until requested.");
            plugin.ShowSettings();
            plugin.ShowSettings();
            snapshot = model.Data;
            Wait(Task.Delay(100, TestContext.Current.CancellationToken));
            Assert.False(owner.OwnedWindows.Count != 1 || owner.OwnedWindows.Single().Title != "Debug Setting" ||
                !ReferenceEquals(snapshot, model.Data),
                "Settings must keep one window and must not start the simulation.");
            Wait(plugin.ShowAsync());
            var window = owner.OwnedWindows.Single(child => child.DataContext is DebugViewModel);
            var handles = (DebugViewModel)window.DataContext!;
            handles.Power = 7;
            PumpUntil(() => model.Data.Speed > 20);
            Wait(plugin.ShowAsync());
            Assert.False(owner.OwnedWindows.Count != 2, "Repeated activation must keep one Debug window and one simulation.");
            window.Close();
            snapshot = model.Data;
            Wait(Task.Delay(150, TestContext.Current.CancellationToken));
            Assert.False(!ReferenceEquals(snapshot, model.Data) || owner.Title != "Debug plugin check", "Closing Debug must stop all updates and preserve the owner's title.");
            Wait(plugin.ShowAsync());
            Assert.False(!ReferenceEquals(owner.OwnedWindows.Single(child => child.DataContext is DebugViewModel).DataContext, handles) || handles.Power != 7 ||
                model.Data.Speed != snapshot.Speed || model.Data.TextMessages.Count != snapshot.TextMessages.Count,
                "Reopening Debug must preserve handles, speed and sample data.");
            PumpUntil(() => model.Data.Speed > snapshot.Speed);
            Wait(plugin.DisposeAsync().AsTask());
            Wait(plugin.DisposeAsync().AsTask());
            snapshot = model.Data;
            Wait(Task.Delay(100, TestContext.Current.CancellationToken));
            Assert.False(owner.OwnedWindows.Count != 0 || !ReferenceEquals(snapshot, model.Data), "Disposing the plugin must close its window and stop simulation.");
            Assert.ThrowsAny<ObjectDisposedException>(() => Wait(plugin.ShowAsync()));
            string text = File.ReadAllText(Directory.GetFiles(logDirectory, "*.log").Single());
            Assert.False(!text.Contains("Debug 插件已初始化") || !text.Contains("启动模拟") ||
                !text.Contains("模拟已暂停"),
                "插件必须使用启动入口传入的 Logger 记录生命周期。");
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
}
