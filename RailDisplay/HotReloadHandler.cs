#if DEBUG
using System;
using System.Linq;
using System.Reflection.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RailDisplay.Controls;

[assembly: MetadataUpdateHandler(typeof(RailDisplay.HotReloadHandler))]

namespace RailDisplay;

public static class HotReloadHandler
{
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            foreach (var window in desktop.Windows.ToArray())
                ForceRedraw(window);
            Avalonia.Logging.Logger.TryGet(Avalonia.Logging.LogEventLevel.Information, "HotReload")?
                .Log(null, "已重绘窗口，保留当前业务与手柄状态。");
        });
    }

    public static void ForceRedraw(Window window)
    {
        Dispatcher.UIThread.VerifyAccess();
        var visuals = window.GetVisualDescendants().Prepend(window).ToArray();
        foreach (var visual in visuals)
        {
            if (visual is Layoutable layout)
            {
                layout.InvalidateMeasure();
                layout.InvalidateArrange();
            }
            visual.InvalidateVisual();
        }
        window.UpdateLayout();
        foreach (var display in visuals.OfType<PixelDisplay>())
            display.ForceRedraw();
    }
}
#endif
