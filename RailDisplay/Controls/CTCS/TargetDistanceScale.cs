using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RailDisplay.Controls.CTCS;

public sealed class TargetDistanceScale : Control
{
    private const double FullHeight = 172;

    // 高度分配比例
    private const double LinearRatio = 13.0 / 15;
    private const double LogRatio = 2.0 / 15;

    public static readonly StyledProperty<double?> TargetDistanceProperty =
        AvaloniaProperty.Register<TargetDistanceScale, double?>(
            nameof(TargetDistance),
            validate: value => value is null ||
                               (double.IsFinite(value.Value) && value.Value >= 0));

    /// <summary>
    /// 目标距离，单位米；null 表示未收到。
    /// </summary>
    public double? TargetDistance
    {
        get => GetValue(TargetDistanceProperty);
        set => SetValue(TargetDistanceProperty, value);
    }

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<TargetDistanceScale, IBrush>(
            nameof(Foreground),
            Brushes.White);

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// 光带高度，最大 172px。
    /// </summary>
    public double BandHeight => DistanceHeight(TargetDistance ?? 0);

    static TargetDistanceScale()
    {
        AffectsRender<TargetDistanceScale>(
            TargetDistanceProperty,
            ForegroundProperty);
    }

    /// <summary>
    /// 距离转换为高度。
    /// 0–100m：线性
    /// 100–1000m：对数
    /// </summary>
    private static double DistanceHeight(double distance)
    {
        distance = Math.Clamp(distance, 0, 1000);

        // 0–100m：线性映射
        if (distance <= 100)
        {
            return FullHeight *
                   LinearRatio *
                   (distance / 100.0);
        }

        // 100–1000m：对数映射
        double logFraction = Math.Log10(distance / 100.0);

        return FullHeight *
               (LinearRatio + LogRatio * logFraction);
    }

    /// <summary>
    /// 绘制刻度线。
    /// </summary>
    private void DrawTick(
        DrawingContext context,
        double distance,
        bool major)
    {
        double thickness = major ? 2 : 1;
        double x = major ? 8 : 18;
        double width = major ? 22 : 12;

        double y = Math.Clamp(
            Math.Round(FullHeight - DistanceHeight(distance)),
            0,
            FullHeight - thickness);

        context.DrawRectangle(
            Foreground,
            null,
            new Rect(x, y, width, thickness));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (TargetDistance is null ||
            Bounds.Width <= 0 ||
            Bounds.Height <= 0)
            return;

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            // 主要刻度
            DrawTick(context, 0, true);
            DrawTick(context, 100, true);
            DrawTick(context, 1000, true);

            // 0–100m 线性区间
            foreach (int distance in new[] { 70, 80, 90, 95 })
            {
                DrawTick(context, distance, false);
            }

            // 100–1000m 对数区间
            // 绘制 4 条细刻度，视觉间距完全相等
            for (int i = 1; i <= 4; i++)
            {
                double fraction = i / 5.0;

                double distance = 100.0 *
                                  Math.Pow(10.0, fraction);

                DrawTick(context, distance, false);
            }

            // 绘制目标距离光带
            if (BandHeight > 0)
            {
                context.DrawRectangle(
                    Foreground,
                    null,
                    new Rect(
                        38,
                        FullHeight - BandHeight,
                        15,
                        BandHeight));
            }
        }
    }
}