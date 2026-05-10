// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class DedupTests
{
    [Fact]
    public async Task SameExceptionLoggedManyTimes_ProducesOneRecord_AndIncrementsOccurrence()
    {
        var store = new FakeDiagnosticStore();
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(),
            new NoOpDiagnosticAgent(),
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        for (var i = 0; i < 100; i++)
        {
            var evt = MakeEvent(SyntheticException.Create());
            await pipeline.ProcessAsync(evt, CancellationToken.None);
        }

        var records = store.ByFingerprint;
        Assert.Single(records);
        var only = records.Single().Value;
        Assert.Equal(100, only.OccurrenceCount);
        Assert.Equal(100, store.Occurrences.Count);
    }

    [Fact]
    public async Task TwoDifferentExceptionTypes_ProduceSeparateRecords()
    {
        var store = new FakeDiagnosticStore();
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(),
            new NoOpDiagnosticAgent(),
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        for (var i = 0; i < 5; i++)
        {
            await pipeline.ProcessAsync(MakeEvent(SyntheticException.Create<InvalidOperationException>()), CancellationToken.None);
        }
        for (var i = 0; i < 3; i++)
        {
            await pipeline.ProcessAsync(MakeEvent(SyntheticException.Create<ArgumentException>()), CancellationToken.None);
        }

        Assert.Equal(2, store.ByFingerprint.Count);
    }

    [Fact]
    public async Task EventWithoutException_IsNotPersisted()
    {
        var store = new FakeDiagnosticStore();
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(),
            new NoOpDiagnosticAgent(),
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        var evt = new LogEvent(
            DateTimeOffset.UtcNow, LogLevel.Information,
            "cat", new EventId(1), "no-ex", Exception: null,
            Array.Empty<KeyValuePair<string, object?>>(), null, null);

        await pipeline.ProcessAsync(evt, CancellationToken.None);

        Assert.Empty(store.ByFingerprint);
    }

    [Fact]
    public async Task StoreFailure_DoesNotThrow_PipelineSurvives()
    {
        var store = new FakeDiagnosticStore { ThrowOnUpsert = true };
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(),
            new NoOpDiagnosticAgent(),
            store,
            NullLogger<DefaultDiagnosticPipeline>.Instance);

        var evt = MakeEvent(SyntheticException.Create());
        // Must not throw — logger invariant transfers to the diagnostic pipeline.
        await pipeline.ProcessAsync(evt, CancellationToken.None);

        Assert.Empty(store.ByFingerprint);
    }

    private static LogEvent MakeEvent(Exception ex) => new(
        DateTimeOffset.UtcNow, LogLevel.Error,
        "cat", new EventId(1), "msg", ex,
        Array.Empty<KeyValuePair<string, object?>>(), null, null);
}
