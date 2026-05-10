// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Cheap secondary <see cref="IPLogSink"/> that pushes interesting events
/// onto the diagnostic channel. The expensive enrich → diagnose → persist
/// work runs in <see cref="DiagnosticPipelineService"/> off the main drain.
/// </summary>
public sealed class DiagnosticCaptureSink : IPLogSink
{
    private readonly DiagnosticChannel _channel;
    private readonly IOptionsMonitor<PLoggerDiagnosticsOptions> _options;

    public DiagnosticCaptureSink(
        DiagnosticChannel channel,
        IOptionsMonitor<PLoggerDiagnosticsOptions> options)
    {
        _channel = channel;
        _options = options;
    }

    public ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            return ValueTask.CompletedTask;
        }

        if (evt.Level < opts.MinSeverity)
        {
            return ValueTask.CompletedTask;
        }

        // Phase 2: capture only events that carry an exception. Pure
        // information / warning logs without an exception aren't actionable
        // by the diagnostic pipeline (no fingerprint to dedup on).
        if (evt.Exception is null)
        {
            return ValueTask.CompletedTask;
        }

        _channel.TryWrite(evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        // The pipeline service owns the channel lifecycle; the sink's
        // DisposeAsync runs inside Core's drain teardown which has no
        // ordering guarantee against the diagnostic pipeline's StopAsync.
        // Completing the channel here would close it out from under the
        // pipeline mid-foreach.
        return ValueTask.CompletedTask;
    }
}
