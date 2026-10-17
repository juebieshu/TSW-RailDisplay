using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RailDisplay.Controls.CTCS;

public class SpeedScale : Control
{
    // ==============================
    // 速度范围
    // ==============================

    private const int MinSpeed = 0;
    private const int MaxSpeed = 400;

    // ==============================
    // 速度与角度映射
    // ==============================

    private const double StartAngle = -140;
    private const double CenterAngle = -5;
    private const double EndAngle = 140;

    private const int CenterSpeed = 150;

    // ==============================
    // 刻度参数
    // ==============================

    private const int TickInterval = 10;

    private const double MajorTickLength = 25;
    private const double MinorTickLength = 15;

    private const double TickWidth = 1;

    // ==============================
    // 字体参数
    // ==============================

    private const double FontSize = 16;
    private const double TextMargin = 5;

    private static readonly Typeface SpeedTypeface =
        new("Arial", weight: FontWeight.Bold);

    // ==============================
    // 环形光带参数
    // ==============================

    // 背景光带从刻度起点之前开始，终点跟随允许速度刻度。
    private const double BackgroundStartAngle = -145;

    // 光带宽度
    private const double BandThickness = 9;

    // 光带与刻度的间距
    private const double BandGap = 3;

    // ==============================
    // 端部矩形参数
    // ==============================

    // 径向长度
    private const double EndBlockWidth = 20;

    // 切向宽度
    private const double EndBlockHeight = 6;

    // ==============================
    // Avalonia 属性
    // ==============================

    // 刻度表直径
    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<SpeedScale, double>(
            nameof(Diameter),
            248
        );

    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    // 当前速度
    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<SpeedScale, double>(
            nameof(Speed),
            0
        );

    public double Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    // null 表示没有目标速度；0 仍是有效的目标速度。
    public static readonly StyledProperty<double?> TargetSpeedProperty =
        AvaloniaProperty.Register<SpeedScale, double?>(
            nameof(TargetSpeed), null,
            validate: value => value is null || double.IsFinite(value.Value));

    public double? TargetSpeed
    {
        get => GetValue(TargetSpeedProperty);
        set => SetValue(TargetSpeedProperty, value);
    }

    public static readonly StyledProperty<double> PermittedSpeedProperty =
        AvaloniaProperty.Register<SpeedScale, double>(
            nameof(PermittedSpeed), 0, validate: double.IsFinite);

    public double PermittedSpeed
    {
        get => GetValue(PermittedSpeedProperty);
        set => SetValue(PermittedSpeedProperty, value);
    }

    // 隐藏两层光带时仍保留允许速度端头。
    public static readonly StyledProperty<bool> ShowLightBandProperty =
        AvaloniaProperty.Register<SpeedScale, bool>(nameof(ShowLightBand), true);

    public bool ShowLightBand
    {
        get => GetValue(ShowLightBandProperty);
        set => SetValue(ShowLightBandProperty, value);
    }

    public static readonly StyledProperty<bool> ShowCsgProperty =
        AvaloniaProperty.Register<SpeedScale, bool>(nameof(ShowCsg), true);

    public bool ShowCsg
    {
        get => GetValue(ShowCsgProperty);
        set => SetValue(ShowCsgProperty, value);
    }

    public static readonly StyledProperty<double?> WarningBandEndSpeedProperty =
        AvaloniaProperty.Register<SpeedScale, double?>(
            nameof(WarningBandEndSpeed), null,
            validate: value => value is null || double.IsFinite(value.Value));

    public double? WarningBandEndSpeed
    {
        get => GetValue(WarningBandEndSpeedProperty);
        set => SetValue(WarningBandEndSpeedProperty, value);
    }

    public static readonly StyledProperty<IBrush?> PointerBrushProperty =
        AvaloniaProperty.Register<SpeedScale, IBrush?>(nameof(PointerBrush), Brushes.Gray);

    public IBrush? PointerBrush
    {
        get => GetValue(PointerBrushProperty);
        set => SetValue(PointerBrushProperty, value);
    }

    // 动态光带颜色
    public static readonly StyledProperty<IBrush?> LightBandBrushProperty =
        AvaloniaProperty.Register<SpeedScale, IBrush?>(
            nameof(LightBandBrush),
            Brushes.White
        );

    public IBrush? LightBandBrush
    {
        get => GetValue(LightBandBrushProperty);
        set => SetValue(LightBandBrushProperty, value);
    }

    // 背景光带颜色
    public static readonly StyledProperty<IBrush?> BackgroundBandBrushProperty =
        AvaloniaProperty.Register<SpeedScale, IBrush?>(
            nameof(BackgroundBandBrush),
            Brushes.DimGray
        );

    public IBrush? BackgroundBandBrush
    {
        get => GetValue(BackgroundBandBrushProperty);
        set => SetValue(BackgroundBandBrushProperty, value);
    }

    // ==============================
    // 属性变化时重新绘制
    // ==============================

    static SpeedScale()
    {
        AffectsRender<SpeedScale>(
            DiameterProperty,
            SpeedProperty,
            TargetSpeedProperty,
            PermittedSpeedProperty,
            ShowCsgProperty,
            ShowLightBandProperty,
            WarningBandEndSpeedProperty,
            PointerBrushProperty,
            LightBandBrushProperty,
            BackgroundBandBrushProperty
        );
    }

    // ==============================
    // 速度转换为角度
    // ==============================

    private static double SpeedToAngle(double speed)
    {
        speed = Math.Clamp(
            speed,
            MinSpeed,
            MaxSpeed
        );

        // 0～150：-140°～-5°。
        if (speed <= CenterSpeed)
        {
            return StartAngle +
                   speed / CenterSpeed *
                   (CenterAngle - StartAngle);
        }

        // 150～400：-5°～140°，每 50 km/h 一组长刻度。
        return CenterAngle +
               (speed - CenterSpeed) /
               (MaxSpeed - CenterSpeed) *
               (EndAngle - CenterAngle);
    }

    // ==============================
    // 判断是否绘制速度数字
    // ==============================

    private static bool ShouldDrawLabel(int speed)
    {
        return speed % 50 == 0;
    }

    // ==============================
    // 极坐标转换
    // ==============================

    private static Point CirclePoint(
        double centerX,
        double centerY,
        double radius,
        double angle)
    {
        double radians = angle * Math.PI / 180.0;

        return new Point(
            centerX + radius * Math.Sin(radians),
            centerY - radius * Math.Cos(radians)
        );
    }

    // ==============================
    // 绘制圆弧
    // ==============================

    private static void DrawBandArc(
        DrawingContext context,
        double centerX,
        double centerY,
        double radius,
        double startAngle,
        double endAngle,
        IBrush brush,
        double thickness = BandThickness)
    {
        if (endAngle <= startAngle)
            return;

        var geometry = new StreamGeometry();

        using (var gc = geometry.Open())
        {
            Point start = CirclePoint(
                centerX,
                centerY,
                radius,
                startAngle
            );

            gc.BeginFigure(start, false);

            double totalAngle =
                endAngle - startAngle;

            // 每 0.5° 生成一个顶点
            int segments = Math.Max(
                1,
                (int)Math.Ceiling(totalAngle / 0.5)
            );

            for (int i = 1; i <= segments; i++)
            {
                double progress =
                    (double)i / segments;

                double angle =
                    startAngle + totalAngle * progress;

                Point point = CirclePoint(
                    centerX,
                    centerY,
                    radius,
                    angle
                );

                gc.LineTo(point);
            }

            gc.EndFigure(false);
        }

        // 平直端点，避免圆弧超出指定角度
        var pen = new Pen(
            brush,
            thickness,
            lineCap: PenLineCap.Flat
        );

        context.DrawGeometry(
            null,
            pen,
            geometry
        );
    }

    // ==============================
    // 绘制端部矩形
    // ==============================

    private static void DrawEndBlock(
        DrawingContext context,
        double centerX,
        double centerY,
        double radius,
        double angle,
        IBrush brush)
    {
        double radians =
            angle * Math.PI / 180.0;

        // 光带外半径
        double outerRadius =
            radius + BandThickness / 2;

        // 矩形中心半径
        // 考虑矩形切向宽度，确保四角
        // 均不超出光带外圆
        double halfLength =
            EndBlockWidth / 2;

        // 刻度有线宽，结束边对齐其较高速度一侧的可见边缘。
        double endOffset = TickWidth / 2;
        double maxTangent = Math.Max(Math.Abs(endOffset), Math.Abs(endOffset - EndBlockHeight));

        double blockRadius =
            Math.Sqrt(
                outerRadius * outerRadius -
                maxTangent * maxTangent
            ) - halfLength;

        // 以允许速度刻度为基准，矩形向较低速度一侧展开。
        Point center = CirclePoint(
            centerX,
            centerY,
            blockRadius,
            angle
        );

        // 径向单位向量
        double rx = Math.Sin(radians);
        double ry = -Math.Cos(radians);

        // 切向单位向量
        double tx = Math.Cos(radians);
        double ty = Math.Sin(radians);

        // 计算矩形四个顶点
        Point Corner(double radial, double tangent)
        {
            return new Point(
                center.X + rx * radial + tx * tangent,
                center.Y + ry * radial + ty * tangent
            );
        }

        Point p1 = Corner(
            -halfLength,
            endOffset - EndBlockHeight
        );

        Point p2 = Corner(
            halfLength,
            endOffset - EndBlockHeight
        );

        Point p3 = Corner(
            halfLength,
            endOffset
        );

        Point p4 = Corner(
            -halfLength,
            endOffset
        );

        var geometry = new StreamGeometry();

        using (var gc = geometry.Open())
        {
            gc.BeginFigure(p1, true);

            gc.LineTo(p2);
            gc.LineTo(p3);
            gc.LineTo(p4);

            gc.EndFigure(true);
        }

        context.DrawGeometry(
            brush,
            null,
            geometry
        );
    }

    // ==============================
    // 绘制背景光带和动态光带
    // ==============================

    private void DrawLightBand(
        DrawingContext context,
        double centerX,
        double centerY,
        double tickRadius)
    {
        if (!ShowCsg)
            return;

        double permittedSpeed = Math.Clamp(
            PermittedSpeed,
            MinSpeed,
            MaxSpeed
        );
        // 未设置目标速度时区间为空，只绘制背景光带和背景色端部。
        double targetSpeed = Math.Clamp(TargetSpeed ?? permittedSpeed, MinSpeed, permittedSpeed);

        // 恢复外侧光带位置，计入刻度线宽后保留 3px 间距。
        double bandRadius =
            tickRadius + TickWidth / 2 + BandGap + BandThickness / 2;

        double permittedAngle = SpeedToAngle(permittedSpeed);
        double targetAngle = SpeedToAngle(targetSpeed);

        // ------------------------------
        // 1. 背景光带：-145° ～ 允许速度
        // ------------------------------

        if (ShowLightBand && BackgroundBandBrush != null)
        {
            DrawBandArc(
                context,
                centerX,
                centerY,
                bandRadius,
                BackgroundStartAngle,
                permittedAngle,
                BackgroundBandBrush
            );
        }

        // ------------------------------
        // 2. 绘制动态光带
        // 范围：目标速度 ～ 允许速度
        // ------------------------------

        if (ShowLightBand && targetSpeed < permittedSpeed && LightBandBrush != null)
        {
            DrawBandArc(
                context,
                centerX,
                centerY,
                bandRadius,
                targetAngle,
                permittedAngle,
                LightBandBrush
            );
        }

        // 加宽警示光带向内扩展，保持外缘位置不变。
        if (ShowLightBand && WarningBandEndSpeed is { } warningEnd && PointerBrush is { } warningBrush)
        {
            DrawBandArc(context, centerX, centerY,
                bandRadius - BandThickness / 2,
                permittedAngle, SpeedToAngle(warningEnd),
                warningBrush, BandThickness * 2);
        }

        // ------------------------------
        // 3. 绘制端部矩形
        // ------------------------------

        // 端部矩形标记允许速度；没有亮起区间时使用背景色。
        IBrush? endBrush = targetSpeed < permittedSpeed
            ? LightBandBrush
            : BackgroundBandBrush;

        if (endBrush != null)
        {
            DrawEndBlock(
                context,
                centerX,
                centerY,
                bandRadius,
                permittedAngle,
                endBrush
            );
        }
    }

    // ==============================
    // 主绘制方法
    // ==============================

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 控件中心
        double centerX = Bounds.Width / 2;
        double centerY = Bounds.Height / 2;

        // 刻度表半径
        double radius = Diameter / 2;

        // ------------------------------
        // 1. 绘制刻度与数字
        // ------------------------------

        var pen = new Pen(
            Brushes.White,
            TickWidth
        );

        for (int speed = MinSpeed;
             speed <= MaxSpeed;
             speed += TickInterval)
        {
            // 全程每 50 km/h 一个长刻度，中间保留四个短刻度。
            bool isMajor = ShouldDrawLabel(speed);

            double length = isMajor
                ? MajorTickLength
                : MinorTickLength;

            // 速度映射到角度
            double angle = SpeedToAngle(speed);

            // 刻度外端
            Point outer = CirclePoint(
                centerX,
                centerY,
                radius,
                angle
            );

            // 刻度内端
            Point inner = CirclePoint(
                centerX,
                centerY,
                radius - length,
                angle
            );

            context.DrawLine(
                pen,
                inner,
                outer
            );

            // ------------------------------
            // 3. 绘制速度数字
            // ------------------------------

            if (ShouldDrawLabel(speed))
            {
                var text = new FormattedText(
                    speed.ToString(
                        CultureInfo.InvariantCulture
                    ),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    SpeedTypeface,
                    FontSize,
                    Brushes.White
                );

                // 数字中心半径
                double textRadius =
                    radius -
                    MajorTickLength -
                    TextMargin -
                    FontSize / 2;

                // 数字中心位置
                Point textCenter = CirclePoint(
                    centerX,
                    centerY,
                    textRadius,
                    angle
                );

                // 文字水平居中
                double textX =
                    textCenter.X - text.Width / 2;

                double textY =
                    textCenter.Y - text.Height / 2 - 3;

                context.DrawText(
                    text,
                    new Point(textX, textY)
                );
            }
        }

        DrawPointer(context, centerX, centerY);

        // 光带和端头后绘制，覆盖刻度。
        DrawLightBand(context, centerX, centerY, radius);
    }

    private void DrawPointer(DrawingContext context, double centerX, double centerY)
    {
        if (PointerBrush is not { } brush || !double.IsFinite(Speed))
            return;

        double angle = SpeedToAngle(Speed);
        var direction = CirclePoint(0, 0, 1, angle);
        Point Corner(double distance, double offset) => new(
            centerX + direction.X * distance - direction.Y * offset,
            centerY + direction.Y * distance + direction.X * offset);

        // 使用单个闭合轮廓，避免各段独立抗锯齿产生接缝。
        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(Corner(0, 6), true);
            gc.LineTo(Corner(82, 6));
            gc.LineTo(Corner(82, 4.5));
            gc.LineTo(Corner(86, 4.5));
            gc.LineTo(Corner(86, 3));
            gc.LineTo(Corner(90, 3));
            gc.LineTo(Corner(90, 1.5));
            gc.LineTo(Corner(105, 1.5));
            gc.LineTo(Corner(105, -1.5));
            gc.LineTo(Corner(90, -1.5));
            gc.LineTo(Corner(90, -3));
            gc.LineTo(Corner(86, -3));
            gc.LineTo(Corner(86, -4.5));
            gc.LineTo(Corner(82, -4.5));
            gc.LineTo(Corner(82, -6));
            gc.LineTo(Corner(0, -6));
            gc.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }
}
