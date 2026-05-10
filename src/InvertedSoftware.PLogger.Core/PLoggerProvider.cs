// Copyright (c) Inverted Software. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// <see cref="ILoggerProvider"/> that hands out <see cref="PLogger"/>
/// instances. One logger per category is cached. Scope provision is
/// delegated to the host via <see cref="ISupportExternalScope"/>.
/// </summary>
[ProviderAlias("PLogger")]
public sealed class PLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IOptionsMonitor<PLoggerOptions> _options;
    private readonly PLoggerChannel _channel;
    private readonly ConcurrentDictionary<string, PLogger> _loggers = new(StringComparer.Ordinal);

    private IExternalScopeProvider? _scopeProvider;

    public PLoggerProvider(IOptionsMonitor<PLoggerOptions> options, PLoggerChannel channel)
    {
        _options = options;
        _channel = channel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(
            categoryName ?? string.Empty,
            static (cat, st) => new PLogger(cat, st.Options, st.Channel, st.Scope),
            (Options: _options, Channel: _channel, Scope: _scopeProvider));
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
