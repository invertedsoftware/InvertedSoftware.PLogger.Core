// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

namespace InvertedSoftware.PLogger.Diagnostics;

/// <summary>
/// Phase-2 default agent: always returns null. The store still receives the
/// upsert (so dedup + frequency counting works), the diagnosis column just
/// stays NULL until Phase 3's <c>AiDiagnosisAgent</c> overrides this.
/// </summary>
public sealed class NoOpDiagnosticAgent : IDiagnosticAgent
{
    public ValueTask<DiagnosisResult?> DiagnoseAsync(DiagnosticContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult<DiagnosisResult?>(null);
}
