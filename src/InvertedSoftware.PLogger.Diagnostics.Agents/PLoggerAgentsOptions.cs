// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Per-stage configuration for the multi-agent pipeline. Bound from
/// <c>Logging:PLogger:Diagnostics:Agents</c>.
/// </summary>
public sealed class PLoggerAgentsOptions
{
    public const string SectionName = "Logging:PLogger:Diagnostics:Agents";

    /// <summary>Cheap model used by the triage agent.</summary>
    public string TriageModel { get; set; } = "gpt-5-mini";

    /// <summary>Frontier model used by Context / Diagnosis / Fix agents.</summary>
    public string FrontierModel { get; set; } = "gpt-5";

    /// <summary>Per-agent token budget cap.</summary>
    public int MaxTokensPerAgent { get; set; } = 1500;

    /// <summary>How many fix-retry attempts are allowed if validation fails.</summary>
    public int FixRetryCount { get; set; } = 1;

    /// <summary>Per-agent call timeout.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
