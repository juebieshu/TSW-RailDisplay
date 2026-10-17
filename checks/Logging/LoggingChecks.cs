using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using RailDisplay.Infrastructure.Logging;
using RailDisplay.Logging;

internal static class LoggingChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RailDisplay-logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckLevels(directory);
            CheckRolling(directory);
            string blocker = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocker, "keep");
            try
            {
                using var invalid = ApplicationLogging.CreateFactory(blocker);
                throw new Exception("日志目录为文件时必须明确失败。");
            }
            catch (IOException) { }
            if (File.ReadAllText(blocker) != "keep")
                throw new Exception("日志初始化失败不得覆盖现有文件。");

            using (var factory = ApplicationLogging.CreateFactory(directory, LogLevel.Debug))
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
                if (sink.IsEnabled(LogEventLevel.Debug, "Binding") ||
                    sink.IsEnabled(LogEventLevel.Information, "Layout") ||
                    !sink.IsEnabled(LogEventLevel.Warning, "Binding"))
                    throw new Exception("Avalonia 必须过滤低于 WARN 的框架诊断，并保留警告。");
                sink.Log(LogEventLevel.Warning, "Binding", new object(), "绑定失败 {Property}", "Speed");
                sink.Log(LogEventLevel.Verbose, "Binding", null, "framework-filter-check");
                sink.Log(LogEventLevel.Information, "Layout", null, "layout-spam-check");
                sink.Log(LogEventLevel.Debug, "Fonts", null, "font-list-spam-check");
                sink.Log(LogEventLevel.Debug, "Platform", null, "theme-spam-check");
                sink.Log(LogEventLevel.Information, "HotReload", null, "hot-reload-check");
                sink.Log(LogEventLevel.Fatal, "Platform", null, "fatal-level-check");

                // 默认逐条刷新，进程崩溃前已经写出的日志也必须可读。
                if (!File.ReadAllText(Directory.GetFiles(directory, "raildisplay-*.log").Single())
                    .Contains("日志中文检查 42"))
                    throw new Exception("日志必须在运行期间及时落盘。");
            }

            string text = File.ReadAllText(Directory.GetFiles(directory, "raildisplay-*.log").Single());
            if (text.Contains("SessionId") || !text.Contains("LoggingChecks") ||
                text.Contains("ProcessId") || !text.Contains("System.InvalidOperationException: exception-check") ||
                !text.Contains("LoggingChecks.Run") || !text.Contains("绑定失败 Speed") ||
                !text.Contains("hot-reload-check") || !text.Contains("[FATAL]") ||
                text.Contains("trace-filter-check") || text.Contains("framework-filter-check") ||
                text.Contains("layout-spam-check") || text.Contains("font-list-spam-check") || text.Contains("theme-spam-check"))
                throw new Exception("日志必须保留来源、中文、异常堆栈及框架消息，并过滤低级别输出和附加属性块。");
            if (!text.Contains("debug-level-check")) throw new Exception("显式 DEBUG 级别必须记录 Debug 日志。");
            foreach (var line in new[]
                     {
                         "[INFO] RailDisplay.App: 音频输出可用：False",
                         "[INFO] LoggingChecks: 保留括号 {}"
                     })
                if (!text.Contains(line + Environment.NewLine))
                    throw new Exception($"日志来源和空属性格式不正确：{line}");
            // 重开同一目录继续追加；释放后的文件不得残留句柄。
            using (var factory = ApplicationLogging.CreateFactory(directory, LogLevel.Information))
                factory.CreateLogger("LoggingChecks").LogInformation("reopen-check");
            text = File.ReadAllText(Directory.GetFiles(directory, "raildisplay-*.log").Single());
            if (!text.Contains("日志中文检查 42") || !text.Contains("reopen-check"))
                throw new Exception("重新初始化日志必须保留旧日志并继续追加。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine("日志落盘、过滤、标准格式、异常、框架接入及释放检查通过。");
    }

    private static void CheckLevels(string directory)
    {
#if DEBUG
        if (ApplicationLogging.DefaultMinimumLevel != LogLevel.Debug)
#else
        if (ApplicationLogging.DefaultMinimumLevel != LogLevel.Information)
#endif
            throw new Exception("默认级别必须为 Debug 构建 DEBUG、Release 构建 INFO。");
        if (ApplicationLogging.ParseMinimumLevel(null) != ApplicationLogging.DefaultMinimumLevel ||
            ApplicationLogging.ParseMinimumLevel(" ") != ApplicationLogging.DefaultMinimumLevel ||
            ApplicationLogging.ParseMinimumLevel(" info ") != LogLevel.Information ||
            ApplicationLogging.ParseMinimumLevel("warn") != LogLevel.Warning ||
            ApplicationLogging.ParseMinimumLevel("FATAL") != LogLevel.Critical ||
            ApplicationLogging.ParseMinimumLevel("ALL") != LogLevel.Trace ||
            ApplicationLogging.ParseMinimumLevel("OFF") != LogLevel.None)
            throw new Exception("默认级别和 Log4j 级别名称映射错误。");
        try
        {
            ApplicationLogging.ParseMinimumLevel("typo");
            throw new Exception("未知级别不得静默采用默认值。");
        }
        catch (ArgumentException) { }
        try
        {
            using var invalid = ApplicationLogging.CreateFactory(directory, (LogLevel)99);
            throw new Exception("非法级别枚举不得被接纳。");
        }
        catch (ArgumentOutOfRangeException) { }

        foreach (var minimum in Enum.GetValues<LogLevel>())
        {
            string levelDirectory = Path.Combine(directory, minimum.ToString());
            using (var factory = minimum == ApplicationLogging.DefaultMinimumLevel
                       ? ApplicationLogging.CreateFactory(levelDirectory)
                       : ApplicationLogging.CreateFactory(levelDirectory, minimum))
            {
                var logger = factory.CreateLogger("LevelChecks");
                var framework = new AvaloniaLogSink(factory.CreateLogger("Avalonia"));
                if (framework.IsEnabled(LogEventLevel.Information, "Layout") ||
                    framework.IsEnabled(LogEventLevel.Debug, "Binding") ||
                    framework.IsEnabled(LogEventLevel.Warning, "Binding") != (minimum <= LogLevel.Warning) ||
                    framework.IsEnabled(LogEventLevel.Information, "HotReload") != (minimum <= LogLevel.Information))
                    throw new Exception($"级别 {minimum} 的框架过滤或热重载通知错误。");
                if (minimum == LogLevel.None && logger.IsEnabled(LogLevel.Critical))
                    throw new Exception("OFF 必须禁用全部级别且不创建日志文件。");
                foreach (var level in Enum.GetValues<LogLevel>().Where(level => level != LogLevel.None))
                    logger.Log(level, "level-{LevelNumber}", (int)level);
            }
            string text = Directory.Exists(levelDirectory)
                ? string.Concat(Directory.GetFiles(levelDirectory, "*.log").Select(File.ReadAllText)) : "";
            foreach (var level in Enum.GetValues<LogLevel>().Where(level => level != LogLevel.None))
                if (text.Contains("level-" + (int)level) != (minimum != LogLevel.None && level >= minimum))
                    throw new Exception($"级别 {minimum} 未正确过滤 {level}。");
        }
    }

    private static void CheckRolling(string directory)
    {
        string rolling = Path.Combine(directory, "rolling");
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
        if (files.Length != 14 || !File.Exists(next) || !File.ReadAllText(next).Contains("rolling-check"))
            throw new Exception("达到 5 MiB 后必须轮转，且只保留最近 14 份日志。");
    }
}
