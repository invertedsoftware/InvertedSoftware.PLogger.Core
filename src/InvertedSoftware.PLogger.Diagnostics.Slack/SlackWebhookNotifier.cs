// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvertedSoftware.PLogger.Diagnostics.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Slack;

/// <summary>
/// Slack notifier — posts a Block Kit card to an incoming webhook.
/// Idempotency is per-process: a tiny bounded set of recently-notified
/// fingerprints prevents the same card going out twice in quick
/// succession when the same exception fires before the diagnostic
/// pipeline catches up.
/// </summary>
public sealed class SlackWebhookNotifier : IDiagnosticNotifier
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<SlackNotifierOptions> _options;
    private readonly ILogger<SlackWebhookNotifier> _logger;
    private readonly RecentSet _recent = new(capacity: 256, ttl: TimeSpan.FromMinutes(15));

    public SlackWebhookNotifier(
        HttpClient http,
        IOptionsMonitor<SlackNotifierOptions> options,
        ILogger<SlackWebhookNotifier> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async ValueTask NotifyAsync(
        DiagnosticRecord record,
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled || string.IsNullOrEmpty(opts.WebhookUrl))
        {
            return;
        }

        if (record.OccurrenceCount < opts.MinOccurrenceCount)
        {
            return;
        }

        if (!_recent.Add(context.Fingerprint.HashHex))
        {
            return;
        }

        var payload = BuildBlockKit(record, context, diagnosis, opts.AppDisplayName);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.RequestTimeout);

        var response = await _http.PostAsJsonAsync(opts.WebhookUrl, payload,
            SlackJsonContext.Default.SlackBlockKitPayload, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PLogger Slack: webhook returned {Status} for fingerprint {Fingerprint}.",
                (int)response.StatusCode, context.Fingerprint.HashHex);
        }
    }

    internal static SlackBlockKitPayload BuildBlockKit(
        DiagnosticRecord record,
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        string appDisplay)
    {
        var blocks = new List<SlackBlock>
        {
            new SlackBlock("header", new SlackText("plain_text",
                $":warning: {record.ExceptionType} in {appDisplay}")),
            new SlackBlock("section", new SlackText("mrkdwn",
                $"*Occurrences:* {record.OccurrenceCount}  •  *First seen:* {record.FirstSeenUtc:u}  •  *Last seen:* {record.LastSeenUtc:u}\n*Fingerprint:* `{context.Fingerprint.HashHex}`")),
            new SlackBlock("section", new SlackText("mrkdwn",
                $"```\n{Truncate(record.ExceptionMessage, 800)}\n```")),
        };

        if (diagnosis is not null)
        {
            blocks.Add(new SlackBlock("section", new SlackText("mrkdwn",
                $"*Hypothesis ({diagnosis.Hypothesis.Category}, confidence {diagnosis.ConfidenceScore:P0}):*\n{Truncate(diagnosis.Hypothesis.Explanation, 1500)}")));
        }

        return new SlackBlockKitPayload(blocks.ToArray());
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.AsSpan(0, max).ToString() + "…";
}

public sealed record SlackBlockKitPayload(
    [property: JsonPropertyName("blocks")] SlackBlock[] Blocks);

public sealed record SlackBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] SlackText Text);

public sealed record SlackText(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SlackBlockKitPayload))]
internal sealed partial class SlackJsonContext : JsonSerializerContext;
