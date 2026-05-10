// Copyright (c) Inverted Software. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Sinks;

/// <summary>
/// The "primary" sink — picks file or SQL based on
/// <see cref="PLoggerOptions.PLogType"/>. Wrapped in a concrete class so the
/// DI container can register it via <c>TryAddEnumerable</c> (which dedupes
/// on implementation type, not service type) alongside any Phase 2+ sinks.
/// </summary>
internal sealed class DefaultPLogSink : IPLogSink
{
    private readonly IPLogSink _inner;

    public DefaultPLogSink(IOptionsMonitor<PLoggerOptions> options, ILoggerFactory loggerFactory)
    {
        var current = options.CurrentValue;
        _inner = current.PLogType.Equals("file", StringComparison.OrdinalIgnoreCase)
            ? new RollingFileLogSink(options, loggerFactory.CreateLogger<RollingFileLogSink>())
            : new SqlLogSink(options, loggerFactory.CreateLogger<SqlLogSink>());
    }

    public ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken)
        => _inner.WriteAsync(evt, cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
