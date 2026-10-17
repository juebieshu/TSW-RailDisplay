using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RailDisplay.Controls;

public sealed class PixelDisplay : ContentControl
{
    public static readonly StyledProperty<bool> UsePixelRenderingProperty =
        AvaloniaProperty.Register<PixelDisplay, bool>(nameof(UsePixelRendering), true);

    public bool UsePixelRendering
    {
        get => GetValue(UsePixelRenderingProperty);
        set => SetValue(UsePixelRenderingProperty, value);
    }

    public static readonly StyledProperty<PixelSize> RenderResolutionProperty =
        AvaloniaProperty.Register<PixelDisplay, PixelSize>(
            nameof(RenderResolution), new PixelSize(640, 480),
            validate: size => size.Width > 0 && size.Height > 0);

    public PixelSize RenderResolution
    {
        get => GetValue(RenderResolutionProperty);
        set => SetValue(RenderResolutionProperty, value);
    }

    private readonly DispatcherTimer _frameTimer;
    private Grid? _source;
    private Image? _image;
    private RenderTargetBitmap? _frame;
    private bool _attached;

    public PixelDisplay()
    {
        _frameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / 30)
        };
        _frameTimer.Tick += (_, _) => RefreshFrame();

        Template = new FuncControlTemplate<PixelDisplay>((_, scope) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            scope.Register(presenter.Name, presenter);
            presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentProperty));

            _image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
            _source = new Grid { Width = 640, Height = 480, Background = Brushes.Black, ClipToBounds = true };
            _source.Children.Add(presenter);
            var surface = new Grid { Width = 640, Height = 480, ClipToBounds = true };
            surface.Children.Add(_source);
            surface.Children.Add(_image);
            return new Viewbox { Stretch = Stretch.Uniform, Child = surface };
        });
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateRenderingMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UsePixelRenderingProperty || change.Property == RenderResolutionProperty)
            UpdateRenderingMode();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateRenderingMode();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameTimer.Stop();
        ClearFrame();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateRenderingMode()
    {
        _frameTimer.Stop();
        if (_source is null || _image is null)
            return;

        RenderOptions.SetEdgeMode(_source, UsePixelRendering ? EdgeMode.Aliased : EdgeMode.Antialias);
        TextOptions.SetTextRenderingMode(_source, UsePixelRendering ? TextRenderingMode.Alias : TextRenderingMode.Antialias);
        _image.IsVisible = UsePixelRendering;
        ClearFrame();

        if (_attached && UsePixelRendering && !Design.IsDesignMode)
            _frameTimer.Start();
    }

    private void ClearFrame()
    {
        if (_image is not null)
            _image.Source = null;
        _frame?.Dispose();
        _frame = null;
    }

    private void RefreshFrame()
    {
        if (!UsePixelRendering || !IsVisible || _source is null || _image is null ||
            _source.Bounds.Size != new Size(640, 480))
            return;

        // 按输出分辨率调整 DPI，将完整的 640×480 布局绘制到目标像素尺寸。
        var resolution = RenderResolution;
        var dpi = new Vector(96.0 * resolution.Width / 640, 96.0 * resolution.Height / 480);
        var nextFrame = new RenderTargetBitmap(resolution, dpi);
        try
        {
            nextFrame.Render(_source);
        }
        catch
        {
            nextFrame.Dispose();
            throw;
        }

        var previousFrame = _frame;
        _frame = nextFrame;
        _image.Source = nextFrame;
        previousFrame?.Dispose();
    }

    public void ForceRedraw()
    {
        Dispatcher.UIThread.VerifyAccess();
        ClearFrame();
        RefreshFrame();
        InvalidateVisual();
    }
}
