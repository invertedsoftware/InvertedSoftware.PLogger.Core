// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Diagnostics;
using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

[Collection("PipelineSerial")]
public class PipelineServiceTests
{
    [Fact]
    public async Task Pipeline_DrainsPendingEntries_OnShutdownWithinGrace()
    {
        var store = new FakeDiagnosticStore();
        var channel = new DiagnosticChannel(64);
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(), new NoOpDiagnosticAgent(), store, NullLogger<DefaultDiagnosticPipeline>.Instance);
        var monitor = new TestMonitor(new PLoggerDiagnosticsOptions { Enabled = true });
        var service = new DiagnosticPipelineService(channel, pipeline, monitor, NullLogger<DiagnosticPipelineService>.Instance);

        await service.StartAsync(CancellationToken.None);

        for (var i = 0; i < 20; i++)
        {
            channel.TryWrite(MakeEvent(SyntheticException.Create()));
        }

        await WaitForAsync(() => store.Occurrences.Count == 20);
        var sw = Stopwatch.StartNew();
        await service.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(20, store.Occurrences.Count);
        // Same call site → same fingerprint → exactly one record with OccurrenceCount=20.
        Assert.Single(store.ByFingerprint);
        Assert.Equal(20, store.ByFingerprint.Single().Value.OccurrenceCount);
    }

    [Fact]
    public async Task Pipeline_StoreFailure_DoesNotKillTheService()
    {
        var store = new FakeDiagnosticStore { ThrowOnUpsert = true };
        var channel = new DiagnosticChannel(64);
        var pipeline = new DefaultDiagnosticPipeline(
            new DefaultDiagnosticEnricher(), new NoOpDiagnosticAgent(), store, NullLogger<DefaultDiagnosticPipeline>.Instance);
        var monitor = new TestMonitor(new PLoggerDiagnosticsOptions { Enabled = true });
        var service = new DiagnosticPipelineService(channel, pipeline, monitor, NullLogger<DiagnosticPipelineService>.Instance);

        await service.StartAsync(CancellationToken.None);

        for (var i = 0; i < 5; i++)
        {
            channel.TryWrite(MakeEvent(SyntheticException.Create()));
        }

        // Switch the store back to working mid-flight; later events should land.
        await Task.Delay(50);
        store.ThrowOnUpsert = false;

        for (var i = 0; i < 3; i++)
        {
            channel.TryWrite(MakeEvent(SyntheticException.Create()));
        }

        await WaitForAsync(() => store.Occurrences.Count >= 3);
        await service.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        Assert.True(store.Occurrences.Count >= 3, $"expected at least 3 successful occurrences, got {store.Occurrences.Count}");
    }

    private static LogEvent MakeEvent(Exception ex) => new(
        DateTimeOffset.UtcNow, LogLevel.Error,
        "cat", new EventId(1), "msg", ex,
        Array.Empty<KeyValuePair<string, object?>>(), null, null);

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(5);
        }
    }

    private sealed class TestMonitor : IOptionsMonitor<PLoggerDiagnosticsOptions>
    {
        public TestMonitor(PLoggerDiagnosticsOptions value) { CurrentValue = value; }
        public PLoggerDiagnosticsOptions CurrentValue { get; }
        public PLoggerDiagnosticsOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerDiagnosticsOptions, string?> listener) => null;
    }
}
