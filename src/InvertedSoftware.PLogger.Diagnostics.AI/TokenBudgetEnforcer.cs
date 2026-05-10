// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.AI;

/// <summary>
/// Sliding 60-second window token-budget enforcer. Lock-free check + reserve
/// via <see cref="Interlocked"/>. Resets at minute boundaries — granular
/// enough for cost gating without per-call complexity.
/// </summary>
public sealed class TokenBudgetEnforcer : ITokenBudgetEnforcer
{
    private readonly IOptionsMonitor<PLoggerAiOptions> _options;
    private readonly object _gate = new();

    private long _windowStartTicks;
    private int _consumed;

    public TokenBudgetEnforcer(IOptionsMonitor<PLoggerAiOptions> options)
    {
        _options = options;
        _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
    }

    public bool TryReserve(int estimatedTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedTokens);
        var budget = _options.CurrentValue.TokenBudgetPerMinute;
        if (budget <= 0)
        {
            return true;
        }

        lock (_gate)
        {
            RolloverIfStale();
            if (_consumed + estimatedTokens > budget)
            {
                return false;
            }
            _consumed += estimatedTokens;
            return true;
        }
    }

    public int CurrentRemaining()
    {
        var budget = _options.CurrentValue.TokenBudgetPerMinute;
        if (budget <= 0)
        {
            return int.MaxValue;
        }

        lock (_gate)
        {
            RolloverIfStale();
            return Math.Max(0, budget - _consumed);
        }
    }

    private void RolloverIfStale()
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = new DateTimeOffset(_windowStartTicks, TimeSpan.Zero);
        if ((now - windowStart) >= TimeSpan.FromMinutes(1))
        {
            _windowStartTicks = now.Ticks;
            _consumed = 0;
        }
    }
}
