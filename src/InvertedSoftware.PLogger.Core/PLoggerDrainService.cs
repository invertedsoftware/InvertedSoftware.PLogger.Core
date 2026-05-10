// Copyright (c) Inverted Software. All rights reserved.

using InvertedSoftware.PLogger.Core.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// Single-reader background service that pulls <see cref="LogEvent"/>s off the
/// channel and dispatches them to every registered <see cref="IPLogSink"/>.
/// </summary>
/// <remarks>
/// <para>The drain is allowed to fail per-entry per-sink; the loop continues.
/// Sink failures are reported via the host's <see cref="ILogger"/> (not via
/// PLogger itself, to avoid recursion).</para>
/// <para>On shutdown the service drains pending entries with a bounded grace
/// period — see <see cref="PLoggerOptions.ShutdownDrainTimeout"/>.</para>
/// </remarks>
public sealed class PLoggerDrainService : BackgroundService
{
    private readonly PLoggerChannel _channel;
    private readonly IPLogSink[] _sinks;
    private readonly ILogger<PLoggerDrainService> _selfLogger;
    private readonly IOptionsMonitor<PLoggerOptions> _options;

    public PLoggerDrainService(
        PLoggerChannel channel,
        IEnumerable<IPLogSink> sinks,
        IOptionsMonitor<PLoggerOptions> options,
        ILogger<PLoggerDrainService> selfLogger)
    {
        _channel = channel;
        _sinks = sinks?.ToArray() ?? Array.Empty<IPLogSink>();
        _selfLogger = selfLogger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DispatchAsync(evt, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected shutdown — fall through to drain
        }

        await DrainRemainingAsync().ConfigureAwait(false);
        PLoggerLog.DrainStopped(_selfLogger);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Complete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DispatchAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        // Fan-out: each sink fails independently. One bad sink never starves the others.
        // We deliberately do NOT rethrow on cancellation here — the event has already
        // been dequeued, so abandoning it mid-fan-out would lose deliveries that the
        // grace-period drain cannot recover. The outer foreach detects cancellation
        // on the next MoveNextAsync and exits naturally.
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PLoggerLog.DrainEntryFailed(_selfLogger, sink.GetType().Name, ex);
            }
        }
    }

    private async Task DrainRemainingAsync()
    {
        var grace = _options.CurrentValue.ShutdownDrainTimeout;
        if (grace <= TimeSpan.Zero)
        {
            await DisposeSinksAsync().ConfigureAwait(false);
            return;
        }

        using var cts = new CancellationTokenSource(grace);
        var pending = _channel.Reader.Count;
        PLoggerLog.DrainStopping(_selfLogger, pending, grace.TotalMilliseconds);

        try
        {
            while (_channel.Reader.TryRead(out var evt))
            {
                if (cts.Token.IsCancellationRequested)
                {
                    break;
                }

                foreach (var sink in _sinks)
                {
                    try
                    {
                        await sink.WriteAsync(evt, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // grace exhausted — stop draining; don't surface
                        return;
                    }
                    catch (Exception ex)
                    {
                        PLoggerLog.DrainEntryFailed(_selfLogger, sink.GetType().Name, ex);
                    }
                }
            }
        }
        finally
        {
            await DisposeSinksAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeSinksAsync()
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PLoggerLog.SinkShutdownFailed(_selfLogger, sink.GetType().Name, ex);
            }
        }
    }
}
