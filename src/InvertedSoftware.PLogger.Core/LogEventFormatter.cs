// Copyright (c) Inverted Software. All rights reserved.

using System.Globalization;
using System.Text;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// Renders a <see cref="LogEvent"/> back to the legacy single-string format
/// the v1 sinks consumed. Kept here (not on the sink) because both file and
/// SQL sinks need identical output for back-compat.
/// </summary>
internal static class LogEventFormatter
{
    public static string Format(LogEvent evt, string? template)
    {
        var levelName = LogLevelName(evt.Level);

        var sb = new StringBuilder(256);
        sb.Append(levelName).Append(" Level Message. EventID: ")
          .Append(evt.EventId.Id.ToString(CultureInfo.InvariantCulture))
          .Append(" Message: ");

        sb.Append(evt.Category);
        sb.AppendLine();

        var message = evt.Message;
        if (!string.IsNullOrWhiteSpace(template) && template != "{0}")
        {
            try
            {
                message = string.Format(CultureInfo.InvariantCulture, template, message);
            }
            catch (FormatException)
            {
                // bad template, fall back to raw message
            }
        }
        sb.Append(message);

        if (evt.Exception is not null)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(evt.Exception);
        }

        if (evt.Scope.Count > 0)
        {
            foreach (var kv in evt.Scope)
            {
                sb.AppendLine();
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
        }

        if (evt.TraceId is not null)
        {
            sb.AppendLine();
            sb.Append("TraceId=").Append(evt.TraceId);
            if (evt.SpanId is not null)
            {
                sb.Append(" SpanId=").Append(evt.SpanId);
            }
        }

        return sb.ToString();
    }

    private static string LogLevelName(Microsoft.Extensions.Logging.LogLevel level) => level switch
    {
        Microsoft.Extensions.Logging.LogLevel.Trace => "Trace",
        Microsoft.Extensions.Logging.LogLevel.Debug => "Debug",
        Microsoft.Extensions.Logging.LogLevel.Information => "Information",
        Microsoft.Extensions.Logging.LogLevel.Warning => "Warning",
        Microsoft.Extensions.Logging.LogLevel.Error => "Error",
        Microsoft.Extensions.Logging.LogLevel.Critical => "Critical",
        Microsoft.Extensions.Logging.LogLevel.None => "None",
        _ => level.ToString(),
    };
}
