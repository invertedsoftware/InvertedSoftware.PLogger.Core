// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Per-app, per-minute token budget gate. Implementations short-circuit
/// LLM calls when the budget is exhausted; overflow handling is up to the
/// caller (queue to disk, drop, etc.).
/// </summary>
public interface ITokenBudgetEnforcer
{
    /// <summary>
    /// Atomically checks whether <paramref name="estimatedTokens"/> are
    /// available in the current window and reserves them if so.
    /// </summary>
    /// <returns>True if the call may proceed; false if the budget is exhausted.</returns>
    bool TryReserve(int estimatedTokens);

    /// <summary>
    /// Returns previously-reserved tokens to the budget when the work
    /// the caller intended to do didn't actually consume them
    /// (cancellation, parse failure before any LLM call, etc.).
    /// Implementations clamp to zero — no negative consumption.
    /// </summary>
    void Refund(int tokens);

    /// <summary>Returns the remaining budget in the current minute window.</summary>
    int CurrentRemaining();
}

/// <summary>
/// Optional plugin interface for richer PII detection (e.g. Microsoft
/// Presidio). The built-in <c>RedactionEngine</c> already handles the
/// 80% case via <c>[GeneratedRegex]</c>; PII scanners cover the long tail.
/// </summary>
public interface IPiiScanner
{
    ValueTask<string> RedactAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Context describing what's being redacted. Allows the engine to vary
/// redaction strategy by call site (e.g. stricter for source snippets
/// than for log messages).
/// </summary>
public sealed record RedactionContext(string Source, bool AllowFreeText);
