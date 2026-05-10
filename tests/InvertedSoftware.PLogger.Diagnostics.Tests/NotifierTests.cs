// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Net;
using System.Text;
using System.Text.Json;
using InvertedSoftware.PLogger.Diagnostics;
using InvertedSoftware.PLogger.Diagnostics.GitHub;
using InvertedSoftware.PLogger.Diagnostics.Slack;
using InvertedSoftware.PLogger.Diagnostics.Teams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvertedSoftware.PLogger.Diagnostics.Tests;

public class NotifierTests
{
    [Fact]
    public async Task GitHubIssueNotifier_PostsIssue_WhenNotAlreadyFiled()
    {
        var handler = new RecordingHandler(searchTotalCount: 0, postStatus: HttpStatusCode.Created);
        var http = new HttpClient(handler);
        var notifier = new GitHubIssueNotifier(http,
            Monitor(new GitHubNotifierOptions
            {
                Enabled = true,
                Repository = "owner/repo",
                Token = "ghp_x",
            }),
            NullLogger<GitHubIssueNotifier>.Instance);

        var (record, ctx) = MakeRecord();
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);

        Assert.Equal(2, handler.Calls.Count); // search + create
        var post = handler.Calls.Last(c => c.Method == HttpMethod.Post);
        Assert.Contains("/issues", post.Path, StringComparison.Ordinal);
        // Title contains the fingerprint tag for idempotency lookups.
        var sent = JsonDocument.Parse(post.Body!);
        // Title carries the [plogger-fp:HEX] idempotency tag.
        Assert.Contains("[plogger-fp:", sent.RootElement.GetProperty("title").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubIssueNotifier_DoesNotPost_WhenSearchReportsExisting()
    {
        var handler = new RecordingHandler(searchTotalCount: 1, postStatus: HttpStatusCode.Created);
        var http = new HttpClient(handler);
        var notifier = new GitHubIssueNotifier(http,
            Monitor(new GitHubNotifierOptions
            {
                Enabled = true, Repository = "owner/repo", Token = "ghp_x",
            }),
            NullLogger<GitHubIssueNotifier>.Instance);

        var (record, ctx) = MakeRecord();
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);

        Assert.Single(handler.Calls); // only the search; no POST
        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
    }

    [Fact]
    public async Task SlackWebhookNotifier_Posts_BlockKitPayload()
    {
        var handler = new RecordingHandler(postStatus: HttpStatusCode.OK);
        var http = new HttpClient(handler);
        var notifier = new SlackWebhookNotifier(http,
            Monitor(new SlackNotifierOptions
            {
                Enabled = true, WebhookUrl = "https://hooks.slack.test/abc",
            }),
            NullLogger<SlackWebhookNotifier>.Instance);

        var (record, ctx) = MakeRecord();
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);

        Assert.Single(handler.Calls);
        var sent = JsonDocument.Parse(handler.Calls[0].Body!);
        Assert.True(sent.RootElement.TryGetProperty("blocks", out var blocks));
        Assert.True(blocks.GetArrayLength() >= 3);
    }

    [Fact]
    public async Task SlackWebhookNotifier_DedupsRapidRepeats()
    {
        var handler = new RecordingHandler(postStatus: HttpStatusCode.OK);
        var http = new HttpClient(handler);
        var notifier = new SlackWebhookNotifier(http,
            Monitor(new SlackNotifierOptions
            {
                Enabled = true, WebhookUrl = "https://hooks.slack.test/abc",
            }),
            NullLogger<SlackWebhookNotifier>.Instance);

        var (record, ctx) = MakeRecord();
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);

        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task TeamsWebhookNotifier_Posts_AdaptiveCardPayload()
    {
        var handler = new RecordingHandler(postStatus: HttpStatusCode.OK);
        var http = new HttpClient(handler);
        var notifier = new TeamsWebhookNotifier(http,
            Monitor(new TeamsNotifierOptions
            {
                Enabled = true, WebhookUrl = "https://outlook.office.test/abc",
            }),
            NullLogger<TeamsWebhookNotifier>.Instance);

        var (record, ctx) = MakeRecord();
        await notifier.NotifyAsync(record, ctx, diagnosis: null, CancellationToken.None);

        Assert.Single(handler.Calls);
        var sent = JsonDocument.Parse(handler.Calls[0].Body!);
        Assert.Equal("message", sent.RootElement.GetProperty("type").GetString());
        Assert.Equal("application/vnd.microsoft.card.adaptive",
            sent.RootElement.GetProperty("attachments")[0].GetProperty("contentType").GetString());
    }

    private static (DiagnosticRecord, DiagnosticContext) MakeRecord()
    {
        var fingerprint = new byte[16];
        Random.Shared.NextBytes(fingerprint);
        var ex = new InvalidOperationException("boom");
        var record = new DiagnosticRecord(
            Id: 1,
            Fingerprint: fingerprint,
            FirstSeenUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastSeenUtc: DateTimeOffset.UtcNow,
            OccurrenceCount: 5,
            AppName: "test",
            Environment: "Test",
            ExceptionType: "InvalidOperationException",
            ExceptionMessage: "boom",
            NormalizedStack: "stack",
            SourceFile: null, SourceLine: null,
            Diagnosis: null,
            ResolutionState: ResolutionState.Triaged,
            GitHubIssueUrl: null, AppliedCommitSha: null);

        var fp = new ExceptionFingerprint(fingerprint, "stack");
        var evt = new InvertedSoftware.PLogger.Core.LogEvent(
            DateTimeOffset.UtcNow, Microsoft.Extensions.Logging.LogLevel.Error,
            "cat", new Microsoft.Extensions.Logging.EventId(1), "msg", ex,
            Array.Empty<KeyValuePair<string, object?>>(), null, null);
        var ctx = new DiagnosticContext(evt, fp, Source: null,
            Array.Empty<InvertedSoftware.PLogger.Core.LogEvent>(),
            Array.Empty<DiagnosticRecord>());
        return (record, ctx);
    }

    private static IOptionsMonitor<T> Monitor<T>(T value) where T : class => new TestMonitor<T>(value);

    private sealed class TestMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>HttpMessageHandler that records every call and returns canned responses.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedCall> Calls { get; } = new();
        private readonly int? _searchTotalCount;
        private readonly HttpStatusCode _postStatus;

        public RecordingHandler(HttpStatusCode postStatus = HttpStatusCode.OK, int? searchTotalCount = null)
        {
            _postStatus = postStatus;
            _searchTotalCount = searchTotalCount;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new RecordedCall(request.Method, request.RequestUri!.AbsolutePath, body));

            if (_searchTotalCount is { } count && request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"total_count\":{count},\"items\":[]}}", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(_postStatus)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedCall(HttpMethod Method, string Path, string? Body);
}
