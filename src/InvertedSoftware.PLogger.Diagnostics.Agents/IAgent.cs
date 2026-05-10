// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Internal contract every agent in the pipeline implements. The
/// orchestrator uses these directly; downstream consumers see only
/// <see cref="IDiagnosticAgent"/> via <c>MultiAgentDiagnosisAgent</c>.
/// </summary>
internal interface IAgent<TInput, TOutput>
{
    ValueTask<TOutput?> RunAsync(TInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Triage classification: which bucket the error falls into and whether
/// downstream stages should run at all.
/// </summary>
public sealed record TriageResult(ErrorCategory Category, bool DeepAnalysisWarranted, string Reasoning);

/// <summary>
/// Context-agent output: the diagnostic context enriched with extra
/// grounding (recent logs, similar prior errors, optionally git blame).
/// In Phase 4 the agent is mostly a pass-through; Phase 4.x adds the
/// real tool surface.
/// </summary>
public sealed record EnrichedContext(DiagnosticContext Context, string AdditionalNotes);

/// <summary>Validator outcome: did the proposed fix survive structural checks.</summary>
public sealed record ValidationResult(bool IsValid, string? Reason);
