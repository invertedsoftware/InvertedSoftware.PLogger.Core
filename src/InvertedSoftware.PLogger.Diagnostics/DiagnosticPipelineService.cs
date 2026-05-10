// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Background reader of the <see cref="DiagnosticChannel"/>. Pulls events,
/// hands them to the <see cref="IDiagnosticPipeline"/>, and on shutdown
/// drains pending entries with a bounded grace period.
/// </summary>
public sealed class DiagnosticPipelineService : BackgroundService
{
    private readonly DiagnosticChannel _channel;
    private readonly IDiagnosticPipeline _pipeline;
    private readonly IOptionsMonitor<PLoggerDiagnosticsOptions> _options;
    private readonly ILogger<DiagnosticPipelineService> _logger;

    public DiagnosticPipelineService(
        DiagnosticChannel channel,
        IDiagnosticPipeline pipeline,
        IOptionsMonitor<PLoggerDiagnosticsOptions> options,
        ILogger<DiagnosticPipelineService> logger)
    {
        _channel = channel;
        _pipeline = pipeline;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await _pipeline.ProcessAsync(evt, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected — fall through to drain
        }

        await DrainRemainingAsync().ConfigureAwait(false);
        DiagnosticsLog.PipelineStopped(_logger);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Complete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DrainRemainingAsync()
    {
        var grace = _options.CurrentValue.ShutdownTimeout;
        if (grace <= TimeSpan.Zero)
        {
            return;
        }

        using var cts = new CancellationTokenSource(grace);
        var pending = _channel.Reader.Count;
        DiagnosticsLog.PipelineStopping(_logger, pending, grace.TotalMilliseconds);

        // Hand each event a fresh CancellationToken.None so a single OCE
        // doesn't abort the rest of the drain. The grace token is checked
        // between events; an exhausted grace breaks cleanly.
        while (_channel.Reader.TryRead(out var evt))
        {
            if (cts.Token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await _pipeline.ProcessAsync(evt, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.PipelineDrainEntryFailed(_logger, ex);
            }
        }
    }
}
