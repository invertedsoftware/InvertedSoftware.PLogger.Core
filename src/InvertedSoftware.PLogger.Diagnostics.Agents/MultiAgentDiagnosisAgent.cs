// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Orchestrates Triage → Context → Diagnosis → Fix → Validator and
/// returns a typed <see cref="DiagnosisResult"/>. Replaces the
/// single-prompt <see cref="AiDiagnosisAgent"/> from Phase 3.
/// </summary>
public sealed partial class MultiAgentDiagnosisAgent : IDiagnosticAgent
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "PLogger Agents: validator rejected fix on attempt {Attempt}: {Reason}")]
    private static partial void LogValidatorRejected(ILogger logger, int attempt, string? reason);

    private readonly TriageAgent _triage;
    private readonly ContextAgent _context;
    private readonly DiagnosisAgent _diagnosis;
    private readonly FixAgent _fix;
    private readonly ValidatorAgent _validator;
    private readonly IOptionsMonitor<PLoggerAgentsOptions> _options;
    private readonly ITokenBudgetEnforcer _budget;
    private readonly ILogger<MultiAgentDiagnosisAgent> _logger;

    public MultiAgentDiagnosisAgent(
        TriageAgent triage,
        ContextAgent context,
        DiagnosisAgent diagnosis,
        FixAgent fix,
        ValidatorAgent validator,
        IOptionsMonitor<PLoggerAgentsOptions> options,
        ITokenBudgetEnforcer budget,
        ILogger<MultiAgentDiagnosisAgent> logger)
    {
        _triage = triage;
        _context = context;
        _diagnosis = diagnosis;
        _fix = fix;
        _validator = validator;
        _options = options;
        _budget = budget;
        _logger = logger;
    }

    public async ValueTask<DiagnosisResult?> DiagnoseAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var opts = _options.CurrentValue;

        // Stage 1 — triage. Cheap; budget gate applied per-agent below.
        if (!_budget.TryReserve(opts.MaxTokensPerAgent))
        {
            return null;
        }
        var triage = await _triage.RunAsync(context, cancellationToken).ConfigureAwait(false);
        if (triage is null)
        {
            return null;
        }

        if (!triage.DeepAnalysisWarranted)
        {
            // Bail early. Persist the triage outcome as a low-confidence
            // hypothesis so the store still records something.
            return new DiagnosisResult(
                Hypothesis: new RootCauseHypothesis(triage.Category, triage.Reasoning, Array.Empty<string>()),
                Fix: null,
                ConfidenceScore: 0.3,
                ModelVersion: opts.TriageModel,
                Duration: sw.Elapsed);
        }

        // Stage 2 — context grounding.
        var enriched = await _context.RunAsync(context, cancellationToken).ConfigureAwait(false);
        if (enriched is null)
        {
            return null;
        }

        // Stage 3 — diagnosis.
        if (!_budget.TryReserve(opts.MaxTokensPerAgent))
        {
            return null;
        }
        var hypothesis = await _diagnosis.RunAsync(enriched, cancellationToken).ConfigureAwait(false);
        if (hypothesis is null)
        {
            return null;
        }

        // Stage 4 — fix + validate, optionally retried.
        ProposedFix? validated = null;
        for (var attempt = 0; attempt <= opts.FixRetryCount; attempt++)
        {
            if (!_budget.TryReserve(opts.MaxTokensPerAgent))
            {
                break;
            }

            var candidate = await _fix.RunAsync((enriched, hypothesis), cancellationToken).ConfigureAwait(false);
            if (candidate is null)
            {
                break;
            }

            var verdict = await _validator.RunAsync((candidate, candidate.FilePath), cancellationToken).ConfigureAwait(false);
            if (verdict is { IsValid: true })
            {
                validated = candidate;
                break;
            }

            LogValidatorRejected(_logger, attempt, verdict?.Reason);
        }

        sw.Stop();

        return new DiagnosisResult(
            Hypothesis: hypothesis,
            Fix: validated,
            ConfidenceScore: validated is null ? 0.6 : 0.8,
            ModelVersion: opts.FrontierModel,
            Duration: sw.Elapsed);
    }
}
