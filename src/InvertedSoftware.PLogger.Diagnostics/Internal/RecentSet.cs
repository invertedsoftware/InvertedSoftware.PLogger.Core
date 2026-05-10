// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Internal;

/// <summary>
/// Bounded LRU set with TTL. Used by webhook notifiers (Slack, Teams,
/// any future in-process notifier) to dedupe rapid repeats of the same
/// fingerprint between channel-flush cycles. The authoritative dedup is
/// in <see cref="IDiagnosticStore"/>; this is a process-local cache that
/// stops a single tight error loop from spamming external endpoints.
/// </summary>
public sealed class RecentSet
{
    private readonly LinkedList<(string Key, DateTimeOffset Until)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, DateTimeOffset Until)>> _index = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();

    public RecentSet(int capacity, TimeSpan ttl)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _ttl = ttl;
    }

    /// <summary>True if newly added; false if it was already present (and still fresh).</summary>
    public bool Add(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            EvictExpired(now);

            if (_index.TryGetValue(key, out var node) && node.Value.Until > now)
            {
                return false;
            }

            if (_index.Count >= _capacity)
            {
                var oldest = _order.First!;
                _order.RemoveFirst();
                _index.Remove(oldest.Value.Key);
            }

            var newNode = _order.AddLast((key, now + _ttl));
            _index[key] = newNode;
            return true;
        }
    }

    private void EvictExpired(DateTimeOffset now)
    {
        while (_order.First is { } first && first.Value.Until <= now)
        {
            _order.RemoveFirst();
            _index.Remove(first.Value.Key);
        }
    }
}
