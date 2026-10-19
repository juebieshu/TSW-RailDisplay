using System.Globalization;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Formatting.Json;
using Serilog.Parsing;

namespace RailDisplay.Infrastructure.Logging;

internal sealed class ApplicationLogFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _message = new("{Message:l}", CultureInfo.InvariantCulture);
    private readonly JsonValueFormatter _properties = new();

    public void Format(LogEvent logEvent, TextWriter output)
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
        string source = "Application";
        if (logEvent.Properties.TryGetValue("SourceContext", out var context) &&
            context is ScalarValue { Value: string { Length: > 0 } category })
        {
            if (category.StartsWith("RailDisplay.", StringComparison.Ordinal))
                category = category["RailDisplay.".Length..];
            if (category.StartsWith("Core.", StringComparison.Ordinal))
            {
                var parts = category.Split('.');
                int module = parts[1] is "Controllers" or "Models" ? 2 : 1;
                source = parts.Length > module ? "Core/" + parts[module] : "Core";
            }
            else
                source = category.StartsWith("Debug.", StringComparison.Ordinal)
                    ? "Debug" : category[(category.LastIndexOf('.') + 1)..];
        }

        output.Write(logEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        output.Write($" [{level}] ({source}) ");
        _message.Format(logEvent, output);
        var remaining = logEvent.Properties
            .Where(property => property.Key != "SourceContext" &&
                !logEvent.MessageTemplate.Tokens.OfType<PropertyToken>().Any(token => token.PropertyName == property.Key))
            .Select(property => new LogEventProperty(property.Key, property.Value)).ToArray();
        if (remaining.Length > 0)
        {
            output.Write(' ');
            _properties.Format(new StructureValue(remaining), output);
        }
        output.WriteLine();
        if (logEvent.Exception is { } error)
            output.WriteLine(error);
    }
}
