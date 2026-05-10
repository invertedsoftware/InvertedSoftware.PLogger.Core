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
    private readonly IDiagnosticNotifier[] _notifiers;
    private readonly ILogger<DefaultDiagnosticPipeline> _logger;

    public DefaultDiagnosticPipeline(
        IDiagnosticEnricher enricher,
        IDiagnosticAgent agent,
        IDiagnosticStore store,
        ILogger<DefaultDiagnosticPipeline> logger,
        IEnumerable<IDiagnosticNotifier>? notifiers = null)
    {
        _enricher = enricher;
        _agent = agent;
        _store = store;
        _notifiers = notifiers?.ToArray() ?? Array.Empty<IDiagnosticNotifier>();
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

        // Vector dedup gate: if the enricher populated an embedding and the
        // store finds a similar prior diagnosis, reuse its hypothesis and
        // skip the (expensive) agent call. Phase 3+ enricher populates this;
        // Phase 2's default leaves it null and the gate is a no-op.
        DiagnosisResult? diagnosis = null;
        if (ctx.Embedding is { } embedding)
        {
            try
            {
                var similar = await _store.FindSimilarAsync(embedding, threshold: 0.92, limit: 1, cancellationToken).ConfigureAwait(false);
                if (similar.Count > 0 && !string.IsNullOrEmpty(similar[0].Diagnosis))
                {
                    // Reuse — no need to call the agent.
                    await _store.UpsertAsync(ctx, diagnosis: null, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.StoreUpsertFailed(_logger, ctx.Fingerprint.HashHex, ex);
                // Fall through — try to diagnose normally.
            }
        }

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

        DiagnosticRecord? persisted = null;
        try
        {
            persisted = await _store.UpsertAsync(ctx, diagnosis, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.StoreUpsertFailed(_logger, ctx.Fingerprint.HashHex, ex);
        }

        if (persisted is null || _notifiers.Length == 0)
        {
            return;
        }

        // Notifier fan-out. Each notifier is independently best-effort —
        // a failing GitHub token shouldn't block the Slack card.
        foreach (var notifier in _notifiers)
        {
            try
            {
                await notifier.NotifyAsync(persisted, ctx, diagnosis, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.NotifierFailed(_logger, notifier.GetType().Name, ex);
            }
        }
    }
}
