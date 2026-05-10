// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using InvertedSoftware.PLogger.Diagnostics.AI;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class TokenBudgetTests
{
    [Fact]
    public void TryReserve_WhenWithinBudget_ReturnsTrue()
    {
        var enforcer = new TokenBudgetEnforcer(new TestMonitor(new PLoggerAiOptions { TokenBudgetPerMinute = 1000 }));
        Assert.True(enforcer.TryReserve(500));
        Assert.True(enforcer.TryReserve(400));
        Assert.False(enforcer.TryReserve(200)); // 500+400+200 > 1000
    }

    [Fact]
    public void TryReserve_AtExactBudget_ReturnsTrueOnce()
    {
        var enforcer = new TokenBudgetEnforcer(new TestMonitor(new PLoggerAiOptions { TokenBudgetPerMinute = 1000 }));
        Assert.True(enforcer.TryReserve(1000));
        Assert.False(enforcer.TryReserve(1));
    }

    [Fact]
    public void TryReserve_ZeroBudget_AlwaysReturnsTrue()
    {
        var enforcer = new TokenBudgetEnforcer(new TestMonitor(new PLoggerAiOptions { TokenBudgetPerMinute = 0 }));
        Assert.True(enforcer.TryReserve(int.MaxValue));
        Assert.True(enforcer.TryReserve(int.MaxValue));
    }

    [Fact]
    public void CurrentRemaining_ReflectsConsumption()
    {
        var enforcer = new TokenBudgetEnforcer(new TestMonitor(new PLoggerAiOptions { TokenBudgetPerMinute = 1000 }));
        Assert.Equal(1000, enforcer.CurrentRemaining());
        enforcer.TryReserve(300);
        Assert.Equal(700, enforcer.CurrentRemaining());
    }

    private sealed class TestMonitor : IOptionsMonitor<PLoggerAiOptions>
    {
        public TestMonitor(PLoggerAiOptions value) { CurrentValue = value; }
        public PLoggerAiOptions CurrentValue { get; }
        public PLoggerAiOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PLoggerAiOptions, string?> listener) => null;
    }
}
