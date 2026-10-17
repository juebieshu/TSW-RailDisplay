using Avalonia.Logging;
using Microsoft.Extensions.Logging;

namespace RailDisplay.Logging;

public sealed class AvaloniaLogSink(ILogger logger) : ILogSink
{
    // 框架布局等诊断会随刷新刷屏；HotReload 是应用主动发出的完成通知。
    public bool IsEnabled(LogEventLevel level, string area)
        => (level >= LogEventLevel.Warning || area == "HotReload") && logger.IsEnabled(ToLogLevel(level));

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        => Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        object?[] values = [area, source?.GetType().FullName, .. propertyValues];
        logger.Log(ToLogLevel(level), "[{Area}] {Source}: " + messageTemplate, values);
    }

    private static LogLevel ToLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        LogEventLevel.Fatal => LogLevel.Critical,
        _ => LogLevel.None
    };
}
