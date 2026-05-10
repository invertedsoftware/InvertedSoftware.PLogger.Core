// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.Mcp;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class McpToolsTests
{
    [Fact]
    public async Task GetDiagnosis_ReturnsRecord_WhenStoreHasFingerprint()
    {
        var store = new FakeDiagnosticStore();
        var fingerprint = new byte[16];
        var seedRecord = new DiagnosticRecord(
            Id: 7,
            Fingerprint: fingerprint,
            FirstSeenUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastSeenUtc: DateTimeOffset.UtcNow,
            OccurrenceCount: 13,
            AppName: "test",
            Environment: "Test",
            ExceptionType: "InvalidOperationException",
            ExceptionMessage: "boom",
            NormalizedStack: "stack",
            SourceFile: null,
            SourceLine: null,
            Diagnosis: "{\"category\":\"Logic\"}",
            ResolutionState: ResolutionState.Triaged,
            GitHubIssueUrl: null,
            AppliedCommitSha: null);

        // Seed the fake by calling Upsert with a context that fingerprints to fingerprint.
        // Easier: directly inject via the store's public API (UpsertAsync). We use a
        // synthetic exception so the resulting fingerprint matches what the store keys on.
        // For a tools-level round-trip, we replace with a custom store that returns the
        // seedRecord regardless. Inline:
        var keyedStore = new KeyedSeedStore(seedRecord);
        var tools = new PLoggerDiagnosticsTools(keyedStore);

        var hex = Convert.ToHexString(fingerprint);
        var read = await tools.get_diagnosis(hex, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(7, read.Id);
        Assert.Equal(13, read.OccurrenceCount);
    }

    [Fact]
    public async Task MarkFixApplied_ReachesStore()
    {
        var keyedStore = new KeyedSeedStore(null);
        var tools = new PLoggerDiagnosticsTools(keyedStore);

        var hex = new string('A', 32);
        var result = await tools.mark_fix_applied(hex, "abc123def456", CancellationToken.None);

        Assert.Equal("applied", result);
        Assert.Single(keyedStore.MarkFixApplied);
        Assert.Equal("abc123def456", keyedStore.MarkFixApplied[0]);
    }

    [Fact]
    public void ListTools_IncludesGetDiagnosisAndMarkFixApplied()
    {
        var tools = new PLoggerDiagnosticsTools(new KeyedSeedStore(null));
        var advertised = tools.list_tools();
        Assert.Contains("get_diagnosis", advertised);
        Assert.Contains("mark_fix_applied", advertised);
    }

    private sealed class KeyedSeedStore : IDiagnosticStore
    {
        private readonly DiagnosticRecord? _seed;
        public List<string> MarkFixApplied { get; } = new();

        public KeyedSeedStore(DiagnosticRecord? seed) { _seed = seed; }

        public ValueTask<DiagnosticRecord> UpsertAsync(
            DiagnosticContext context, DiagnosisResult? diagnosis, CancellationToken cancellationToken)
            => ValueTask.FromResult(_seed!);

        public ValueTask<DiagnosticRecord?> GetByFingerprintAsync(byte[] fingerprint, CancellationToken cancellationToken)
            => ValueTask.FromResult(_seed);

        public ValueTask<IReadOnlyList<DiagnosticRecord>> FindSimilarAsync(
            ReadOnlyMemory<float> embedding, double threshold, int limit, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<DiagnosticRecord>>(Array.Empty<DiagnosticRecord>());

        public ValueTask MarkFixAppliedAsync(byte[] fingerprint, string commitSha, CancellationToken cancellationToken)
        {
            MarkFixApplied.Add(commitSha);
            return ValueTask.CompletedTask;
        }
    }
}
