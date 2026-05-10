// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

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

    // Read via Volatile.Read so cached PLoggers always see the latest pushed value;
    // SetScopeProvider is called by the logger factory AFTER the provider is
    // constructed and possibly after some loggers have been resolved, so a
    // captured snapshot would be stale.
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
            static (cat, st) => new PLogger(cat, st.Options, st.Channel, st.ScopeLookup),
            (Options: _options, Channel: _channel, ScopeLookup: (Func<IExternalScopeProvider?>)GetScopeProvider));
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        Volatile.Write(ref _scopeProvider, scopeProvider);
    }

    private IExternalScopeProvider? GetScopeProvider() => Volatile.Read(ref _scopeProvider);

    public void Dispose()
    {
        _loggers.Clear();
    }
}
