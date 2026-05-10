// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Core.Fingerprinting;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Phase-2 enricher: builds the fingerprint via
/// <see cref="StackFingerprinter"/> and returns a <see cref="DiagnosticContext"/>
/// with empty trail / no source snippet. Phase 3 replaces this with a richer
/// implementation that resolves Source Link + queries prior similar errors.
/// </summary>
public sealed class DefaultDiagnosticEnricher : IDiagnosticEnricher
{
    private static readonly IReadOnlyList<LogEvent> EmptyTrail = Array.Empty<LogEvent>();
    private static readonly IReadOnlyList<DiagnosticRecord> EmptyPrior = Array.Empty<DiagnosticRecord>();

    public ValueTask<DiagnosticContext?> EnrichAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        if (evt.Exception is null)
        {
            return ValueTask.FromResult<DiagnosticContext?>(null);
        }

        var hash = StackFingerprinter.Compute(evt.Exception);
        var normalized = StackFingerprinter.Normalize(evt.Exception);
        var fingerprint = new ExceptionFingerprint(hash, normalized);

        var ctx = new DiagnosticContext(evt, fingerprint, Source: null, EmptyTrail, EmptyPrior);
        return ValueTask.FromResult<DiagnosticContext?>(ctx);
    }
}
