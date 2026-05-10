// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Threading.Channels;
using InvertedSoftware.PLogger.Core;

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// The secondary channel: events flow file/SQL → drain (instant), and ALSO
/// the capture sink → this channel → diagnostic pipeline (slow path).
/// Independent capacity + DropOldest semantics from the file/SQL drain.
/// </summary>
public sealed class DiagnosticChannel
{
    private readonly Channel<LogEvent> _channel;

    public DiagnosticChannel(int capacity)
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

    /// <summary>Non-blocking write. Returns false only if completed.</summary>
    public bool TryWrite(LogEvent evt) => _channel.Writer.TryWrite(evt);

    /// <summary>Reader for the diagnostic pipeline service.</summary>
    public ChannelReader<LogEvent> Reader => _channel.Reader;

    /// <summary>Signals shutdown — no further writes will arrive.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
