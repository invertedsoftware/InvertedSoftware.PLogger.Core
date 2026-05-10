// Copyright (c) Inverted Software. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// The hot-path <see cref="ILogger"/> implementation. Materializes a single
/// <see cref="LogEvent"/> per call and posts it to the channel. Never
/// performs I/O, never blocks, never throws into user code.
/// </summary>
public sealed class PLogger : ILogger
{
    private static readonly IReadOnlyList<KeyValuePair<string, object?>> EmptyScope = Array.Empty<KeyValuePair<string, object?>>();

    [ThreadStatic]
    private static List<KeyValuePair<string, object?>>? t_scopeBuffer;

    private static readonly Action<object?, List<KeyValuePair<string, object?>>> AppendScope = static (scope, buf) =>
    {
        if (scope is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            foreach (var kv in kvps)
            {
                buf.Add(kv);
            }
        }
        else if (scope is not null)
        {
            buf.Add(new KeyValuePair<string, object?>("Scope", scope));
        }
    };

    private readonly string _category;
    private readonly IOptionsMonitor<PLoggerOptions> _options;
    private readonly PLoggerChannel _channel;
    private readonly Func<IExternalScopeProvider?> _scopeProviderLookup;

    public PLogger(
        string category,
        IOptionsMonitor<PLoggerOptions> options,
        PLoggerChannel channel,
        Func<IExternalScopeProvider?> scopeProviderLookup)
    {
        _category = string.IsNullOrEmpty(category) ? nameof(PLogger) : category;
        _options = options;
        _channel = channel;
        _scopeProviderLookup = scopeProviderLookup;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _scopeProviderLookup()?.Push(state);

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
        {
            return false;
        }

        var opts = _options.CurrentValue;
        if (!opts.PLogEnabled)
        {
            return false;
        }

        return opts.Filter is null || opts.Filter(_category, logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);

        string message;
        try
        {
            message = formatter(state, exception);
        }
        catch
        {
            // Logger invariant: a faulty formatter must never bubble.
            return;
        }

        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var scope = CaptureScope();
        var activity = Activity.Current;

        var evt = new LogEvent(
            TimestampUtc: DateTimeOffset.UtcNow,
            Level: logLevel,
            Category: _category,
            EventId: eventId,
            Message: message,
            Exception: exception,
            Scope: scope,
            TraceId: activity?.TraceId.ToString(),
            SpanId: activity?.SpanId.ToString());

        try
        {
            _channel.TryWrite(evt);
        }
        catch
        {
            // Logger invariant: dropping a log entry is preferable to throwing.
        }
    }

    private IReadOnlyList<KeyValuePair<string, object?>> CaptureScope()
    {
        var scopeProvider = _scopeProviderLookup();
        if (scopeProvider is null)
        {
            return EmptyScope;
        }

        var buf = t_scopeBuffer ??= new List<KeyValuePair<string, object?>>(4);
        buf.Clear();
        scopeProvider.ForEachScope(AppendScope, buf);

        if (buf.Count == 0)
        {
            return EmptyScope;
        }

        var snapshot = new KeyValuePair<string, object?>[buf.Count];
        buf.CopyTo(snapshot);
        buf.Clear();
        return snapshot;
    }
}
