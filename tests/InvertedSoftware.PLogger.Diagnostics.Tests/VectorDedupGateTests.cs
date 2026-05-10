// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Core.Fingerprinting;
using InvertedSoftware.PLogger.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class VectorDedupGateTests
{
    [Fact]
    public async Task PipelineSkipsAgent_WhenStoreReturnsSimilarPriorDiagnosis()
    {
        var counter = new CountingAgent();
        var store = new FakeDiagnosticStore
        {
            // Pre-canned "similar" response with a non-empty Diagnosis JSON
            // to trigger the short-circuit branch.
            SimilarResponse = new[]
            {
                new DiagnosticRecord(
                    Id: 1,
                    Fingerprint: new byte[16],
                    FirstSeenUtc: DateTimeOffset.UtcNow.AddDays(-1),
                    LastSeenUtc: DateTimeOffset.UtcNow.AddHours(-1),
                    OccurrenceCount: 42,
                    AppName: "test", Environment: "Test",
                    ExceptionType: "InvalidOperationException",
                    ExceptionMessage: "prior",
                    NormalizedStack: "stack",
                    SourceFile: null, SourceLine: null,
                    Diagnosis: "{\"category\":\"Logic\"}",
                    ResolutionState: ResolutionState.Triaged,
                    GitHubIssueUrl: null, AppliedCommitSha: null),
            },
        };

        var pipeline = new DefaultDiagnosticPipeline(
            new EmbeddingFakeEnricher(),
            counter,
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        var evt = MakeEvent(SyntheticException.Create());
        await pipeline.ProcessAsync(evt, CancellationToken.None);

        Assert.Equal(0, counter.Calls);
        Assert.Equal(1, store.FindSimilarCalls);
    }

    [Fact]
    public async Task PipelineCallsAgent_WhenNoSimilarPrior()
    {
        var counter = new CountingAgent();
        var store = new FakeDiagnosticStore { SimilarResponse = Array.Empty<DiagnosticRecord>() };

        var pipeline = new DefaultDiagnosticPipeline(
            new EmbeddingFakeEnricher(),
            counter,
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        var evt = MakeEvent(SyntheticException.Create());
        await pipeline.ProcessAsync(evt, CancellationToken.None);

        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task PipelineCallsAgent_WhenNoEmbeddingPresent()
    {
        var counter = new CountingAgent();
        var store = new FakeDiagnosticStore();
        // DefaultDiagnosticEnricher leaves Embedding null → gate is bypassed.
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(),
            counter,
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        var evt = MakeEvent(SyntheticException.Create());
        await pipeline.ProcessAsync(evt, CancellationToken.None);

        Assert.Equal(1, counter.Calls);
        Assert.Equal(0, store.FindSimilarCalls);
    }

    private static LogEvent MakeEvent(Exception ex) => new(
        DateTimeOffset.UtcNow, LogLevel.Error, "cat", new EventId(1), "msg", ex,
        Array.Empty<KeyValuePair<string, object?>>(), null, null);

    private sealed class CountingAgent : IDiagnosticAgent
    {
        public int Calls { get; private set; }
        public ValueTask<DiagnosisResult?> DiagnoseAsync(DiagnosticContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult<DiagnosisResult?>(null);
        }
    }

    /// <summary>Test enricher that always populates an Embedding so the gate fires.</summary>
    private sealed class EmbeddingFakeEnricher : IDiagnosticEnricher
    {
        public ValueTask<DiagnosticContext?> EnrichAsync(LogEvent evt, CancellationToken cancellationToken)
        {
            if (evt.Exception is null)
            {
                return ValueTask.FromResult<DiagnosticContext?>(null);
            }

            var hash = StackFingerprinter.Compute(evt.Exception);
            var normalized = StackFingerprinter.Normalize(evt.Exception);
            var ctx = new DiagnosticContext(evt,
                new ExceptionFingerprint(hash, normalized),
                Source: null,
                Array.Empty<LogEvent>(),
                Array.Empty<DiagnosticRecord>())
            {
                Embedding = new float[] { 0.1f, 0.2f, 0.3f },
            };
            return ValueTask.FromResult<DiagnosticContext?>(ctx);
        }
    }
}
