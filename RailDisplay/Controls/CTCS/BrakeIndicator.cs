using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RailDisplay.Core.Models.CTCS;

namespace RailDisplay.Controls.CTCS;

public sealed class BrakeIndicator : Control
{
    public static readonly StyledProperty<CtcsBrakeLevel> BrakeLevelProperty =
        AvaloniaProperty.Register<BrakeIndicator, CtcsBrakeLevel>(
            nameof(BrakeLevel), CtcsBrakeLevel.ReleaseAllowed,
            validate: level => Enum.IsDefined(level));

    public CtcsBrakeLevel BrakeLevel
    {
        get => GetValue(BrakeLevelProperty);
        set => SetValue(BrakeLevelProperty, value);
    }

    public static readonly StyledProperty<IBrush> RedBrushProperty =
        AvaloniaProperty.Register<BrakeIndicator, IBrush>(nameof(RedBrush), Brushes.Red);

    public IBrush RedBrush
    {
        get => GetValue(RedBrushProperty);
        set => SetValue(RedBrushProperty, value);
    }

    public static readonly StyledProperty<IBrush> BlackBrushProperty =
        AvaloniaProperty.Register<BrakeIndicator, IBrush>(nameof(BlackBrush), Brushes.Black);

    public IBrush BlackBrush
    {
        get => GetValue(BlackBrushProperty);
        set => SetValue(BlackBrushProperty, value);
    }

    static BrakeIndicator()
    {
        AffectsRender<BrakeIndicator>(BrakeLevelProperty, RedBrushProperty, BlackBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (BrakeLevel is CtcsBrakeLevel.NoBrake or CtcsBrakeLevel.ReleaseAllowed || Bounds.Width <= 4 || Bounds.Height <= 4)
            return;

        // 边框由外层 C9 绘制，图标在可用内容区域内保留 2px 留白。
        var area = new Rect(2, 2, Bounds.Width - 4, Bounds.Height - 4);
        double stripeHeight = (area.Height - 2 * 3) / 3;
        if (stripeHeight <= 0)
            return;
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            if (BrakeLevel == CtcsBrakeLevel.EmergencyBrake)
                context.DrawRectangle(RedBrush, null, area);
            else
            {
                if (BrakeLevel is CtcsBrakeLevel.MediumServiceBrake or CtcsBrakeLevel.ServiceBrake)
                {
                    context.DrawRectangle(RedBrush, null, new Rect(area.X, area.Y, area.Width, stripeHeight));
                    context.DrawRectangle(RedBrush, null, new Rect(area.X, area.Bottom - stripeHeight, area.Width, stripeHeight));
                }
                if (BrakeLevel is CtcsBrakeLevel.WeakServiceBrake or CtcsBrakeLevel.ServiceBrake)
                    context.DrawRectangle(RedBrush, null,
                        new Rect(area.X, area.Y + (area.Height - stripeHeight) / 2, area.Width, stripeHeight));
            }

            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double radius = area.Height / 2;
            context.DrawEllipse(BlackBrush, null, center, radius + 4, radius + 4);
            context.DrawEllipse(RedBrush, null, center, radius, radius);
        }
    }
}
