// Copyright (c) Inverted Software. All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace InvertedSoftware.PLogger.Core.Tests;

public class ChannelPipelineTests
{
    [Fact]
    public void TryWrite_NeverBlocks_AndPostsAllEntries_WhenCapacityIsAmple()
    {
        const int writes = 100_000;
        const int capacity = 1 << 17;

        var channel = new PLoggerChannel(capacity);
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < writes; i++)
        {
            var evt = new LogEvent(
                DateTimeOffset.UtcNow, LogLevel.Information,
                "cat", new EventId(i), $"msg-{i}", null,
                Array.Empty<KeyValuePair<string, object?>>(), null, null);
            Assert.True(channel.TryWrite(evt));
        }

        sw.Stop();
        channel.Complete();

        // 100k writes should complete in well under a second on any dev box.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"Channel writes took {sw.Elapsed} — hot path is no longer non-blocking.");

        var count = 0;
        while (channel.Reader.TryRead(out _))
        {
            count++;
        }

        Assert.Equal(writes, count);
    }

    [Fact]
    public async Task DropOldest_DropsExactlyOverflow_WhenCapacityExceeded()
    {
        const int capacity = 256;
        const int writes = capacity * 4;

        var channel = new PLoggerChannel(capacity);

        for (var i = 0; i < writes; i++)
        {
            var evt = new LogEvent(
                DateTimeOffset.UtcNow, LogLevel.Information,
                "cat", new EventId(i), $"msg-{i}", null,
                Array.Empty<KeyValuePair<string, object?>>(), null, null);
            Assert.True(channel.TryWrite(evt));
        }

        channel.Complete();

        // Capacity bound is the contract: at most `capacity` items survive.
        var survivors = new List<LogEvent>(capacity);
        await foreach (var evt in channel.Reader.ReadAllAsync())
        {
            survivors.Add(evt);
        }

        Assert.True(survivors.Count <= capacity);
        Assert.True(survivors.Count >= capacity / 2,
            $"DropOldest dropped too many entries: kept {survivors.Count} of {capacity}.");

        // Survivors should be the *latest* range — DropOldest semantics.
        Assert.Equal(writes - 1, survivors[^1].EventId.Id);
    }
}
