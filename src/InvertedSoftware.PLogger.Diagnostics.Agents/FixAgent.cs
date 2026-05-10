// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Text;
using System.Text.Json;
using InvertedSoftware.PLogger.Diagnostics.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Frontier-model agent that proposes a code fix as a unified diff.
/// Output is always validated by <see cref="ValidatorAgent"/> before
/// being returned upstream.
/// </summary>
public sealed class FixAgent : IAgent<(EnrichedContext Context, RootCauseHypothesis Hypothesis), ProposedFix>
{
    private const string SystemPrompt =
        "Given an exception's root-cause hypothesis and the relevant source span, " +
        "propose a single concrete code change as a unified diff. The diff MUST touch " +
        "only the implicated file. Reply ONLY in JSON: " +
        "{\"file_path\":\"…\",\"unified_diff\":\"…\",\"rationale\":\"…\"," +
        "\"risk\":\"Low|Medium|High|RequiresReview\"," +
        "\"regression_concerns\":[\"…\"]}";

    private readonly IChatClient _chat;
    private readonly IOptionsMonitor<PLoggerAgentsOptions> _options;
    private readonly ILogger<FixAgent> _logger;

    public FixAgent(IChatClient chat, IOptionsMonitor<PLoggerAgentsOptions> options, ILogger<FixAgent> logger)
    {
        _chat = chat;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<ProposedFix?> RunAsync(
        (EnrichedContext Context, RootCauseHypothesis Hypothesis) input,
        CancellationToken cancellationToken)
    {
        var ctx = input.Context.Context;
        if (ctx.Source is null)
        {
            // Without a SourceSnippet there's no anchor for a unified diff —
            // skip silently rather than ask the model to hallucinate paths.
            return null;
        }

        var opts = _options.CurrentValue;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.CallTimeout);

        var prompt = BuildPrompt(ctx, input.Hypothesis);

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
                    ModelId = opts.FrontierModel,
                    MaxOutputTokens = opts.MaxTokensPerAgent,
                    Temperature = 0.0f,
                },
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }

        return TryParse(response.Text);
    }

    internal static string BuildPrompt(DiagnosticContext ctx, RootCauseHypothesis hypothesis)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("Hypothesis:");
        sb.Append("  Category: ").AppendLine(hypothesis.Category.ToString());
        sb.Append("  Explanation: ").AppendLine(hypothesis.Explanation);
        if (hypothesis.ContributingFactors.Count > 0)
        {
            sb.AppendLine("  Contributing factors:");
            foreach (var factor in hypothesis.ContributingFactors)
            {
                sb.Append("    - ").AppendLine(factor);
            }
        }

        sb.AppendLine();
        sb.Append("Source file: ").AppendLine(ctx.Source!.FilePath);
        sb.Append("Implicated line: ").AppendLine(ctx.Source.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.AppendLine("Source span:");
        sb.AppendLine(RedactionEngine.Redact(ctx.Source.CodeSpan));
        return sb.ToString();
    }

    internal static ProposedFix? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var path = root.TryGetProperty("file_path", out var pEl) ? pEl.GetString() : null;
            var diff = root.TryGetProperty("unified_diff", out var dEl) ? dEl.GetString() : null;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(diff))
            {
                return null;
            }

            var rationale = root.TryGetProperty("rationale", out var rEl) ? rEl.GetString() ?? string.Empty : string.Empty;
            var risk = root.TryGetProperty("risk", out var riskEl)
                       && Enum.TryParse<RiskLevel>(riskEl.GetString(), ignoreCase: true, out var r)
                ? r : RiskLevel.RequiresReview;

            var regressions = new List<string>();
            if (root.TryGetProperty("regression_concerns", out var regEl) && regEl.ValueKind == JsonValueKind.Array)
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
        catch (JsonException)
        {
            return null;
        }
    }
}
