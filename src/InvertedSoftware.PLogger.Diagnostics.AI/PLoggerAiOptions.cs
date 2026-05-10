// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.AI;

/// <summary>
/// Options for the AI-driven diagnostic agent. Bound from
/// <c>Logging:PLogger:Diagnostics:AI</c>.
/// </summary>
public sealed class PLoggerAiOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:AI";

    /// <summary>Master switch. If false, the agent stays the no-op default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Model identifier passed to <c>IChatClient</c> (e.g. "gpt-5-mini", "qwen2.5:14b").</summary>
    public string Model { get; set; } = "gpt-5-mini";

    /// <summary>Per-app per-minute token budget. 0 = unlimited.</summary>
    public int TokenBudgetPerMinute { get; set; } = 50_000;

    /// <summary>
    /// Maximum tokens per individual diagnosis call. Used as the <c>max_tokens</c>
    /// hint and as the budget reservation amount when calling
    /// <see cref="ITokenBudgetEnforcer.TryReserve"/>.
    /// </summary>
    public int MaxTokensPerCall { get; set; } = 2000;

    /// <summary>
    /// Cosine-similarity threshold for the vector dedup gate. If a prior
    /// diagnosis embeds with similarity ≥ this value, reuse the prior
    /// result and skip the LLM call.
    /// </summary>
    public double VectorSimilarityThreshold { get; set; } = 0.92;

    /// <summary>Per-call timeout. The chat client is given this much wall time per diagnosis.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
