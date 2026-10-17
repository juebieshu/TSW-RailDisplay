using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Threading;
using RailDisplay.Controls;
using RailDisplay.Core.Models.CTCS;
using RailDisplay.Controls.CTCS;
using RailDisplay.Core.Controllers.CTCS;
using RailDisplay.Models.CTCS;
using RailDisplay.ViewModels;
using RailDisplay.ViewModels.CTCS;
using RailDisplay.Views;
using SkiaSharp;

internal static class PixelDisplayChecks
{
    public static void Run()
    {
        AppBuilder.Configure<RailDisplay.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
#if DEBUG
        DebugUiChecks.Run();
#endif
        CheckBrakeWarningView();
        CheckBrakeIcons();
        CheckTargetDistanceView();
        CheckClockView();
        CheckDistanceScale();
        CheckCtcsView();
        var block = new Rectangle { Width = 20, Height = 20, Fill = Brushes.White };
        var content = new Canvas();
        content.Children.Add(block);
        var display = new PixelDisplay { Content = content };
        display.ApplyTemplate();
        display.Measure(new Size(1280, 960));
        display.Arrange(new Rect(0, 0, 1280, 960));
        var source = content.GetVisualAncestors().OfType<Grid>().First();
        var refresh = typeof(PixelDisplay).GetMethod("RefreshFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstFrame = Capture();
        block.Fill = Brushes.Red;
        var secondFrame = Capture();
        if (firstFrame.SequenceEqual(secondFrame))
            throw new Exception("Changing content did not refresh the pixel frame.");

        display.UsePixelRendering = false;
        refresh.Invoke(display, null);
        var image = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(display).OfType<Image>().Single();
        if (image.Source is not null || image.IsVisible)
            throw new Exception("Native rendering must remove the pixel overlay.");
        if (TextOptions.GetTextRenderingMode(source) == TextRenderingMode.Alias)
            throw new Exception("Native rendering must restore text antialiasing.");

        display.UsePixelRendering = true;
        if (!Capture().SequenceEqual(secondFrame))
            throw new Exception("Switching back to pixel rendering changed the frame.");
        display.RenderResolution = new PixelSize(800, 600);
        Capture();
        try
        {
            display.RenderResolution = new PixelSize(0, 480);
            throw new Exception("An invalid resolution was accepted.");
        }
        catch (ArgumentException)
        {
            if (display.RenderResolution != new PixelSize(800, 600))
                throw new Exception("Invalid input changed the render resolution.");
        }
        display.UsePixelRendering = false;
        Console.WriteLine("Pixel resolution, refresh and native/pixel switching checks passed.");

        byte[] Capture()
        {
            refresh.Invoke(display, null);
            var frameImage = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(display).OfType<Image>().Single();
            if (frameImage.Source is not RenderTargetBitmap frame || frame.PixelSize != display.RenderResolution)
                throw new Exception("Enlarging the display must preserve the configured source resolution.");
            if (RenderOptions.GetBitmapInterpolationMode(frameImage) != BitmapInterpolationMode.None)
                throw new Exception("Pixel enlargement must disable smooth interpolation.");
            using var stream = new MemoryStream();
            frame.Save(stream);
            return stream.ToArray();
        }
    }

    private static void CheckTargetDistanceView()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var view = new CTCS2_200H { DataContext = model };
        ((PixelDisplay)view.Content!).UsePixelRendering = false;
        var window = new Window { Content = view, Width = 640, Height = 480 };
        try
        {
            window.Show();
            var ruler = view.FindControl<TargetDistanceScale>("TargetDistanceRuler")!;
            var value = view.FindControl<TextBlock>("TargetDistanceValue")!;
            if (ruler.IsVisible || value.Text != "")
                throw new Exception("A missing target distance must leave A2 blank.");
            foreach (var size in new[] { new Size(320, 240), new Size(640, 480), new Size(1280, 960) })
            {
                foreach (var (distance, height) in new[]
                {
                    (1120.0, 172.0), (1000.0, 172.0), (670.0, 172.0 * 823 / 900),
                    (500.0, 172.0 * 47 / 54),
                    (200.0, 172.0 * 107 / 135), (100.0, 172.0 * 23 / 30), (50.0, 172.0 * 23 / 60),
                    (0.0, 0.0), (100000.0, 172.0)
                })
                {
                    model.Update(new CtcsData
                    {
                        TargetDistance = distance, TargetSpeed = 0, TsmEntryDistance = 100000,
                        Speed = 220, PermittedSpeed = 200, ControlMode = CtcsControlMode.FullSupervision
                    });
                    Dispatcher.UIThread.RunJobs();
                    view.Measure(size);
                    view.Arrange(new Rect(size));
                    if (!ruler.IsVisible || ruler.Bounds.Size != new Size(53, 172) || ruler.TargetDistance != distance ||
                        Math.Abs(ruler.BandHeight - height) > 1e-9 ||
                        ruler.Foreground is not SolidColorBrush { Color: var color } || color != Colors.White ||
                        value.FontFamily.Name != "Arial" || value.FontSize != 16 || value.Text != model.TargetDistanceText ||
                        value.TextAlignment != TextAlignment.Right ||
                        value.DesiredSize.Width > 53 || value.Bounds.Bottom > ruler.Bounds.Top)
                        throw new Exception("A2 must retain its white 15x172 band and bound Arial 16px distance.");
                    using var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                    frame.Render(view);
                    using var stream = new MemoryStream();
                    frame.Save(stream, PngBitmapEncoderOptions.Default);
                    using var pixels = SKBitmap.Decode(stream.ToArray());
                    double scale = size.Width / 640;
                    int left = (int)(38 * scale), top = (int)(86 * scale);
                    int right = (int)(53 * scale), bottom = (int)(258 * scale);
                    int bandTop = (int)Math.Ceiling((258 - height) * scale + 1);
                    for (int y = bandTop; y < bottom - 1; y++)
                    {
                        for (int x = left + 1; x < right - 1; x++)
                            if (pixels.GetPixel(x, y) != SKColors.White)
                                throw new Exception("The target distance band must stay solid white below its distance height.");
                    }
                    for (int y = top + 1; y < (258 - height) * scale - 1; y++)
                        if (pixels.GetPixel((int)(45 * scale), y) != SKColors.Black)
                            throw new Exception("The distance band must shorten from the top while its bottom remains fixed.");
                    if (size == new Size(640, 480) &&
                        (pixels.GetPixel(37, 150) != SKColors.Black || pixels.GetPixel(53, 150) == SKColors.White))
                        throw new Exception("The 15px band must retain its gap from the ticks and the A2 border.");
                    if (size == new Size(640, 480))
                    {
                        int[] tickRows = { 86, 95, 104, 113, 122, 126, 133, 139, 153, 166, 256 };
                        int[] majorRows = { 86, 256 };
                        for (int y = 86; y < 258; y++)
                        {
                            bool major = majorRows.Any(row => y >= row && y < row + 2);
                            bool tick = major || tickRows.Contains(y);
                            if (pixels.GetPixel(20, y) != (tick ? SKColors.White : SKColors.Black) ||
                                pixels.GetPixel(10, y) != (major ? SKColors.White : SKColors.Black))
                                throw new Exception("A2 must show 0/100/1000m endpoints and 70/80/90/95/200/400/600/800m ticks.");
                        }
                        if (distance == 1120)
                            frame.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RailDisplay-A2.png"), PngBitmapEncoderOptions.Default);
                    }
                }
            }
            model.Update(model.Data with { TargetDistance = 100001 });
            if (ruler.IsVisible || value.Text != "")
                throw new Exception("Leaving TSM must hide both the distance band and its number.");
            model.Update(model.Data with { TargetDistance = 2000, ControlMode = CtcsControlMode.PartialSupervision });
            if (ruler.IsVisible || value.Text != "")
                throw new Exception("Leaving full supervision must hide the entire target distance display.");
            model.Update(new CtcsData());
            if (ruler.IsVisible || value.Text != "")
                throw new Exception("Removing a target distance must clear the previously displayed target.");
        }
        finally
        {
            window.Close();
        }
        Console.WriteLine("A2 distance bindings, white band, fonts and scaled rendering checks passed.");
    }

    private static void CheckClockView()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var view = new CTCS2_200H { DataContext = model };
        ((PixelDisplay)view.Content!).UsePixelRendering = false;
        var window = new Window { Content = view, Width = 640, Height = 480 };
        try
        {
            window.Show();
            model.UpdateDisplayTime(new DateTimeOffset(2026, 10, 15, 23, 7, 49, TimeSpan.FromHours(8)));
            Dispatcher.UIThread.RunJobs();
            foreach (var size in new[] { new Size(320, 240), new Size(640, 480), new Size(1280, 960) })
            {
                view.Measure(size);
                view.Arrange(new Rect(size));
                var area = view.FindControl<Border>("E17")!;
                var labels = area.GetVisualDescendants().OfType<TextBlock>().ToArray();
                if (labels.Length != 3 || labels[0].Text != "日期时间" || labels[1].Text != "23:07:49" ||
                    labels[2].Text != "26-10-15" || labels.Any(label => label.FontSize != 12) ||
                    labels[0].FontFamily.Name != "SimSun" || labels[1].FontFamily.Name != "Arial" ||
                    labels[2].FontFamily.Name != "Arial")
                    throw new Exception("E17 must display three lines in SimSun/Arial at 12px.");
                foreach (var label in labels)
                {
                    if (label.DesiredSize.Width > area.Bounds.Width || label.Bounds.Bottom > area.Bounds.Height)
                        throw new Exception("An E17 clock line does not fit its region.");
                }
                using var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                frame.Render(view);
                if (size == new Size(640, 480))
                    frame.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RailDisplay-E17.png"));
            }
        }
        finally
        {
            window.Close();
        }
        Console.WriteLine("E17 clock bindings, fonts, layout and scaled rendering checks passed.");
    }

    private static void CheckDistanceScale()
    {
        var ruler = new DistanceScale
        {
            Width = 243, Height = 245,
            GridBrush = new SolidColorBrush(Color.Parse("#555555"))
        };
        var area = new Canvas { Width = 244, Height = 300 };
        Canvas.SetTop(ruler, 55);
        area.Children.Add(ruler);
        area.Measure(new Size(244, 300));
        area.Arrange(new Rect(0, 0, 244, 300));
        foreach (int maximum in new[] { 8000, 16000, 8000 })
        {
            ruler.MaximumDistance = maximum;
            using var frame = new RenderTargetBitmap(new PixelSize(244, 300), new Vector(96, 96));
            frame.Render(area);
            using var stream = new MemoryStream();
            frame.Save(stream);
            using var pixels = SKBitmap.Decode(stream.ToArray());
            double[] positions = maximum == 8000
                ? new[] { 0.0, 1.0 / 3, 5.0 / 9, 7.0 / 9, 1.0 }
                : new[] { 0.0, 1.0 / 3, 0.5, 2.0 / 3, 5.0 / 6, 1.0 };
            for (int x = 0; x < 244; x++)
            {
                bool expected = positions.Any(position => Math.Abs(x - position * 241) <= 1);
                bool visible = pixels.GetPixel(x, 260).Alpha > 0;
                if (visible && !expected)
                    throw new Exception($"Unexpected distance tick at {x} for {maximum}.");
                if ((x >= 32 && pixels.GetPixel(x, 54).Alpha != 0) || pixels.GetPixel(x, 0).Alpha != 0)
                    throw new Exception("Distance grid entered D8 or D6.");
                if (pixels.GetPixel(x, 281).Alpha != 0)
                    throw new Exception("D1 must not contain white tick extensions.");
            }
            foreach (double position in positions)
            {
                int x = (int)Math.Round(position * 241);
                if (!Enumerable.Range(Math.Max(0, x - 1), Math.Min(243, x + 1) - Math.Max(0, x - 1) + 1)
                    .Any(column => pixels.GetPixel(column, 260).Alpha > 0))
                    throw new Exception($"Missing distance tick at {position} for {maximum}.");
            }
            foreach (int y in new[] { 55, 200, 260, 279 })
            {
                if (pixels.GetPixel(0, y) != new SKColor(85, 85, 85) ||
                    pixels.GetPixel(241, y) != new SKColor(85, 85, 85) ||
                    pixels.GetPixel(242, y).Alpha != 0 ||
                    pixels.GetPixel(243, y).Alpha != 0)
                    throw new Exception("Distance grid color, extent or 1px right inset is incorrect.");
            }
            int[] speedRows = { 90, 110, 129, 148, 167, 186, 205 };
            foreach (int y in speedRows)
            {
                if (pixels.GetPixel(40, y).Alpha == 0 || pixels.GetPixel(241, y).Alpha == 0 ||
                    pixels.GetPixel(242, y).Alpha == 0 || pixels.GetPixel(243, y).Alpha != 0)
                    throw new Exception("Horizontal speed lines must reach the inside of the right border.");
                bool hasLabel = Enumerable.Range(y - 18, 17).Any(row =>
                    Enumerable.Range(2, 30).Any(column =>
                    {
                        var pixel = pixels.GetPixel(column, row);
                        return pixel.Alpha > 0 && pixel.Red > 85;
                    }));
                if (!hasLabel)
                    throw new Exception("Missing speed number above the left end of a D7 line.");
                if (!Enumerable.Range(y - 2, 3).Any(row => Enumerable.Range(2, 30).Any(column =>
                    pixels.GetPixel(column, row).Red > 85)))
                    throw new Exception("Speed text must touch its horizontal line without a blank row.");
            }
            for (int y = 0; y < 280; y++)
            {
                if (pixels.GetPixel(40, y).Alpha > 0 && !speedRows.Any(row => Math.Abs(row - y) <= 1))
                    throw new Exception("A horizontal speed line escaped its D7 position.");
            }
        }
        try
        {
            ruler.MaximumDistance = 12000;
            throw new Exception("The distance control accepted an invalid range.");
        }
        catch (ArgumentException)
        {
            if (ruler.MaximumDistance != 8000)
                throw new Exception("Invalid range changed the rendered scale.");
        }
        Console.WriteLine("Distance scale endpoints, spacing and range round-trip checks passed.");
    }

    private static void CheckBrakeWarningView()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H);
        var view = new CTCS2_200H { DataContext = model };
        var display = (PixelDisplay)view.Content!;
        var window = new Window { Content = view, Width = 640, Height = 480 };
        try
        {
            window.Show();
            var brakeIcon = view.FindControl<Border>("C9")!.GetVisualDescendants().OfType<BrakeIndicator>().Single();
            foreach (var (speed, expected) in new[]
            {
                (165d, CtcsBrakeLevel.NoBrake), (166d, CtcsBrakeLevel.ServiceBrake),
                (175d, CtcsBrakeLevel.ServiceBrake), (176d, CtcsBrakeLevel.EmergencyBrake),
                (150d, CtcsBrakeLevel.EmergencyBrake)
            })
            {
                model.Update(new CtcsData
                {
                    Speed = speed, SbiSpeed = 165, EbiSpeed = 175,
                    ControlMode = CtcsControlMode.FullSupervision, BrakeLevel = CtcsBrakeLevel.NoBrake
                });
                Dispatcher.UIThread.RunJobs();
                if (brakeIcon.BrakeLevel != expected)
                    throw new Exception("C9 must bind the calculated, latched Core brake state.");
            }
            Console.WriteLine("C9 calculated brake binding, thresholds, EB priority and latching checks passed.");
            foreach (bool pixelMode in new[] { false, true })
            foreach (int scale in new[] { 1, 2 })
            foreach (var (time, speed, section, size, color) in new[]
            {
                (10d, 150d, SpeedMonitoringSection.CSM, 0, SKColors.Black),
                (8d, 150d, SpeedMonitoringSection.CSM, 5, new SKColor(195, 195, 195)),
                (10d, 150d, SpeedMonitoringSection.TSM, 5, new SKColor(223, 223, 0)),
                (6d, 150d, SpeedMonitoringSection.TSM, 27, new SKColor(223, 223, 0)),
                (4d, 168d, SpeedMonitoringSection.TSM, 41, new SKColor(234, 145, 0)),
                (2d, 175d, SpeedMonitoringSection.TSM, 54, new SKColor(191, 0, 2))
            })
            {
                display.UsePixelRendering = pixelMode;
                model.Update(new CtcsData
                {
                    Speed = speed, PermittedSpeed = 160, SbiSpeed = 165, EbiSpeed = 175,
                    BrakeWarningTime = time, MonitoringSection = section, ControlMode = CtcsControlMode.FullSupervision
                });
                Dispatcher.UIThread.RunJobs();
                view.Measure(new Size(640 * scale, 480 * scale));
                view.Arrange(new Rect(0, 0, 640 * scale, 480 * scale));
                display.ForceRedraw();
                var icon = view.FindControl<Border>("BrakeWarningIcon")!;
                if (icon.Bounds.Size != new Size(size, size))
                    throw new Exception("A1 bound square dimensions changed with window scaling.");
                using var frame = new RenderTargetBitmap(new PixelSize(640 * scale, 480 * scale), new Vector(96, 96));
                frame.Render(view);
                using var stream = new MemoryStream();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                using var pixels = SKBitmap.Decode(stream.ToArray());
                int offset = (54 - size) / 2;
                for (int y = 0; y < 53 * scale; y++)
                for (int x = 0; x < 53 * scale; x++)
                {
                    bool filled = size > 0 && x >= offset * scale && x < (offset + size) * scale &&
                                  y >= offset * scale && y < (offset + size) * scale;
                    if (pixels.GetPixel(x, y) != (filled ? color : SKColors.Black))
                        throw new Exception($"A1 size/color/position mismatch at {x},{y}, size {size}, scale {scale}, pixels {pixelMode}.");
                }
                if (pixelMode && scale == 1 && size == 41)
                    frame.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RailDisplay-A1.png"), PngBitmapEncoderOptions.Default);
            }
            model.Update(new CtcsData());
            model.Update(new CtcsData
            {
                ControlMode = CtcsControlMode.FullSupervision, MonitoringSection = SpeedMonitoringSection.TSM,
                Speed = 0, PermittedSpeed = 30, SbiSpeed = 32.4, EbiSpeed = 36
            });
            foreach (double speed in new[] { 3.6, 7.2, 10.8, 14.4, 18.0 })
            {
                model.Update(model.Data with { Speed = speed });
                model.AdvanceBrakeWarning(TimeSpan.FromSeconds(1));
            }
            Dispatcher.UIThread.RunJobs();
            var predictedIcon = view.FindControl<Border>("BrakeWarningIcon")!;
            if (Math.Abs(model.Data.BrakeWarningTime!.Value - 5) > 1e-9 || predictedIcon.Width != 27 ||
                predictedIcon.Height != 27 || !predictedIcon.Classes.Contains("yellow"))
                throw new Exception("Timed FS prediction must update the actual A1 bindings to a yellow 27px square.");
        }
        finally { window.Close(); }
        Console.WriteLine("A1 page bindings, four square sizes, colors and native/pixel scaling checks passed.");
    }

    private static void CheckBrakeIcons()
    {
        foreach (var level in Enum.GetValues<CtcsBrakeLevel>())
        {
            var icon = new BrakeIndicator { BrakeLevel = level, Width = 53, Height = 26 };
            icon.Measure(new Size(53, 26));
            icon.Arrange(new Rect(0, 0, 53, 26));
            using var frame = new RenderTargetBitmap(new PixelSize(53, 26), new Vector(96, 96));
            frame.Render(icon);
            using var stream = new MemoryStream();
            frame.Save(stream);
            using var pixels = SKBitmap.Decode(stream.ToArray());
            if (level is CtcsBrakeLevel.NoBrake or CtcsBrakeLevel.ReleaseAllowed)
            {
                if (pixels.Pixels.Any(pixel => pixel.Alpha != 0))
                    throw new Exception($"The brake icon must be empty for {level}.");
            }
            bool outer = level is CtcsBrakeLevel.MediumServiceBrake or CtcsBrakeLevel.ServiceBrake or CtcsBrakeLevel.EmergencyBrake;
            bool middle = level is CtcsBrakeLevel.WeakServiceBrake or CtcsBrakeLevel.ServiceBrake or CtcsBrakeLevel.EmergencyBrake;
            if ((pixels.GetPixel(3, 3) == SKColors.Red) != outer ||
                (pixels.GetPixel(3, 22) == SKColors.Red) != outer ||
                (pixels.GetPixel(3, 13) == SKColors.Red) != middle ||
                (pixels.GetPixel(3, 8) == SKColors.Red) != (level == CtcsBrakeLevel.EmergencyBrake))
                throw new Exception($"Incorrect brake stripes for {level}.");
            if (pixels.GetPixel(1, 13).Alpha != 0 || pixels.GetPixel(3, 1).Alpha != 0)
                throw new Exception("The brake icon must retain its 2px margin.");
        }
    }

    private static void CheckCtcsView()
    {
        var model = new CtcsViewModel(new CtcsController(), CtcsScreenProfile.CTCS2_200H with { SupportsLevel3 = true });
        var audio = new RailDisplay.Infrastructure.Controllers.Audio.CTCS.Ctcs2_200HAudioController();
        var window = new MainWindow { DataContext = new MainViewModel(model, audio) };
        try
        {
            var view = (CTCS2_200H)window.Content!;
            if (!ReferenceEquals(view.DataContext, model))
                throw new Exception("The main window must pass the CTCS ViewModel to its screen.");
            ((PixelDisplay)view.Content!).UsePixelRendering = false;
            model.Update(new CtcsData { CurrentLevel = CtcsLevel.Level3, ControlMode = CtcsControlMode.Guiding });
            view.Measure(new Size(640, 480));
            view.Arrange(new Rect(0, 0, 640, 480));
            var labels = view.GetVisualDescendants().OfType<TextBlock>().ToArray();
            if (!labels.Any(label => label.Text == "CTCS 3") || !labels.Any(label => label.Text == "引导"))
                throw new Exception("The moved mode and level bindings must still update the actual screen.");

            model.Update(model.Data with
            {
                Speed = 205, PermittedSpeed = 200, SbiSpeed = 210, EbiSpeed = 220,
                ControlMode = CtcsControlMode.FullSupervision
            });
            var speed = view.FindControl<SpeedScale>("SpeedDial")!;
            var distance = view.FindControl<DistanceScale>("DistanceRuler")!;
            var planBackground = view.FindControl<Border>("D7")!;
            var csgSnapshot = model.Data;
            foreach (var mode in Enum.GetValues<CtcsControlMode>())
            {
                model.Update(csgSnapshot with { ControlMode = mode });
                if (speed.ShowCsg != (mode == CtcsControlMode.FullSupervision))
                    throw new Exception("The screen must bind CSG visibility to full supervision only.");
                bool showPlan = mode is CtcsControlMode.FullSupervision or CtcsControlMode.PartialSupervision;
                if (distance.IsVisible != showPlan || planBackground.IsVisible != showPlan)
                    throw new Exception("The D-zone plan scale and background must only be visible in full or partial supervision.");
                if (mode is CtcsControlMode.FullSupervision or CtcsControlMode.Guiding)
                {
                    using var frame = new RenderTargetBitmap(new PixelSize(640, 480), new Vector(96, 96));
                    frame.Render(view);
                    using var stream = new MemoryStream();
                    frame.Save(stream);
                    using var pixels = SKBitmap.Decode(stream.ToArray());
                    var expected = showPlan ? new SKColor(10, 24, 70) : SKColors.Black;
                    if (pixels.GetPixel(450, 100) != expected)
                        throw new Exception("The D-zone background must render only with its supervision plan.");
                }
            }
            model.Update(csgSnapshot);
            if (!speed.ShowCsg)
                throw new Exception("The screen must restore CSG when returning to full supervision.");
            if (Canvas.GetTop(distance) != 55 || distance.Width != 243 || distance.Height != 245 ||
                distance.GridBrush is not SolidColorBrush { Color: var gridColor } || gridColor != Color.Parse("#555555"))
                throw new Exception("The screen must keep the distance grid inside D7–D2 with a 1px right inset.");
            model.MaximumDisplayDistance = 16000;
            if (distance.MaximumDistance != 16000)
                throw new Exception("The distance range binding did not update.");
            if (speed.Speed != 205 || !speed.ShowLightBand || speed.WarningBandEndSpeed != 210 ||
                !labels.Any(label => label.Text == "完全"))
                throw new Exception("The migrated CTCS bindings must respond to subsequent core updates.");
            foreach (var size in new[] { new Size(320, 240), new Size(640, 480), new Size(1280, 960) })
            {
                foreach (int maximum in new[] { 8000, 16000 })
                {
                    model.MaximumDisplayDistance = maximum;
                    view.Measure(size);
                    view.Arrange(new Rect(size));
                    using var frame = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                    frame.Render(view);
                    if (size == new Size(640, 480))
                    {
                        using var stream = new MemoryStream();
                        frame.Save(stream);
                        using var pixels = SKBitmap.Decode(stream.ToArray());
                        if (pixels.GetPixel(575, 100) != new SKColor(85, 85, 85) ||
                            pixels.GetPixel(576, 100) != new SKColor(10, 24, 70) ||
                            pixels.GetPixel(577, 100) != new SKColor(40, 40, 40))
                            throw new Exception("The last distance line must leave one blank pixel before the D border.");
                    }
                }
            }
        }
        finally
        {
            window.Close();
            audio.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        Console.WriteLine("CTCS view composition, bindings and scaled rendering checks passed.");
    }
}
