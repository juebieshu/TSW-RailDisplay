using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using RailDisplay.ViewModels.CTCS;

namespace RailDisplay.Views;

public partial class CTCS2_200H : UserControl
{
    private readonly DispatcherTimer _messageBlinkTimer;
    private readonly DispatcherTimer _clockTimer;
    public CTCS2_200H()
    {
        InitializeComponent();
        _messageBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _messageBlinkTimer.Tick += (_, _) =>
        {
            if (DataContext is CtcsViewModel model)
                model.ToggleTextConfirmationBlink();
        };
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => RefreshLocalTime();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!Design.IsDesignMode)
        {
            _messageBlinkTimer.Start();
            RefreshLocalTime();
            _clockTimer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _messageBlinkTimer.Stop();
        _clockTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshLocalTime()
    {
        // shortcut: 当前显示电脑本地时间，接入游戏时间后替换本地计时器数据源。
        if (DataContext is CtcsViewModel model)
            model.UpdateDisplayTime(DateTimeOffset.Now);
    }

}
