// Copyright (c) Inverted Software. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using InvertedSoftware.PLogger.Core.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Core.Internal;

/// <summary>
/// Minimal self-contained host used when a v1 caller invokes
/// <see cref="ILoggerFactory"/>.<c>AddPLogger</c> outside of a real DI/Host
/// pipeline. Spins up a channel, sink, and a single drain task.
/// </summary>
internal sealed class StandalonePLoggerHost : IDisposable
{
    public PLoggerProvider Provider { get; }

    private readonly PLoggerChannel _channel;
    private readonly IPLogSink _sink;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _drainTask;

    private StandalonePLoggerHost(PLoggerOptions options)
    {
        var monitor = new StaticOptionsMonitor<PLoggerOptions>(options);
        _channel = new PLoggerChannel(options.ChannelCapacity);
        _sink = options.PLogType.Equals("file", StringComparison.OrdinalIgnoreCase)
            ? new RollingFileLogSink(monitor, NullLogger<RollingFileLogSink>.Instance)
            : new SqlLogSink(monitor, NullLogger<SqlLogSink>.Instance);

        Provider = new PLoggerProvider(monitor, _channel);
        _drainTask = Task.Run(() => RunDrainAsync(_cts.Token));
    }

    public static StandalonePLoggerHost Create(PLoggerOptions options) => new(options);

    private async Task RunDrainAsync(CancellationToken token)
    {
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await _sink.WriteAsync(evt, token).ConfigureAwait(false);
                }
                catch
                {
                    // logger invariant — keep draining
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    public void Dispose()
    {
        _channel.Complete();
        _cts.Cancel();
        try
        {
            _drainTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // ignored
        }
        _sink.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}

internal sealed class StaticOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>
    : IOptionsMonitor<T>
    where T : class
{
    public StaticOptionsMonitor(T value) { CurrentValue = value; }

    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
