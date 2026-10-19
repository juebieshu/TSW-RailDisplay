using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using RailDisplay.Infrastructure.Logging;
using RailDisplay.Logging;

namespace RailDisplay.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "RailDisplay-logging-" + Guid.NewGuid().ToString("N"));

    public LoggingTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void CreateFactory_FailsWithoutOverwritingWhenDirectoryIsAFile()
    {
        string blocker = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blocker, "keep");
        Assert.ThrowsAny<IOException>(() => ApplicationLogging.CreateFactory(blocker).Dispose());
        Assert.Equal("keep", File.ReadAllText(blocker));
    }

    [Fact]
    public void Logger_WritesFilteredFormattedEntriesAndAppendsOnReopen()
    {
        using (var factory = ApplicationLogging.CreateFactory(_directory, LogLevel.Debug))
        {
            var logger = factory.CreateLogger("LoggingChecks");
            using (logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = "session-check" }))
                logger.LogInformation("日志中文检查 {Batch}", 42);
            logger.LogTrace("trace-filter-check");
            logger.LogDebug("debug-level-check");
            factory.CreateLogger("RailDisplay.App").LogInformation("音频输出可用：{AudioSupported}", false);
            logger.LogInformation("保留括号 {{}}");
            try { throw new InvalidOperationException("exception-check"); }
            catch (InvalidOperationException error) { logger.LogError(error, "故障检查"); }

            var sink = new AvaloniaLogSink(factory.CreateLogger("Avalonia"));
            Assert.False(sink.IsEnabled(LogEventLevel.Debug, "Binding") ||
                sink.IsEnabled(LogEventLevel.Information, "Layout") ||
                !sink.IsEnabled(LogEventLevel.Warning, "Binding"),
                "Avalonia 必须过滤低于 WARN 的框架诊断，并保留警告。");
            sink.Log(LogEventLevel.Warning, "Binding", new object(), "绑定失败 {Property}", "Speed");
            sink.Log(LogEventLevel.Verbose, "Binding", null, "framework-filter-check");
            sink.Log(LogEventLevel.Information, "Layout", null, "layout-spam-check");
            sink.Log(LogEventLevel.Debug, "Fonts", null, "font-list-spam-check");
            sink.Log(LogEventLevel.Debug, "Platform", null, "theme-spam-check");
            sink.Log(LogEventLevel.Information, "HotReload", null, "hot-reload-check");
            sink.Log(LogEventLevel.Fatal, "Platform", null, "fatal-level-check");

            // 默认逐条刷新，进程崩溃前已经写出的日志也必须可读。
            Assert.True(ReadLog().Contains("日志中文检查 42"), "日志必须在运行期间及时落盘。");
        }

        string text = ReadLog();
        Assert.False(text.Contains("SessionId") || !text.Contains("LoggingChecks") ||
            text.Contains("ProcessId") || !text.Contains("System.InvalidOperationException: exception-check") ||
            !text.Contains(nameof(Logger_WritesFilteredFormattedEntriesAndAppendsOnReopen)) || !text.Contains("绑定失败 Speed") ||
            !text.Contains("hot-reload-check") || !text.Contains("[FATAL]") ||
            text.Contains("trace-filter-check") || text.Contains("framework-filter-check") ||
            text.Contains("layout-spam-check") || text.Contains("font-list-spam-check") || text.Contains("theme-spam-check"),
            "日志必须保留来源、中文、异常堆栈及框架消息，并过滤低级别输出和附加属性块。");
        Assert.True(text.Contains("debug-level-check"), "显式 DEBUG 级别必须记录 Debug 日志。");
        foreach (var line in new[] { "[INFO] RailDisplay.App: 音频输出可用：False", "[INFO] LoggingChecks: 保留括号 {}" })
            Assert.True(text.Contains(line + Environment.NewLine), $"日志来源和空属性格式不正确：{line}");

        // 重开同一目录继续追加；释放后的文件不得残留句柄。
        using (var factory = ApplicationLogging.CreateFactory(_directory, LogLevel.Information))
            factory.CreateLogger("LoggingChecks").LogInformation("reopen-check");
        text = ReadLog();
        Assert.True(text.Contains("日志中文检查 42") && text.Contains("reopen-check"), "重新初始化日志必须保留旧日志并继续追加。");
    }

    [Fact]
    public void MinimumLevel_UsesBuildDefaultAndLog4jNames()
    {
#if DEBUG
        Assert.Equal(LogLevel.Debug, ApplicationLogging.DefaultMinimumLevel);
#else
        Assert.Equal(LogLevel.Information, ApplicationLogging.DefaultMinimumLevel);
#endif
        Assert.Equal(ApplicationLogging.DefaultMinimumLevel, ApplicationLogging.ParseMinimumLevel(null));
        Assert.Equal(ApplicationLogging.DefaultMinimumLevel, ApplicationLogging.ParseMinimumLevel(" "));
        Assert.Equal(LogLevel.Information, ApplicationLogging.ParseMinimumLevel(" info "));
        Assert.Equal(LogLevel.Warning, ApplicationLogging.ParseMinimumLevel("warn"));
        Assert.Equal(LogLevel.Critical, ApplicationLogging.ParseMinimumLevel("FATAL"));
        Assert.Equal(LogLevel.Trace, ApplicationLogging.ParseMinimumLevel("ALL"));
        Assert.Equal(LogLevel.None, ApplicationLogging.ParseMinimumLevel("OFF"));
        Assert.ThrowsAny<ArgumentException>(() => ApplicationLogging.ParseMinimumLevel("typo"));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => ApplicationLogging.CreateFactory(_directory, (LogLevel)99).Dispose());
    }

    [Theory]
    [MemberData(nameof(LogLevels))]
    public void MinimumLevel_FiltersApplicationAndFrameworkEntries(LogLevel minimum)
    {
        string levelDirectory = Path.Combine(_directory, minimum.ToString());
        using (var factory = minimum == ApplicationLogging.DefaultMinimumLevel
                   ? ApplicationLogging.CreateFactory(levelDirectory)
                   : ApplicationLogging.CreateFactory(levelDirectory, minimum))
        {
            var logger = factory.CreateLogger("LevelChecks");
            var framework = new AvaloniaLogSink(factory.CreateLogger("Avalonia"));
            Assert.False(framework.IsEnabled(LogEventLevel.Information, "Layout") ||
                framework.IsEnabled(LogEventLevel.Debug, "Binding") ||
                framework.IsEnabled(LogEventLevel.Warning, "Binding") != (minimum <= LogLevel.Warning) ||
                framework.IsEnabled(LogEventLevel.Information, "HotReload") != (minimum <= LogLevel.Information),
                $"级别 {minimum} 的框架过滤或热重载通知错误。");
            Assert.False(minimum == LogLevel.None && logger.IsEnabled(LogLevel.Critical), "OFF 必须禁用全部级别且不创建日志文件。");
            foreach (var level in Enum.GetValues<LogLevel>().Where(level => level != LogLevel.None))
                logger.Log(level, "level-{LevelNumber}", (int)level);
        }
        string text = Directory.Exists(levelDirectory)
            ? string.Concat(Directory.GetFiles(levelDirectory, "*.log").Select(File.ReadAllText)) : "";
        foreach (var level in Enum.GetValues<LogLevel>().Where(level => level != LogLevel.None))
            Assert.True(text.Contains("level-" + (int)level) == (minimum != LogLevel.None && level >= minimum),
                $"级别 {minimum} 未正确过滤 {level}。");
    }

    public static TheoryData<LogLevel> LogLevels() => new(Enum.GetValues<LogLevel>());

    [Fact]
    public void Files_RollAtFiveMebibytesAndKeepFourteen()
    {
        string rolling = Path.Combine(_directory, "rolling");
        Directory.CreateDirectory(rolling);
        for (int i = 1; i <= 16; i++)
            File.WriteAllText(Path.Combine(rolling,
                "raildisplay-" + DateTime.Today.AddDays(-i).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"), "old");
        string today = Path.Combine(rolling,
            "raildisplay-" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        using (var file = File.Create(today)) file.SetLength(5 * 1024 * 1024);
        using (var factory = ApplicationLogging.CreateFactory(rolling))
            factory.CreateLogger("RollingChecks").LogError("rolling-check");
        var files = Directory.GetFiles(rolling, "raildisplay-*.log");
        string next = Path.ChangeExtension(today, null) + "_001.log";
        Assert.False(files.Length != 14 || !File.Exists(next) || !File.ReadAllText(next).Contains("rolling-check"),
            "达到 5 MiB 后必须轮转，且只保留最近 14 份日志。");
    }

    private string ReadLog() => File.ReadAllText(Directory.GetFiles(_directory, "raildisplay-*.log").Single());
}
