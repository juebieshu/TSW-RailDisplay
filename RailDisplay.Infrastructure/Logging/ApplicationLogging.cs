using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace RailDisplay.Infrastructure.Logging;

public static class ApplicationLogging
{
#if DEBUG
    public const LogLevel DefaultMinimumLevel = LogLevel.Debug;
#else
    public const LogLevel DefaultMinimumLevel = LogLevel.Information;
#endif

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RailDisplay", "Logs");

    public static LogLevel ParseMinimumLevel(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" => DefaultMinimumLevel,
        "ERROR" => LogLevel.Error,
        "ALL" or "TRACE" => LogLevel.Trace,
        "DEBUG" => LogLevel.Debug,
        "INFO" or "INFORMATION" => LogLevel.Information,
        "WARN" or "WARNING" => LogLevel.Warning,
        "FATAL" or "CRITICAL" => LogLevel.Critical,
        "OFF" or "NONE" => LogLevel.None,
        _ => throw new ArgumentException("日志级别必须为 ALL、TRACE、DEBUG、INFO、WARN、ERROR、FATAL 或 OFF。", nameof(value))
    };

    public static ILoggerFactory CreateFactory(string? directory = null, LogLevel minimumLevel = DefaultMinimumLevel)
    {
        if (!Enum.IsDefined(minimumLevel))
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        directory ??= LogDirectory;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (minimumLevel == LogLevel.None)
            return NullLoggerFactory.Instance;
        Directory.CreateDirectory(directory);
        const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Log4Level:l}] {SourceContext}: " +
                                "{Message:l}{NewLine}{Exception}";
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(LevelConvert.ToSerilogLevel(minimumLevel))
            .Enrich.FromLogContext()
            .Enrich.With(new Log4LevelEnricher())
            .WriteTo.File(Path.Combine(directory, "raildisplay-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true, retainedFileCountLimit: 14,
                shared: true, outputTemplate: template);
#if DEBUG
        configuration.WriteTo.Console(outputTemplate: template);
#endif
        return new SerilogLoggerFactory(configuration.CreateLogger(), dispose: true);
    }

    private sealed class Log4LevelEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            string level = logEvent.Level switch
            {
                LogEventLevel.Verbose => "TRACE",
                LogEventLevel.Debug => "DEBUG",
                LogEventLevel.Information => "INFO",
                LogEventLevel.Warning => "WARN",
                LogEventLevel.Error => "ERROR",
                LogEventLevel.Fatal => "FATAL",
                _ => throw new ArgumentOutOfRangeException(nameof(logEvent))
            };
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Log4Level", level));
        }
    }
}
