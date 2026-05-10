// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.GitHub;

/// <summary>
/// Files a GitHub issue per unique fingerprint. Idempotent: searches by
/// the fingerprint hex tag in the issue title before creating a new one.
/// </summary>
public sealed class GitHubIssueNotifier : IDiagnosticNotifier
{
    internal const string FingerprintTagPrefix = "[plogger-fp:";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<GitHubNotifierOptions> _options;
    private readonly ILogger<GitHubIssueNotifier> _logger;

    public GitHubIssueNotifier(
        HttpClient http,
        IOptionsMonitor<GitHubNotifierOptions> options,
        ILogger<GitHubIssueNotifier> logger)
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
        if (!opts.Enabled || string.IsNullOrEmpty(opts.Repository) || string.IsNullOrEmpty(opts.Token))
        {
            return;
        }

        if (record.OccurrenceCount < opts.MinOccurrenceCount)
        {
            return;
        }

        ConfigureClient(opts);
        var fingerprintHex = context.Fingerprint.HashHex;

        if (await IsAlreadyFiledAsync(opts.Repository, fingerprintHex, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var (title, body) = RenderIssue(record, context, diagnosis, fingerprintHex);
        var payload = new GitHubCreateIssueRequest(title, body, opts.Labels.ToArray());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(opts.RequestTimeout);

        var url = $"https://api.github.com/repos/{opts.Repository}/issues";
        var response = await _http.PostAsJsonAsync(url, payload, GitHubJsonContext.Default.GitHubCreateIssueRequest, cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PLogger GitHub: issue creation returned {Status} for fingerprint {Fingerprint}.",
                (int)response.StatusCode, fingerprintHex);
        }
    }

    private async Task<bool> IsAlreadyFiledAsync(string repo, string fingerprintHex, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"repo:{repo} {FingerprintTagPrefix}{fingerprintHex}] in:title");
        var url = $"https://api.github.com/search/issues?q={query}";
        try
        {
            var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("total_count", out var count)
                && count.ValueKind == JsonValueKind.Number
                && count.GetInt32() > 0;
        }
        catch
        {
            // If the search fails, fall through and create — duplicate is
            // still better than missing the notification.
            return false;
        }
    }

    private static (string Title, string Body) RenderIssue(
        DiagnosticRecord record,
        DiagnosticContext context,
        DiagnosisResult? diagnosis,
        string fingerprintHex)
    {
        var title = $"{FingerprintTagPrefix}{fingerprintHex}] {record.ExceptionType}: {Truncate(record.ExceptionMessage, 120)}";

        var body = new StringBuilder(2048);
        body.Append("**Fingerprint:** `").Append(fingerprintHex).AppendLine("`");
        body.Append("**Occurrences:** ").Append(record.OccurrenceCount).AppendLine();
        body.Append("**First seen:** ").Append(record.FirstSeenUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        body.Append("**Last seen:** ").Append(record.LastSeenUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        body.Append("**App / Environment:** ").Append(record.AppName).Append(" / ").AppendLine(record.Environment);
        body.AppendLine();
        body.AppendLine("### Exception");
        body.Append("```\n").Append(record.ExceptionType).Append(": ").Append(record.ExceptionMessage).AppendLine("\n```");
        body.AppendLine();
        body.AppendLine("### Normalized stack");
        body.Append("```\n").Append(record.NormalizedStack).AppendLine("\n```");

        if (diagnosis is not null)
        {
            body.AppendLine();
            body.AppendLine("### Hypothesis");
            body.Append("- **Category:** ").AppendLine(diagnosis.Hypothesis.Category.ToString());
            body.Append("- **Confidence:** ").Append(diagnosis.ConfidenceScore.ToString("P0", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
            body.Append("- **Explanation:** ").AppendLine(diagnosis.Hypothesis.Explanation);
            if (diagnosis.Hypothesis.ContributingFactors.Count > 0)
            {
                body.AppendLine();
                body.AppendLine("**Contributing factors:**");
                foreach (var factor in diagnosis.Hypothesis.ContributingFactors)
                {
                    body.Append("- ").AppendLine(factor);
                }
            }

            if (diagnosis.Fix is { } fix)
            {
                body.AppendLine();
                body.AppendLine("### Proposed fix");
                body.Append("**File:** `").Append(fix.FilePath).AppendLine("`");
                body.Append("**Risk:** ").AppendLine(fix.Risk.ToString());
                body.AppendLine("**Rationale:**");
                body.AppendLine(fix.Rationale);
                body.AppendLine();
                body.AppendLine("```diff");
                body.AppendLine(fix.UnifiedDiff);
                body.AppendLine("```");
            }
        }

        return (title, body.ToString());
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.AsSpan(0, max).ToString() + "…";

    private void ConfigureClient(GitHubNotifierOptions opts)
    {
        // HttpClient is provided fresh per request scope by IHttpClientFactory;
        // setting headers each call is safe.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.Token);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PLogger", "2.0"));
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }
}

/// <summary>Wire-format request body for issue creation.</summary>
public sealed record GitHubCreateIssueRequest(string title, string body, string[] labels);

/// <summary>AOT-safe JSON context for the GitHub notifier wire types.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = false)]
[System.Text.Json.Serialization.JsonSerializable(typeof(GitHubCreateIssueRequest))]
internal sealed partial class GitHubJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
