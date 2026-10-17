using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RailDisplay.Controls.CTCS;

public sealed class DistanceScale : Control
{
    public static readonly StyledProperty<int> MaximumDistanceProperty =
        AvaloniaProperty.Register<DistanceScale, int>(nameof(MaximumDistance), 8000,
            validate: value => value is 8000 or 16000);

    /// <summary>显示量程，单位为米，仅接受 8000 或 16000。</summary>
    public int MaximumDistance
    {
        get => GetValue(MaximumDistanceProperty);
        set => SetValue(MaximumDistanceProperty, value);
    }

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<DistanceScale, IBrush>(nameof(Foreground), Brushes.White);

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<DistanceScale, IBrush>(nameof(GridBrush), Brushes.Transparent);

    public IBrush GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    private static readonly Typeface LabelTypeface = new("Arial");

    static DistanceScale()
    {
        AffectsRender<DistanceScale>(MaximumDistanceProperty, ForegroundProperty, GridBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        int intervals = MaximumDistance == 8000 ? 3 : 4;
        var gridPen = new Pen(GridBrush, 1);
        double labelTop = Math.Max(0, Bounds.Height - 20);
        // D7 高 150px；顶线偏移由 speedGridTop 设置，0 底线位置不变。
        double speedGridHeight = Math.Min(150, labelTop);
        const double speedGridTop = 30;
        double distanceWidth = Math.Max(0, Bounds.Width - 1);
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            for (int index = 0; index <= intervals + 1; index++)
            {
                double position = index == 0 ? 0 : 1.0 / 3 + (index - 1) * (2.0 / 3 / intervals);
                double x = position * Math.Max(0, distanceWidth - 1) + 0.5;
                context.DrawLine(gridPen, new Point(x, 0), new Point(x, labelTop));
                string label = index == 0 ? "0" : $"{1 << (index - 1)}k";
                var text = new FormattedText(label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 11, Foreground);
                // 两端文字向内对齐，刻度位置仍在标尺首尾。
                double textX = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, distanceWidth - text.Width));
                context.DrawText(text, new Point(textX, labelTop + 4));
            }
        }
        if (speedGridHeight > speedGridTop)
        {
            for (int speed = 0; speed <= 300; speed += 50)
            {
                double y = speedGridTop + 0.5 + (1 - speed / 300.0) * (speedGridHeight - speedGridTop);
                using (context.PushClip(new Rect(Bounds.Size)))
                    context.DrawLine(gridPen, new Point(0, y), new Point(Bounds.Width, y));
                var text = new FormattedText(speed.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 11, Foreground);
                context.DrawText(text, new Point(3, y - text.Height));
            }
        }
    }
}
