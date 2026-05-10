// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Core.Fingerprinting;

namespace InvertedSoftware.PLogger.Diagnostics.SourceLink;

/// <summary>
/// Enricher that decorates the default fingerprint-only context with a
/// resolved <see cref="SourceSnippet"/> when the source file is reachable.
/// Replaces <c>DefaultDiagnosticEnricher</c> when
/// <c>AddPLoggerSourceLink()</c> is registered.
/// </summary>
public sealed class SourceLinkDiagnosticEnricher : IDiagnosticEnricher
{
    private static readonly IReadOnlyList<LogEvent> EmptyTrail = Array.Empty<LogEvent>();
    private static readonly IReadOnlyList<DiagnosticRecord> EmptyPrior = Array.Empty<DiagnosticRecord>();

    private readonly ISourceLinkResolver _resolver;

    public SourceLinkDiagnosticEnricher(ISourceLinkResolver resolver)
    {
        _resolver = resolver;
    }

    public async ValueTask<DiagnosticContext?> EnrichAsync(LogEvent evt, CancellationToken cancellationToken)
    {
        if (evt.Exception is null)
        {
            return null;
        }

        var hash = StackFingerprinter.Compute(evt.Exception);
        var normalized = StackFingerprinter.Normalize(evt.Exception);
        var fingerprint = new ExceptionFingerprint(hash, normalized);

        SourceSnippet? source = null;
        try
        {
            source = await _resolver.ResolveAsync(evt.Exception, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Resolver failure must not poison the diagnostic context — we
            // still want fingerprint + dedup. Source snippet stays null.
        }

        return new DiagnosticContext(evt, fingerprint, source, EmptyTrail, EmptyPrior);
    }
}
