// Copyright (c) Inverted Software. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Tests;

[Collection("DrainSerial")]
public class PLoggerDrainServiceTests
{
    [Fact]
    public async Task Drain_PostedEntries_AreWrittenToSink()
    {
        var channel = new PLoggerChannel(64);
        var sink = new RecordingSink();
        var monitor = new TestMonitor(new PLoggerOptions());
        var drain = new PLoggerDrainService(channel, sink, monitor, NullLogger<PLoggerDrainService>.Instance);

        await drain.StartAsync(CancellationToken.None);

        for (var i = 0; i < 10; i++)
        {
            channel.TryWrite(MakeEvent(i));
        }

        var sw = Stopwatch.StartNew();
        await drain.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Drain shutdown took {sw.Elapsed} — should complete within the configured grace period.");

        Assert.Equal(10, sink.Written.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(i, sink.Written[i].EventId.Id);
        }
        Assert.True(sink.Disposed);
    }

    [Fact]
    public async Task Drain_SinkException_DoesNotKillTheLoop()
    {
        var channel = new PLoggerChannel(64);
        var sink = new RecordingSink { ThrowOnId = 3 };
        var monitor = new TestMonitor(new PLoggerOptions());
        var drain = new PLoggerDrainService(channel, sink, monitor, NullLogger<PLoggerDrainService>.Instance);

        for (var i = 0; i < 6; i++)
        {
            channel.TryWrite(MakeEvent(i));
        }

        // Pre-load the channel before starting so the drain sees all entries
        // immediately and doesn't race with the test thread.
        await drain.StartAsync(CancellationToken.None);
        await drain.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        // Entry 3 was thrown on; entries 0,1,2,4,5 should land. The drain
        // invariant is that one bad entry doesn't take down the pipeline.
        Assert.DoesNotContain(sink.Written, e => e.EventId.Id == 3);
        Assert.Equal(5, sink.Written.Count);
    }

    private static LogEvent MakeEvent(int id) => new(
        DateTimeOffset.UtcNow, LogLevel.Information,
        "cat", new EventId(id), $"msg-{id}", null,
        Array.Empty<KeyValuePair<string, object?>>(), null, null);

    private sealed class RecordingSink : IPLogSink
    {
        private readonly List<LogEvent> _written = new();
        private readonly object _gate = new();

        public IReadOnlyList<LogEvent> Written
        {
            get { lock (_gate) { return _written.ToArray(); } }
        }

        public int? ThrowOnId { get; set; }
        public bool Disposed { get; private set; }

        public async ValueTask WriteAsync(LogEvent evt, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (ThrowOnId is { } id && evt.EventId.Id == id)
            {
                throw new InvalidOperationException("test sink failure");
            }
            lock (_gate)
            {
                _written.Add(evt);
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestMonitor : IOptionsMonitor<PLoggerOptions>
    {
        public TestMonitor(PLoggerOptions value) { CurrentValue = value; }
        public PLoggerOptions CurrentValue { get; }
        public PLoggerOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerOptions, string?> listener) => null;
    }
}
