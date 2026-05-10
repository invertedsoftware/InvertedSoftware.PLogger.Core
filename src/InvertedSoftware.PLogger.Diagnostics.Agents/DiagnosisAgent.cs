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
/// Frontier-model agent that produces <see cref="RootCauseHypothesis"/>
/// from an <see cref="EnrichedContext"/>. Reuses <see cref="RedactionEngine"/>
/// before any LLM call.
/// </summary>
public sealed class DiagnosisAgent : IAgent<EnrichedContext, RootCauseHypothesis>
{
    private const string SystemPrompt =
        "You are a senior .NET diagnostic engineer. Given a redacted exception context, " +
        "produce a typed root-cause hypothesis. Reply ONLY in JSON: " +
        "{\"category\":\"…\",\"explanation\":\"…\",\"contributing_factors\":[\"…\"]}";

    private readonly IChatClient _chat;
    private readonly IOptionsMonitor<PLoggerAgentsOptions> _options;
    private readonly ILogger<DiagnosisAgent> _logger;

    public DiagnosisAgent(IChatClient chat, IOptionsMonitor<PLoggerAgentsOptions> options, ILogger<DiagnosisAgent> logger)
    {
        _chat = chat;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<RootCauseHypothesis?> RunAsync(EnrichedContext input, CancellationToken cancellationToken)
    {
        var ctx = input.Context;
        if (ctx.Event.Exception is null)
        {
            return null;
        }

        var opts = _options.CurrentValue;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.CallTimeout);

        var prompt = BuildPrompt(ctx, input.AdditionalNotes);

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

    internal static string BuildPrompt(DiagnosticContext ctx, string additionalNotes)
    {
        var ex = ctx.Event.Exception!;
        var sb = new StringBuilder(1024);
        sb.AppendLine("Exception:");
        sb.Append("  Type: ").AppendLine(ex.GetType().FullName ?? "?");
        sb.Append("  Message: ").AppendLine(RedactionEngine.Redact(ex.Message ?? string.Empty));
        sb.AppendLine();
        sb.AppendLine("Normalized stack:");
        sb.AppendLine(RedactionEngine.Redact(ctx.Fingerprint.NormalizedStack));
        if (ctx.Source is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Source span:");
            sb.AppendLine(RedactionEngine.Redact(ctx.Source.CodeSpan));
        }
        if (!string.IsNullOrEmpty(additionalNotes))
        {
            sb.AppendLine();
            sb.AppendLine("Additional notes:");
            sb.AppendLine(additionalNotes);
        }
        return sb.ToString();
    }

    internal static RootCauseHypothesis? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var cat = root.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
                && Enum.TryParse<ErrorCategory>(c.GetString(), ignoreCase: true, out var parsed)
                ? parsed : ErrorCategory.Unknown;
            var explanation = root.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? string.Empty : string.Empty;
            var factors = new List<string>();
            if (root.TryGetProperty("contributing_factors", out var fEl) && fEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fEl.EnumerateArray())
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
            return new RootCauseHypothesis(cat, explanation, factors);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
