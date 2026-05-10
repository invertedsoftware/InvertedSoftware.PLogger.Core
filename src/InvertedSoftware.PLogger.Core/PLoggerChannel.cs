// Copyright (c) Inverted Software. All rights reserved.

using System.Threading.Channels;

namespace InvertedSoftware.PLogger.Core;

/// <summary>
/// Owns the bounded <see cref="Channel{T}"/> that connects the logging hot
/// path to the drain background service. A single instance is registered in
/// DI so hosted services and providers see the same writer/reader pair.
/// </summary>
public sealed class PLoggerChannel
{
    private readonly Channel<LogEvent> _channel;

    public PLoggerChannel(int capacity = 8192)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>
    /// Non-blocking write. Returns false only if the channel is closed —
    /// <see cref="BoundedChannelFullMode.DropOldest"/> means a full channel
    /// silently drops the oldest entry rather than refusing the write.
    /// </summary>
    public bool TryWrite(LogEvent evt) => _channel.Writer.TryWrite(evt);

    /// <summary>Reader handle for the drain service.</summary>
    public ChannelReader<LogEvent> Reader => _channel.Reader;

    /// <summary>Signals no further writes will arrive. Drain will exit once empty.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
