// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Diagnostics.Internal;
using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Default Phase-2 implementation: enrich → diagnose → persist. Each stage
/// is best-effort — a failure in any stage is logged and skipped, never
/// rethrown. The capture path's logger invariant (log writes always succeed)
/// extends transitively here.
/// </summary>
public sealed class DefaultDiagnosticPipeline : IDiagnosticPipeline
{
    private readonly IDiagnosticEnricher _enricher;
    private readonly IDiagnosticAgent _agent;
    private readonly IDiagnosticStore _store;
    private readonly ILogger<DefaultDiagnosticPipeline> _logger;

    public DefaultDiagnosticPipeline(
        IDiagnosticEnricher enricher,
        IDiagnosticAgent agent,
        IDiagnosticStore store,
        ILogger<DefaultDiagnosticPipeline> logger)
    {
        _enricher = enricher;
        _agent = agent;
        _store = store;
        _logger = logger;
    }

    public async ValueTask ProcessAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        DiagnosticContext? ctx;
        try
        {
            ctx = await _enricher.EnrichAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.EnrichmentFailed(_logger, evt.Category, ex);
            return;
        }

        if (ctx is null)
        {
            return;
        }

        DiagnosisResult? diagnosis = null;
        try
        {
            diagnosis = await _agent.DiagnoseAsync(ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.AgentFailed(_logger, ctx.Fingerprint.HashHex, ex);
        }

        try
        {
            await _store.UpsertAsync(ctx, diagnosis, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.StoreUpsertFailed(_logger, ctx.Fingerprint.HashHex, ex);
        }
    }
}
