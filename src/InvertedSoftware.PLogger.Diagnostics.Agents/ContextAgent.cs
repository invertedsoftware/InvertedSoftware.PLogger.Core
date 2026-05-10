// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Phase-4 context agent: stub. The full surface (ReadSourceFile,
/// FindSimilarErrors, GetGitBlame, GetOtelTrace, GetRecentLogs as
/// LLM-callable tools) lands in Phase 4.x. For now we pass the existing
/// <see cref="DiagnosticContext"/> through; the SourceLink enricher
/// already populated <c>Source</c> upstream so the diagnosis agent
/// already has the most important grounding signal.
/// </summary>
public sealed class ContextAgent : IAgent<DiagnosticContext, EnrichedContext>
{
    public ValueTask<EnrichedContext?> RunAsync(DiagnosticContext input, CancellationToken cancellationToken)
        => ValueTask.FromResult<EnrichedContext?>(new EnrichedContext(input, AdditionalNotes: string.Empty));
}
