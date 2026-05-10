// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.AI;

/// <summary>
/// Single-prompt LLM agent. Builds a redacted prompt from the diagnostic
/// context, calls <see cref="IChatClient"/> with structured output
/// constrained to <see cref="DiagnosisResult"/>, and respects the token
/// budget. Phase 4 swaps this for the multi-agent orchestrator without
/// changing the registration surface.
/// </summary>
public sealed partial class AiDiagnosisAgent : IDiagnosticAgent
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "PLogger AI: token budget exhausted; skipping diagnosis for {Fingerprint}.")]
    private static partial void LogBudgetExhausted(ILogger logger, string fingerprint);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "PLogger AI: chat client failed for {Fingerprint}.")]
    private static partial void LogChatClientFailed(ILogger logger, string fingerprint, Exception exception);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "PLogger AI: model returned non-JSON output for {Fingerprint}.")]
    private static partial void LogNonJsonOutput(ILogger logger, string fingerprint);

    private const string SystemPrompt =
        "You are a senior .NET diagnostic engineer. Given a redacted exception, " +
        "stack trace, and source span, return a JSON object matching the schema:\n" +
        "{\n" +
        "  \"category\": \"Logic|NullDeref|Concurrency|Config|ExternalDependency|Data|Transient|Unknown\",\n" +
        "  \"explanation\": string,\n" +
        "  \"contributing_factors\": [string],\n" +
        "  \"confidence\": float between 0 and 1,\n" +
        "  \"proposed_fix\": null OR { \"file_path\": string, \"unified_diff\": string, \"rationale\": string, \"risk\": \"Low|Medium|High|RequiresReview\", \"regression_concerns\": [string] }\n" +
        "}\n" +
        "Return ONLY the JSON, no commentary.";

    private readonly IChatClient _chat;
    private readonly ITokenBudgetEnforcer _budget;
    private readonly IOptionsMonitor<PLoggerAiOptions> _options;
    private readonly ILogger<AiDiagnosisAgent> _logger;
    private readonly IReadOnlyList<IPiiScanner> _piiScanners;

    public AiDiagnosisAgent(
        IChatClient chat,
        ITokenBudgetEnforcer budget,
        IOptionsMonitor<PLoggerAiOptions> options,
        ILogger<AiDiagnosisAgent> logger,
        IEnumerable<IPiiScanner>? piiScanners = null)
    {
        _chat = chat;
        _budget = budget;
        _options = options;
        _logger = logger;
        _piiScanners = piiScanners?.ToArray() ?? Array.Empty<IPiiScanner>();
    }

    public async ValueTask<DiagnosisResult?> DiagnoseAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            return null;
        }

        if (!_budget.TryReserve(opts.MaxTokensPerCall))
        {
            LogBudgetExhausted(_logger, context.Fingerprint.HashHex);
            return null;
        }

        var prompt = await BuildRedactedPromptAsync(context, cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.CallTimeout);

        var sw = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await _chat.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, prompt),
                },
                new ChatOptions
                {
                    ModelId = opts.Model,
                    MaxOutputTokens = opts.MaxTokensPerCall,
                    Temperature = 0.0f,
                },
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled before we sent — refund the reservation.
            _budget.Refund(opts.MaxTokensPerCall);
            return null;
        }
        catch (Exception ex)
        {
            // Network / SDK failure before we got a response — refund.
            _budget.Refund(opts.MaxTokensPerCall);
            LogChatClientFailed(_logger, context.Fingerprint.HashHex, ex);
            return null;
        }
        sw.Stop();

        var text = response.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            // Model returned nothing usable; tokens were spent on the request
            // even if the response was empty, so do NOT refund here.
            return null;
        }

        if (!TryParse(text, out var hypothesis, out var fix, out var confidence))
        {
            // Same — tokens were spent, just not usefully.
            LogNonJsonOutput(_logger, context.Fingerprint.HashHex);
            return null;
        }

        return new DiagnosisResult(
            Hypothesis: hypothesis,
            Fix: fix,
            ConfidenceScore: confidence,
            ModelVersion: opts.Model,
            Duration: sw.Elapsed);
    }

    private async ValueTask<string> BuildRedactedPromptAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("Exception:");
        sb.Append("  Type: ").AppendLine(context.Event.Exception?.GetType().FullName ?? "?");
        sb.Append("  Message: ").AppendLine(RedactionEngine.Redact(context.Event.Exception?.Message ?? string.Empty));
        sb.AppendLine();
        sb.AppendLine("Normalized stack:");
        sb.AppendLine(RedactionEngine.Redact(context.Fingerprint.NormalizedStack));

        if (context.Source is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Source span:");
            sb.Append("  Path: ").AppendLine(context.Source.FilePath);
            sb.Append("  Line: ").AppendLine(context.Source.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("  Code:");
            sb.AppendLine(RedactionEngine.Redact(context.Source.CodeSpan));
        }

        var raw = sb.ToString();

        // Run any registered PII scanners over the prompt — additive on top
        // of the regex pipeline above.
        foreach (var scanner in _piiScanners)
        {
            try
            {
                raw = await scanner.RedactAsync(raw, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Don't let a faulty scanner block the diagnosis.
            }
        }
        return raw;
    }

    private static bool TryParse(
        string text,
        out RootCauseHypothesis hypothesis,
        out ProposedFix? fix,
        out double confidence)
    {
        hypothesis = new RootCauseHypothesis(ErrorCategory.Unknown, string.Empty, Array.Empty<string>());
        fix = null;
        confidence = 0;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var category = root.TryGetProperty("category", out var catEl) && catEl.ValueKind == JsonValueKind.String
                ? Enum.TryParse<ErrorCategory>(catEl.GetString(), ignoreCase: true, out var c) ? c : ErrorCategory.Unknown
                : ErrorCategory.Unknown;

            var explanation = root.TryGetProperty("explanation", out var expEl) && expEl.ValueKind == JsonValueKind.String
                ? expEl.GetString() ?? string.Empty
                : string.Empty;

            var factors = new List<string>();
            if (root.TryGetProperty("contributing_factors", out var factorsEl) && factorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in factorsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            factors.Add(s);
                        }
                    }
                }
            }

            confidence = root.TryGetProperty("confidence", out var confEl) && confEl.ValueKind == JsonValueKind.Number
                ? Math.Clamp(confEl.GetDouble(), 0, 1)
                : 0;

            hypothesis = new RootCauseHypothesis(category, explanation, factors);

            if (root.TryGetProperty("proposed_fix", out var fixEl) && fixEl.ValueKind == JsonValueKind.Object)
            {
                fix = ParseFix(fixEl);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProposedFix? ParseFix(JsonElement el)
    {
        var path = el.TryGetProperty("file_path", out var pEl) ? pEl.GetString() : null;
        var diff = el.TryGetProperty("unified_diff", out var dEl) ? dEl.GetString() : null;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(diff))
        {
            return null;
        }

        var rationale = el.TryGetProperty("rationale", out var rEl) ? rEl.GetString() ?? string.Empty : string.Empty;
        var risk = el.TryGetProperty("risk", out var riskEl)
                   && Enum.TryParse<RiskLevel>(riskEl.GetString(), ignoreCase: true, out var r)
            ? r : RiskLevel.RequiresReview;

        var regressions = new List<string>();
        if (el.TryGetProperty("regression_concerns", out var regEl) && regEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in regEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        regressions.Add(s);
                    }
                }
            }
        }

        return new ProposedFix(path, diff, rationale, risk, regressions);
    }
}
