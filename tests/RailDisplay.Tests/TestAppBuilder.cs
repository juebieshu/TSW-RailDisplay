using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(RailDisplay.Tests.TestAppBuilder))]

namespace RailDisplay.Tests;

/// <summary>无窗口的 Avalonia 测试环境：使用 Skia 真实绘制，便于检查像素。</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}
