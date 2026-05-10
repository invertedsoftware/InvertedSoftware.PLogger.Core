// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

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
/// <remarks>
/// Implements both <see cref="IDisposable"/> and <see cref="IAsyncDisposable"/>
/// so callers in async-aware contexts (modern ASP.NET, generic-host apps)
/// avoid the sync-over-async deadlock pattern. The synchronous
/// <see cref="Dispose"/> path is best-effort: it does NOT cancel the
/// drain (which would lose dequeued events), only completes the channel
/// and waits for the natural drain to finish, then disposes the sink.
/// </remarks>
internal sealed class StandalonePLoggerHost : IAsyncDisposable, IDisposable
{
    public PLoggerProvider Provider { get; }

    private readonly PLoggerChannel _channel;
    private readonly IPLogSink _sink;
    private readonly Task _drainTask;
    private int _disposed;

    private StandalonePLoggerHost(PLoggerOptions options)
    {
        var monitor = new StaticOptionsMonitor<PLoggerOptions>(options);
        _channel = new PLoggerChannel(options.ChannelCapacity);
        _sink = options.PLogType.Equals("file", StringComparison.OrdinalIgnoreCase)
            ? new RollingFileLogSink(monitor, NullLogger<RollingFileLogSink>.Instance)
            : new SqlLogSink(monitor, NullLogger<SqlLogSink>.Instance);

        Provider = new PLoggerProvider(monitor, _channel);
        _drainTask = Task.Run(RunDrainAsync);
    }

    public static StandalonePLoggerHost Create(PLoggerOptions options) => new(options);

    private async Task RunDrainAsync()
    {
        // Read until the channel is completed and empty; never cancelled —
        // this is the no-host fallback so we don't have a stoppingToken,
        // and the dispose path uses Complete() to terminate the drain.
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await _sink.WriteAsync(evt, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // logger invariant — keep draining
                }
            }
        }
        catch
        {
            // No path here should propagate.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Complete();
        try
        {
            await _drainTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // grace exhausted; we still try to dispose the sink below.
        }

        try
        {
            await _sink.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// Synchronous fallback for callers that can't await. Avoids the
    /// classic sync-over-async deadlock by running the async dispose on
    /// a fresh thread-pool task with no captured SynchronizationContext.
    /// </summary>
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // Run the async path with no captured context; the join is bounded.
        Task.Run(async () => await DisposeAsync().ConfigureAwait(false))
            .Wait(TimeSpan.FromSeconds(8));
    }
}

internal sealed class StaticOptionsMonitor<
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>
    : IOptionsMonitor<T>
    where T : class
{
    public StaticOptionsMonitor(T value) { CurrentValue = value; }

    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
