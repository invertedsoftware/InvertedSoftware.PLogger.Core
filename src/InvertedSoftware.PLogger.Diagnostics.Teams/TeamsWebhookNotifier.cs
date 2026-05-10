// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Teams;

/// <summary>
/// Microsoft Teams notifier — POSTs an Adaptive Card to an incoming
/// webhook. Same idempotency / dedup posture as the Slack notifier.
/// </summary>
public sealed class TeamsWebhookNotifier : IDiagnosticNotifier
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<TeamsNotifierOptions> _options;
    private readonly ILogger<TeamsWebhookNotifier> _logger;
    private readonly RecentSet _recent = new(capacity: 256, ttl: TimeSpan.FromMinutes(15));

    public TeamsWebhookNotifier(
        HttpClient http,
        IOptionsMonitor<TeamsNotifierOptions> options,
        ILogger<TeamsWebhookNotifier> logger)
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

        var payload = BuildAdaptiveCard(record, context, diagnosis, opts.AppDisplayName);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.RequestTimeout);

        var response = await _http.PostAsJsonAsync(opts.WebhookUrl, payload,
            TeamsJsonContext.Default.TeamsAdaptiveCardEnvelope, cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PLogger Teams: webhook returned {Status} for fingerprint {Fingerprint}.",
                (int)response.StatusCode, context.Fingerprint.HashHex);
        }
    }

    internal static TeamsAdaptiveCardEnvelope BuildAdaptiveCard(
        DiagnosticRecord record,
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        string appDisplay)
    {
        var body = new List<TeamsCardElement>
        {
            new TeamsCardElement("TextBlock",
                $"⚠ {record.ExceptionType} in {appDisplay}",
                Weight: "Bolder", Size: "Large"),
            new TeamsCardElement("TextBlock",
                $"Occurrences: {record.OccurrenceCount} • First: {record.FirstSeenUtc:u} • Last: {record.LastSeenUtc:u}",
                IsSubtle: true, Wrap: true),
            new TeamsCardElement("TextBlock",
                Truncate(record.ExceptionMessage, 800),
                Wrap: true),
        };

        if (diagnosis is not null)
        {
            body.Add(new TeamsCardElement("TextBlock",
                $"Hypothesis ({diagnosis.Hypothesis.Category}, confidence {diagnosis.ConfidenceScore:P0}):",
                Weight: "Bolder"));
            body.Add(new TeamsCardElement("TextBlock",
                Truncate(diagnosis.Hypothesis.Explanation, 1500),
                Wrap: true));
        }

        var card = new TeamsAdaptiveCard(body.ToArray());
        var attachment = new TeamsAttachment("application/vnd.microsoft.card.adaptive", card);
        return new TeamsAdaptiveCardEnvelope("message", new[] { attachment });
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.AsSpan(0, max).ToString() + "…";
}

public sealed record TeamsAdaptiveCardEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("attachments")] TeamsAttachment[] Attachments);

public sealed record TeamsAttachment(
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("content")] TeamsAdaptiveCard Content);

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "System.Text.Json does not serialize static members.")]
public sealed record TeamsAdaptiveCard(
    [property: JsonPropertyName("body")] TeamsCardElement[] Body)
{
    [JsonPropertyName("$schema")]
    public string Schema => "http://adaptivecards.io/schemas/adaptive-card.json";
    [JsonPropertyName("type")]
    public string Type => "AdaptiveCard";
    [JsonPropertyName("version")]
    public string Version => "1.5";
}

public sealed record TeamsCardElement(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("weight")] string? Weight = null,
    [property: JsonPropertyName("size")] string? Size = null,
    [property: JsonPropertyName("isSubtle")] bool? IsSubtle = null,
    [property: JsonPropertyName("wrap")] bool? Wrap = null);

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TeamsAdaptiveCardEnvelope))]
internal sealed partial class TeamsJsonContext : JsonSerializerContext;

internal sealed class RecentSet
{
    private readonly LinkedList<(string Key, DateTimeOffset Until)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, DateTimeOffset Until)>> _index = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();

    public RecentSet(int capacity, TimeSpan ttl)
    {
        _capacity = capacity;
        _ttl = ttl;
    }

    public bool Add(string key)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            while (_order.First is { } first && first.Value.Until <= now)
            {
                _order.RemoveFirst();
                _index.Remove(first.Value.Key);
            }

            if (_index.TryGetValue(key, out var node) && node.Value.Until > now)
            {
                return false;
            }

            if (_index.Count >= _capacity)
            {
                var oldest = _order.First!;
                _order.RemoveFirst();
                _index.Remove(oldest.Value.Key);
            }

            var newNode = _order.AddLast((key, now + _ttl));
            _index[key] = newNode;
            return true;
        }
    }
}
