// Copyright (c) Inverted Software. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Tests;

public class PLoggerHotPathTests
{
    [Fact]
    public void Log_PostsLogEvent_OntoChannel()
    {
        var channel = new PLoggerChannel(64);
        var monitor = new TestOptionsMonitor(new PLoggerOptions());
        var logger = new PLogger("MyCat", monitor, channel, scopeProvider: null);

        logger.LogInformation("hello");

        Assert.True(channel.Reader.TryRead(out var evt));
        Assert.NotNull(evt);
        Assert.Equal("MyCat", evt.Category);
        Assert.Equal(LogLevel.Information, evt.Level);
        Assert.Equal("hello", evt.Message);
        Assert.Null(evt.Exception);
    }

    [Fact]
    public void Log_WhenDisabled_DoesNotEnqueue()
    {
        var channel = new PLoggerChannel(64);
        var monitor = new TestOptionsMonitor(new PLoggerOptions { PLogEnabled = false });
        var logger = new PLogger("Off", monitor, channel, scopeProvider: null);

        logger.LogError("nope");

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void Log_CapturesActivityCorrelation_WhenActivityIsCurrent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = static _ => { },
            ActivityStopped = static _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("plogger.tests");
        using var activity = source.StartActivity("op");
        Assert.NotNull(activity);

        var channel = new PLoggerChannel(64);
        var monitor = new TestOptionsMonitor(new PLoggerOptions());
        var logger = new PLogger("Otel", monitor, channel, scopeProvider: null);

        logger.LogInformation("traced");

        Assert.True(channel.Reader.TryRead(out var evt));
        Assert.NotNull(evt);
        Assert.Equal(activity.TraceId.ToString(), evt.TraceId);
        Assert.Equal(activity.SpanId.ToString(), evt.SpanId);
    }

    [Fact]
    public void Log_DoesNotThrow_WhenFormatterFails()
    {
        var channel = new PLoggerChannel(64);
        var monitor = new TestOptionsMonitor(new PLoggerOptions());
        var logger = new PLogger("BadFmt", monitor, channel, scopeProvider: null);

        // Custom formatter that throws — must not bubble.
        logger.Log<string>(LogLevel.Error, new EventId(1), state: "x", exception: null,
            formatter: static (_, _) => throw new InvalidOperationException("boom"));

        Assert.False(channel.Reader.TryRead(out _));
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<PLoggerOptions>
    {
        public TestOptionsMonitor(PLoggerOptions value) { CurrentValue = value; }
        public PLoggerOptions CurrentValue { get; }
        public PLoggerOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerOptions, string?> listener) => null;
    }
}
