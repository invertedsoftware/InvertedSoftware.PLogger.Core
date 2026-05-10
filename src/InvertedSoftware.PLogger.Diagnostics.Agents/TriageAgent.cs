// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Agents;

/// <summary>
/// Cheap classifier. Skips obvious transient errors before any frontier
/// model call. Returns a typed <see cref="TriageResult"/>.
/// </summary>
public sealed class TriageAgent : IAgent<DiagnosticContext, TriageResult>
{
    private const string SystemPrompt =
        "You are a triage classifier for .NET exceptions. Given an exception type and message, " +
        "classify it into one of: Transient, Logic, Config, NullDeref, Concurrency, ExternalDependency, Data, Unknown. " +
        "Also decide whether deep analysis is warranted (false for clearly transient errors like " +
        "OperationCanceledException or known TaskCanceledException, true otherwise). " +
        "Reply ONLY in JSON: {\"category\":\"…\",\"deep_analysis\":bool,\"reasoning\":\"…\"}.";

    private readonly IChatClient _chat;
    private readonly IOptionsMonitor<PLoggerAgentsOptions> _options;
    private readonly ILogger<TriageAgent> _logger;

    public TriageAgent(IChatClient chat, IOptionsMonitor<PLoggerAgentsOptions> options, ILogger<TriageAgent> logger)
    {
        _chat = chat;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<TriageResult?> RunAsync(DiagnosticContext input, CancellationToken cancellationToken)
    {
        var ex = input.Event.Exception;
        if (ex is null)
        {
            return null;
        }

        // Hard-coded fast path: transient cancellations should never call out to the model.
        if (ex is OperationCanceledException)
        {
            return new TriageResult(ErrorCategory.Transient, DeepAnalysisWarranted: false, Reasoning: "OperationCanceledException — short-circuit.");
        }

        var opts = _options.CurrentValue;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.CallTimeout);

        var userPrompt = $"Type: {ex.GetType().FullName}\nMessage: {ex.Message}";

        ChatResponse response;
        try
        {
            response = await _chat.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt),
                },
                new ChatOptions
                {
                    ModelId = opts.TriageModel,
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

    internal static TriageResult? TryParse(string? text)
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
            var deep = root.TryGetProperty("deep_analysis", out var d) && d.ValueKind == JsonValueKind.True;
            var reason = root.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() ?? string.Empty : string.Empty;
            return new TriageResult(cat, deep, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
