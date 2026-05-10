// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Default fallback store registered by <c>AddPLoggerDiagnostics()</c>.
/// Accepts upserts but persists nothing — the diagnostic pipeline runs but
/// is essentially a no-op until <c>AddSqlServerStore()</c> (or another
/// store) replaces this registration.
/// </summary>
public sealed class NullDiagnosticStore : IDiagnosticStore
{
    private static readonly DiagnosticRecord Empty = new(
        Id: 0,
        Fingerprint: Array.Empty<byte>(),
        FirstSeenUtc: DateTimeOffset.MinValue,
        LastSeenUtc: DateTimeOffset.MinValue,
        OccurrenceCount: 0,
        AppName: string.Empty,
        Environment: string.Empty,
        ExceptionType: string.Empty,
        ExceptionMessage: string.Empty,
        NormalizedStack: string.Empty,
        SourceFile: null,
        SourceLine: null,
        Diagnosis: null,
        ResolutionState: ResolutionState.New,
        GitHubIssueUrl: null,
        AppliedCommitSha: null);

    public ValueTask<DiagnosticRecord> UpsertAsync(
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(Empty);

    public ValueTask<DiagnosticRecord?> GetByFingerprintAsync(byte[] fingerprint, CancellationToken cancellationToken)
        => ValueTask.FromResult<DiagnosticRecord?>(null);

    public ValueTask<IReadOnlyList<DiagnosticRecord>> FindSimilarAsync(
        ReadOnlyMemory<float> embedding,
        double threshold,
        int limit,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<DiagnosticRecord>>(Array.Empty<DiagnosticRecord>());

    public ValueTask MarkFixAppliedAsync(byte[] fingerprint, string commitSha, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
