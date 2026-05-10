// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Core;
using InvertedSoftware.PLogger.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class CaptureSinkTests
{
    [Fact]
    public async Task CaptureSink_Disabled_DoesNotEnqueue()
    {
        var channel = new DiagnosticChannel(64);
        var sink = new DiagnosticCaptureSink(channel, new TestMonitor(new PLoggerDiagnosticsOptions { Enabled = false }));

        await sink.WriteAsync(MakeEvent(SyntheticException.Create()), CancellationToken.None);

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task CaptureSink_BelowMinSeverity_DoesNotEnqueue()
    {
        var channel = new DiagnosticChannel(64);
        var sink = new DiagnosticCaptureSink(channel,
            new TestMonitor(new PLoggerDiagnosticsOptions { Enabled = true, MinSeverity = LogLevel.Critical }));

        await sink.WriteAsync(MakeEvent(SyntheticException.Create(), LogLevel.Error), CancellationToken.None);

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task CaptureSink_NoException_DoesNotEnqueue()
    {
        var channel = new DiagnosticChannel(64);
        var sink = new DiagnosticCaptureSink(channel,
            new TestMonitor(new PLoggerDiagnosticsOptions { Enabled = true, MinSeverity = LogLevel.Information }));

        var evt = new LogEvent(DateTimeOffset.UtcNow, LogLevel.Error, "c", new EventId(1), "msg",
            Exception: null, Array.Empty<KeyValuePair<string, object?>>(), null, null);
        await sink.WriteAsync(evt, CancellationToken.None);

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task CaptureSink_QualifyingEvent_IsEnqueued()
    {
        var channel = new DiagnosticChannel(64);
        var sink = new DiagnosticCaptureSink(channel,
            new TestMonitor(new PLoggerDiagnosticsOptions { Enabled = true, MinSeverity = LogLevel.Error }));

        await sink.WriteAsync(MakeEvent(SyntheticException.Create()), CancellationToken.None);

        Assert.True(channel.Reader.TryRead(out var read));
        Assert.NotNull(read);
        Assert.NotNull(read.Exception);
    }

    private static LogEvent MakeEvent(Exception ex, LogLevel level = LogLevel.Error) => new(
        DateTimeOffset.UtcNow, level,
        "cat", new EventId(1), "msg", ex,
        Array.Empty<KeyValuePair<string, object?>>(), null, null);

    private sealed class TestMonitor : IOptionsMonitor<PLoggerDiagnosticsOptions>
    {
        public TestMonitor(PLoggerDiagnosticsOptions value) { CurrentValue = value; }
        public PLoggerDiagnosticsOptions CurrentValue { get; }
        public PLoggerDiagnosticsOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerDiagnosticsOptions, string?> listener) => null;
    }
}
