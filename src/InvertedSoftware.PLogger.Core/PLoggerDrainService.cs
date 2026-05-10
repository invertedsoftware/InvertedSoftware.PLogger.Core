// Copyright (c) Inverted Software. All rights reserved.

using InvertedSoftware.PLogger.Core.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// Single-reader background service that pulls <see cref="LogEvent"/>s off the
/// channel and dispatches them to the configured <see cref="IPLogSink"/>.
/// </summary>
/// <remarks>
/// <para>The drain is allowed to fail per-entry; the loop continues. Sink
/// failures are reported via the host's <see cref="ILogger"/> (not via PLogger
/// itself, to avoid recursion).</para>
/// <para>On shutdown the service drains pending entries with a bounded grace
/// period — see <see cref="PLoggerOptions.ShutdownDrainTimeout"/>.</para>
/// </remarks>
public sealed class PLoggerDrainService : BackgroundService
{
    private readonly PLoggerChannel _channel;
    private readonly IPLogSink _sink;
    private readonly ILogger<PLoggerDrainService> _selfLogger;
    private readonly IOptionsMonitor<PLoggerOptions> _options;

    public PLoggerDrainService(
        PLoggerChannel channel,
        IPLogSink sink,
        IOptionsMonitor<PLoggerOptions> options,
        ILogger<PLoggerDrainService> selfLogger)
    {
        _channel = channel;
        _sink = sink;
        _selfLogger = selfLogger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sinkType = _sink.GetType().Name;
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _sink.WriteAsync(evt, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PLoggerLog.DrainEntryFailed(_selfLogger, sinkType, ex);
                }
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

    private async Task DrainRemainingAsync()
    {
        var grace = _options.CurrentValue.ShutdownDrainTimeout;
        if (grace <= TimeSpan.Zero)
        {
            return;
        }

        using var cts = new CancellationTokenSource(grace);
        var sinkType = _sink.GetType().Name;
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

                try
                {
                    await _sink.WriteAsync(evt, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PLoggerLog.DrainEntryFailed(_selfLogger, sinkType, ex);
                }
            }
        }
        finally
        {
            try
            {
                await _sink.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PLoggerLog.SinkShutdownFailed(_selfLogger, sinkType, ex);
            }
        }
    }
}
