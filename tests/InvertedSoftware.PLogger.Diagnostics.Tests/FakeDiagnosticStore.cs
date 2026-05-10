// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

/// <summary>
/// In-memory <see cref="IDiagnosticStore"/> for unit tests. Mimics the SQL
/// dedup contract: same fingerprint → existing record's OccurrenceCount
/// increments. Records every per-occurrence row in <see cref="Occurrences"/>.
/// </summary>
internal sealed class FakeDiagnosticStore : IDiagnosticStore
{
    private readonly Dictionary<string, DiagnosticRecord> _byFingerprint = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _nextId;

    public List<(byte[] Fingerprint, DateTimeOffset OccurredUtc)> Occurrences { get; } = new();

    public bool ThrowOnUpsert { get; set; }

    public IReadOnlyDictionary<string, DiagnosticRecord> ByFingerprint
    {
        get { lock (_gate) { return new Dictionary<string, DiagnosticRecord>(_byFingerprint); } }
    }

    public ValueTask<DiagnosticRecord> UpsertAsync(
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        CancellationToken cancellationToken)
    {
        if (ThrowOnUpsert)
        {
            throw new InvalidOperationException("test fake — store unavailable");
        }

        lock (_gate)
        {
            var key = context.Fingerprint.HashHex;
            if (_byFingerprint.TryGetValue(key, out var existing))
            {
                var updated = existing with
                {
                    OccurrenceCount = existing.OccurrenceCount + 1,
                    LastSeenUtc = context.Event.TimestampUtc,
                };
                _byFingerprint[key] = updated;
                Occurrences.Add((context.Fingerprint.Hash, context.Event.TimestampUtc));
                return ValueTask.FromResult(updated);
            }

            var ex = context.Event.Exception!;
            var record = new DiagnosticRecord(
                ++_nextId,
                context.Fingerprint.Hash,
                context.Event.TimestampUtc,
                context.Event.TimestampUtc,
                1,
                "test-app",
                "Test",
                ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message,
                context.Fingerprint.NormalizedStack,
                SourceFile: null,
                SourceLine: null,
                Diagnosis: null,
                ResolutionState.New,
                GitHubIssueUrl: null,
                AppliedCommitSha: null);

            _byFingerprint[key] = record;
            Occurrences.Add((context.Fingerprint.Hash, context.Event.TimestampUtc));
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<DiagnosticRecord?> GetByFingerprintAsync(byte[] fingerprint, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(
                _byFingerprint.TryGetValue(Convert.ToHexString(fingerprint), out var rec) ? rec : null);
        }
    }

    public ValueTask<IReadOnlyList<DiagnosticRecord>> FindSimilarAsync(
        ReadOnlyMemory<float> embedding,
        double threshold,
        int limit,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<DiagnosticRecord>>(Array.Empty<DiagnosticRecord>());

    public ValueTask MarkFixAppliedAsync(byte[] fingerprint, string commitSha, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = Convert.ToHexString(fingerprint);
            if (_byFingerprint.TryGetValue(key, out var rec))
            {
                _byFingerprint[key] = rec with { AppliedCommitSha = commitSha, ResolutionState = ResolutionState.Applied };
            }
        }
        return ValueTask.CompletedTask;
    }
}
