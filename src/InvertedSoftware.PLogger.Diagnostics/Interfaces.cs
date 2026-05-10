// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Builds a <see cref="DiagnosticContext"/> from a raw <see cref="LogEvent"/>.
/// Phase 2 ships a default that wires up the fingerprint and an empty
/// recent-trail. Phase 3+ replace it to add Source Link snippets and prior
/// vector hits.
/// </summary>
public interface IDiagnosticEnricher
{
    ValueTask<DiagnosticContext?> EnrichAsync(LogEvent evt, CancellationToken cancellationToken);
}

/// <summary>
/// LLM (or other) agent that produces a <see cref="DiagnosisResult"/> from
/// the enriched context. Phase 2's <c>NoOpDiagnosticAgent</c> always returns
/// <c>null</c> — the diagnostic record is still persisted, just without a
/// hypothesis or fix attached.
/// </summary>
public interface IDiagnosticAgent
{
    ValueTask<DiagnosisResult?> DiagnoseAsync(DiagnosticContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Persistence layer for diagnostic records. Implementations dedup on
/// <see cref="DiagnosticContext.Fingerprint"/> — second occurrence of the
/// same fingerprint should increment a counter, not insert a new row.
/// </summary>
public interface IDiagnosticStore
{
    /// <summary>Insert or merge by fingerprint. Returns the persisted record.</summary>
    ValueTask<DiagnosticRecord> UpsertAsync(
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        CancellationToken cancellationToken);

    /// <summary>Read a record by its fingerprint. Returns null if absent.</summary>
    ValueTask<DiagnosticRecord?> GetByFingerprintAsync(byte[] fingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Cosine-similarity search over <c>DiagnosisVector</c>. Phase 2 stub —
    /// returns empty list until embeddings land in Phase 3.
    /// </summary>
    ValueTask<IReadOnlyList<DiagnosticRecord>> FindSimilarAsync(
        ReadOnlyMemory<float> embedding,
        double threshold,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Closes the feedback loop: a fix landed in commit X.</summary>
    ValueTask MarkFixAppliedAsync(byte[] fingerprint, string commitSha, CancellationToken cancellationToken);
}

/// <summary>
/// The orchestrator that ties an enricher, an agent, and a store together.
/// Phase 2 ships a default implementation; Phase 3+ extend the same shape
/// (vector dedup gate, notifier fan-out) without changing callers.
/// </summary>
public interface IDiagnosticPipeline
{
    /// <summary>
    /// Process one <see cref="LogEvent"/> end-to-end: enrich, diagnose,
    /// persist. Errors must be swallowed — the diagnostic pipeline is
    /// best-effort by design.
    /// </summary>
    ValueTask ProcessAsync(LogEvent evt, CancellationToken cancellationToken);
}
